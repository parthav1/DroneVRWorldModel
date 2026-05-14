using System.Globalization;
using System.IO;
using UnityEngine;

namespace DroneVR.Simulation
{
    public class DatasetExporter : MonoBehaviour
    {
        [SerializeField] private string datasetRootFolder = "DroneDatasets";
        [SerializeField] private string posesFileName = "poses.csv";
        [SerializeField] private bool flushEveryPose = true;

        private StreamWriter posesWriter;
        private string sessionDirectory;
        private string framesDirectory;
        private string activeRunId;
        private bool isInitialized;

        public string SessionDirectory => sessionDirectory;
        public string FramesDirectory => framesDirectory;

        private void OnDestroy()
        {
            Close();
        }

        public void InitializeSession(string runId)
        {
            string safeRunId = Sanitize(runId);
            if (isInitialized)
            {
                if (activeRunId == safeRunId)
                {
                    return;
                }

                Close();
            }

            activeRunId = safeRunId;
            sessionDirectory = Path.Combine(Application.persistentDataPath, datasetRootFolder, safeRunId);
            framesDirectory = Path.Combine(sessionDirectory, "frames");
            Directory.CreateDirectory(framesDirectory);

            posesWriter = new StreamWriter(Path.Combine(sessionDirectory, posesFileName), false);
            posesWriter.WriteLine("frame,timestamp_s,tx,ty,tz,qx,qy,qz,qw");
            isInitialized = true;
        }

        public void SaveFrameAndPose(string frameFileName, byte[] pngBytes, DroneSample sample)
        {
            if (!isInitialized)
            {
                InitializeSession("run");
            }

            File.WriteAllBytes(Path.Combine(framesDirectory, frameFileName), pngBytes);
            RecordPose(frameFileName, sample);
        }

        public void WriteMetadata(RunMetadata metadata)
        {
            if (!isInitialized)
            {
                InitializeSession(metadata != null ? metadata.runId : "run");
            }

            string json = JsonUtility.ToJson(metadata, true);
            File.WriteAllText(Path.Combine(sessionDirectory, "metadata.json"), json);
        }

        public void RecordPose(string frameFileName, DroneSample sample)
        {
            if (!isInitialized)
            {
                InitializeSession("run");
            }

            posesWriter.WriteLine(string.Join(",",
                frameFileName,
                sample.timestampSeconds.ToString("F6", CultureInfo.InvariantCulture),
                sample.position.x.ToString("F6", CultureInfo.InvariantCulture),
                sample.position.y.ToString("F6", CultureInfo.InvariantCulture),
                sample.position.z.ToString("F6", CultureInfo.InvariantCulture),
                sample.rotation.x.ToString("F8", CultureInfo.InvariantCulture),
                sample.rotation.y.ToString("F8", CultureInfo.InvariantCulture),
                sample.rotation.z.ToString("F8", CultureInfo.InvariantCulture),
                sample.rotation.w.ToString("F8", CultureInfo.InvariantCulture)));

            if (flushEveryPose)
            {
                posesWriter.Flush();
            }
        }

        public void Close()
        {
            if (!isInitialized)
            {
                return;
            }

            posesWriter.Flush();
            posesWriter.Dispose();
            posesWriter = null;
            activeRunId = null;
            isInitialized = false;
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

    [System.Serializable]
    public class RunMetadata
    {
        public string runId;
        public string sceneName;
        public string pathPolicy;
        public string environmentRootName;
        public int randomSeed;
        public float runDurationSeconds;
        public float sampleFrequencyHz;
        public int captureSampleStride;
        public int maxFramesPerRun;
        public string imageFormat;
        public int imageWidth;
        public int imageHeight;
        public string createdUtc;
        public Vector3 startPosition;
        public Quaternion startRotation;
        public DroneMetadata drone;
        public PathMetadata path;
    }

    [System.Serializable]
    public class DroneMetadata
    {
        public float maxSpeedMetersPerSecond;
        public float accelerationMetersPerSecondSquared;
        public float yawRateDegreesPerSecond;
        public float pitchRateDegreesPerSecond;
        public bool clampAltitude;
        public float minAltitude;
        public float maxAltitude;
        public bool maintainGroundClearance;
        public float groundClearance;
    }

    [System.Serializable]
    public class PathMetadata
    {
        public float waypointReachDistance;
        public int waypointCount;
        public float horizontalPadding;
        public float maxBoundsSize;
        public bool useCustomHorizontalSize;
        public float customBoundsWidth;
        public float customBoundsLength;
        public bool altitudeRelativeToHighestPoint;
        public float clearanceAboveHighestPoint;
        public float minFlightAltitude;
        public float maxFlightAltitude;
        public Vector3 sweepMin;
        public Vector3 sweepMax;
        public float sweepSpacing;
        public Vector3 orbitCenter;
        public float orbitRadius;
        public float orbitAltitude;
        public float orbitAngularSpeedDegrees;
        public Vector3 randomBoundsMin;
        public Vector3 randomBoundsMax;
    }
}
