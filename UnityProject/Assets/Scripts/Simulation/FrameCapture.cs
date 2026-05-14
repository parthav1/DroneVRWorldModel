using System.IO;
using UnityEngine;

namespace DroneVR.Simulation
{
    public class FrameCapture : MonoBehaviour
    {
        private enum CaptureImageFormat
        {
            Png,
            Jpg
        }

        [SerializeField] private Camera captureCamera;
        [SerializeField] private TrajectoryLogger logger;
        [SerializeField] private DatasetExporter exporter;
        [SerializeField] private int imageWidth = 1280;
        [SerializeField] private int imageHeight = 720;
        [SerializeField] private bool captureEveryLoggedSample = true;
        [SerializeField] private int captureSampleStride = 5;
        [SerializeField] private int maxFramesPerRun;
        [SerializeField] private CaptureImageFormat imageFormat = CaptureImageFormat.Jpg;
        [SerializeField, Range(1, 100)] private int jpgQuality = 90;

        private string fallbackFramesDirectory;
        private int capturedFrameCount;

        public int ImageWidth => imageWidth;
        public int ImageHeight => imageHeight;
        public int CaptureSampleStride => captureSampleStride;
        public int MaxFramesPerRun => maxFramesPerRun;
        public string ImageFormatName => imageFormat.ToString();

        private void OnValidate()
        {
            imageWidth = Mathf.Max(1, imageWidth);
            imageHeight = Mathf.Max(1, imageHeight);
            captureSampleStride = Mathf.Max(1, captureSampleStride);
            maxFramesPerRun = Mathf.Max(0, maxFramesPerRun);
            jpgQuality = Mathf.Clamp(jpgQuality, 1, 100);
        }

        private void Awake()
        {
            if (captureCamera == null)
            {
                captureCamera = Camera.main;
            }

            if (logger == null)
            {
                logger = GetComponent<TrajectoryLogger>();
                if (logger == null)
                {
                    logger = FindFirstObjectByType<TrajectoryLogger>();
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

        private void OnEnable()
        {
            if (logger != null)
            {
                logger.SampleRecorded += HandleSampleRecorded;
            }
        }

        private void OnDisable()
        {
            if (logger != null)
            {
                logger.SampleRecorded -= HandleSampleRecorded;
            }
        }

        private void HandleSampleRecorded(DroneSample sample)
        {
            if (!captureEveryLoggedSample || captureCamera == null)
            {
                return;
            }

            if (sample.sampleIndex == 0)
            {
                capturedFrameCount = 0;
            }

            if (sample.sampleIndex % captureSampleStride != 0)
            {
                return;
            }

            if (maxFramesPerRun > 0 && capturedFrameCount >= maxFramesPerRun)
            {
                return;
            }

            string frameFileName = $"frame_{sample.sampleIndex:D06}.{GetFileExtension()}";
            byte[] imageBytes = CaptureImage();
            capturedFrameCount++;

            if (exporter != null)
            {
                exporter.SaveFrameAndPose(frameFileName, imageBytes, sample);
                return;
            }

            if (string.IsNullOrWhiteSpace(fallbackFramesDirectory))
            {
                string baseDirectory = logger != null && !string.IsNullOrWhiteSpace(logger.SessionDirectory)
                    ? logger.SessionDirectory
                    : Path.Combine(Application.persistentDataPath, "DroneDatasets", "frames");
                fallbackFramesDirectory = Path.Combine(baseDirectory, "frames");
                Directory.CreateDirectory(fallbackFramesDirectory);
            }

            File.WriteAllBytes(Path.Combine(fallbackFramesDirectory, frameFileName), imageBytes);
        }

        private string GetFileExtension()
        {
            return imageFormat == CaptureImageFormat.Jpg ? "jpg" : "png";
        }

        private byte[] CaptureImage()
        {
            RenderTexture previousTarget = captureCamera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture renderTexture = new RenderTexture(imageWidth, imageHeight, 24, RenderTextureFormat.ARGB32);
            Texture2D texture = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);

            captureCamera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            captureCamera.Render();
            texture.ReadPixels(new Rect(0f, 0f, imageWidth, imageHeight), 0, 0);
            texture.Apply();

            byte[] imageBytes = imageFormat == CaptureImageFormat.Jpg
                ? texture.EncodeToJPG(jpgQuality)
                : texture.EncodeToPNG();

            captureCamera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Destroy(renderTexture);
            Destroy(texture);

            return imageBytes;
        }
    }
}
