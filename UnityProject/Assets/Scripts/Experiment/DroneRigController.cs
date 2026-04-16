using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;

namespace DroneVR.Experiment
{
    public class DroneRigController : MonoBehaviour
    {
        public enum ControlMode
        {
            AutoDetect,
            Desktop,
            VR
        }

        public enum NavigationMode
        {
            FreeFly,
            PointAndFly
        }

        [Header("Mode")]
        [SerializeField] private ControlMode controlMode = ControlMode.AutoDetect;
        [SerializeField] private NavigationMode navigationMode = NavigationMode.FreeFly;
        [SerializeField] private KeyCode toggleNavigationModeKey = KeyCode.F;
        [SerializeField] private bool allowMouseLookInVR = false;
        [SerializeField] private bool lockCursorOnPlay = true;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 8f;
        [SerializeField] private float verticalSpeed = 5f;
        [SerializeField] private float sprintMultiplier = 2f;
        [SerializeField] private float smoothing = 8f;

        [Header("Look")]
        [SerializeField] private float mouseSensitivity = 2f;
        [SerializeField] private float controllerTurnSpeed = 90f;
        [SerializeField] private float pitchMin = -85f;
        [SerializeField] private float pitchMax = 85f;
        [SerializeField] private bool invertMouseY = false;

        [Header("Spawn")]
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 3f, -10f);
        [SerializeField] private Vector3 spawnEulerAngles = Vector3.zero;
        [SerializeField] private KeyCode resetKey = KeyCode.R;

        [Header("Point And Fly")]
        [SerializeField] private LayerMask pointerLayers = ~0;
        [SerializeField] private float pointerMaxDistance = 500f;
        [SerializeField] private float pointTurnSpeed = 6f;
        [SerializeField] private Transform pointerVisual;
        [SerializeField] private Vector3 pointerVisualOffset = new Vector3(0f, 0.15f, 0f);
        [SerializeField] private bool showDebugRay = true;

        private Vector3 currentVelocity;
        private float yaw;
        private float pitch;
        private bool cursorLocked;
        private RaycastHit currentPointerHit;
        private bool hasPointerHit;

        public string ActiveModeLabel => ResolveMode() == ControlMode.VR ? "VR" : "Desktop";
        public string NavigationModeLabel => navigationMode == NavigationMode.PointAndFly ? "PointAndFly" : "FreeFly";

        private void Awake()
        {
            ResetToSpawn();
            SetCursorState(lockCursorOnPlay);
        }

        private void OnEnable()
        {
            SetCursorState(lockCursorOnPlay);
        }

        private void OnDisable()
        {
            SetCursorState(false);
        }

        private void Update()
        {
            if (WasKeyPressed(resetKey))
            {
                ResetToSpawn();
            }

            if (WasKeyPressed(toggleNavigationModeKey))
            {
                ToggleNavigationMode();
            }

            UpdatePointer();

            if (navigationMode == NavigationMode.PointAndFly)
            {
                UpdatePointAndFlyMovement();
            }
            else if (ResolveMode() == ControlMode.VR)
            {
                UpdateVRMovement();
                if (allowMouseLookInVR)
                {
                    UpdateDesktopLook();
                }
            }
            else
            {
                UpdateDesktopLook();
                UpdateDesktopMovement();
            }

            if (WasKeyPressed(KeyCode.Escape))
            {
                SetCursorState(!cursorLocked);
            }
        }

        public void SetSpawnPoint(Vector3 position, Vector3 eulerAngles)
        {
            spawnPosition = position;
            spawnEulerAngles = eulerAngles;
        }

        public void ResetToSpawn()
        {
            transform.position = spawnPosition;
            transform.rotation = Quaternion.Euler(spawnEulerAngles);
            currentVelocity = Vector3.zero;
            yaw = spawnEulerAngles.y;
            pitch = NormalizePitch(spawnEulerAngles.x);
        }

        public void ResetToTransform(Transform targetTransform)
        {
            if (targetTransform == null)
            {
                ResetToSpawn();
                return;
            }

            SetSpawnPoint(targetTransform.position, targetTransform.rotation.eulerAngles);
            ResetToSpawn();
        }

        public Vector3 GetSpawnPosition()
        {
            return spawnPosition;
        }

        public void SetNavigationMode(NavigationMode mode)
        {
            navigationMode = mode;
            currentVelocity = Vector3.zero;
        }

