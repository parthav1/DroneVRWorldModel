import argparse
import json
import math
import re
from pathlib import Path

import numpy as np
import pandas as pd


CANONICAL_COLUMNS = [
    "sequence_id",
    "frame",
    "timestamp_s",
    "x",
    "y",
    "z",
    "qx",
    "qy",
    "qz",
    "qw",
    "source",
]

PAIR_COLUMNS = [
    "sequence_id",
    "frame_i",
    "frame_j",
    "timestamp_i",
    "timestamp_j",
    "dt",
    "image_i",
    "image_j",
    "rel_tx",
    "rel_ty",
    "rel_tz",
    "rel_qx",
    "rel_qy",
    "rel_qz",
    "rel_qw",
    "source",
]

IMAGE_EXTENSIONS = (".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff")


def parse_frame_index(value):
    if pd.isna(value):
        return None
    match = re.findall(r"\d+", str(value))
    if not match:
        return None
    return int(match[-1])


def normalize_quaternion_xyzw(q):
    q = np.asarray(q, dtype=np.float64)
    norm = np.linalg.norm(q)
    if norm < 1e-12:
        return np.array([0.0, 0.0, 0.0, 1.0], dtype=np.float64)
    q = q / norm
    if q[3] < 0:
        q = -q
    return q


def quat_to_matrix_xyzw(q):
    x, y, z, w = normalize_quaternion_xyzw(q)
    xx, yy, zz = x * x, y * y, z * z
    xy, xz, yz = x * y, x * z, y * z
    wx, wy, wz = w * x, w * y, w * z
    return np.array(
        [
            [1.0 - 2.0 * (yy + zz), 2.0 * (xy - wz), 2.0 * (xz + wy)],
            [2.0 * (xy + wz), 1.0 - 2.0 * (xx + zz), 2.0 * (yz - wx)],
            [2.0 * (xz - wy), 2.0 * (yz + wx), 1.0 - 2.0 * (xx + yy)],
        ],
        dtype=np.float64,
    )


def matrix_to_quat_xyzw(R):
    R = np.asarray(R, dtype=np.float64)
    trace = np.trace(R)
    if trace > 0.0:
        s = math.sqrt(trace + 1.0) * 2.0
        qw = 0.25 * s
        qx = (R[2, 1] - R[1, 2]) / s
        qy = (R[0, 2] - R[2, 0]) / s
        qz = (R[1, 0] - R[0, 1]) / s
    elif R[0, 0] > R[1, 1] and R[0, 0] > R[2, 2]:
        s = math.sqrt(max(1.0 + R[0, 0] - R[1, 1] - R[2, 2], 0.0)) * 2.0
        qw = (R[2, 1] - R[1, 2]) / s
        qx = 0.25 * s
        qy = (R[0, 1] + R[1, 0]) / s
        qz = (R[0, 2] + R[2, 0]) / s
    elif R[1, 1] > R[2, 2]:
        s = math.sqrt(max(1.0 + R[1, 1] - R[0, 0] - R[2, 2], 0.0)) * 2.0
        qw = (R[0, 2] - R[2, 0]) / s
        qx = (R[0, 1] + R[1, 0]) / s
        qy = 0.25 * s
        qz = (R[1, 2] + R[2, 1]) / s
    else:
        s = math.sqrt(max(1.0 + R[2, 2] - R[0, 0] - R[1, 1], 0.0)) * 2.0
        qw = (R[1, 0] - R[0, 1]) / s
        qx = (R[0, 2] + R[2, 0]) / s
        qy = (R[1, 2] + R[2, 1]) / s
        qz = 0.25 * s
    return normalize_quaternion_xyzw([qx, qy, qz, qw])


def matrix_to_euler_xyz_deg(R):
    R = np.asarray(R, dtype=np.float64)
    sy = math.sqrt(R[0, 0] * R[0, 0] + R[1, 0] * R[1, 0])
    singular = sy < 1e-6
    if not singular:
        x = math.atan2(R[2, 1], R[2, 2])
        y = math.atan2(-R[2, 0], sy)
        z = math.atan2(R[1, 0], R[0, 0])
    else:
        x = math.atan2(-R[1, 2], R[1, 1])
        y = math.atan2(-R[2, 0], sy)
        z = 0.0
    return np.rad2deg(np.array([x, y, z], dtype=np.float64))


def pose_to_matrix(row):
    T = np.eye(4, dtype=np.float64)
    T[:3, :3] = quat_to_matrix_xyzw([row.qx, row.qy, row.qz, row.qw])
    T[:3, 3] = np.array([row.x, row.y, row.z], dtype=np.float64)
    return T


