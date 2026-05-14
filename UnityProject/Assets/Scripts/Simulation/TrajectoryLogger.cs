using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DroneVR.Simulation
{
    [Serializable]
    public struct DroneSample
    {
        public int sampleIndex;
        public double timestampSeconds;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
    }

    public class TrajectoryLogger : MonoBehaviour
    {
        [SerializeField] private DroneController drone;
        [SerializeField] private DatasetExporter exporter;
        [SerializeField] private float sampleFrequencyHz = 20f;
        [SerializeField] private string fileName = "trajectory.csv";
        [SerializeField] private bool flushEverySample = true;

        private StreamWriter writer;
        private string sessionDirectory;
        private double runStartTime;
        private double nextSampleTime;
        private double previousTimestamp;
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private int sampleIndex;
        private bool isLogging;

        public event Action<DroneSample> SampleRecorded;

        public bool IsLogging => isLogging;
        public string SessionDirectory => sessionDirectory;
        public float SampleFrequencyHz => sampleFrequencyHz;

        private void Awake()
        {
            if (drone == null)
            {
                drone = FindFirstObjectByType<DroneController>();
            }

            if (exporter == null)
            {
                exporter = FindFirstObjectByType<DatasetExporter>();
            }
        }

        private void Update()
        {
            if (!isLogging || drone == null)
            {
                return;
            }

            double now = Time.timeAsDouble;
            while (now + 0.000001d >= nextSampleTime)
            {
                RecordSample(nextSampleTime);
                nextSampleTime += 1d / Mathf.Max(1f, sampleFrequencyHz);
            }
        }

        private void OnDestroy()
        {
            StopLogging();
        }

        public void StartLogging(string runId)
        {
            StopLogging();

            if (exporter != null)
            {
                exporter.InitializeSession(runId);
                sessionDirectory = exporter.SessionDirectory;
            }
            else
            {
                sessionDirectory = Path.Combine(Application.persistentDataPath, "DroneDatasets", Sanitize(runId));
                Directory.CreateDirectory(sessionDirectory);
            }

            writer = new StreamWriter(Path.Combine(sessionDirectory, fileName), false);
            writer.WriteLine("sample_index,timestamp_s,pos_x,pos_y,pos_z,rot_x,rot_y,rot_z,rot_w,vel_x,vel_y,vel_z,ang_vel_x,ang_vel_y,ang_vel_z");

            runStartTime = Time.timeAsDouble;
            nextSampleTime = runStartTime;
            previousTimestamp = 0d;
            previousPosition = drone.transform.position;
            previousRotation = drone.transform.rotation;
            sampleIndex = 0;
            isLogging = true;
        }

        public void StopLogging()
        {
            isLogging = false;

            if (writer == null)
            {
                return;
            }

            writer.Flush();
            writer.Dispose();
            writer = null;
        }

        private void RecordSample(double sampleTime)
        {
            double timestamp = sampleTime - runStartTime;
            float deltaTime = sampleIndex == 0 ? 0f : Mathf.Max(0.0001f, (float)(timestamp - previousTimestamp));

            Vector3 position = drone.transform.position;
            Quaternion rotation = drone.transform.rotation;
            Vector3 linearVelocity = sampleIndex == 0 ? Vector3.zero : (position - previousPosition) / deltaTime;
            Vector3 angularVelocity = sampleIndex == 0 ? Vector3.zero : EstimateAngularVelocity(previousRotation, rotation, deltaTime);

            DroneSample sample = new DroneSample
            {
                sampleIndex = sampleIndex,
                timestampSeconds = timestamp,
                position = position,
                rotation = rotation,
                linearVelocity = linearVelocity,
                angularVelocity = angularVelocity
            };

            writer.WriteLine(FormatSample(sample));
            if (flushEverySample)
            {
                writer.Flush();
            }

            SampleRecorded?.Invoke(sample);
            previousTimestamp = timestamp;
            previousPosition = position;
            previousRotation = rotation;
            sampleIndex++;
        }

        private static Vector3 EstimateAngularVelocity(Quaternion previous, Quaternion current, float deltaTime)
        {
            Quaternion delta = current * Quaternion.Inverse(previous);
            delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);
            if (angleDegrees > 180f)
            {
                angleDegrees -= 360f;
            }

            if (axis.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            return axis.normalized * (angleDegrees * Mathf.Deg2Rad / deltaTime);
        }

        private static string FormatSample(DroneSample sample)
        {
            return string.Join(",",
                sample.sampleIndex.ToString(CultureInfo.InvariantCulture),
                sample.timestampSeconds.ToString("F6", CultureInfo.InvariantCulture),
                sample.position.x.ToString("F6", CultureInfo.InvariantCulture),
                sample.position.y.ToString("F6", CultureInfo.InvariantCulture),
                sample.position.z.ToString("F6", CultureInfo.InvariantCulture),
                sample.rotation.x.ToString("F8", CultureInfo.InvariantCulture),
                sample.rotation.y.ToString("F8", CultureInfo.InvariantCulture),
                sample.rotation.z.ToString("F8", CultureInfo.InvariantCulture),
                sample.rotation.w.ToString("F8", CultureInfo.InvariantCulture),
                sample.linearVelocity.x.ToString("F6", CultureInfo.InvariantCulture),
                sample.linearVelocity.y.ToString("F6", CultureInfo.InvariantCulture),
                sample.linearVelocity.z.ToString("F6", CultureInfo.InvariantCulture),
                sample.angularVelocity.x.ToString("F6", CultureInfo.InvariantCulture),
                sample.angularVelocity.y.ToString("F6", CultureInfo.InvariantCulture),
                sample.angularVelocity.z.ToString("F6", CultureInfo.InvariantCulture));
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "run" : value;
        }
    }
}
