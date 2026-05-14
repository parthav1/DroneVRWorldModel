using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DroneVR.Simulation
{
    public class TaskManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DroneController drone;
        [SerializeField] private PathGenerator pathGenerator;
        [SerializeField] private TrajectoryLogger trajectoryLogger;
        [SerializeField] private DatasetExporter exporter;

        [Header("Run")]
        [SerializeField] private PathPolicy pathPolicy = PathPolicy.Waypoints;
        [SerializeField] private int randomSeed = 12345;
        [SerializeField] private Transform startPose;
        [SerializeField] private bool autoStart;
        [SerializeField] private float runDurationSeconds = 60f;

        [Header("Controls")]
        [SerializeField] private KeyCode startRunKey = KeyCode.Return;
        [SerializeField] private KeyCode stopRunKey = KeyCode.Backspace;
        [SerializeField] private KeyCode resetDroneKey = KeyCode.R;
        [SerializeField] private bool showStatusOverlay = true;

        private float runStartTime;
        private string activeRunId;
        private bool isRunning;

        public bool IsRunning => isRunning;
        public string ActiveRunId => activeRunId;
        public PathPolicy CurrentPathPolicy => pathPolicy;
        public int RandomSeed => randomSeed;
        public float RunDurationSeconds => runDurationSeconds;

        private void Awake()
        {
            if (drone == null)
            {
                drone = FindFirstObjectByType<DroneController>();
            }

            if (pathGenerator == null)
            {
                pathGenerator = FindFirstObjectByType<PathGenerator>();
            }

            if (trajectoryLogger == null)
            {
                trajectoryLogger = FindFirstObjectByType<TrajectoryLogger>();
            }

            if (exporter == null)
            {
                exporter = FindFirstObjectByType<DatasetExporter>();
            }
        }

        private void Start()
        {
            ResetDrone();

            if (autoStart)
            {
                StartRun();
            }
        }

        private void Update()
        {
            if (WasKeyPressed(startRunKey) && !isRunning)
            {
                StartRun();
            }

            if (WasKeyPressed(stopRunKey) && isRunning)
            {
                StopRun();
            }

            if (WasKeyPressed(resetDroneKey) && !isRunning)
            {
                ResetDrone();
            }

            if (!isRunning)
            {
                return;
            }

            if (runDurationSeconds > 0f && Time.time - runStartTime >= runDurationSeconds)
            {
                StopRun();
            }
        }

        public void StartRun()
        {
            StartRunWithLabel(pathPolicy.ToString());
        }

        public void StartRunWithLabel(string runLabel)
        {
            if (isRunning)
            {
                StopRun();
            }

            ResetDrone();

            activeRunId = $"{SanitizeRunLabel(runLabel)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_seed{randomSeed}";

            if (exporter != null)
            {
                exporter.InitializeSession(activeRunId);
            }

            if (trajectoryLogger != null)
            {
                trajectoryLogger.StartLogging(activeRunId);
            }

            if (pathGenerator != null)
            {
                pathGenerator.StartPolicy(pathPolicy, randomSeed);
            }

            runStartTime = Time.time;
            isRunning = true;
        }

        public void StartRun(PathPolicy nextPolicy, int seed, float durationSeconds)
        {
            pathPolicy = nextPolicy;
            randomSeed = seed;
            runDurationSeconds = durationSeconds;
            StartRun();
        }

        public void StartRun(PathPolicy nextPolicy, int seed, float durationSeconds, string runLabel)
        {
            pathPolicy = nextPolicy;
            randomSeed = seed;
            runDurationSeconds = durationSeconds;
            StartRunWithLabel(string.IsNullOrWhiteSpace(runLabel) ? nextPolicy.ToString() : runLabel);
        }

        public void StopRun()
        {
            isRunning = false;

            if (pathGenerator != null)
            {
                pathGenerator.StopPolicy();
            }

            if (trajectoryLogger != null)
            {
                trajectoryLogger.StopLogging();
            }

            if (exporter != null)
            {
                exporter.Close();
            }
        }

        public void ResetDrone()
        {
            if (drone == null)
            {
                return;
            }

            if (startPose != null)
            {
                drone.ResetToPose(startPose);
                return;
            }

            if (pathGenerator != null)
            {
                pathGenerator.FitBoundsToEnvironment();
                drone.SetAltitudeLimits(pathGenerator.RandomBoundsMin.y, pathGenerator.RandomBoundsMax.y);
                drone.ResetToPose(pathGenerator.SuggestedStartPosition, pathGenerator.SuggestedStartRotation);
                return;
            }

            drone.ResetToStartPose();
        }

        public void SetPathPolicy(PathPolicy nextPolicy)
        {
            pathPolicy = nextPolicy;
            if (pathGenerator != null)
            {
                pathGenerator.SetPolicy(nextPolicy);
            }
        }

        public void SetRandomSeed(int seed)
        {
            randomSeed = seed;
            if (pathGenerator != null)
            {
                pathGenerator.SetSeed(seed);
            }
        }

        private static bool WasKeyPressed(KeyCode keyCode)
        {
            if (Keyboard.current == null)
            {
                return false;
            }

            Key key;
            switch (keyCode)
            {
                case KeyCode.Return:
                    key = Key.Enter;
                    break;
                case KeyCode.Backspace:
                    key = Key.Backspace;
                    break;
                case KeyCode.R:
                    key = Key.R;
                    break;
                default:
                    key = Key.None;
                    break;
            }

            return key != Key.None && Keyboard.current[key].wasPressedThisFrame;
        }

        private static string SanitizeRunLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return "run";
            }

            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                label = label.Replace(invalid, '_');
            }

            return label.Replace(' ', '_');
        }

        private void OnGUI()
        {
            if (!showStatusOverlay)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(20f, 20f, 420f, 96f), GUI.skin.box);
            GUILayout.Label(isRunning ? $"Running: {activeRunId}" : "Simulation stopped");
            GUILayout.Label($"Policy: {pathPolicy}  Seed: {randomSeed}");
            GUILayout.Label("Enter start  |  Backspace stop  |  R reset");
            GUILayout.EndArea();
        }
    }
}
