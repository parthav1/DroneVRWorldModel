using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DroneVR.Experiment
{
    public class ExperimentLogger : MonoBehaviour
    {
        [SerializeField] private string sessionPrefix = "navigation-study";
        [SerializeField] private float sampleIntervalSeconds = 0.2f;
        [SerializeField] private bool flushAfterEveryWrite = true;

        private readonly List<TaskResultRecord> taskResults = new List<TaskResultRecord>();
        private StreamWriter sampleWriter;
        private StreamWriter taskWriter;
        private string sessionFolderPath;
        private string sessionId;
        private string activeTaskId = "idle";
        private float lastSampleTime = float.MinValue;
        private bool sessionInitialized;

        public string SessionId => sessionId;
        public string SessionFolderPath => sessionFolderPath;
        public float SampleIntervalSeconds => sampleIntervalSeconds;

        private void Awake()
        {
            InitializeSession();
        }

        private void OnDestroy()
        {
            CloseWriters();
        }

        private void OnApplicationQuit()
        {
            CloseWriters();
        }

        public void InitializeSession()
        {
            if (sessionInitialized)
            {
                return;
            }

            sessionId = $"{sessionPrefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            sessionFolderPath = Path.Combine(Application.persistentDataPath, "ExperimentLogs", sessionId);
            Directory.CreateDirectory(sessionFolderPath);

            sampleWriter = new StreamWriter(Path.Combine(sessionFolderPath, "trajectory_samples.csv"), false);
            sampleWriter.WriteLine("iso_timestamp,session_id,task_id,mode,pos_x,pos_y,pos_z,rot_x,rot_y,rot_z");

            taskWriter = new StreamWriter(Path.Combine(sessionFolderPath, "task_results.csv"), false);
            taskWriter.WriteLine("iso_timestamp,session_id,task_id,target_id,mode,start_time_utc,end_time_utc,duration_seconds,path_length_m,straight_line_distance_m,efficiency_ratio,completed");

            sessionInitialized = true;
            WriteSessionMetadata();
        }

        public void SetCurrentTask(string taskId)
        {
            activeTaskId = string.IsNullOrWhiteSpace(taskId) ? "idle" : taskId;
        }

        public void LogPose(Transform subject, string modeLabel)
        {
            if (!sessionInitialized || subject == null)
            {
                return;
            }

            if (Time.unscaledTime - lastSampleTime < sampleIntervalSeconds)
            {
                return;
            }

            lastSampleTime = Time.unscaledTime;
            Vector3 position = subject.position;
            Vector3 rotation = subject.eulerAngles;
            string line = string.Join(",",
                GetUtcTimestamp(),
                sessionId,
                Escape(activeTaskId),
                Escape(modeLabel),
                FormatFloat(position.x),
                FormatFloat(position.y),
                FormatFloat(position.z),
                FormatFloat(rotation.x),
                FormatFloat(rotation.y),
                FormatFloat(rotation.z));

            sampleWriter.WriteLine(line);
            FlushIfNeeded(sampleWriter);
        }

        public void LogTaskResult(TaskResultRecord result)
        {
            if (!sessionInitialized)
            {
                return;
            }

            taskResults.Add(result);

            string line = string.Join(",",
                GetUtcTimestamp(),
                sessionId,
                Escape(result.taskId),
                Escape(result.targetId),
                Escape(result.mode),
                Escape(result.startTimeUtc),
                Escape(result.endTimeUtc),
                FormatFloat(result.durationSeconds),
                FormatFloat(result.pathLengthMeters),
                FormatFloat(result.straightLineDistanceMeters),
                FormatFloat(result.efficiencyRatio),
                result.completed ? "true" : "false");

            taskWriter.WriteLine(line);
            FlushIfNeeded(taskWriter);
            WriteSessionMetadata();
        }

        private void WriteSessionMetadata()
        {
            if (!sessionInitialized)
            {
                return;
            }

            SessionMetadata metadata = new SessionMetadata
            {
                sessionId = sessionId,
                createdUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                persistentDataPath = Application.persistentDataPath,
                taskResults = taskResults.ToArray()
            };

            string json = JsonUtility.ToJson(metadata, true);
            File.WriteAllText(Path.Combine(sessionFolderPath, "session_summary.json"), json);
        }

        private void FlushIfNeeded(StreamWriter writer)
        {
            if (flushAfterEveryWrite)
            {
                writer.Flush();
            }
        }

        private void CloseWriters()
        {
            if (!sessionInitialized)
            {
                return;
            }

            FlushAndDispose(sampleWriter);
            FlushAndDispose(taskWriter);
            sampleWriter = null;
            taskWriter = null;
            sessionInitialized = false;
        }

        private static void FlushAndDispose(StreamWriter writer)
        {
            if (writer == null)
            {
                return;
            }

            writer.Flush();
            writer.Dispose();
        }

        private static string GetUtcTimestamp()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace(",", "_");
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }

        [Serializable]
        private class SessionMetadata
        {
            public string sessionId;
            public string createdUtc;
            public string persistentDataPath;
            public TaskResultRecord[] taskResults;
        }
    }

    [Serializable]
    public struct TaskResultRecord
    {
        public string taskId;
        public string targetId;
        public string mode;
        public string startTimeUtc;
        public string endTimeUtc;
        public float durationSeconds;
        public float pathLengthMeters;
        public float straightLineDistanceMeters;
        public float efficiencyRatio;
        public bool completed;
    }
}
