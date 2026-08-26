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
        [SerializeField, Min(1f)] float accelerationForce = 11000f;
        [SerializeField, Min(1f)] float brakeForce = 12000f;
        [SerializeField, Min(1f)] float maximumSpeedKph = 370f;
        [SerializeField, Min(1f)] float steeringSpeedDegrees = 75f;
        [SerializeField, Range(0f, 1f)] float gripThreshold = 0.55f;
        [SerializeField, Min(0f)] float reverseHoldDelay = 1f;
        [SerializeField, Min(0.1f)] float reverseSpeedMps = 2f;

        [Header("Handling")]
        [SerializeField, Min(0f)] float lateralGrip = 12f;
        [SerializeField, Min(0f)] float rollingResistance = 0.08f;

        [Header("Presentation")]
        [SerializeField] Transform steeringWheel;
        [SerializeField] Transform[] frontWheelPivots;
        [SerializeField] Transform[] wheelVisuals;
        [SerializeField, Min(1f)] float maximumVisualWheelAngle = 32f;
        [SerializeField, Min(1f)] float maximumSteeringWheelAngle = 120f;
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
        InputAction directThrottleAction;
        Transform leftControllerTransform;
        Transform rightControllerTransform;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        float grabStartAngle;
        float steeringAtGrabStart;
        float steeringInput;
        float throttleInput;
        float brakeInput;
        float reverseHoldStartedAt = -1f;
        bool isSteeringGrabbing;
        bool isReversing;
        bool wasGrabbing;
        bool respawnPressed;
        bool steeringWheelPivotCentered;
        Vector3 previousPosition;
        float lastGroundedTime = float.NegativeInfinity;
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
            ConfigureGroundContact();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            previousPosition = transform.position;
            CacheWheelVisuals();

            if (engineAudio == null)
                engineAudio = GetComponentInChildren<AudioSource>();

            CacheInputActions();
            respawnAction = new InputAction(
                "Respawn",
                InputActionType.Button,
                "<XRController>{RightHand}/primaryButton");
            directThrottleAction = new InputAction(
                "Drive Throttle",
                InputActionType.Value,
                "<XRController>{RightHand}/{Trigger}");
        }

        public float SpeedKph => body == null ? 0f : body.linearVelocity.magnitude * 3.6f;
        public float ForwardSpeedMps => body == null
            ? 0f
            : Vector3.Dot(body.linearVelocity, transform.forward);
        public float ThrottleInput => throttleInput;
        public float BrakeInput => brakeInput;
        public bool IsReversing => isReversing;
        public float SteeringInput => steeringInput;
        public Vector3 Velocity => body == null ? Vector3.zero : body.linearVelocity;
        public Vector3 AngularVelocity => body == null ? Vector3.zero : body.angularVelocity;
        public bool IsGrounded => Time.time - lastGroundedTime <= 0.1f;
        public bool IsThrottleActionEnabled => throttleAction?.enabled ?? false;
        public bool IsDirectThrottleActionEnabled => directThrottleAction?.enabled ?? false;
        public bool IsSteeringGrabbing => isSteeringGrabbing;

        void DetachXrOriginFromVehicle()
        {
            XROrigin xrOrigin = GetComponentInChildren<XROrigin>(true);
            Transform seatAnchor = transform.Find("DriverSeatAnchor");
            if (xrOrigin == null || seatAnchor == null)
                return;

            leftControllerTransform = FindChildTransform(xrOrigin.transform, "Left Controller");
            rightControllerTransform = FindChildTransform(xrOrigin.transform, "Right Controller");
            xrOrigin.transform.SetParent(null, true);

            VehicleSeatFollower follower = xrOrigin.GetComponent<VehicleSeatFollower>();
            if (follower == null)
                follower = xrOrigin.gameObject.AddComponent<VehicleSeatFollower>();

            follower.Configure(seatAnchor);
        }

        void ConfigureGroundContact()
        {
            Collider vehicleCollider = GetComponent<Collider>();
            if (vehicleCollider == null)
                return;

            vehicleCollider.material = new PhysicsMaterial("Driving Vehicle Contact")
            {
                staticFriction = 0f,
                dynamicFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
            };
        }

        void OnEnable()
        {
            respawnAction?.Enable();
            directThrottleAction?.Enable();
        }

        void OnDisable()
        {
            respawnAction?.Disable();
            directThrottleAction?.Disable();
        }

        void OnDestroy()
        {
            respawnAction?.Dispose();
            directThrottleAction?.Dispose();
        }

        void Start()
        {
#if UNITY_EDITOR
            Debug.Log(
                $"[DrivingTest] Input ready: throttle action=" +
                $"{(throttleAction != null)}, enabled={throttleAction?.enabled ?? false}, " +
                $"directThrottleEnabled={directThrottleAction?.enabled ?? false}, " +
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
            throttleInput = Mathf.Max(
                ReadAxis(throttleAction, XRNode.RightHand, CommonUsages.trigger),
                ReadDirectThrottle());
            brakeInput = ReadAxis(brakeAction, XRNode.LeftHand, CommonUsages.trigger);
            UpdateReverseState();
#if UNITY_EDITOR
            UpdateThrottleDiagnostic();
#endif

            isSteeringGrabbing = ReadGrip(leftGripAction, XRNode.LeftHand) &&
                ReadGrip(rightGripAction, XRNode.RightHand);
            if (isSteeringGrabbing && !wasGrabbing && TryGetHandAngle(out float handAngle))
            {
                grabStartAngle = handAngle;
                steeringAtGrabStart = steeringInput;
            }

            if (isSteeringGrabbing && TryGetHandAngle(out handAngle))
            {
                float delta = Mathf.DeltaAngle(grabStartAngle, handAngle);
                steeringInput = Mathf.Clamp(steeringAtGrabStart - delta / 120f, -1f, 1f);
            }
            else
            {
                steeringInput = Mathf.MoveTowards(steeringInput, 0f, Time.deltaTime * 3f);
            }

            wasGrabbing = isSteeringGrabbing;
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

            if (isReversing)
            {
                ApplyReverseMotion();
            }
            else if (throttleInput > 0f && forwardSpeed < maximumSpeed)
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

            if (!isReversing && brakeInput > 0f && body.linearVelocity.sqrMagnitude > 0.0001f)
            {
                body.AddForce(-body.linearVelocity.normalized * (brakeInput * brakeForce),
                    ForceMode.Force);
            }

            if (IsGrounded && !isReversing)
                ApplyGroundHandling();

            float speedRatio = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 4f);
            float yaw = steeringInput * steeringSpeedDegrees * speedRatio * Time.fixedDeltaTime;
            body.MoveRotation(body.rotation * Quaternion.Euler(0f, yaw, 0f));
        }

        void UpdateReverseState()
        {
            if (brakeInput <= 0.1f)
            {
                reverseHoldStartedAt = -1f;
                isReversing = false;
                return;
            }

            if (reverseHoldStartedAt < 0f)
                reverseHoldStartedAt = Time.time;

            isReversing = Time.time - reverseHoldStartedAt >= reverseHoldDelay;
        }

        void ApplyReverseMotion()
        {
            float verticalSpeed = Vector3.Dot(body.linearVelocity, transform.up);
            body.linearVelocity = -transform.forward * reverseSpeedMps +
                transform.up * verticalSpeed;
        }

        void ApplyGroundHandling()
        {
            float lateralSpeed = Vector3.Dot(body.linearVelocity, transform.right);
            body.AddForce(-transform.right * (lateralSpeed * lateralGrip),
                ForceMode.Acceleration);

            float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
            body.AddForce(-transform.forward * (forwardSpeed * rollingResistance),
                ForceMode.Acceleration);
        }

        void UpdatePresentation()
        {
            if (steeringWheel == null)
                steeringWheel = FindChildTransform(transform, "SteeringWheel");

            if (steeringWheel != null)
            {
                if (!steeringWheelPivotCentered)
                {
                    CenterVisualPivot(steeringWheel);
                    steeringWheelPivotCentered = true;
                }

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

        void CacheWheelVisuals()
        {
            if (wheelVisuals != null && wheelVisuals.Length > 0)
                return;

            Transform visualRoot = transform.Find("VehicleVisual");
            if (visualRoot == null)
                return;

            string[] wheelNames =
            {
                "FrontLeftTyre", "FrontRightTyre", "RearLeftTyre", "RearRightTyre",
            };
            List<Transform> wheels = new();
            foreach (string wheelName in wheelNames)
            {
                Transform wheel = FindChildTransform(visualRoot, wheelName);
                if (wheel == null)
                    continue;

                CenterVisualPivot(wheel);
                wheels.Add(wheel);
            }

            wheelVisuals = wheels.ToArray();
        }

        static void CenterVisualPivot(Transform pivot)
        {
            Renderer[] renderers = pivot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            List<Transform> children = new();
            foreach (Transform child in pivot)
                children.Add(child);

            foreach (Transform child in children)
                child.SetParent(pivot.parent, true);

            pivot.position = bounds.center;

            foreach (Transform child in children)
                child.SetParent(pivot, true);
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

        void OnCollisionStay(Collision collision)
        {
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (Vector3.Dot(collision.GetContact(i).normal, transform.up) > 0.5f)
                {
                    lastGroundedTime = Time.time;
                    return;
                }
            }
        }

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
            if (!TryGetSteeringHandPositions(out Vector3 left, out Vector3 right))
                return false;

            Vector3 line = transform.InverseTransformDirection(right - left);
            if (line.sqrMagnitude < 0.0001f)
                return false;

            angle = Mathf.Atan2(line.y, line.x) * Mathf.Rad2Deg;
            return true;
        }

        public bool TryGetSteeringHandPositions(out Vector3 left, out Vector3 right)
        {
            if (leftControllerTransform != null && rightControllerTransform != null)
            {
                left = leftControllerTransform.position;
                right = rightControllerTransform.position;
                return true;
            }

            right = default;
            return TryGetPosition(leftPositionAction, XRNode.LeftHand, out left) &&
                TryGetPosition(rightPositionAction, XRNode.RightHand, out right);
        }

        static Transform FindChildTransform(Transform root, string childName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                    return child;
            }

            return null;
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

        float ReadDirectThrottle()
        {
            return directThrottleAction != null && directThrottleAction.enabled
                ? directThrottleAction.ReadValue<float>()
                : 0f;
        }
    }
}
