import argparse
import json
import os
import random
from pathlib import Path

import numpy as np
import torch
import torch.optim as optim
from torch.utils.data import DataLoader, Subset
from tqdm import tqdm

from dataset import DroneVODataset
from model import VOModel
from params import par


def seed_everything(seed):
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.cuda.manual_seed_all(seed)


def quat_mul_xyzw(a, b):
    ax, ay, az, aw = a.unbind(-1)
    bx, by, bz, bw = b.unbind(-1)
    return torch.stack(
        [
            aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz,
        ],
        dim=-1,
    )


def euler_xyz_deg_to_quat_xyzw(euler_deg):
    angles = torch.deg2rad(euler_deg) * 0.5
    sx, sy, sz = torch.sin(angles[:, 0]), torch.sin(angles[:, 1]), torch.sin(angles[:, 2])
    cx, cy, cz = torch.cos(angles[:, 0]), torch.cos(angles[:, 1]), torch.cos(angles[:, 2])
    zeros = torch.zeros_like(sx)
    qx = torch.stack([sx, zeros, zeros, cx], dim=-1)
    qy = torch.stack([zeros, sy, zeros, cy], dim=-1)
    qz = torch.stack([zeros, zeros, sz, cz], dim=-1)
    q = quat_mul_xyzw(quat_mul_xyzw(qz, qy), qx)
    return torch.nn.functional.normalize(q, dim=-1)


def geodesic_rotation_error_rad(q_pred, q_gt):
    q_pred = torch.nn.functional.normalize(q_pred, dim=-1)
    q_gt = torch.nn.functional.normalize(q_gt, dim=-1)
    dot = torch.sum(q_pred * q_gt, dim=-1).abs().clamp(0.0, 1.0 - 1e-6)
    return 2.0 * torch.acos(dot)


def supervised_losses(pred_pose, gt_pose, weights):
    pred_t = pred_pose[:, :3]
    pred_q = euler_xyz_deg_to_quat_xyzw(pred_pose[:, 3:6])
    gt_t = gt_pose[:, :3]
    gt_q = gt_pose[:, 15:19]

    loss_t = torch.nn.functional.smooth_l1_loss(pred_t, gt_t)
    rot_err = geodesic_rotation_error_rad(pred_q, gt_q)
    loss_r = rot_err.mean()

    pred_scale = torch.linalg.norm(pred_t, dim=-1)
    gt_scale = torch.linalg.norm(gt_t, dim=-1)
    valid_scale = gt_scale > 1e-8
    if valid_scale.any():
        loss_s = torch.abs(torch.log(pred_scale[valid_scale] + 1e-8) - torch.log(gt_scale[valid_scale] + 1e-8)).mean()
    else:
        loss_s = pred_scale.sum() * 0.0

    total = weights["translation"] * loss_t + weights["rotation"] * loss_r + weights["scale"] * loss_s
    metrics = {
        "loss": total.detach(),
        "translation_loss": loss_t.detach(),
        "rotation_loss_rad": loss_r.detach(),
        "scale_loss": loss_s.detach(),
        "translation_error": torch.linalg.norm(pred_t - gt_t, dim=-1).mean().detach(),
        "rotation_error_deg": torch.rad2deg(rot_err).mean().detach(),
        "scale_error": torch.abs(pred_scale / torch.clamp(gt_scale, min=1e-8) - 1.0)[valid_scale].mean().detach()
        if valid_scale.any()
        else pred_scale.sum().detach() * 0.0,
    }
    return total, metrics


def load_checkpoint(model, checkpoint_path, device):
    if not checkpoint_path:
        return
    checkpoint = torch.load(checkpoint_path, map_location=device)
    state = checkpoint.get("model_state_dict", checkpoint.get("state_dict", checkpoint))
    clean_state = {}
    for key, value in state.items():
        key = key[7:] if key.startswith("module.") else key
        clean_state[key] = value
    missing, unexpected = model.load_state_dict(clean_state, strict=False)
    print(f"Loaded checkpoint {checkpoint_path}")
    print(f"Missing keys: {len(missing)} | unexpected keys: {len(unexpected)}")


def set_head_only(model):
    for param in model.parameters():
        param.requires_grad = False
    for param in model.decoder.parameters():
        param.requires_grad = True


def set_full_finetune(model):
    for param in model.parameters():
        param.requires_grad = True


def make_optimizer(model, stage, args):
    if stage == "head":
        return optim.AdamW(filter(lambda p: p.requires_grad, model.parameters()), lr=args.lr_head, weight_decay=args.weight_decay)
    return optim.AdamW(
        [
            {"params": model.decoder.parameters(), "lr": args.lr_decoder_full},
            {"params": model.encoder.parameters(), "lr": args.lr_encoder},
            {"params": model.transformer.parameters(), "lr": args.lr_encoder},
        ],
        weight_decay=args.weight_decay,
    )


def current_lrs(optimizer):
    return {f"lr/group_{idx}": group["lr"] for idx, group in enumerate(optimizer.param_groups)}


