using UnityEngine;

namespace DroneVR.Simulation
{
    public class DroneController : MonoBehaviour
    {
        [Header("Motion")]
        [SerializeField] private float maxSpeedMetersPerSecond = 8f;
        [SerializeField] private float accelerationMetersPerSecondSquared = 12f;
        [SerializeField] private float yawRateDegreesPerSecond = 120f;
        [SerializeField] private float pitchRateDegreesPerSecond = 90f;

        [Header("Altitude")]
        [SerializeField] private bool clampAltitude = true;
        [SerializeField] private float minAltitude = 1f;
        [SerializeField] private float maxAltitude = 60f;

        [Header("Ground Safety")]
        [SerializeField] private bool maintainGroundClearance = true;
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private float groundClearance = 4f;
        [SerializeField] private float groundRaycastDistance = 200f;

        [Header("Start Pose")]
        [SerializeField] private Vector3 startPosition = new Vector3(0f, 5f, -10f);
        [SerializeField] private Vector3 startEulerAngles = Vector3.zero;
        [SerializeField] private bool resetOnStart = true;

        private Vector3 desiredWorldVelocity;
        private Vector3 currentWorldVelocity;
        private Quaternion desiredRotation;
        private bool hasRotationCommand;

        public Vector3 CurrentVelocity => currentWorldVelocity;
        public Vector3 DesiredVelocity => desiredWorldVelocity;
        public float MaxSpeedMetersPerSecond => maxSpeedMetersPerSecond;
        public float AccelerationMetersPerSecondSquared => accelerationMetersPerSecondSquared;
        public float YawRateDegreesPerSecond => yawRateDegreesPerSecond;
        public float PitchRateDegreesPerSecond => pitchRateDegreesPerSecond;
        public bool ClampAltitude => clampAltitude;
        public float MinAltitude => minAltitude;
        public float MaxAltitude => maxAltitude;
        public bool MaintainGroundClearance => maintainGroundClearance;
        public float GroundClearance => groundClearance;

        private void Start()
        {
            desiredRotation = transform.rotation;

            if (resetOnStart)
            {
                ResetToStartPose();
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            currentWorldVelocity = Vector3.MoveTowards(
                currentWorldVelocity,
                Vector3.ClampMagnitude(desiredWorldVelocity, maxSpeedMetersPerSecond),
                accelerationMetersPerSecondSquared * deltaTime);

            transform.position += currentWorldVelocity * deltaTime;
            ApplyAltitudeLimits();
            ApplyGroundClearance();
            ApplyRotation(deltaTime);
        }

        public void SetDesiredWorldVelocity(Vector3 velocity)
        {
            desiredWorldVelocity = Vector3.ClampMagnitude(velocity, maxSpeedMetersPerSecond);
        }

        public void Stop()
        {
            desiredWorldVelocity = Vector3.zero;
        }

        public void SetLookDirection(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            desiredRotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
            hasRotationCommand = true;
        }

        public void SetLookRotation(Quaternion rotation)
        {
            desiredRotation = rotation;
            hasRotationCommand = true;
        }

        public void ClearRotationCommand()
        {
            hasRotationCommand = false;
        }

        public void SetAltitudeLimits(float minimumAltitude, float maximumAltitude)
        {
            minAltitude = Mathf.Min(minimumAltitude, maximumAltitude);
            maxAltitude = Mathf.Max(minimumAltitude, maximumAltitude);
        }

        public void ResetToStartPose()
        {
            ResetToPose(startPosition, Quaternion.Euler(startEulerAngles));
        }

        public void ResetToPose(Transform pose)
        {
            if (pose == null)
            {
                ResetToStartPose();
                return;
            }

            ResetToPose(pose.position, pose.rotation);
        }

        public void ResetToPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            currentWorldVelocity = Vector3.zero;
            desiredWorldVelocity = Vector3.zero;
            desiredRotation = rotation;
            hasRotationCommand = false;
        }

        private void ApplyRotation(float deltaTime)
        {
            if (!hasRotationCommand)
            {
                return;
            }

            Vector3 currentEuler = transform.rotation.eulerAngles;
            Vector3 targetEuler = desiredRotation.eulerAngles;

            float nextPitch = Mathf.MoveTowardsAngle(
                currentEuler.x,
                targetEuler.x,
                pitchRateDegreesPerSecond * deltaTime);

            float nextYaw = Mathf.MoveTowardsAngle(
                currentEuler.y,
                targetEuler.y,
                yawRateDegreesPerSecond * deltaTime);

            transform.rotation = Quaternion.Euler(nextPitch, nextYaw, 0f);
        }

        private void ApplyAltitudeLimits()
        {
            if (!clampAltitude)
            {
                return;
            }

            Vector3 position = transform.position;
            float clampedY = Mathf.Clamp(position.y, minAltitude, maxAltitude);
            if (!Mathf.Approximately(position.y, clampedY))
            {
                position.y = clampedY;
                transform.position = position;
                currentWorldVelocity.y = 0f;
                desiredWorldVelocity.y = Mathf.Min(desiredWorldVelocity.y, 0f);
            }
        }

        private void ApplyGroundClearance()
        {
            if (!maintainGroundClearance)
            {
                return;
            }

            Vector3 origin = transform.position;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRaycastDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            float minimumY = hit.point.y + groundClearance;
            if (transform.position.y >= minimumY)
            {
                return;
            }

            Vector3 position = transform.position;
            position.y = minimumY;
            transform.position = position;

            if (currentWorldVelocity.y < 0f)
            {
                currentWorldVelocity.y = 0f;
            }

            if (desiredWorldVelocity.y < 0f)
            {
                desiredWorldVelocity.y = 0f;
            }
        }
    }
}
