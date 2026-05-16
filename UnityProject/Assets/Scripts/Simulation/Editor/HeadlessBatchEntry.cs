using System;
using System.IO;
using DroneVR.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DroneVR.Simulation.Editor
{
    public static class HeadlessBatchEntry
    {
        private const string DefaultScenePath = "Assets/Scenes/leeland.unity";
        private const string ActiveKey = "DroneVR.HeadlessBatch.Active";
        private const string SceneQueueKey = "DroneVR.HeadlessBatch.SceneQueue";
        private const string SceneIndexKey = "DroneVR.HeadlessBatch.SceneIndex";
        private const string QuitWhenFinishedKey = "DroneVR.HeadlessBatch.QuitWhenFinished";
        private const string RandomWalkRunsKey = "DroneVR.HeadlessBatch.RandomWalkRuns";
        private const string RunDurationKey = "DroneVR.HeadlessBatch.RunDurationSeconds";
        private const string SampleFrequencyKey = "DroneVR.HeadlessBatch.SampleFrequencyHz";
        private const string CaptureStrideKey = "DroneVR.HeadlessBatch.CaptureSampleStride";
        private const string MaxFramesKey = "DroneVR.HeadlessBatch.MaxFramesPerRun";

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            RegisterPlayModeCallback();
        }

        public static void Run()
        {
            string[] scenePaths = GetScenePaths();

            if (scenePaths.Length == 0)
            {
                Debug.LogError("Headless batch failed. No scenes were provided.");
                EditorApplication.Exit(1);
                return;
            }

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(SceneQueueKey, string.Join("\n", scenePaths));
            SessionState.SetInt(SceneIndexKey, 0);
            SessionState.SetBool(QuitWhenFinishedKey, GetBoolArgument("-quitWhenFinished", true));
            SessionState.SetInt(RandomWalkRunsKey, GetIntArgument("-randomWalkRuns", 10));
            SessionState.SetFloat(RunDurationKey, GetFloatArgument("-runDurationSeconds", 500f));
            SessionState.SetFloat(SampleFrequencyKey, GetFloatArgument("-sampleFrequencyHz", 20f));
            SessionState.SetInt(CaptureStrideKey, GetIntArgument("-captureSampleStride", 20));
            SessionState.SetInt(MaxFramesKey, GetIntArgument("-maxFramesPerRun", 500));

            RegisterPlayModeCallback();
            RunCurrentScene();
        }

        private static void RegisterPlayModeCallback()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private static void RunCurrentScene()
        {
            string[] sceneQueue = GetPersistedSceneQueue();
            int sceneIndex = SessionState.GetInt(SceneIndexKey, 0);
            if (!SessionState.GetBool(ActiveKey, false) || sceneQueue.Length == 0)
            {
                return;
            }

            if (sceneIndex < 0 || sceneIndex >= sceneQueue.Length)
            {
                FinishBatch();
                return;
            }

            string scenePath = sceneQueue[sceneIndex];
            if (!File.Exists(scenePath))
            {
                Debug.LogError($"Headless batch failed. Scene not found: {scenePath}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"Opening scene {sceneIndex + 1}/{sceneQueue.Length}: {scenePath}");
            EditorSceneManager.OpenScene(scenePath);

            BatchExperimentRunner batchRunner = UnityEngine.Object.FindFirstObjectByType<BatchExperimentRunner>();
            if (batchRunner == null)
            {
                Debug.LogError("Headless batch failed. No BatchExperimentRunner was found in the scene.");
                EditorApplication.Exit(1);
                return;
            }

            ConfigureBatchRunner(batchRunner, false);

            Debug.Log($"Starting headless random-walk batch for scene: {scenePath}. Runs: {SessionState.GetInt(RandomWalkRunsKey, 10)}, duration: {SessionState.GetFloat(RunDurationKey, 500f)}s.");
            EditorApplication.EnterPlaymode();
        }

        private static void ConfigureBatchRunner(BatchExperimentRunner batchRunner, bool quitWhenFinished)
        {
            SerializedObject serializedRunner = new SerializedObject(batchRunner);
            SetBool(serializedRunner, "runBatchOnStart", true);
            SetBool(serializedRunner, "exitPlayModeWhenFinished", true);
            SetBool(serializedRunner, "quitApplicationWhenFinished", quitWhenFinished);
            SetBool(serializedRunner, "generateRandomWalkBatchOnStart", true);
            SetInt(serializedRunner, "randomWalkRunCount", SessionState.GetInt(RandomWalkRunsKey, 10));
            SetFloat(serializedRunner, "randomWalkDurationSeconds", SessionState.GetFloat(RunDurationKey, 500f));
            serializedRunner.ApplyModifiedPropertiesWithoutUndo();

            TaskManager taskManager = UnityEngine.Object.FindFirstObjectByType<TaskManager>();
            if (taskManager != null)
            {
                SerializedObject serializedTaskManager = new SerializedObject(taskManager);
                SetBool(serializedTaskManager, "autoStart", false);
                serializedTaskManager.ApplyModifiedPropertiesWithoutUndo();
            }

            TrajectoryLogger trajectoryLogger = UnityEngine.Object.FindFirstObjectByType<TrajectoryLogger>();
            if (trajectoryLogger != null)
            {
                SerializedObject serializedLogger = new SerializedObject(trajectoryLogger);
                SetFloat(serializedLogger, "sampleFrequencyHz", SessionState.GetFloat(SampleFrequencyKey, 20f));
                serializedLogger.ApplyModifiedPropertiesWithoutUndo();
            }

            FrameCapture frameCapture = UnityEngine.Object.FindFirstObjectByType<FrameCapture>();
            if (frameCapture != null)
            {
                SerializedObject serializedFrameCapture = new SerializedObject(frameCapture);
                SetInt(serializedFrameCapture, "captureSampleStride", SessionState.GetInt(CaptureStrideKey, 20));
                SetInt(serializedFrameCapture, "maxFramesPerRun", SessionState.GetInt(MaxFramesKey, 500));
                serializedFrameCapture.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(ActiveKey, false))
            {
                return;
            }

            string[] sceneQueue = GetPersistedSceneQueue();
            int sceneIndex = SessionState.GetInt(SceneIndexKey, 0);
            sceneIndex++;
            SessionState.SetInt(SceneIndexKey, sceneIndex);
            if (sceneIndex >= sceneQueue.Length)
            {
                FinishBatch();
                return;
            }

            EditorApplication.delayCall += RunCurrentScene;
        }

        private static void FinishBatch()
        {
            bool shouldQuit = SessionState.GetBool(QuitWhenFinishedKey, true);
            ClearBatchState();
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            Debug.Log("Headless multi-scene batch finished.");
            if (shouldQuit)
            {
                EditorApplication.Exit(0);
            }
        }

        private static void ClearBatchState()
        {
            SessionState.EraseBool(ActiveKey);
            SessionState.EraseString(SceneQueueKey);
            SessionState.EraseInt(SceneIndexKey);
            SessionState.EraseBool(QuitWhenFinishedKey);
            SessionState.EraseInt(RandomWalkRunsKey);
            SessionState.EraseFloat(RunDurationKey);
            SessionState.EraseFloat(SampleFrequencyKey);
            SessionState.EraseInt(CaptureStrideKey);
            SessionState.EraseInt(MaxFramesKey);
        }

        private static string[] GetPersistedSceneQueue()
        {
            string sceneQueue = SessionState.GetString(SceneQueueKey, string.Empty);
            return string.IsNullOrWhiteSpace(sceneQueue)
                ? Array.Empty<string>()
                : sceneQueue.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetInt(SerializedObject serializedObject, string propertyName, int value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void SetFloat(SerializedObject serializedObject, string propertyName, float value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static string[] GetScenePaths()
        {
            string sceneList = GetArgument("-sceneList", string.Empty);
            if (!string.IsNullOrWhiteSpace(sceneList))
            {
                string[] split = sceneList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < split.Length; i++)
                {
                    split[i] = split[i].Trim();
                }

                return split;
            }

            string scenePath = GetArgument("-scenePath", DefaultScenePath);
            return new[] { scenePath };
        }

        private static string GetArgument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return fallback;
        }

        private static int GetIntArgument(string name, int fallback)
        {
            string value = GetArgument(name, fallback.ToString());
            return int.TryParse(value, out int parsed) ? parsed : fallback;
        }

        private static float GetFloatArgument(string name, float fallback)
        {
            string value = GetArgument(name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;
        }

        private static bool GetBoolArgument(string name, bool fallback)
        {
            string value = GetArgument(name, fallback ? "true" : "false");
            return bool.TryParse(value, out bool parsed) ? parsed : fallback;
        }
    }
}
