using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;
using InputAction = UnityEngine.InputSystem.InputAction;
using InputActionAsset = UnityEngine.InputSystem.InputActionAsset;
using InputActionType = UnityEngine.InputSystem.InputActionType;

namespace F1XR.Driving
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-1000)]
    public sealed class VRVehicleDriver : MonoBehaviour
    {
        [Header("Drive")]
        [SerializeField, Min(1f)] float accelerationForce = 8000f;
        [SerializeField, Min(1f)] float brakeForce = 12000f;
        [SerializeField, Min(1f)] float maximumSpeedKph = 160f;
        [SerializeField, Min(1f)] float steeringSpeedDegrees = 75f;
        [SerializeField, Range(0f, 1f)] float gripThreshold = 0.55f;
        [SerializeField, Range(0f, 0.5f)] float steeringDeadzone = 0.08f;

        [Header("Presentation")]
        [SerializeField] Transform steeringWheel;
        [SerializeField] Transform[] frontWheelPivots;
        [SerializeField] Transform[] wheelVisuals;
        [SerializeField, Min(1f)] float maximumVisualWheelAngle = 32f;
        [SerializeField, Min(1f)] float maximumSteeringWheelAngle = 360f;
        [SerializeField, Min(0.01f)] float wheelRadius = 0.33f;
        [SerializeField] Vector3 wheelRollAxis = Vector3.right;
        [SerializeField] AudioSource engineAudio;

        [Header("XR Input")]
        [SerializeField] InputActionAsset xrInputActions;

        readonly List<UnityEngine.XR.InputDevice> inputDevices = new();

        Rigidbody body;
        InputAction leftPositionAction;
        InputAction rightPositionAction;
        InputAction leftGripAction;
        InputAction rightGripAction;
        InputAction throttleAction;
        InputAction brakeAction;
        InputAction respawnAction;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        float grabStartAngle;
        float steeringAtGrabStart;
        float steeringInput;
        float throttleInput;
        float brakeInput;
        bool wasGrabbing;
        bool respawnPressed;
        Vector3 previousPosition;
#if UNITY_EDITOR
        float previousThrottleInput;
        float throttleDiagnosticStartedAt = -1f;
        bool forceDiagnosticLogged;
        bool firstCollisionLogged;
#endif

        void Awake()
        {
            DetachXrOriginFromVehicle();
            body = GetComponent<Rigidbody>();
            body.constraints = RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationZ;
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            previousPosition = transform.position;

            if (engineAudio == null)
                engineAudio = GetComponentInChildren<AudioSource>();

            CacheInputActions();
            respawnAction = new InputAction(
                "Respawn",
                InputActionType.Button,
                "<XRController>{RightHand}/primaryButton");
        }

        void DetachXrOriginFromVehicle()
        {
            XROrigin xrOrigin = GetComponentInChildren<XROrigin>(true);
            Transform seatAnchor = transform.Find("DriverSeatAnchor");
            if (xrOrigin == null || seatAnchor == null)
                return;

            xrOrigin.transform.SetParent(null, true);

            VehicleSeatFollower follower = xrOrigin.GetComponent<VehicleSeatFollower>();
            if (follower == null)
                follower = xrOrigin.gameObject.AddComponent<VehicleSeatFollower>();

            follower.Configure(seatAnchor);
        }

        void OnEnable()
        {
            respawnAction?.Enable();
        }

        void OnDisable()
        {
            respawnAction?.Disable();
        }

        void OnDestroy()
        {
            respawnAction?.Dispose();
        }

        void Start()
        {
#if UNITY_EDITOR
            Debug.Log(
                $"[DrivingTest] Input ready: throttle action=" +
                $"{(throttleAction != null)}, enabled={throttleAction?.enabled ?? false}, " +
                $"rigidbodyKinematic={body.isKinematic}.", this);
#endif
        }

        public void ConfigureInputActions(InputActionAsset actions)
        {
            xrInputActions = actions;
            CacheInputActions();
        }

        void Update()
        {
            throttleInput = ReadAxis(throttleAction, XRNode.RightHand, CommonUsages.trigger);
            brakeInput = ReadAxis(brakeAction, XRNode.LeftHand, CommonUsages.trigger);
#if UNITY_EDITOR
            UpdateThrottleDiagnostic();
#endif

            bool isGrabbing = ReadGrip(leftGripAction, XRNode.LeftHand) &&
                ReadGrip(rightGripAction, XRNode.RightHand);
            if (isGrabbing && !wasGrabbing && TryGetHandAngle(out float handAngle))
            {
                grabStartAngle = handAngle;
                steeringAtGrabStart = steeringInput;
            }

            if (isGrabbing && TryGetHandAngle(out handAngle))
            {
                float delta = Mathf.DeltaAngle(grabStartAngle, handAngle);
                steeringInput = Mathf.Clamp(steeringAtGrabStart + delta / 120f, -1f, 1f);
                if (Mathf.Abs(steeringInput) < steeringDeadzone)
                    steeringInput = 0f;
            }
            else
            {
                steeringInput = Mathf.MoveTowards(steeringInput, 0f, Time.deltaTime * 3f);
            }

            wasGrabbing = isGrabbing;
            UpdatePresentation();
            UpdateAudio();

            bool pressed = respawnAction != null && respawnAction.IsPressed();
            if (pressed && !respawnPressed)
                Respawn();

            respawnPressed = pressed;
        }

        void FixedUpdate()
        {
            float maximumSpeed = maximumSpeedKph / 3.6f;
            float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);

            if (throttleInput > 0f && forwardSpeed < maximumSpeed)
            {
                body.AddForce(transform.forward * (throttleInput * accelerationForce), ForceMode.Force);
#if UNITY_EDITOR
                if (throttleDiagnosticStartedAt >= 0f && !forceDiagnosticLogged)
                {
                    forceDiagnosticLogged = true;
                    Debug.Log(
                        $"[DrivingTest] Force applied: throttle={throttleInput:F2}, " +
                        $"force={throttleInput * accelerationForce:F0}, " +
                        $"forwardSpeed={forwardSpeed:F2} m/s.", this);
                }
#endif
            }

            if (brakeInput > 0f && body.linearVelocity.sqrMagnitude > 0.0001f)
            {
                body.AddForce(-body.linearVelocity.normalized * (brakeInput * brakeForce),
                    ForceMode.Force);
            }

            float speedRatio = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 4f);
            float yaw = steeringInput * steeringSpeedDegrees * speedRatio * Time.fixedDeltaTime;
            body.MoveRotation(body.rotation * Quaternion.Euler(0f, yaw, 0f));
        }

        void UpdatePresentation()
        {
            if (steeringWheel != null)
            {
                steeringWheel.localRotation = Quaternion.AngleAxis(
                    -steeringInput * maximumSteeringWheelAngle,
                    Vector3.forward);
            }

            foreach (Transform pivot in frontWheelPivots)
            {
                if (pivot != null)
                    pivot.localRotation = Quaternion.Euler(0f,
                        steeringInput * maximumVisualWheelAngle, 0f);
            }

            float distance = Vector3.Dot(transform.position - previousPosition, transform.forward);
            previousPosition = transform.position;
            float roll = distance / wheelRadius * Mathf.Rad2Deg;
            foreach (Transform wheel in wheelVisuals)
            {
                if (wheel != null)
                    wheel.Rotate(wheelRollAxis, roll, Space.Self);
            }
        }

