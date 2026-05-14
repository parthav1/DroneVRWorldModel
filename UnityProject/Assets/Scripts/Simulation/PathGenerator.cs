using System;
using System.Collections.Generic;
using UnityEngine;

namespace DroneVR.Simulation
{
    public enum PathPolicy
    {
        Waypoints,
        Lawnmower,
        Orbit,
        RandomWalk
    }

    public class PathGenerator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DroneController drone;
        [SerializeField] private Transform environmentRoot;

        [Header("Run")]
        [SerializeField] private PathPolicy policy = PathPolicy.Waypoints;
        [SerializeField] private int randomSeed = 12345;
        [SerializeField] private bool faceTravelDirection = true;
        [SerializeField] private bool useSurveyCameraPitch = true;
        [SerializeField] private float surveyPitchDegrees = 20f;
        [SerializeField] private float waypointReachDistance = 2.5f;
        [SerializeField] private float slowdownDistance = 8f;

        [Header("Environment Bounds")]
        [SerializeField] private bool fitBoundsFromEnvironmentOnStart = true;
        [SerializeField] private float horizontalPadding = 8f;
        [SerializeField] private float maxBoundsSize = 120f;
        [SerializeField] private bool useCustomHorizontalSize;
        [SerializeField] private float customBoundsWidth = 80f;
        [SerializeField] private float customBoundsLength = 80f;
        [SerializeField] private bool altitudeRelativeToHighestPoint = true;
        [SerializeField] private float clearanceAboveHighestPoint = 3.05f;
        [SerializeField] private float minFlightAltitude = 6f;
        [SerializeField] private float maxFlightAltitude = 24f;
        [SerializeField] private float orbitRadiusScale = 0.35f;
        [SerializeField] private bool drawConfiguredBounds = true;

        [Header("Waypoint Following")]
        [SerializeField] private List<Transform> waypointTransforms = new List<Transform>();

        [Header("Lawnmower Sweep")]
        [SerializeField] private Vector3 sweepMin = new Vector3(-10f, 8f, -10f);
        [SerializeField] private Vector3 sweepMax = new Vector3(10f, 8f, 10f);
        [SerializeField] private float sweepSpacing = 5f;
        [SerializeField] private bool startLawnmowerNearDrone = true;

        [Header("Orbit")]
        [SerializeField] private Transform orbitCenterTransform;
        [SerializeField] private Vector3 orbitCenter = Vector3.zero;
        [SerializeField] private float orbitRadius = 10f;
        [SerializeField] private float orbitAltitude = 8f;
        [SerializeField] private float orbitAngularSpeedDegrees = 20f;

        [Header("Random Walk")]
        [SerializeField] private Vector3 randomBoundsMin = new Vector3(-20f, 4f, -20f);
        [SerializeField] private Vector3 randomBoundsMax = new Vector3(20f, 20f, 20f);
        [SerializeField] private float randomTargetReachDistance = 1.5f;
        [SerializeField] private float randomMinimumStepDistance = 8f;
        [SerializeField] private bool randomWalkConstantAltitude = true;

        private readonly List<Vector3> generatedWaypoints = new List<Vector3>();
        private System.Random random;
        private Vector3 activeRandomTarget;
        private Vector3 activePathTarget;
        private int waypointIndex;
        private float orbitAngleDegrees;
        private bool isRunning;
        private bool hasFitBounds;

        public PathPolicy ActivePolicy => policy;
        public bool IsRunning => isRunning;
        public int RandomSeed => randomSeed;
        public float WaypointReachDistance => waypointReachDistance;
        public Vector3 SweepMin => sweepMin;
        public Vector3 SweepMax => sweepMax;
        public float SweepSpacing => sweepSpacing;
        public Vector3 OrbitCenter => orbitCenterTransform != null ? orbitCenterTransform.position : orbitCenter;
        public float OrbitRadius => orbitRadius;
        public float OrbitAltitude => orbitAltitude;
        public float OrbitAngularSpeedDegrees => orbitAngularSpeedDegrees;
        public Vector3 RandomBoundsMin => randomBoundsMin;
        public Vector3 RandomBoundsMax => randomBoundsMax;
        public int WaypointCount => waypointTransforms.Count;
        public Transform EnvironmentRoot => environmentRoot;
        public float HorizontalPadding => horizontalPadding;
        public float MaxBoundsSize => maxBoundsSize;
        public bool UseCustomHorizontalSize => useCustomHorizontalSize;
        public float CustomBoundsWidth => customBoundsWidth;
        public float CustomBoundsLength => customBoundsLength;
        public bool AltitudeRelativeToHighestPoint => altitudeRelativeToHighestPoint;
        public float ClearanceAboveHighestPoint => clearanceAboveHighestPoint;
        public float MinFlightAltitude => minFlightAltitude;
        public float MaxFlightAltitude => maxFlightAltitude;
        public Vector3 SuggestedStartPosition => new Vector3(sweepMin.x, sweepMin.y, sweepMin.z);
        public Quaternion SuggestedStartRotation
        {
            get
            {
                Vector3 center = new Vector3(
                    (sweepMin.x + sweepMax.x) * 0.5f,
                    sweepMin.y,
                    (sweepMin.z + sweepMax.z) * 0.5f);
                Vector3 direction = center - SuggestedStartPosition;
                direction.y = 0f;
                return direction.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                    : Quaternion.identity;
            }
        }

