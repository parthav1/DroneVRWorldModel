using UnityEngine;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;

namespace DroneVR.Experiment
{
    public class VRDroneController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float verticalSpeed = 5f;
        [SerializeField] private float horizontalSpeed = 8f;
        [SerializeField] private float smoothing = 8f;

        [Header("Rotation")]
        [SerializeField] private float yawSpeed = 90f;
        [SerializeField] private float pitchSpeed = 90f;
        [SerializeField] private float rollSpeed = 90f;
        [SerializeField] private float maxPitchAngle = 35f;
        [SerializeField] private float maxRollAngle = 35f;
        [SerializeField] private float rotationSnapDegrees = 5f;

        [Header("Input")]
        [SerializeField] private float inputDeadzone = 0.12f;
        [SerializeField] private float triggerPressThreshold = 0.5f;
        [SerializeField] private bool disableExistingDroneRigControllerOnEnable = true;

        private XRInputDevice leftController;
        private XRInputDevice rightController;
        private Vector3 currentVelocity;
        private float yaw;
        private float pitch;
        private float roll;
        private bool leftTriggerWasPressed;
        private bool rightTriggerWasPressed;

        private void Awake()
        {
            Vector3 euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = NormalizeAngle(euler.x);
            roll = NormalizeAngle(euler.z);
        }

        private void OnEnable()
        {
            if (disableExistingDroneRigControllerOnEnable)
            {
                DroneRigController existingController = GetComponent<DroneRigController>();
                if (existingController != null)
                {
                    existingController.enabled = false;
                }
            }

            RefreshControllerReferences();
        }

        private void Update()
        {
            RefreshControllerReferencesIfNeeded();

            Vector2 leftStick = ReadStick(leftController);
            Vector2 rightStick = ReadStick(rightController);

            bool leftTriggerPressed = IsTriggerPressed(leftController);
            bool rightTriggerPressed = IsTriggerPressed(rightController);

            if (leftTriggerPressed && !leftTriggerWasPressed)
            {
                yaw -= rotationSnapDegrees;
            }

            if (rightTriggerPressed && !rightTriggerWasPressed)
            {
                yaw += rotationSnapDegrees;
            }

            leftTriggerWasPressed = leftTriggerPressed;
            rightTriggerWasPressed = rightTriggerPressed;

            UpdateRotation(leftStick, rightStick);
            UpdateMovement(leftStick, rightStick);
        }

        private void UpdateRotation(Vector2 leftStick, Vector2 rightStick)
        {
            yaw += leftStick.x * yawSpeed * Time.deltaTime;

            float targetPitch = rightStick.y * maxPitchAngle;
            float targetRoll = -rightStick.x * maxRollAngle;

            pitch = Mathf.LerpAngle(pitch, targetPitch, GetSmoothingFactor(pitchSpeed));
            roll = Mathf.LerpAngle(roll, targetRoll, GetSmoothingFactor(rollSpeed));

            transform.rotation = Quaternion.Euler(pitch, yaw, roll);
        }

        private void UpdateMovement(Vector2 leftStick, Vector2 rightStick)
        {
            Vector3 yawForward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 yawRight = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

            Vector3 horizontalInput = yawForward * rightStick.y + yawRight * rightStick.x;
            horizontalInput = Vector3.ClampMagnitude(horizontalInput, 1f);

            Vector3 desiredVelocity =
                horizontalInput * horizontalSpeed +
                Vector3.up * leftStick.y * verticalSpeed;

            currentVelocity = Vector3.Lerp(currentVelocity, desiredVelocity, GetSmoothingFactor(smoothing));
            transform.position += currentVelocity * Time.deltaTime;
        }

        private void RefreshControllerReferencesIfNeeded()
        {
            if (!leftController.isValid || !rightController.isValid)
            {
                RefreshControllerReferences();
            }
        }

        private void RefreshControllerReferences()
        {
            leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }

        private Vector2 ReadStick(XRInputDevice controller)
        {
            if (!controller.isValid ||
                !controller.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick))
            {
                return Vector2.zero;
            }

            return ApplyDeadzone(stick);
        }

        private bool IsTriggerPressed(XRInputDevice controller)
        {
            if (!controller.isValid)
            {
                return false;
            }

            if (controller.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerButtonPressed) &&
                triggerButtonPressed)
            {
                return true;
            }

            return controller.TryGetFeatureValue(CommonUsages.trigger, out float triggerValue) &&
                   triggerValue >= triggerPressThreshold;
        }

        private Vector2 ApplyDeadzone(Vector2 input)
        {
            if (input.magnitude <= inputDeadzone)
            {
                return Vector2.zero;
            }

            return input;
        }

        private float GetSmoothingFactor(float speed)
        {
            return 1f - Mathf.Exp(-speed * Time.deltaTime);
        }

        private static float NormalizeAngle(float angle)
        {
            while (angle > 180f)
            {
                angle -= 360f;
            }

            while (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }
    }
}