#if UNITY_EDITOR
        void UpdateThrottleDiagnostic()
        {
            if (throttleInput > 0.1f && previousThrottleInput <= 0.1f)
            {
                throttleDiagnosticStartedAt = Time.time;
                forceDiagnosticLogged = false;
                Debug.Log(
                    $"[DrivingTest] Trigger received: throttle={throttleInput:F2}, " +
                    $"actionEnabled={throttleAction?.enabled ?? false}.", this);
            }

            if (throttleDiagnosticStartedAt >= 0f &&
                Time.time - throttleDiagnosticStartedAt >= 0.25f)
            {
                Debug.Log(
                    $"[DrivingTest] Motion check: velocity={body.linearVelocity.magnitude:F2} m/s, " +
                    $"forwardSpeed={Vector3.Dot(body.linearVelocity, transform.forward):F2} m/s, " +
                    $"velocityVector={body.linearVelocity}, position={body.position}.",
                    this);
                throttleDiagnosticStartedAt = -1f;
            }

            previousThrottleInput = throttleInput;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (firstCollisionLogged)
                return;

            firstCollisionLogged = true;
            Vector3 normal = collision.contactCount > 0
                ? collision.GetContact(0).normal
                : Vector3.zero;
            Debug.Log(
                $"[DrivingTest] First collision: object={collision.collider.name}, " +
                $"normal={normal}, relativeVelocity={collision.relativeVelocity}, " +
                $"vehicleVelocity={body.linearVelocity}.", this);
        }