def init_wandb(args, output_dir, train_dataset, val_dataset):
    if not args.wandb:
        return None
    try:
        import wandb
    except ImportError as exc:
        raise ImportError("Install wandb or run without --wandb.") from exc

    tags = [tag for tag in args.wandb_tags.split(",") if tag] if args.wandb_tags else None
    config = vars(args).copy()
    config.update(
        {
            "train_samples": len(train_dataset),
            "val_samples": len(val_dataset) if val_dataset is not None else 0,
            "xvo_multi_modal": par.multi_modal,
        }
    )
    run = wandb.init(
        project=args.wandb_project,
        entity=args.wandb_entity,
        name=args.wandb_run_name,
        group=args.wandb_group,
        tags=tags,
        mode=args.wandb_mode,
        dir=str(output_dir),
        config=config,
    )
    wandb.define_metric("epoch")
    wandb.define_metric("*", step_metric="epoch")
    return run


def log_wandb_metrics(wandb_run, metrics, stage_name, epoch, optimizer):
    if wandb_run is None:
        return
    payload = {"epoch": epoch, "stage": stage_name}
    payload.update(current_lrs(optimizer))
    for split, split_metrics in metrics.items():
        if not isinstance(split_metrics, dict):
            continue
        for key, value in split_metrics.items():
            payload[f"{split}/{key}"] = value
            payload[f"{stage_name}/{split}/{key}"] = value
    wandb_run.log(payload)


def log_wandb_checkpoint(wandb_run, checkpoint_path, name, epoch):
    if wandb_run is None:
        return
    import wandb

    artifact = wandb.Artifact(name=name, type="model", metadata={"epoch": epoch})
    artifact.add_file(str(checkpoint_path))
    wandb_run.log_artifact(artifact)


def run_epoch(model, loader, optimizer, device, weights, train):
    model.train(train)
    totals = {}
    n = 0
    bar = tqdm(loader, leave=False)
    for batch in bar:
        imgs = batch["imgs"].to(device, non_blocking=True)
        gt_pose = batch["pose"].to(device, non_blocking=True)
        with torch.set_grad_enabled(train):
            pred_pose, _, _, _, _, _ = model(imgs)
            loss, metrics = supervised_losses(pred_pose, gt_pose, weights)
            if train:
                optimizer.zero_grad(set_to_none=True)
                loss.backward()
                torch.nn.utils.clip_grad_norm_(filter(lambda p: p.requires_grad, model.parameters()), 1.0)
                optimizer.step()

        batch_size = imgs.shape[0]
        n += batch_size
        for key, value in metrics.items():
            totals[key] = totals.get(key, 0.0) + float(value.cpu()) * batch_size
        bar.set_postfix({"loss": totals["loss"] / n, "rot_deg": totals["rotation_error_deg"] / n})
    return {key: value / max(n, 1) for key, value in totals.items()}


def maybe_subset(dataset, overfit_batches, batch_size):
    if overfit_batches <= 0:
        return dataset
    count = min(len(dataset), max(1, overfit_batches) * batch_size)
    return Subset(dataset, list(range(count)))


def save_checkpoint(model, optimizer, output_dir, name, epoch, metrics, args):
    path = output_dir / name
    torch.save(
        {
            "epoch": epoch,
            "model_state_dict": model.state_dict(),
            "optimizer_state_dict": optimizer.state_dict(),
            "metrics": metrics,
            "args": vars(args),
        },
        path,
    )
    return path


def train_stage(model, stage_name, epochs, train_loader, val_loader, device, weights, args, output_dir, start_epoch=0, wandb_run=None):
    if epochs <= 0:
        return None, start_epoch
    if stage_name == "head":
        set_head_only(model)
    else:
        set_full_finetune(model)
    optimizer = make_optimizer(model, stage_name, args)
    best = None
    best_metric = float("inf")
    for local_epoch in range(epochs):
        epoch = start_epoch + local_epoch
        print(f"\n[{stage_name}] epoch {local_epoch + 1}/{epochs}")
        train_metrics = run_epoch(model, train_loader, optimizer, device, weights, train=True)
        val_metrics = run_epoch(model, val_loader, optimizer, device, weights, train=False) if val_loader is not None else {}
        metrics = {"train": train_metrics, "val": val_metrics, "stage": stage_name}
        latest = save_checkpoint(model, optimizer, output_dir, "latest.pt", epoch, metrics, args)
        metric = val_metrics.get("loss", train_metrics["loss"])
        if metric < best_metric:
            best_metric = metric
            best = save_checkpoint(model, optimizer, output_dir, "best.pt", epoch, metrics, args)
            if args.wandb_log_checkpoints:
                log_wandb_checkpoint(wandb_run, best, f"xvo-drone-{stage_name}-best", epoch)
        log_wandb_metrics(wandb_run, metrics, stage_name, epoch, optimizer)
        print(json.dumps(metrics, indent=2))
        print(f"Saved {latest}")
    return best, start_epoch + epochs


