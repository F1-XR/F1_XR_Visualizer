using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.AR
{
    public sealed class ScaleController : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] float grabRadius = 0.2f;
        [SerializeField] float pinchStartDistance = 0.035f;
        [SerializeField] float pinchEndDistance = 0.05f;
        [SerializeField] float minScale = 0.02f;
        [SerializeField] float maxScale = 5f;
        [SerializeField] XRGrabInteractable grab;

        static readonly List<XRHandSubsystem> HandSubsystems = new();

        XRHandSubsystem handSubsystem;
        bool scaling;
        bool leftPinching;
        bool rightPinching;
        bool hadGrab;
        bool wasGrabEnabled;
        bool waitForPinchRelease;
        float startHandDistance;
        Vector3 startHandVector;
        Vector3 startScale;

        void Awake()
        {
            if (target == null)
                target = transform;

            if (grab == null)
                grab = GetComponent<XRGrabInteractable>();
        }

        void OnEnable()
        {
            FindHandSubsystem();
        }

        void OnDisable()
        {
            StopScaling();
        }

        void Update()
        {
            if (handSubsystem == null || !handSubsystem.running)
                FindHandSubsystem();

            if (handSubsystem == null)
                return;

            var hasLeft = TryGetHandPoint(handSubsystem.leftHand, out var leftPoint);
            var hasRight = TryGetHandPoint(handSubsystem.rightHand, out var rightPoint);
            var hasLeftGrab = TryGetPinchPoint(handSubsystem.leftHand, out var leftGrabPoint);
            var hasRightGrab = TryGetPinchPoint(handSubsystem.rightHand, out var rightGrabPoint);

            leftPinching = hasLeftGrab && IsPinching(handSubsystem.leftHand, leftPinching);
            rightPinching = hasRightGrab && IsPinching(handSubsystem.rightHand, rightPinching);

            if (!leftPinching || !rightPinching)
            {
                waitForPinchRelease = false;
                StopScaling();
                return;
            }

            if (waitForPinchRelease)
                return;

            var handDistance = Vector3.Distance(leftPoint, rightPoint);
            if (handDistance <= Mathf.Epsilon)
                return;

            var handVector = rightPoint - leftPoint;
            if (!scaling)
            {
                if (!IsNearTarget(leftGrabPoint) || !IsNearTarget(rightGrabPoint))
                    return;

                scaling = true;
                startHandDistance = handDistance;
                startHandVector = handVector;
                startScale = target.localScale;
                SetGrabEnabled(false);
                return;
            }

            if (Vector3.Dot(startHandVector.normalized, handVector.normalized) <= 0.25f)
            {
                waitForPinchRelease = true;
                StopScaling();
                return;
            }

            var scaleRatio = handDistance / startHandDistance;
            target.localScale = ClampScale(startScale * scaleRatio);
        }

        void StopScaling()
        {
            if (!scaling)
                return;

            scaling = false;
            SetGrabEnabled(true);
        }

        void SetGrabEnabled(bool enabled)
        {
            if (grab == null)
                return;

            if (!enabled)
            {
                hadGrab = true;
                wasGrabEnabled = grab.enabled;
                grab.enabled = false;
                return;
            }

            if (hadGrab)
                grab.enabled = wasGrabEnabled;

            hadGrab = false;
        }

        void FindHandSubsystem()
        {
            HandSubsystems.Clear();
            SubsystemManager.GetSubsystems(HandSubsystems);

            foreach (var subsystem in HandSubsystems)
            {
                if (subsystem.running)
                {
                    handSubsystem = subsystem;
                    return;
                }
            }
        }

        bool IsNearTarget(Vector3 point)
        {
            return Vector3.Distance(target.position, point) <= grabRadius;
        }

        bool IsPinching(XRHand hand, bool wasPinching)
        {
            if (!TryGetJointPose(hand, XRHandJointID.ThumbTip, out var thumbPose) ||
                !TryGetJointPose(hand, XRHandJointID.IndexTip, out var indexPose))
                return false;

            var distance = Vector3.Distance(thumbPose.position, indexPose.position);
            return wasPinching
                ? distance <= pinchEndDistance
                : distance <= pinchStartDistance;
        }

        static bool TryGetHandPoint(XRHand hand, out Vector3 point)
        {
            if (TryGetJointPose(hand, XRHandJointID.Palm, out var palmPose))
            {
                point = palmPose.position;
                return true;
            }

            if (TryGetJointPose(hand, XRHandJointID.Wrist, out var wristPose))
            {
                point = wristPose.position;
                return true;
            }

            point = default;
            return false;
        }

        static bool TryGetPinchPoint(XRHand hand, out Vector3 point)
        {
            if (!TryGetJointPose(hand, XRHandJointID.ThumbTip, out var thumbPose) ||
                !TryGetJointPose(hand, XRHandJointID.IndexTip, out var indexPose))
            {
                point = default;
                return false;
            }

            point = Vector3.Lerp(thumbPose.position, indexPose.position, 0.5f);
            return true;
        }

        static bool TryGetJointPose(XRHand hand, XRHandJointID jointId, out Pose pose)
        {
            var joint = hand.GetJoint(jointId);
            return joint.TryGetPose(out pose);
        }

        Vector3 ClampScale(Vector3 scale)
        {
            var size = Mathf.Max(scale.x, scale.y, scale.z);
            if (size <= Mathf.Epsilon)
                return scale;

            var ratio = Mathf.Clamp(size, minScale, maxScale) / size;
            return scale * ratio;
        }
    }
}