def rel_pose(row_i, row_j):
    T_i = pose_to_matrix(row_i)
    T_j = pose_to_matrix(row_j)
    T_rel = np.linalg.inv(T_i) @ T_j
    q_rel = matrix_to_quat_xyzw(T_rel[:3, :3])
    return T_rel[:3, 3], q_rel, T_rel


def pair_pose_vector(row):
    q = np.array([row.rel_qx, row.rel_qy, row.rel_qz, row.rel_qw], dtype=np.float64)
    R = quat_to_matrix_xyzw(q)
    euler = matrix_to_euler_xyz_deg(R)
    return np.concatenate(
        [
            np.array([row.rel_tx, row.rel_ty, row.rel_tz], dtype=np.float64),
            euler,
            R.reshape(-1),
            q,
        ]
    ).astype(np.float32)


def sequence_dirs(bundle_dir):
    bundle_dir = Path(bundle_dir)
    return sorted(
        p
        for p in bundle_dir.iterdir()
        if p.is_dir() and ((p / "poses.csv").exists() or (p / "trajectory.csv").exists())
    )


def collect_images(sequence_dir):
    images = []
    for path in sorted(Path(sequence_dir).iterdir()):
        if path.suffix.lower() not in IMAGE_EXTENSIONS:
            continue
        frame = parse_frame_index(path.name)
        timestamp = None
        if frame is None:
            try:
                timestamp = float(path.stem)
            except ValueError:
                timestamp = None
        images.append({"path": str(path.resolve()), "frame": frame, "timestamp_s": timestamp})
    return images


def normalize_csv(csv_path, sequence_id, source):
    raw = pd.read_csv(csv_path)
    if source == "poses":
        frame = raw["frame"].map(parse_frame_index) if "frame" in raw else raw.index
        cols = {
            "timestamp_s": "timestamp_s",
            "x": "tx",
            "y": "ty",
            "z": "tz",
            "qx": "qx",
            "qy": "qy",
            "qz": "qz",
            "qw": "qw",
        }
    else:
        frame = raw["sample_index"].map(parse_frame_index) if "sample_index" in raw else raw.index
        cols = {
            "timestamp_s": "timestamp_s",
            "x": "pos_x",
            "y": "pos_y",
            "z": "pos_z",
            "qx": "rot_x",
            "qy": "rot_y",
            "qz": "rot_z",
            "qw": "rot_w",
        }

    missing = [src for src in cols.values() if src not in raw.columns]
    if missing:
        raise ValueError(f"{csv_path} missing required columns for {source}: {missing}")

    out = pd.DataFrame(
        {
            "sequence_id": sequence_id,
            "frame": frame.astype("Int64"),
            "timestamp_s": raw[cols["timestamp_s"]].astype(float),
            "x": raw[cols["x"]].astype(float),
            "y": raw[cols["y"]].astype(float),
            "z": raw[cols["z"]].astype(float),
            "qx": raw[cols["qx"]].astype(float),
            "qy": raw[cols["qy"]].astype(float),
            "qz": raw[cols["qz"]].astype(float),
            "qw": raw[cols["qw"]].astype(float),
            "source": source,
        }
    )
    q = np.stack([out.qx, out.qy, out.qz, out.qw], axis=1)
    q = np.stack([normalize_quaternion_xyzw(row) for row in q], axis=0)
    out[["qx", "qy", "qz", "qw"]] = q
    return out[CANONICAL_COLUMNS].dropna(subset=["frame", "timestamp_s"])


def normalize_sequence(sequence_dir):
    sequence_dir = Path(sequence_dir)
    frames = []
    if (sequence_dir / "poses.csv").exists():
        frames.append(normalize_csv(sequence_dir / "poses.csv", sequence_dir.name, "poses"))
    if (sequence_dir / "trajectory.csv").exists():
        frames.append(normalize_csv(sequence_dir / "trajectory.csv", sequence_dir.name, "trajectory"))
    if not frames:
        return pd.DataFrame(columns=CANONICAL_COLUMNS)
    return pd.concat(frames, ignore_index=True)[CANONICAL_COLUMNS]


def choose_source(canonical, requested_source):
    if requested_source != "auto":
        return canonical[canonical.source == requested_source].copy()
    for source in ("poses", "trajectory"):
        rows = canonical[canonical.source == source]
        if len(rows):
            return rows.copy()
    return canonical.iloc[0:0].copy()


def attach_images(canonical, images, timestamp_tolerance_s):
    if not len(canonical) or not images:
        return canonical.iloc[0:0].copy()

    image_by_frame = {img["frame"]: img for img in images if img["frame"] is not None}
    image_timestamps = [img for img in images if img["timestamp_s"] is not None]
    rows = []
    for row in canonical.itertuples(index=False):
        image = image_by_frame.get(int(row.frame))
        match_method = "frame"
        if image is None and image_timestamps:
            nearest = min(image_timestamps, key=lambda img: abs(img["timestamp_s"] - row.timestamp_s))
            if abs(nearest["timestamp_s"] - row.timestamp_s) <= timestamp_tolerance_s:
                image = nearest
                match_method = "timestamp"
        if image is None:
            continue
        item = row._asdict()
        item["image_path"] = image["path"]
        item["match_method"] = match_method
        rows.append(item)
    return pd.DataFrame(rows)