        private void Awake()
        {
            if (drone == null)
            {
                drone = FindFirstObjectByType<DroneController>();
            }

            ResolveEnvironmentRoot();
        }

        private void OnValidate()
        {
            horizontalPadding = Mathf.Max(0f, horizontalPadding);
            maxBoundsSize = Mathf.Max(1f, maxBoundsSize);
            customBoundsWidth = Mathf.Max(1f, customBoundsWidth);
            customBoundsLength = Mathf.Max(1f, customBoundsLength);
            clearanceAboveHighestPoint = Mathf.Max(0f, clearanceAboveHighestPoint);
            maxFlightAltitude = Mathf.Max(minFlightAltitude, maxFlightAltitude);
            sweepSpacing = Mathf.Max(0.1f, sweepSpacing);
            waypointReachDistance = Mathf.Max(0.1f, waypointReachDistance);
            slowdownDistance = Mathf.Max(waypointReachDistance + 0.1f, slowdownDistance);
            randomTargetReachDistance = Mathf.Max(0.1f, randomTargetReachDistance);
            randomMinimumStepDistance = Mathf.Max(randomTargetReachDistance + 0.1f, randomMinimumStepDistance);

            if (fitBoundsFromEnvironmentOnStart && environmentRoot != null)
            {
                FitBoundsToEnvironment(false);
            }
        }

        private void Update()
        {
            if (!isRunning || drone == null)
            {
                return;
            }

            switch (policy)
            {
                case PathPolicy.Waypoints:
                    FollowWaypointList();
                    break;
                case PathPolicy.Lawnmower:
                    FollowLawnmower();
                    break;
                case PathPolicy.Orbit:
                    FollowOrbit();
                    break;
                case PathPolicy.RandomWalk:
                    FollowRandomWalk();
                    break;
            }
        }

        public void StartPolicy(PathPolicy nextPolicy, int seed)
        {
            policy = nextPolicy;
            randomSeed = seed;
            StartPolicy();
        }

        public void StartPolicy()
        {
            if (fitBoundsFromEnvironmentOnStart)
            {
                FitBoundsToEnvironment();
            }

            random = new System.Random(randomSeed);
            waypointIndex = 0;
            orbitAngleDegrees = GetInitialOrbitAngleDegrees();
            generatedWaypoints.Clear();

            if (policy == PathPolicy.Waypoints)
            {
                foreach (Transform waypoint in waypointTransforms)
                {
                    if (waypoint != null)
                    {
                        generatedWaypoints.Add(waypoint.position);
                    }
                }

                if (generatedWaypoints.Count == 0)
                {
                    Debug.LogWarning("PathGenerator is set to Waypoints, but no waypoint transforms are assigned. Add waypoints or switch TaskManager to Orbit, Lawnmower, or RandomWalk.");
                }
            }
            else if (policy == PathPolicy.Lawnmower)
            {
                GenerateLawnmowerWaypoints();
            }
            else if (policy == PathPolicy.RandomWalk)
            {
                activeRandomTarget = NextRandomPoint();
            }

            isRunning = true;
        }

        public void StopPolicy()
        {
            isRunning = false;
            if (drone != null)
            {
                drone.Stop();
            }
        }

        public void SetPolicy(PathPolicy nextPolicy)
        {
            policy = nextPolicy;
        }

        public void SetSeed(int seed)
        {
            randomSeed = seed;
        }

        [ContextMenu("Fit Bounds To Environment")]
        public void FitBoundsToEnvironment()
        {
            FitBoundsToEnvironment(true);
        }

