# Drone Simulation Data Collection

This folder contains the newer simulator/data collection stack. It is separate from the earlier interactive experiment scripts.

## Components

- `DroneController`
  Moves the drone/camera with configurable speed, acceleration, yaw and pitch rates, and altitude limits.
- `PathGenerator`
  Drives the drone with waypoint, lawnmower, orbit, or constrained random-walk policies. Random-walk runs are reproducible from a seed.
- `TaskManager`
  Resets the drone, starts/stops runs, selects the path policy, and passes the seed into the path generator.
- `TrajectoryLogger`
  Records samples at a fixed frequency, including timestamp, position, quaternion rotation, linear velocity, and angular velocity.
- `FrameCapture`
  Captures camera images whenever `TrajectoryLogger` records a sample.
- `DatasetExporter`
  Writes a VO-style dataset folder with `frames/`, `poses.csv`, and `trajectory.csv`.
- `BatchExperimentRunner`
  Runs a list of trajectory presets back to back so datasets can be generated unattended.

## Scene Setup

1. Put `DroneController` on the camera or on a parent `Drone` object that owns the camera.
2. Create an empty object named `SimulationManager`.
3. Add `PathGenerator`, `TaskManager`, `TrajectoryLogger`, `FrameCapture`, and `DatasetExporter` to `SimulationManager`.
4. Assign references in the Inspector:
   - `TaskManager.drone` points to the drone.
   - `TaskManager.pathGenerator`, `trajectoryLogger`, and `exporter` point to the components on `SimulationManager`.
   - `PathGenerator.drone` points to the drone.
   - `TrajectoryLogger.drone` points to the drone.
   - `FrameCapture.captureCamera` points to the drone camera.
   - `TrajectoryLogger.exporter` points to `DatasetExporter`.
5. Choose a `PathPolicy` and set `randomSeed`.
6. Drag the reconstruction root object, such as `LeelandConstruction`, into `PathGenerator.environmentRoot`.
7. Leave `fitBoundsFromEnvironmentOnStart` enabled to derive lawnmower, orbit, and random-walk bounds from the mesh render bounds.
8. For `Waypoints`, create empty GameObjects and add them to `PathGenerator.waypointTransforms`.
9. For custom bounds, disable `fitBoundsFromEnvironmentOnStart` and edit `sweepMin`, `sweepMax`, `randomBoundsMin`, and `randomBoundsMax` manually.

## Batch Experiments

Add `BatchExperimentRunner` to `SimulationManager` when you want Unity to generate multiple datasets without manual supervision.

1. Turn off `TaskManager.autoStart`.
2. Add or edit rows in `BatchExperimentRunner.runs`.
3. Leave `BatchExperimentRunner.runBatchOnStart` enabled.
4. Press Play.

The runner starts each policy, waits for its duration, closes the dataset, then starts the next run.

## Command-Line Batch

Use the editor entry point when you want to run the batch without sitting in the Unity UI.

```bash
"/Applications/Unity/Hub/Editor/6000.3.13f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode \
  -projectPath "/Users/spicydorito/Documents/CMSC731/DroneVRWorldModel/UnityProject" \
  -scenePath "Assets/Scenes/SampleScene.unity" \
  -executeMethod DroneVR.Simulation.Editor.HeadlessBatchEntry.Run \
  -logFile "/tmp/drone_vo_batch.log"
```

Do not add `-nographics` when capturing frames, because camera rendering may not work without graphics.

## Output

Runs save under:

`Application.persistentDataPath/DroneDatasets/<run-id>/`

The run folder contains:

- `trajectory.csv`
- `poses.csv`
- `metadata.json`
- `frames/frame_000000.png`, `frame_000001.png`, ...

`poses.csv` is intended for VO-style pipelines:

`frame,timestamp_s,tx,ty,tz,qx,qy,qz,qw`
