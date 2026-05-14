import argparse
import json
from pathlib import Path

import numpy as np
import pandas as pd
import torch
from torch.utils.data import DataLoader
from tqdm import tqdm

from dataset import DroneVODataset
from drone_pose_utils import quat_to_matrix_xyzw
from model import VOModel
from params import par
from train_drone_finetune import euler_xyz_deg_to_quat_xyzw, geodesic_rotation_error_rad, load_checkpoint


def transform_from_tq(t, q):
    T = np.eye(4, dtype=np.float64)
    T[:3, :3] = quat_to_matrix_xyzw(q)
    T[:3, 3] = np.asarray(t, dtype=np.float64)
    return T


def predict_pairs(model, loader, device):
    rows = []
    model.eval()
    with torch.no_grad():
        for batch in tqdm(loader, leave=False):
            imgs = batch["imgs"].to(device, non_blocking=True)
            gt_pose = batch["pose"].to(device, non_blocking=True)
            pred_pose, _, _, _, _, _ = model(imgs)
            pred_q = euler_xyz_deg_to_quat_xyzw(pred_pose[:, 3:6])
            gt_q = gt_pose[:, 15:19]

            trans_err = torch.linalg.norm(pred_pose[:, :3] - gt_pose[:, :3], dim=-1)
            rot_err = torch.rad2deg(geodesic_rotation_error_rad(pred_q, gt_q))
            pred_scale = torch.linalg.norm(pred_pose[:, :3], dim=-1)
            gt_scale = torch.linalg.norm(gt_pose[:, :3], dim=-1)
            scale_err = torch.abs(pred_scale / torch.clamp(gt_scale, min=1e-8) - 1.0)

            batch_size = imgs.shape[0]
            for i in range(batch_size):
                rows.append(
                    {
                        "sequence_id": batch["sequence_id"][i],
                        "frame_i": int(batch["frame_i"][i]),
                        "frame_j": int(batch["frame_j"][i]),
                        "timestamp_i": float(batch["timestamp_i"][i]),
                        "timestamp_j": float(batch["timestamp_j"][i]),
                        "dt": float(batch["dt"][i]),
                        "stride": int(batch["frame_j"][i]) - int(batch["frame_i"][i]),
                        "image_i": batch["image_i"][i],
                        "image_j": batch["image_j"][i],
                        "gt_tx": float(gt_pose[i, 0].cpu()),
                        "gt_ty": float(gt_pose[i, 1].cpu()),
                        "gt_tz": float(gt_pose[i, 2].cpu()),
                        "gt_qx": float(gt_q[i, 0].cpu()),
                        "gt_qy": float(gt_q[i, 1].cpu()),
                        "gt_qz": float(gt_q[i, 2].cpu()),
                        "gt_qw": float(gt_q[i, 3].cpu()),
                        "pred_tx": float(pred_pose[i, 0].cpu()),
                        "pred_ty": float(pred_pose[i, 1].cpu()),
                        "pred_tz": float(pred_pose[i, 2].cpu()),
                        "pred_qx": float(pred_q[i, 0].cpu()),
                        "pred_qy": float(pred_q[i, 1].cpu()),
                        "pred_qz": float(pred_q[i, 2].cpu()),
                        "pred_qw": float(pred_q[i, 3].cpu()),
                        "translation_error": float(trans_err[i].cpu()),
                        "rotation_error_deg": float(rot_err[i].cpu()),
                        "scale_error": float(scale_err[i].cpu()) if float(gt_scale[i].cpu()) > 1e-8 else np.nan,
                    }
                )
    return pd.DataFrame(rows)


def summarize_metrics(predictions):
    metric_cols = ["translation_error", "rotation_error_deg", "scale_error"]
    overall = predictions[metric_cols].mean(numeric_only=True).to_dict()
    by_sequence = predictions.groupby("sequence_id")[metric_cols].mean(numeric_only=True).reset_index()
    by_stride = predictions.groupby("stride")[metric_cols].mean(numeric_only=True).reset_index()
    return overall, by_sequence, by_stride


