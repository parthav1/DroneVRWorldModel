using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DroneVR.Simulation
{
    public class BatchExperimentRunner : MonoBehaviour
    {
        [Serializable]
        public class BatchRun
        {
            public string label = "orbit";
            public PathPolicy pathPolicy = PathPolicy.Orbit;
            public bool useRandomSeed = true;
            public int randomSeed = 12345;
            public float durationSeconds = 30f;
        }

        [Header("References")]
        [SerializeField] private TaskManager taskManager;
        [SerializeField] private DroneController drone;
        [SerializeField] private PathGenerator pathGenerator;
        [SerializeField] private TrajectoryLogger trajectoryLogger;
        [SerializeField] private FrameCapture frameCapture;
        [SerializeField] private DatasetExporter exporter;

        [Header("Batch")]
        [SerializeField] private bool runBatchOnStart = true;
        [SerializeField] private bool exitPlayModeWhenFinished;
        [SerializeField] private bool quitApplicationWhenFinished;
        [SerializeField] private float secondsBetweenRuns = 1f;
        [SerializeField] private bool generateRandomWalkBatchOnStart;
        [SerializeField] private int randomWalkRunCount = 10;
        [SerializeField] private float randomWalkDurationSeconds = 500f;
        [SerializeField] private List<BatchRun> runs = new List<BatchRun>
        {
            new BatchRun { label = "randomwalk_01", pathPolicy = PathPolicy.RandomWalk, useRandomSeed = true, durationSeconds = 500f }
        };

        private Coroutine batchCoroutine;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            randomWalkRunCount = Mathf.Max(1, randomWalkRunCount);
            randomWalkDurationSeconds = Mathf.Max(0.1f, randomWalkDurationSeconds);
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (taskManager == null)
            {
                taskManager = GetComponent<TaskManager>();
                if (taskManager == null)
                {
                    taskManager = FindFirstObjectByType<TaskManager>();
                }
            }

            if (drone == null)
            {
                drone = FindFirstObjectByType<DroneController>();
            }

            if (pathGenerator == null)
            {
                pathGenerator = GetComponent<PathGenerator>();
                if (pathGenerator == null)
                {
                    pathGenerator = FindFirstObjectByType<PathGenerator>();
                }
            }

            if (trajectoryLogger == null)
            {
                trajectoryLogger = GetComponent<TrajectoryLogger>();
                if (trajectoryLogger == null)
                {
                    trajectoryLogger = FindFirstObjectByType<TrajectoryLogger>();
                }
            }

            if (frameCapture == null)
            {
                frameCapture = GetComponent<FrameCapture>();
                if (frameCapture == null)
                {
                    frameCapture = FindFirstObjectByType<FrameCapture>();
                }
            }

            if (exporter == null)
            {
                exporter = GetComponent<DatasetExporter>();
                if (exporter == null)
                {
                    exporter = FindFirstObjectByType<DatasetExporter>();
                }
            }
        }

        private void Start()
        {
            if (runBatchOnStart)
            {
                StartCoroutine(StartBatchNextFrame());
            }
        }

        private IEnumerator StartBatchNextFrame()
        {
            yield return null;
            StartBatch();
        }

        public void StartBatch()
        {
            if (batchCoroutine != null)
            {
                StopCoroutine(batchCoroutine);
            }

            batchCoroutine = StartCoroutine(RunBatch());
        }

        private IEnumerator RunBatch()
        {
            if (generateRandomWalkBatchOnStart)
            {
                ConfigureRandomWalkBatch(randomWalkRunCount, randomWalkDurationSeconds, true);
            }

            for (int runIndex = 0; runIndex < runs.Count; runIndex++)
            {
                BatchRun run = runs[runIndex];
                if (taskManager == null)
                {
                    yield break;
                }

                int effectiveSeed = ResolveSeed(run, runIndex);
                taskManager.StartRun(run.pathPolicy, effectiveSeed, run.durationSeconds, run.label);
                WriteCurrentRunMetadata(run, effectiveSeed);

                while (taskManager.IsRunning)
                {
                    yield return null;
                }

                if (secondsBetweenRuns > 0f)
                {
                    yield return new WaitForSeconds(secondsBetweenRuns);
                }
            }

            batchCoroutine = null;

            if (quitApplicationWhenFinished)
            {
#if UNITY_EDITOR
                if (Application.isEditor)
                {
                    EditorApplication.ExitPlaymode();
                    EditorApplication.delayCall += () => EditorApplication.Exit(0);
                    yield break;
                }
#endif
                Application.Quit();
            }

#if UNITY_EDITOR
            if (exitPlayModeWhenFinished && Application.isEditor)
            {
                EditorApplication.ExitPlaymode();
            }
#endif
        }

        public void ConfigureRandomWalkBatch(int count, float durationSeconds, bool randomizeSeeds)
        {
            runs.Clear();

            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                runs.Add(new BatchRun
                {
                    label = $"randomwalk_{i + 1:D2}",
                    pathPolicy = PathPolicy.RandomWalk,
                    randomSeed = 0,
                    useRandomSeed = randomizeSeeds,
                    durationSeconds = Mathf.Max(0.1f, durationSeconds)
                });
            }
        }

        private static int ResolveSeed(BatchRun run, int runIndex)
        {
            if (!run.useRandomSeed)
            {
                return run.randomSeed;
            }

            unchecked
            {
                int seed = Guid.NewGuid().GetHashCode();
                seed = (seed * 397) ^ DateTime.UtcNow.Ticks.GetHashCode();
                seed = (seed * 397) ^ Environment.TickCount;
                seed = (seed * 397) ^ runIndex;
                return seed == int.MinValue ? 0 : Mathf.Abs(seed);
            }
        }

        private void WriteCurrentRunMetadata(BatchRun run, int effectiveSeed)
        {
            if (exporter == null || taskManager == null || drone == null || pathGenerator == null)
            {
                return;
            }

            RunMetadata metadata = new RunMetadata
            {
                runId = taskManager.ActiveRunId,
                sceneName = SceneManager.GetActiveScene().name,
                pathPolicy = run.pathPolicy.ToString(),
                environmentRootName = pathGenerator.EnvironmentRoot != null ? pathGenerator.EnvironmentRoot.name : string.Empty,
                randomSeed = effectiveSeed,
                runDurationSeconds = run.durationSeconds,
                sampleFrequencyHz = trajectoryLogger != null ? trajectoryLogger.SampleFrequencyHz : 0f,
                captureSampleStride = frameCapture != null ? frameCapture.CaptureSampleStride : 0,
                maxFramesPerRun = frameCapture != null ? frameCapture.MaxFramesPerRun : 0,
                imageFormat = frameCapture != null ? frameCapture.ImageFormatName : string.Empty,
                imageWidth = frameCapture != null ? frameCapture.ImageWidth : 0,
                imageHeight = frameCapture != null ? frameCapture.ImageHeight : 0,
                createdUtc = DateTime.UtcNow.ToString("o"),
                startPosition = drone.transform.position,
                startRotation = drone.transform.rotation,
                drone = new DroneMetadata
                {
                    maxSpeedMetersPerSecond = drone.MaxSpeedMetersPerSecond,
                    accelerationMetersPerSecondSquared = drone.AccelerationMetersPerSecondSquared,
                    yawRateDegreesPerSecond = drone.YawRateDegreesPerSecond,
                    pitchRateDegreesPerSecond = drone.PitchRateDegreesPerSecond,
                    clampAltitude = drone.ClampAltitude,
                    minAltitude = drone.MinAltitude,
                    maxAltitude = drone.MaxAltitude,
                    maintainGroundClearance = drone.MaintainGroundClearance,
                    groundClearance = drone.GroundClearance
                },
                path = new PathMetadata
                {
                    waypointReachDistance = pathGenerator.WaypointReachDistance,
                    waypointCount = pathGenerator.WaypointCount,
                    horizontalPadding = pathGenerator.HorizontalPadding,
                    maxBoundsSize = pathGenerator.MaxBoundsSize,
                    useCustomHorizontalSize = pathGenerator.UseCustomHorizontalSize,
                    customBoundsWidth = pathGenerator.CustomBoundsWidth,
                    customBoundsLength = pathGenerator.CustomBoundsLength,
                    altitudeRelativeToHighestPoint = pathGenerator.AltitudeRelativeToHighestPoint,
                    clearanceAboveHighestPoint = pathGenerator.ClearanceAboveHighestPoint,
                    minFlightAltitude = pathGenerator.MinFlightAltitude,
                    maxFlightAltitude = pathGenerator.MaxFlightAltitude,
                    sweepMin = pathGenerator.SweepMin,
                    sweepMax = pathGenerator.SweepMax,
                    sweepSpacing = pathGenerator.SweepSpacing,
                    orbitCenter = pathGenerator.OrbitCenter,
                    orbitRadius = pathGenerator.OrbitRadius,
                    orbitAltitude = pathGenerator.OrbitAltitude,
                    orbitAngularSpeedDegrees = pathGenerator.OrbitAngularSpeedDegrees,
                    randomBoundsMin = pathGenerator.RandomBoundsMin,
                    randomBoundsMax = pathGenerator.RandomBoundsMax
                }
            };

            exporter.WriteMetadata(metadata);
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(BatchExperimentRunner.BatchRun))]
    public class BatchRunPropertyDrawer : PropertyDrawer
    {
        private const float VerticalSpacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty useRandomSeed = property.FindPropertyRelative("useRandomSeed");
            int lineCount = useRandomSeed != null && !useRandomSeed.boolValue ? 5 : 4;
            return lineCount * EditorGUIUtility.singleLineHeight + (lineCount - 1) * VerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            SerializedProperty labelProperty = property.FindPropertyRelative("label");
            SerializedProperty pathPolicy = property.FindPropertyRelative("pathPolicy");
            SerializedProperty useRandomSeed = property.FindPropertyRelative("useRandomSeed");
            SerializedProperty randomSeed = property.FindPropertyRelative("randomSeed");
            SerializedProperty durationSeconds = property.FindPropertyRelative("durationSeconds");

            EditorGUI.PropertyField(line, labelProperty);
            line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;

            EditorGUI.PropertyField(line, pathPolicy);
            line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;

            GUIContent freshSeedLabel = new GUIContent(
                "Fresh Seed At Runtime",
                "When enabled, this run ignores the saved seed value and generates a new unpredictable seed when the batch starts. The actual seed is still saved in metadata.");
            EditorGUI.PropertyField(line, useRandomSeed, freshSeedLabel);
            line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;

            if (useRandomSeed != null && !useRandomSeed.boolValue)
            {
                EditorGUI.PropertyField(line, randomSeed, new GUIContent("Fixed Seed"));
                line.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
            }

            EditorGUI.PropertyField(line, durationSeconds);

            EditorGUI.EndProperty();
        }
    }
#endif
}