        public void ToggleNavigationMode()
        {
            SetNavigationMode(
                navigationMode == NavigationMode.FreeFly
                    ? NavigationMode.PointAndFly
                    : NavigationMode.FreeFly);
        }

        private void UpdateDesktopMovement()
        {
            Vector3 planarInput = new Vector3(
                GetAxisRaw(KeyCode.A, KeyCode.D),
                0f,
                GetAxisRaw(KeyCode.S, KeyCode.W));

            float verticalInput = GetAxisRaw(KeyCode.Q, KeyCode.E);
            ApplyMovement(planarInput, verticalInput);
        }

        private void UpdateVRMovement()
        {
            Vector2 planarInput = Vector2.zero;
            float verticalInput = 0f;
            float turnInput = 0f;

            if (TryGetThumbstick(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, out Vector2 leftStick))
            {
                planarInput = leftStick;
            }

            if (TryGetThumbstick(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, out Vector2 rightStick))
            {
                turnInput = rightStick.x;
            }

            if (TryGetButton(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, XRCommonUsages.primaryButton, out bool ascendPressed) && ascendPressed)
            {
                verticalInput += 1f;
            }

            if (TryGetButton(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, XRCommonUsages.secondaryButton, out bool descendPressed) && descendPressed)
            {
                verticalInput -= 1f;
            }

            transform.Rotate(Vector3.up, turnInput * controllerTurnSpeed * Time.deltaTime, Space.World);
            ApplyMovement(new Vector3(planarInput.x, 0f, planarInput.y), verticalInput);
        }

        private void UpdatePointAndFlyMovement()
        {
            if (ResolveMode() != ControlMode.VR)
            {
                UpdateDesktopLook();
            }

            Vector3 movementForward = transform.forward.normalized;
            Vector3 rotationForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (hasPointerHit)
            {
                Vector3 pointerDirection = currentPointerHit.point - transform.position;
                if (pointerDirection.sqrMagnitude > 0.001f)
                {
                    movementForward = pointerDirection.normalized;

                    Vector3 planarPointerDirection = Vector3.ProjectOnPlane(pointerDirection, Vector3.up);
                    if (planarPointerDirection.sqrMagnitude > 0.001f)
                    {
                        rotationForward = planarPointerDirection.normalized;
                    }

                    Quaternion targetRotation = Quaternion.LookRotation(rotationForward, Vector3.up);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        1f - Mathf.Exp(-pointTurnSpeed * Time.deltaTime));

                    Vector3 euler = transform.eulerAngles;
                    yaw = euler.y;
                    pitch = NormalizePitch(euler.x);
                }
            }

            Vector3 planarInput = new Vector3(
                GetAxisRaw(KeyCode.A, KeyCode.D),
                0f,
                GetAxisRaw(KeyCode.S, KeyCode.W));
            float verticalInput = GetAxisRaw(KeyCode.Q, KeyCode.E);

            Vector3 referenceRight = Vector3.Cross(Vector3.up, rotationForward).normalized;
            if (referenceRight.sqrMagnitude < 0.001f)
            {
                referenceRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            }