def build_pairs(matched, strides):
    if not len(matched):
        return pd.DataFrame(columns=PAIR_COLUMNS)
    matched = matched.sort_values("timestamp_s").drop_duplicates("frame")
    rows = list(matched.itertuples(index=False))
    pairs = []
    for stride in strides:
        for idx in range(0, len(rows) - stride):
            a = rows[idx]
            b = rows[idx + stride]
            t_rel, q_rel, _ = rel_pose(a, b)
            pairs.append(
                {
                    "sequence_id": a.sequence_id,
                    "frame_i": int(a.frame),
                    "frame_j": int(b.frame),
                    "timestamp_i": float(a.timestamp_s),
                    "timestamp_j": float(b.timestamp_s),
                    "dt": float(b.timestamp_s - a.timestamp_s),
                    "image_i": a.image_path,
                    "image_j": b.image_path,
                    "rel_tx": float(t_rel[0]),
                    "rel_ty": float(t_rel[1]),
                    "rel_tz": float(t_rel[2]),
                    "rel_qx": float(q_rel[0]),
                    "rel_qy": float(q_rel[1]),
                    "rel_qz": float(q_rel[2]),
                    "rel_qw": float(q_rel[3]),
                    "source": a.source,
                }
            )
    return pd.DataFrame(pairs, columns=PAIR_COLUMNS)


def split_sequence_ids(sequence_ids, train_ratio, val_ratio, seed):
    sequence_ids = sorted(set(sequence_ids))
    rng = np.random.default_rng(seed)
    shuffled = list(sequence_ids)
    rng.shuffle(shuffled)
    n = len(shuffled)
    if n == 0:
        return {"train": [], "val": [], "test": []}
    n_train = max(1, int(round(n * train_ratio))) if n > 1 else 1
    n_val = int(round(n * val_ratio))
    if n >= 3 and n_val == 0:
        n_val = 1
    if n_train + n_val >= n and n > 1:
        n_train = max(1, n - n_val - 1)
    if n_train + n_val > n:
        n_val = max(0, n - n_train)
    return {
        "train": shuffled[:n_train],
        "val": shuffled[n_train : n_train + n_val],
        "test": shuffled[n_train + n_val :],
    }


def save_histograms(canonical, pairs, output_dir):
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    try:
        import matplotlib

        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        records = []
        for name, values in {
            "absolute_x": canonical["x"].to_numpy() if len(canonical) else np.array([]),
            "absolute_y": canonical["y"].to_numpy() if len(canonical) else np.array([]),
            "absolute_z": canonical["z"].to_numpy() if len(canonical) else np.array([]),
            "timestamp_s": canonical["timestamp_s"].to_numpy() if len(canonical) else np.array([]),
        }.items():
            if len(values):
                counts, edges = np.histogram(values, bins=40)
                records.extend(
                    {"histogram": name, "bin_left": float(edges[i]), "bin_right": float(edges[i + 1]), "count": int(counts[i])}
                    for i in range(len(counts))
                )
        pd.DataFrame(records).to_csv(output_dir / "pose_histograms.csv", index=False)

        records = []
        if len(pairs):
            rel_t = pairs[["rel_tx", "rel_ty", "rel_tz"]].to_numpy()
            rel_norm = np.linalg.norm(rel_t, axis=1)
            rot_deg = []
            for row in pairs.itertuples(index=False):
                qw = abs(normalize_quaternion_xyzw([row.rel_qx, row.rel_qy, row.rel_qz, row.rel_qw])[3])
                rot_deg.append(np.rad2deg(2.0 * np.arccos(np.clip(qw, -1.0, 1.0))))
            for name, values in {
                "relative_translation_norm": rel_norm,
                "relative_rotation_deg": np.asarray(rot_deg),
                "dt": pairs["dt"].to_numpy(),
                "frame_stride": pairs["frame_j"].to_numpy() - pairs["frame_i"].to_numpy(),
            }.items():
                counts, edges = np.histogram(values, bins=40)
                records.extend(
                    {"histogram": name, "bin_left": float(edges[i]), "bin_right": float(edges[i + 1]), "count": int(counts[i])}
                    for i in range(len(counts))
                )
        pd.DataFrame(records).to_csv(output_dir / "pair_histograms.csv", index=False)
        return

    if len(canonical):
        fig, axes = plt.subplots(2, 2, figsize=(10, 7))
        for ax, col in zip(axes.flat[:3], ["x", "y", "z"]):
            ax.hist(canonical[col].to_numpy(), bins=40)
            ax.set_title(f"absolute {col}")
        axes.flat[3].hist(canonical["timestamp_s"].to_numpy(), bins=40)
        axes.flat[3].set_title("timestamp_s")
        fig.tight_layout()
        fig.savefig(output_dir / "pose_histograms.png", dpi=150)
        plt.close(fig)
    if len(pairs):
        rel_t = pairs[["rel_tx", "rel_ty", "rel_tz"]].to_numpy()
        rel_norm = np.linalg.norm(rel_t, axis=1)
        rot_deg = []
        for row in pairs.itertuples(index=False):
            qw = abs(normalize_quaternion_xyzw([row.rel_qx, row.rel_qy, row.rel_qz, row.rel_qw])[3])
            rot_deg.append(np.rad2deg(2.0 * np.arccos(np.clip(qw, -1.0, 1.0))))
        stride = pairs["frame_j"].to_numpy() - pairs["frame_i"].to_numpy()
        fig, axes = plt.subplots(2, 2, figsize=(10, 7))
        axes.flat[0].hist(rel_norm, bins=40)
        axes.flat[0].set_title("relative translation norm")
        axes.flat[1].hist(rot_deg, bins=40)
        axes.flat[1].set_title("relative rotation deg")
        axes.flat[2].hist(pairs["dt"].to_numpy(), bins=40)
        axes.flat[2].set_title("dt")
        axes.flat[3].hist(stride, bins=40)
        axes.flat[3].set_title("frame stride")
        fig.tight_layout()
        fig.savefig(output_dir / "pair_histograms.png", dpi=150)
        plt.close(fig)