        private void FitBoundsToEnvironment(bool logResult)
        {
            ResolveEnvironmentRoot();

            if (environmentRoot == null)
            {
                if (logResult)
                {
                    Debug.LogWarning("PathGenerator cannot fit bounds because Environment Root is not assigned. Drag the active reconstruction root object into Environment Root.");
                }

                return;
            }

            if (!TryGetRendererBounds(environmentRoot, out Bounds bounds))
            {
                if (logResult)
                {
                    Debug.LogWarning($"PathGenerator could not find any enabled renderers under {environmentRoot.name}.");
                }

                return;
            }

            float availableHalfX = Mathf.Max(0f, bounds.extents.x - horizontalPadding);
            float availableHalfZ = Mathf.Max(0f, bounds.extents.z - horizontalPadding);
            float requestedHalfX = useCustomHorizontalSize ? customBoundsWidth * 0.5f : maxBoundsSize * 0.5f;
            float requestedHalfZ = useCustomHorizontalSize ? customBoundsLength * 0.5f : maxBoundsSize * 0.5f;
            float usableHalfX = Mathf.Min(availableHalfX, requestedHalfX);
            float usableHalfZ = Mathf.Min(availableHalfZ, requestedHalfZ);
            float minX = bounds.center.x - usableHalfX;
            float maxX = bounds.center.x + usableHalfX;
            float minZ = bounds.center.z - usableHalfZ;
            float maxZ = bounds.center.z + usableHalfZ;

            if (minX > maxX)
            {
                float centerX = bounds.center.x;
                minX = centerX;
                maxX = centerX;
            }

            if (minZ > maxZ)
            {
                float centerZ = bounds.center.z;
                minZ = centerZ;
                maxZ = centerZ;
            }

            float altitudeReference = altitudeRelativeToHighestPoint ? bounds.max.y : bounds.min.y;
            float lowAltitude = altitudeReference + clearanceAboveHighestPoint + minFlightAltitude;
            float highAltitude = altitudeReference + clearanceAboveHighestPoint + Mathf.Max(minFlightAltitude, maxFlightAltitude);
            float cruiseAltitude = (lowAltitude + highAltitude) * 0.5f;

            sweepMin = new Vector3(minX, cruiseAltitude, minZ);
            sweepMax = new Vector3(maxX, cruiseAltitude, maxZ);
            randomBoundsMin = new Vector3(minX, lowAltitude, minZ);
            randomBoundsMax = new Vector3(maxX, highAltitude, maxZ);
            orbitCenter = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z);
            orbitAltitude = cruiseAltitude;
            orbitRadius = Mathf.Max(2f, Mathf.Min(bounds.extents.x, bounds.extents.z) * orbitRadiusScale);
            hasFitBounds = true;