            ApplyMovement(planarInput, verticalInput, movementForward, referenceRight);
        }

        private void UpdateDesktopLook()
        {
            if (!cursorLocked)
            {
                return;
            }

            Vector2 delta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            float mouseX = delta.x * mouseSensitivity * 0.01f;
            float mouseY = delta.y * mouseSensitivity * 0.01f * (invertMouseY ? 1f : -1f);

            yaw += mouseX;
            pitch = Mathf.Clamp(pitch + mouseY, pitchMin, pitchMax);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void ApplyMovement(Vector3 planarInput, float verticalInput)
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            ApplyMovement(planarInput, verticalInput, forward, right);
        }

        private void ApplyMovement(Vector3 planarInput, float verticalInput, Vector3 forward, Vector3 right)
        {
            planarInput = Vector3.ClampMagnitude(planarInput, 1f);
            float speedMultiplier = IsKeyHeld(KeyCode.LeftShift) ? sprintMultiplier : 1f;

            Vector3 desiredVelocity =
                (forward * planarInput.z + right * planarInput.x) * moveSpeed * speedMultiplier +
                Vector3.up * verticalInput * verticalSpeed;

            currentVelocity = Vector3.Lerp(currentVelocity, desiredVelocity, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            transform.position += currentVelocity * Time.deltaTime;
        }

        private void UpdatePointer()
        {
            hasPointerHit = false;

            Camera activeCamera = GetComponent<Camera>();
            if (activeCamera == null)
            {
                activeCamera = Camera.main;
            }

            if (activeCamera == null)
            {
                SetPointerVisual(false, Vector3.zero, Vector3.up);
                return;
            }

            Ray pointerRay = CreatePointerRay(activeCamera);
            if (showDebugRay)
            {
                Debug.DrawRay(pointerRay.origin, pointerRay.direction * pointerMaxDistance, hasPointerHit ? Color.green : Color.cyan);
            }

            if (Physics.Raycast(pointerRay, out RaycastHit hit, pointerMaxDistance, pointerLayers, QueryTriggerInteraction.Ignore))
            {
                currentPointerHit = hit;
                hasPointerHit = true;
                SetPointerVisual(true, hit.point + pointerVisualOffset, hit.normal);
                return;
            }

            SetPointerVisual(false, Vector3.zero, Vector3.up);
        }

        private Ray CreatePointerRay(Camera activeCamera)
        {
            if (cursorLocked || Mouse.current == null)
            {
                Vector3 center = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
                return activeCamera.ScreenPointToRay(center);
            }

            return activeCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        }

        private void SetPointerVisual(bool isVisible, Vector3 position, Vector3 normal)
        {
            if (pointerVisual == null)
            {
                return;
            }

            pointerVisual.gameObject.SetActive(isVisible);
            if (!isVisible)
            {
                return;
            }

            pointerVisual.position = position;
            pointerVisual.rotation = Quaternion.LookRotation(normal == Vector3.zero ? Vector3.up : normal);
        }

        private ControlMode ResolveMode()
        {
            if (controlMode == ControlMode.AutoDetect)
            {
                return XRSettings.isDeviceActive ? ControlMode.VR : ControlMode.Desktop;
            }

            return controlMode;
        }

        private static float GetAxisRaw(KeyCode negativeKey, KeyCode positiveKey)
        {
            float value = 0f;

            if (IsKeyHeld(negativeKey))
            {
                value -= 1f;
            }

            if (IsKeyHeld(positiveKey))
            {
                value += 1f;
            }

            return value;
        }

        private static bool TryGetThumbstick(InputDeviceCharacteristics characteristics, out Vector2 axis)
        {
            axis = Vector2.zero;
            XRInputDevice device = InputDevices.GetDeviceAtXRNode(
                (characteristics & InputDeviceCharacteristics.Left) != 0 ? XRNode.LeftHand : XRNode.RightHand);

            return device.isValid && device.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out axis);
        }

        private static bool TryGetButton(InputDeviceCharacteristics characteristics, InputFeatureUsage<bool> usage, out bool isPressed)
        {
            isPressed = false;
            XRInputDevice device = InputDevices.GetDeviceAtXRNode(
                (characteristics & InputDeviceCharacteristics.Left) != 0 ? XRNode.LeftHand : XRNode.RightHand);

            return device.isValid && device.TryGetFeatureValue(usage, out isPressed);
        }

        private static bool IsKeyHeld(KeyCode keyCode)
        {
            Key key = ConvertKeyCode(keyCode);
            return key != Key.None && Keyboard.current != null && Keyboard.current[key].isPressed;
        }

        private static Key ConvertKeyCode(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.A: return Key.A;
                case KeyCode.D: return Key.D;
                case KeyCode.S: return Key.S;
                case KeyCode.W: return Key.W;
                case KeyCode.Q: return Key.Q;
                case KeyCode.E: return Key.E;
                case KeyCode.F: return Key.F;
                case KeyCode.R: return Key.R;
                case KeyCode.T: return Key.T;
                case KeyCode.N: return Key.N;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Mouse0: return Key.None;
                default: return Key.None;
            }
        }

        private bool WasKeyPressed(KeyCode keyCode)
        {
            if (keyCode == KeyCode.Mouse0)
            {
                return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            }

            Key key = ConvertKeyCode(keyCode);
            return key != Key.None && Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
        }

        private static float NormalizePitch(float value)
        {
            while (value > 180f)
            {
                value -= 360f;
            }

            while (value < -180f)
            {
                value += 360f;
            }

            return Mathf.Clamp(value, -85f, 85f);
        }

        private void SetCursorState(bool shouldLock)
        {
            cursorLocked = shouldLock;
            Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !shouldLock;
        }

    }
}
