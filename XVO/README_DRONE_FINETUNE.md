# Drone Supervised Fine-Tuning

This pipeline fine-tunes XVO on the high-altitude drone bundle in `../share_bundle`.
Input quaternions are interpreted as `qx,qy,qz,qw`, and labels are always motion from `image_i` to `image_j`:

```text
T_rel = inverse(T_i) @ T_j
```

## 1. Environment

Use the normal XVO environment and install the correlation package before training or evaluation:

```bash
cd /fs/nexus-scratch/kjanga/XVO/model/correlation_package
python setup.py install
cd /fs/nexus-scratch/kjanga/XVO
```

The current smoke-test environment can prepare data, but model construction requires `correlation_cuda` from the command above.

## 2. Prepare Drone Pairs

```bash
cd /fs/nexus-scratch/kjanga/XVO
python prepare_drone_data.py \
  --bundle-dir ../share_bundle \
  --output-dir drone_finetune_data \
  --pose-source auto \
  --strides 1 2 4 \
  --timestamp-tolerance-s 0.02 \
  --plots
```

Outputs:

```text
drone_finetune_data/canonical_poses.csv
drone_finetune_data/pairs_all.csv
drone_finetune_data/pairs_train.csv
drone_finetune_data/pairs_val.csv
drone_finetune_data/pairs_test.csv
drone_finetune_data/manifest.json
drone_finetune_data/sanity/pose_histograms.csv or .png
drone_finetune_data/sanity/pair_histograms.csv or .png
```

`canonical_poses.csv` uses:

```text
sequence_id,frame,timestamp_s,x,y,z,qx,qy,qz,qw,source
```

Pair CSVs use:

```text
sequence_id,frame_i,frame_j,timestamp_i,timestamp_j,dt,image_i,image_j,rel_tx,rel_ty,rel_tz,rel_qx,rel_qy,rel_qz,rel_qw,source
```

The split is by `sequence_id`, never by random pair split.

## 3. Fine-Tune

Train the VO decoder/head first, then unfreeze the encoder and transformer at a lower LR:

```bash
python train_drone_finetune.py \
  --prepared-dir drone_finetune_data \
  --pretrained-checkpoint saved_models/YOUR_XVO_CHECKPOINT.pt \
  --output-dir checkpoints/drone_finetune \
  --epochs-head 5 \
  --epochs-full 20 \
  --batch-size 8 \
  --img-h 384 \
  --img-w 640 \
  --lr-head 1e-4 \
  --lr-decoder-full 5e-5 \
  --lr-encoder 1e-5
```

If starting without an XVO checkpoint, omit `--pretrained-checkpoint`.

Enable Weights & Biases logging:

```bash
python train_drone_finetune.py \
  --prepared-dir drone_finetune_data \
  --pretrained-checkpoint saved_models/YOUR_XVO_CHECKPOINT.pt \
  --output-dir checkpoints/drone_finetune \
  --epochs-head 5 \
  --epochs-full 20 \
  --batch-size 8 \
  --wandb \
  --wandb-project xvo-drone-finetune \
  --wandb-run-name drone-xvo-strides-1-2-4 \
  --wandb-tags drone,supervised,xvo
```

For no-network cluster runs, use `--wandb --wandb-mode offline` and sync later with `wandb sync`.
Add `--wandb-log-checkpoints` to upload best checkpoints as W&B model artifacts.

Tiny-batch overfit sanity check:

```bash
python train_drone_finetune.py \
  --prepared-dir drone_finetune_data \
  --output-dir checkpoints/drone_tiny_overfit \
  --epochs-head 20 \
  --epochs-full 0 \
  --batch-size 4 \
  --tiny-overfit-batches 2
```

Loss terms are translation Smooth L1, quaternion geodesic rotation loss, and log-scale loss.

## 4. Evaluate

```bash
python eval_drone.py \
  --pair-csv drone_finetune_data/pairs_test.csv \
  --checkpoint checkpoints/drone_finetune/best.pt \
  --output-dir drone_eval \
  --batch-size 8
```

Outputs:

```text
drone_eval/predictions.csv
drone_eval/metrics_overall.json
drone_eval/metrics_by_sequence.csv
drone_eval/metrics_by_stride.csv
drone_eval/worst_pairs.csv
drone_eval/trajectories/*_trajectory.csv or .png
```

Metrics include translation error, rotation error in degrees, and scale error. `worst_pairs.csv` exports the highest-error image pairs for inspection.
