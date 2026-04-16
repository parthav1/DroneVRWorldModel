using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DroneVR.Experiment
{
    public class ExperimentTaskManager : MonoBehaviour
    {
        [Serializable]
        public class NavigationTask
        {
            public string taskId = "task-01";
            public string promptOverride;
            public NavigationTarget target;
            public Transform spawnOverride;
            public float completionRadiusOverride = -1f;
        }

        [Header("References")]
        [SerializeField] private DroneRigController droneRig;
        [SerializeField] private ExperimentLogger logger;
        [SerializeField] private Transform subjectTransform;

        [Header("Tasks")]
        [SerializeField] private List<NavigationTask> tasks = new List<NavigationTask>();
        [SerializeField] private bool autoStartFirstTask = true;
        [SerializeField] private bool loopTasks;
        [SerializeField] private KeyCode startTaskKey = KeyCode.Return;
        [SerializeField] private KeyCode nextTaskKey = KeyCode.N;
        [SerializeField] private KeyCode restartTaskKey = KeyCode.T;

        [Header("HUD")]
        [SerializeField] private bool showOverlay = true;

        private int currentTaskIndex = -1;
        private float taskStartTime;
        private string taskStartUtc;
        private Vector3 taskStartPosition;
        private Vector3 previousSamplePosition;
        private float accumulatedPathLength;
        private bool taskRunning;
        private string statusMessage = "Press Enter to begin.";

        public NavigationTask CurrentTask =>
            currentTaskIndex >= 0 && currentTaskIndex < tasks.Count ? tasks[currentTaskIndex] : null;

        private void Awake()
        {
            if (droneRig == null)
            {
                droneRig = FindFirstObjectByType<DroneRigController>();
            }

            if (logger == null)
            {
                logger = FindFirstObjectByType<ExperimentLogger>();
            }

            if (subjectTransform == null && droneRig != null)
            {
                subjectTransform = droneRig.transform;
            }
        }

        private void Start()
        {
            if (logger != null)
            {
                logger.InitializeSession();
            }

            if (autoStartFirstTask && tasks.Count > 0)
            {
                StartTask(0);
            }
        }

        private void Update()
        {
            if (logger != null && subjectTransform != null && droneRig != null)
            {
                logger.LogPose(subjectTransform, droneRig.ActiveModeLabel);
            }

            if (WasKeyPressed(startTaskKey) && !taskRunning && tasks.Count > 0)
            {
                StartTask(Mathf.Max(currentTaskIndex, 0));
            }

            if (WasKeyPressed(nextTaskKey))
            {
                AdvanceToNextTask();
            }

            if (WasKeyPressed(restartTaskKey) && CurrentTask != null)
            {
                StartTask(currentTaskIndex);
            }

            if (!taskRunning || CurrentTask == null || subjectTransform == null)
            {
                return;
            }

            accumulatedPathLength += Vector3.Distance(previousSamplePosition, subjectTransform.position);
            previousSamplePosition = subjectTransform.position;

            float? radiusOverride = CurrentTask.completionRadiusOverride > 0f ? CurrentTask.completionRadiusOverride : null;
            if (CurrentTask.target != null && CurrentTask.target.IsReachedBy(subjectTransform.position, radiusOverride))
            {
                CompleteCurrentTask(true);
            }
        }

        public void StartTask(int taskIndex)
        {
            if (taskIndex < 0 || taskIndex >= tasks.Count)
            {
                statusMessage = "No valid task is selected.";
                return;
            }

            SetAllMarkers(false);
            currentTaskIndex = taskIndex;
            NavigationTask task = tasks[currentTaskIndex];

            if (droneRig != null)
            {
                droneRig.ResetToTransform(task.spawnOverride);
            }

            if (task.target != null)
            {
                task.target.SetMarkerVisible(true);
            }

            taskStartTime = Time.time;
            taskStartUtc = DateTime.UtcNow.ToString("o");
            taskStartPosition = subjectTransform != null ? subjectTransform.position : Vector3.zero;
            previousSamplePosition = taskStartPosition;
            accumulatedPathLength = 0f;
            taskRunning = true;

            string prompt = task.target != null ? task.target.Prompt : "Navigate to the next location";
            if (!string.IsNullOrWhiteSpace(task.promptOverride))
            {
                prompt = task.promptOverride;
            }

            statusMessage = prompt;
            if (logger != null)
            {
                logger.SetCurrentTask(task.taskId);
            }
        }

        public void AdvanceToNextTask()
        {
            if (tasks.Count == 0)
            {
                statusMessage = "No tasks configured.";
                return;
            }

            int nextIndex = currentTaskIndex + 1;
            if (nextIndex >= tasks.Count)
            {
                if (!loopTasks)
                {
                    taskRunning = false;
                    statusMessage = "All tasks completed.";
                    SetAllMarkers(false);
                    if (logger != null)
                    {
                        logger.SetCurrentTask("idle");
                    }

                    return;
                }

                nextIndex = 0;
            }

            StartTask(nextIndex);
        }

        public void CompleteCurrentTask(bool completed)
        {
            if (!taskRunning || CurrentTask == null)
            {
                return;
            }

            taskRunning = false;
            SetAllMarkers(false);

            float duration = Time.time - taskStartTime;
            float straightLineDistance = subjectTransform != null
                ? Vector3.Distance(taskStartPosition, CurrentTask.target != null ? CurrentTask.target.transform.position : subjectTransform.position)
                : 0f;

            TaskResultRecord result = new TaskResultRecord
            {
                taskId = CurrentTask.taskId,
                targetId = CurrentTask.target != null ? CurrentTask.target.TargetId : "none",
                mode = droneRig != null ? droneRig.ActiveModeLabel : "Unknown",
                startTimeUtc = taskStartUtc,
                endTimeUtc = DateTime.UtcNow.ToString("o"),
                durationSeconds = duration,
                pathLengthMeters = accumulatedPathLength,
                straightLineDistanceMeters = straightLineDistance,
                efficiencyRatio = accumulatedPathLength > 0.0001f ? straightLineDistance / accumulatedPathLength : 0f,
                completed = completed
            };

            if (logger != null)
            {
                logger.LogTaskResult(result);
                logger.SetCurrentTask("idle");
            }

            statusMessage = completed
                ? $"Completed {CurrentTask.taskId} in {duration:F1}s. Press N for the next task."
                : $"Stopped {CurrentTask.taskId}. Press T to retry.";
        }

        private void SetAllMarkers(bool isVisible)
        {
            foreach (NavigationTask task in tasks)
            {
                if (task.target != null)
                {
                    task.target.SetMarkerVisible(isVisible);
                }
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
                case KeyCode.N:
                    key = Key.N;
                    break;
                case KeyCode.T:
                    key = Key.T;
                    break;
                default:
                    key = Key.None;
                    break;
            }

            return key != Key.None && Keyboard.current[key].wasPressedThisFrame;
        }

        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            const int width = 460;
            const int height = 120;
            GUILayout.BeginArea(new Rect(20f, 20f, width, height), GUI.skin.box);
            GUILayout.Label(CurrentTask != null ? $"Task: {CurrentTask.taskId}" : "Task: none");
            GUILayout.Label(statusMessage);
            GUILayout.Label("Controls: point with crosshair, W/S move, A/D strafe, Q/E vertical, R reset, T retry, N next.");

            if (taskRunning)
            {
                GUILayout.Label($"Timer: {Time.time - taskStartTime:F1}s");
            }

            GUILayout.EndArea();
        }
    }
}