            if (logResult)
            {
                Debug.Log($"PathGenerator fit bounds from {environmentRoot.name}. Random bounds: {randomBoundsMin} to {randomBoundsMax}. Sweep bounds: {sweepMin} to {sweepMax}.");
            }
        }

        [ContextMenu("Auto Assign Environment Root")]
        private void AutoAssignEnvironmentRoot()
        {
            ResolveEnvironmentRoot(true);
        }

        private void ResolveEnvironmentRoot(bool logResult = false)
        {
            if (environmentRoot != null)
            {
                return;
            }

            Transform largestRoot = null;
            Bounds largestBounds = default;
            float largestVolume = 0f;
            GameObject[] rootObjects = gameObject.scene.GetRootGameObjects();

            foreach (GameObject rootObject in rootObjects)
            {
                if (rootObject == null || rootObject == gameObject)
                {
                    continue;
                }

                if (rootObject.GetComponentInChildren<DroneController>() != null ||
                    rootObject.GetComponentInChildren<Camera>() != null ||
                    rootObject.GetComponentInChildren<Light>() != null)
                {
                    continue;
                }

                if (!TryGetRendererBounds(rootObject.transform, out Bounds candidateBounds))
                {
                    continue;
                }

                float volume = candidateBounds.size.x * candidateBounds.size.y * candidateBounds.size.z;
                if (largestRoot == null || volume > largestVolume)
                {
                    largestRoot = rootObject.transform;
                    largestBounds = candidateBounds;
                    largestVolume = volume;
                }
            }

            if (largestRoot == null)
            {
                return;
            }

            environmentRoot = largestRoot;
            hasFitBounds = false;

            if (logResult)
            {
                Debug.Log($"PathGenerator auto-assigned Environment Root to {environmentRoot.name} with renderer bounds centered at {largestBounds.center}.");
            }
        }

        private void FollowWaypointList()
        {
            if (generatedWaypoints.Count == 0)
            {
                drone.Stop();
                return;
            }

            int safety = 0;
            while (generatedWaypoints.Count > 0 &&
                   Vector3.Distance(drone.transform.position, generatedWaypoints[waypointIndex]) <= waypointReachDistance &&
                   safety < generatedWaypoints.Count)
            {
                waypointIndex = (waypointIndex + 1) % generatedWaypoints.Count;
                safety++;
            }

            Vector3 target = generatedWaypoints[waypointIndex];
            activePathTarget = target;
            MoveToward(target, waypointReachDistance);
        }

        private void FollowLawnmower()
        {
            if (generatedWaypoints.Count == 0)
            {
                drone.Stop();
                return;
            }

            Vector3 dronePosition = drone.transform.position;
            int attempts = 0;
            while (attempts < generatedWaypoints.Count)
            {
                Vector3 candidate = generatedWaypoints[waypointIndex];
                float planarDistance = Vector2.Distance(
                    new Vector2(dronePosition.x, dronePosition.z),
                    new Vector2(candidate.x, candidate.z));

                if (planarDistance > waypointReachDistance)
                {
                    activePathTarget = candidate;
                    MoveToward(candidate, waypointReachDistance);
                    return;
                }

                waypointIndex = (waypointIndex + 1) % generatedWaypoints.Count;
                attempts++;
            }

            activePathTarget = generatedWaypoints[waypointIndex];
            drone.Stop();
        }

        private void FollowOrbit()
        {
            Vector3 center = orbitCenterTransform != null ? orbitCenterTransform.position : orbitCenter;
            orbitAngleDegrees += orbitAngularSpeedDegrees * Time.deltaTime;

            float radians = orbitAngleDegrees * Mathf.Deg2Rad;
            Vector3 target = center + new Vector3(Mathf.Cos(radians) * orbitRadius, orbitAltitude, Mathf.Sin(radians) * orbitRadius);
            MoveToward(target, waypointReachDistance);
            drone.SetLookDirection(center - drone.transform.position);
        }

        private float GetInitialOrbitAngleDegrees()
        {
            if (drone == null)
            {
                return 0f;
            }

            Vector3 center = orbitCenterTransform != null ? orbitCenterTransform.position : orbitCenter;
            Vector3 offset = drone.transform.position - center;
            Vector3 planarOffset = Vector3.ProjectOnPlane(offset, Vector3.up);
            if (planarOffset.sqrMagnitude < 0.001f)
            {
                return 0f;
            }

            return Mathf.Atan2(planarOffset.z, planarOffset.x) * Mathf.Rad2Deg;
        }

        private void FollowRandomWalk()
        {
            int attempts = 0;
            while (IsRandomTargetReached(activeRandomTarget) && attempts < 16)
            {
                activeRandomTarget = NextRandomPoint();
                attempts++;
            }

            activePathTarget = activeRandomTarget;
            MoveToward(activeRandomTarget, randomTargetReachDistance);
        }

        private void MoveToward(Vector3 target, float reachDistance)
        {
            Vector3 offset = target - drone.transform.position;
            float distance = offset.magnitude;
            if (distance <= reachDistance)
            {
                drone.SetDesiredWorldVelocity(Vector3.zero);
                return;
            }

            float slowFactor = Mathf.Clamp01(distance / Mathf.Max(reachDistance + 0.01f, slowdownDistance));
            Vector3 desiredVelocity = offset.normalized * drone.MaxSpeedMetersPerSecond * Mathf.Max(0.2f, slowFactor);
            drone.SetDesiredWorldVelocity(desiredVelocity);

            if (faceTravelDirection)
            {
                ApplyLookDirection(desiredVelocity);
            }
        }

        private void ApplyLookDirection(Vector3 desiredVelocity)
        {
            if (!useSurveyCameraPitch)
            {
                drone.SetLookDirection(desiredVelocity);
                return;
            }

            Vector3 planarDirection = Vector3.ProjectOnPlane(desiredVelocity, Vector3.up);
            if (planarDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion yawRotation = Quaternion.LookRotation(planarDirection.normalized, Vector3.up);
            Quaternion pitchRotation = Quaternion.Euler(Mathf.Clamp(surveyPitchDegrees, -85f, 85f), yawRotation.eulerAngles.y, 0f);
            drone.SetLookRotation(pitchRotation);
        }

        private void GenerateLawnmowerWaypoints()
        {
            float minX = Mathf.Min(sweepMin.x, sweepMax.x);
            float maxX = Mathf.Max(sweepMin.x, sweepMax.x);
            float minZ = Mathf.Min(sweepMin.z, sweepMax.z);
            float maxZ = Mathf.Max(sweepMin.z, sweepMax.z);
            float altitude = (sweepMin.y + sweepMax.y) * 0.5f;
            float spacing = Mathf.Max(0.1f, sweepSpacing);
            bool startAtMinZ = true;
            bool leftToRight = true;

            if (startLawnmowerNearDrone && drone != null)
            {
                Vector3 dronePosition = drone.transform.position;
                startAtMinZ = Mathf.Abs(dronePosition.z - minZ) <= Mathf.Abs(dronePosition.z - maxZ);
                leftToRight = Mathf.Abs(dronePosition.x - minX) <= Mathf.Abs(dronePosition.x - maxX);
            }

            List<float> rows = new List<float>();
            if (startAtMinZ)
            {
                for (float z = minZ; z <= maxZ + 0.001f; z += spacing)
                {
                    rows.Add(z);
                }
            }
            else
            {
                for (float z = maxZ; z >= minZ - 0.001f; z -= spacing)
                {
                    rows.Add(z);
                }
            }

            foreach (float z in rows)
            {
                generatedWaypoints.Add(new Vector3(leftToRight ? minX : maxX, altitude, z));
                generatedWaypoints.Add(new Vector3(leftToRight ? maxX : minX, altitude, z));
                leftToRight = !leftToRight;
            }
        }

        private Vector3 NextRandomPoint()
        {
            Vector3 currentPosition = drone != null ? drone.transform.position : Vector3.zero;
            float minAltitude = Mathf.Min(randomBoundsMin.y, randomBoundsMax.y);
            float maxAltitude = Mathf.Max(randomBoundsMin.y, randomBoundsMax.y);
            float targetAltitude = randomWalkConstantAltitude
                ? Mathf.Clamp(currentPosition.y, minAltitude, maxAltitude)
                : RandomRange(minAltitude, maxAltitude);
            Vector3 candidate = currentPosition;

            for (int i = 0; i < 32; i++)
            {
                candidate = new Vector3(
                    RandomRange(randomBoundsMin.x, randomBoundsMax.x),
                    targetAltitude,
                    RandomRange(randomBoundsMin.z, randomBoundsMax.z));

                float planarDistance = Vector2.Distance(
                    new Vector2(currentPosition.x, currentPosition.z),
                    new Vector2(candidate.x, candidate.z));

                if (planarDistance >= randomMinimumStepDistance)
                {
                    return candidate;
                }
            }

            return candidate;
        }

        private bool IsRandomTargetReached(Vector3 target)
        {
            if (drone == null)
            {
                return true;
            }

            Vector3 dronePosition = drone.transform.position;
            float planarDistance = Vector2.Distance(
                new Vector2(dronePosition.x, dronePosition.z),
                new Vector2(target.x, target.z));

            return planarDistance <= randomTargetReachDistance;
        }

        private float RandomRange(float a, float b)
        {
            float min = Mathf.Min(a, b);
            float max = Mathf.Max(a, b);
            return Mathf.Lerp(min, max, (float)random.NextDouble());
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying && fitBoundsFromEnvironmentOnStart && environmentRoot != null && !hasFitBounds)
            {
                FitBoundsToEnvironment(false);
            }

            if (drawConfiguredBounds)
            {
                DrawBoundsGizmo(sweepMin, sweepMax, Color.cyan);
                DrawBoundsGizmo(randomBoundsMin, randomBoundsMax, Color.yellow);
            }

            if (isRunning)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(activePathTarget, 0.75f);
                Gizmos.DrawLine(drone != null ? drone.transform.position : Vector3.zero, activePathTarget);
            }

            Gizmos.color = Color.cyan;
            foreach (Vector3 waypoint in generatedWaypoints)
            {
                Gizmos.DrawSphere(waypoint, 0.35f);
            }

            if (isRunning && policy == PathPolicy.RandomWalk)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(activeRandomTarget, 0.5f);
            }
        }

        private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            bounds = default;
            bool hasBounds = false;

            foreach (Renderer rendererComponent in renderers)
            {
                if (rendererComponent == null || !rendererComponent.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = rendererComponent.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rendererComponent.bounds);
                }
            }

            return hasBounds;
        }

        private static void DrawBoundsGizmo(Vector3 min, Vector3 max, Color color)
        {
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = new Vector3(
                Mathf.Abs(max.x - min.x),
                Mathf.Abs(max.y - min.y),
                Mathf.Abs(max.z - min.z));

            Gizmos.color = color;
            Gizmos.DrawWireCube(center, size);
        }
    }
}