def save_trajectory_plots(predictions, output_dir):
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    try:
        import matplotlib

        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        plt = None

    for sequence_id, seq_df in predictions.groupby("sequence_id"):
        seq_df = seq_df.sort_values(["timestamp_i", "timestamp_j"])
        edges = {}
        for row in seq_df.itertuples(index=False):
            current = edges.get(row.frame_i)
            if current is None or row.timestamp_j < current.timestamp_j:
                edges[row.frame_i] = row
        if not edges:
            continue
        frame = min(edges)
        gt_T = np.eye(4)
        pred_T = np.eye(4)
        gt_points = [gt_T[:3, 3].copy()]
        pred_points = [pred_T[:3, 3].copy()]
        visited = set()
        while frame in edges and frame not in visited:
            visited.add(frame)
            row = edges[frame]
            gt_rel = transform_from_tq([row.gt_tx, row.gt_ty, row.gt_tz], [row.gt_qx, row.gt_qy, row.gt_qz, row.gt_qw])
            pred_rel = transform_from_tq(
                [row.pred_tx, row.pred_ty, row.pred_tz],
                [row.pred_qx, row.pred_qy, row.pred_qz, row.pred_qw],
            )
            gt_T = gt_T @ gt_rel
            pred_T = pred_T @ pred_rel
            gt_points.append(gt_T[:3, 3].copy())
            pred_points.append(pred_T[:3, 3].copy())
            frame = row.frame_j
        gt_points = np.asarray(gt_points)
        pred_points = np.asarray(pred_points)
        if len(gt_points) < 2:
            continue
        traj_df = pd.DataFrame(
            {
                "step": np.arange(len(gt_points)),
                "gt_x": gt_points[:, 0],
                "gt_y": gt_points[:, 1],
                "gt_z": gt_points[:, 2],
                "pred_x": pred_points[:, 0],
                "pred_y": pred_points[:, 1],
                "pred_z": pred_points[:, 2],
            }
        )
        traj_df.to_csv(output_dir / f"{sequence_id}_trajectory.csv", index=False)
        if plt is None:
            continue

        fig, axes = plt.subplots(1, 2, figsize=(11, 4))
        axes[0].plot(gt_points[:, 0], gt_points[:, 1], label="gt")
        axes[0].plot(pred_points[:, 0], pred_points[:, 1], label="pred")
        axes[0].set_title("x/y")
        axes[0].axis("equal")
        axes[0].legend()
        axes[1].plot(gt_points[:, 0], gt_points[:, 2], label="gt")
        axes[1].plot(pred_points[:, 0], pred_points[:, 2], label="pred")
        axes[1].set_title("x/z")
        axes[1].axis("equal")
        axes[1].legend()
        fig.suptitle(sequence_id)
        fig.tight_layout()
        fig.savefig(output_dir / f"{sequence_id}_trajectory.png", dpi=150)
        plt.close(fig)


def build_argparser():
    parser = argparse.ArgumentParser(description="Evaluate an XVO drone fine-tuning checkpoint.")
    parser.add_argument("--pair-csv", default="drone_finetune_data/pairs_test.csv")
    parser.add_argument("--checkpoint", required=True)
    parser.add_argument("--output-dir", default="drone_eval")
    parser.add_argument("--batch-size", type=int, default=8)
    parser.add_argument("--num-workers", type=int, default=4)
    parser.add_argument("--img-h", type=int, default=384)
    parser.add_argument("--img-w", type=int, default=640)
    parser.add_argument("--worst-k", type=int, default=50)
    parser.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    return parser


def main():
    args = build_argparser().parse_args()
    par.multi_modal = False
    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    dataset = DroneVODataset(args.pair_csv, (args.img_h, args.img_w), train=False, return_dict=True)
    loader = DataLoader(
        dataset,
        batch_size=args.batch_size,
        shuffle=False,
        num_workers=args.num_workers,
        pin_memory=args.device.startswith("cuda"),
    )

    device = torch.device(args.device)
    model = VOModel().to(device)
    load_checkpoint(model, args.checkpoint, device)
    predictions = predict_pairs(model, loader, device)
    predictions.to_csv(output_dir / "predictions.csv", index=False)

    overall, by_sequence, by_stride = summarize_metrics(predictions)
    by_sequence.to_csv(output_dir / "metrics_by_sequence.csv", index=False)
    by_stride.to_csv(output_dir / "metrics_by_stride.csv", index=False)
    with open(output_dir / "metrics_overall.json", "w", encoding="utf-8") as f:
        json.dump(overall, f, indent=2)

    worst = predictions.assign(
        worst_score=predictions["translation_error"]
        + predictions["rotation_error_deg"] / 10.0
        + predictions["scale_error"].fillna(0.0)
    ).sort_values("worst_score", ascending=False)
    worst.head(args.worst_k).to_csv(output_dir / "worst_pairs.csv", index=False)
    save_trajectory_plots(predictions, output_dir / "trajectories")
    print(json.dumps(overall, indent=2))


if __name__ == "__main__":
    main()
