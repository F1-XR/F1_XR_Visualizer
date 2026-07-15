using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace F1XR.Interaction.Input
{
    public static class XRHandInput
    {
        private static readonly List<XRHandSubsystem> Subsystems = new();

        public static XRHandSubsystem FindRunningSubsystem()
        {
            Subsystems.Clear();
            SubsystemManager.GetSubsystems(Subsystems);

            foreach (XRHandSubsystem subsystem in Subsystems)
            {
                if (subsystem != null && subsystem.running)
                    return subsystem;
            }

            return null;
        }

        public static bool IsPinching(
            XRHand hand,
            bool wasPinching,
            float startDistance,
            float endDistance)
        {
            if (!TryGetJointPose(hand, XRHandJointID.ThumbTip, out Pose thumbPose) ||
                !TryGetJointPose(hand, XRHandJointID.IndexTip, out Pose indexPose))
                return false;

            float distance = Vector3.Distance(thumbPose.position, indexPose.position);
            return wasPinching
                ? distance <= endDistance
                : distance <= startDistance;
        }

        public static bool TryGetPinchPoint(XRHand hand, out Vector3 point)
        {
            if (!TryGetJointPose(hand, XRHandJointID.ThumbTip, out Pose thumbPose) ||
                !TryGetJointPose(hand, XRHandJointID.IndexTip, out Pose indexPose))
            {
                point = default;
                return false;
            }

            point = Vector3.Lerp(thumbPose.position, indexPose.position, 0.5f);
            return true;
        }

        public static bool TryGetPalmOrWristPoint(XRHand hand, out Vector3 point)
        {
            if (TryGetJointPose(hand, XRHandJointID.Palm, out Pose palmPose))
            {
                point = palmPose.position;
                return true;
            }

            if (TryGetJointPose(hand, XRHandJointID.Wrist, out Pose wristPose))
            {
                point = wristPose.position;
                return true;
            }

            point = default;
            return false;
        }

        public static bool TryGetJointPoint(XRHand hand, XRHandJointID jointId, out Vector3 point)
        {
            if (TryGetJointPose(hand, jointId, out Pose pose))
            {
                point = pose.position;
                return true;
            }

            point = default;
            return false;
        }

        public static bool TryGetJointPose(XRHand hand, XRHandJointID jointId, out Pose pose)
        {
            return hand.GetJoint(jointId).TryGetPose(out pose);
        }
    }
}