def prepare_drone_bundle(args):
    bundle_dir = Path(args.bundle_dir)
    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    canonical_parts = []
    pair_parts = []
    manifest = {"sequences": {}}
    for seq_dir in sequence_dirs(bundle_dir):
        canonical = normalize_sequence(seq_dir)
        images = collect_images(seq_dir)
        active = choose_source(canonical, args.pose_source)
        matched = attach_images(active, images, args.timestamp_tolerance_s)
        pairs = build_pairs(matched, args.strides)
        canonical_parts.append(canonical)
        pair_parts.append(pairs)
        manifest["sequences"][seq_dir.name] = {
            "canonical_rows": int(len(canonical)),
            "active_rows": int(len(active)),
            "matched_rows": int(len(matched)),
            "pairs": int(len(pairs)),
            "source": args.pose_source if args.pose_source != "auto" else (matched.source.iloc[0] if len(matched) else None),
        }

    canonical_all = (
        pd.concat(canonical_parts, ignore_index=True)
        if canonical_parts
        else pd.DataFrame(columns=CANONICAL_COLUMNS)
    )
    pairs_all = pd.concat(pair_parts, ignore_index=True) if pair_parts else pd.DataFrame(columns=PAIR_COLUMNS)
    canonical_all.to_csv(output_dir / "canonical_poses.csv", index=False)
    pairs_all.to_csv(output_dir / "pairs_all.csv", index=False)

    splits = split_sequence_ids(pairs_all.sequence_id.unique(), args.train_ratio, args.val_ratio, args.seed)
    for split, sequence_ids in splits.items():
        split_df = pairs_all[pairs_all.sequence_id.isin(sequence_ids)].copy()
        split_df.to_csv(output_dir / f"pairs_{split}.csv", index=False)
    manifest["splits"] = splits
    manifest["rows"] = {"canonical": int(len(canonical_all)), "pairs": int(len(pairs_all))}
    with open(output_dir / "manifest.json", "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    if args.plots:
        save_histograms(canonical_all, pairs_all, output_dir / "sanity")
    return manifest


def build_argparser():
    parser = argparse.ArgumentParser(description="Prepare high-altitude drone VO pairs for XVO fine-tuning.")
    parser.add_argument("--bundle-dir", default="../share_bundle")
    parser.add_argument("--output-dir", default="drone_finetune_data")
    parser.add_argument("--pose-source", choices=["auto", "poses", "trajectory"], default="auto")
    parser.add_argument("--timestamp-tolerance-s", type=float, default=0.02)
    parser.add_argument("--strides", type=int, nargs="+", default=[1])
    parser.add_argument("--train-ratio", type=float, default=0.8)
    parser.add_argument("--val-ratio", type=float, default=0.1)
    parser.add_argument("--seed", type=int, default=2023)
    parser.add_argument("--plots", action="store_true")
    return parser


if __name__ == "__main__":
    manifest = prepare_drone_bundle(build_argparser().parse_args())
    print(json.dumps(manifest, indent=2))