def resolve_csvs(args):
    if args.prepared_dir:
        prepared = Path(args.prepared_dir)
        return prepared / "pairs_train.csv", prepared / "pairs_val.csv"
    return Path(args.train_csv), Path(args.val_csv) if args.val_csv else None


def build_argparser():
    parser = argparse.ArgumentParser(description="Supervised fine-tuning for XVO on drone VO pairs.")
    parser.add_argument("--prepared-dir", default=None, help="Directory from drone_pose_utils.py with pairs_train.csv/pairs_val.csv.")
    parser.add_argument("--train-csv", default=None)
    parser.add_argument("--val-csv", default=None)
    parser.add_argument("--pretrained-checkpoint", default=None)
    parser.add_argument("--output-dir", default="checkpoints/drone_finetune")
    parser.add_argument("--epochs-head", type=int, default=1)
    parser.add_argument("--epochs-full", type=int, default=5)
    parser.add_argument("--batch-size", type=int, default=8)
    parser.add_argument("--num-workers", type=int, default=4)
    parser.add_argument("--img-h", type=int, default=384)
    parser.add_argument("--img-w", type=int, default=640)
    parser.add_argument("--lr-head", type=float, default=1e-4)
    parser.add_argument("--lr-decoder-full", type=float, default=5e-5)
    parser.add_argument("--lr-encoder", type=float, default=1e-5)
    parser.add_argument("--weight-decay", type=float, default=1e-4)
    parser.add_argument("--translation-weight", type=float, default=1.0)
    parser.add_argument("--rotation-weight", type=float, default=0.1)
    parser.add_argument("--scale-weight", type=float, default=0.1)
    parser.add_argument("--tiny-overfit-batches", type=int, default=0)
    parser.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    parser.add_argument("--seed", type=int, default=2023)
    parser.add_argument("--wandb", action="store_true", help="Enable Weights & Biases logging.")
    parser.add_argument("--wandb-project", default="xvo-drone-finetune")
    parser.add_argument("--wandb-entity", default=None)
    parser.add_argument("--wandb-run-name", default=None)
    parser.add_argument("--wandb-group", default=None)
    parser.add_argument("--wandb-tags", default=None, help="Comma-separated W&B tags.")
    parser.add_argument("--wandb-mode", choices=["online", "offline", "disabled"], default="online")
    parser.add_argument("--wandb-log-checkpoints", action="store_true", help="Upload best checkpoints as W&B artifacts.")
    return parser


def main():
    args = build_argparser().parse_args()
    par.multi_modal = False
    seed_everything(args.seed)
    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    with open(output_dir / "args.json", "w", encoding="utf-8") as f:
        json.dump(vars(args), f, indent=2)

    train_csv, val_csv = resolve_csvs(args)
    if train_csv is None or not train_csv.exists():
        raise FileNotFoundError("Provide --prepared-dir or --train-csv with an existing training pair CSV.")

    train_dataset = DroneVODataset(train_csv, (args.img_h, args.img_w), train=True, return_dict=True)
    train_dataset = maybe_subset(train_dataset, args.tiny_overfit_batches, args.batch_size)
    val_dataset = None
    if val_csv is not None and val_csv.exists() and os.path.getsize(val_csv) > 0:
        val_dataset = DroneVODataset(val_csv, (args.img_h, args.img_w), train=False, return_dict=True)
        val_dataset = maybe_subset(val_dataset, args.tiny_overfit_batches, args.batch_size)

    train_loader = DataLoader(
        train_dataset,
        batch_size=args.batch_size,
        shuffle=True,
        num_workers=args.num_workers,
        pin_memory=args.device.startswith("cuda"),
    )
    val_loader = (
        DataLoader(
            val_dataset,
            batch_size=args.batch_size,
            shuffle=False,
            num_workers=args.num_workers,
            pin_memory=args.device.startswith("cuda"),
        )
        if val_dataset is not None and len(val_dataset)
        else None
    )

    device = torch.device(args.device)
    model = VOModel().to(device)
    load_checkpoint(model, args.pretrained_checkpoint, device)
    weights = {"translation": args.translation_weight, "rotation": args.rotation_weight, "scale": args.scale_weight}
    wandb_run = init_wandb(args, output_dir, train_dataset, val_dataset)
    if wandb_run is not None:
        wandb_run.watch(model, log="gradients", log_freq=100)

    best, next_epoch = train_stage(
        model, "head", args.epochs_head, train_loader, val_loader, device, weights, args, output_dir, wandb_run=wandb_run
    )
    full_best, _ = train_stage(
        model,
        "full",
        args.epochs_full,
        train_loader,
        val_loader,
        device,
        weights,
        args,
        output_dir,
        next_epoch,
        wandb_run=wandb_run,
    )
    best = full_best or best
    if wandb_run is not None:
        wandb_run.summary["best_checkpoint"] = str(best if best is not None else output_dir / "best.pt")
        wandb_run.finish()
    print(f"Done. Best checkpoint: {best if best is not None else output_dir / 'best.pt'}")


if __name__ == "__main__":
    main()
