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
        private static string[] sceneQueue;
        private static int sceneIndex;
        private static bool quitWhenAllScenesFinish;
        private static int randomWalkRunCount;
        private static float randomWalkDurationSeconds;
        private static float sampleFrequencyHz;
        private static int captureSampleStride;
        private static int maxFramesPerRun;

        public static void Run()
        {
            string[] scenePaths = GetScenePaths();
            quitWhenAllScenesFinish = GetBoolArgument("-quitWhenFinished", true);
            randomWalkRunCount = GetIntArgument("-randomWalkRuns", 10);
            randomWalkDurationSeconds = GetFloatArgument("-runDurationSeconds", 500f);
            sampleFrequencyHz = GetFloatArgument("-sampleFrequencyHz", 20f);
            captureSampleStride = GetIntArgument("-captureSampleStride", 20);
            maxFramesPerRun = GetIntArgument("-maxFramesPerRun", 500);

            if (scenePaths.Length == 0)
            {
                Debug.LogError("Headless batch failed. No scenes were provided.");
                EditorApplication.Exit(1);
                return;
            }

            sceneQueue = scenePaths;
            sceneIndex = 0;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            RunCurrentScene();
        }

        private static void RunCurrentScene()
        {
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

            Debug.Log($"Starting headless random-walk batch for scene: {scenePath}. Runs: {randomWalkRunCount}, duration: {randomWalkDurationSeconds}s.");
            EditorApplication.EnterPlaymode();
        }

        private static void ConfigureBatchRunner(BatchExperimentRunner batchRunner, bool quitWhenFinished)
        {
            SerializedObject serializedRunner = new SerializedObject(batchRunner);
            SetBool(serializedRunner, "runBatchOnStart", true);
            SetBool(serializedRunner, "exitPlayModeWhenFinished", true);
            SetBool(serializedRunner, "quitApplicationWhenFinished", quitWhenFinished);
            SetBool(serializedRunner, "generateRandomWalkBatchOnStart", true);
            SetInt(serializedRunner, "randomWalkRunCount", randomWalkRunCount);
            SetFloat(serializedRunner, "randomWalkDurationSeconds", randomWalkDurationSeconds);
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
                SetFloat(serializedLogger, "sampleFrequencyHz", sampleFrequencyHz);
                serializedLogger.ApplyModifiedPropertiesWithoutUndo();
            }

            FrameCapture frameCapture = UnityEngine.Object.FindFirstObjectByType<FrameCapture>();
            if (frameCapture != null)
            {
                SerializedObject serializedFrameCapture = new SerializedObject(frameCapture);
                SetInt(serializedFrameCapture, "captureSampleStride", captureSampleStride);
                SetInt(serializedFrameCapture, "maxFramesPerRun", maxFramesPerRun);
                serializedFrameCapture.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || sceneQueue == null)
            {
                return;
            }

            sceneIndex++;
            if (sceneIndex >= sceneQueue.Length)
            {
                EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
                Debug.Log("Headless multi-scene batch finished.");
                if (quitWhenAllScenesFinish)
                {
                    EditorApplication.Exit(0);
                }

                return;
            }

            EditorApplication.delayCall += RunCurrentScene;
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