#endif

        void UpdateAudio()
        {
            if (engineAudio == null || engineAudio.clip == null)
                return;

            float speedRatio = Mathf.Clamp01(body.linearVelocity.magnitude / (maximumSpeedKph / 3.6f));
            engineAudio.pitch = Mathf.Lerp(0.75f, 1.5f, Mathf.Max(speedRatio, throttleInput));
            engineAudio.volume = Mathf.Lerp(0.15f, 0.9f, Mathf.Max(speedRatio, throttleInput));
            if (!engineAudio.isPlaying)
                engineAudio.Play();
        }

        void Respawn()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = spawnPosition;
            body.rotation = spawnRotation;
            steeringInput = 0f;
            wasGrabbing = false;
        }

        bool TryGetHandAngle(out float angle)
        {
            angle = 0f;
            if (!TryGetPosition(leftPositionAction, XRNode.LeftHand, out Vector3 left) ||
                !TryGetPosition(rightPositionAction, XRNode.RightHand, out Vector3 right))
                return false;

            Vector3 line = transform.InverseTransformDirection(right - left);
            if (line.sqrMagnitude < 0.0001f)
                return false;

            angle = Mathf.Atan2(line.y, line.x) * Mathf.Rad2Deg;
            return true;
        }

        void CacheInputActions()
        {
            if (xrInputActions == null)
                return;

            leftPositionAction = xrInputActions.FindAction("XRI Left/Position", false);
            rightPositionAction = xrInputActions.FindAction("XRI Right/Position", false);
            leftGripAction = xrInputActions.FindAction("XRI Left Interaction/Select Value", false);
            rightGripAction = xrInputActions.FindAction("XRI Right Interaction/Select Value", false);
            throttleAction = xrInputActions.FindAction("XRI Right Interaction/Activate Value", false);
            brakeAction = xrInputActions.FindAction("XRI Left Interaction/Activate Value", false);
        }

        bool ReadGrip(InputAction action, XRNode node)
        {
            return ReadAxis(action, node, CommonUsages.grip) >= gripThreshold;
        }

        bool ReadButton(XRNode node, InputFeatureUsage<bool> usage)
        {
            inputDevices.Clear();
            InputDevices.GetDevicesAtXRNode(node, inputDevices);
            foreach (UnityEngine.XR.InputDevice device in inputDevices)
            {
                if (device.TryGetFeatureValue(usage, out bool value))
                    return value;
            }

            return false;
        }

        bool TryGetPosition(InputAction action, XRNode node, out Vector3 position)
        {
            if (action != null && action.enabled)
            {
                position = action.ReadValue<Vector3>();
                return true;
            }

            inputDevices.Clear();
            InputDevices.GetDevicesAtXRNode(node, inputDevices);
            foreach (UnityEngine.XR.InputDevice device in inputDevices)
            {
                if (device.TryGetFeatureValue(CommonUsages.devicePosition, out position))
                    return true;
            }

            position = default;
            return false;
        }

        float ReadAxis(InputAction action, XRNode node, InputFeatureUsage<float> usage)
        {
            if (action != null && action.enabled)
            {
                float actionValue = action.ReadValue<float>();
                if (actionValue > 0.001f)
                    return actionValue;
            }

            inputDevices.Clear();
            InputDevices.GetDevicesAtXRNode(node, inputDevices);
            foreach (UnityEngine.XR.InputDevice device in inputDevices)
            {
                if (device.TryGetFeatureValue(usage, out float value))
                    return value;
            }

            return 0f;
        }
    }
}
