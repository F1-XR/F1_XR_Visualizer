using UnityEngine;

namespace F1XR.Interaction.Input
{
    /// <summary>
    /// Low-pass filters this object's pose so it lags slightly behind its parent instead of
    /// rigidly snapping to it. Dampens the visible tremble/slide when a controller re-acquires
    /// tracking, at the cost of a tiny bit of latency. Only the object this is on is smoothed —
    /// siblings (the ray interactor) keep following the controller rigidly, so aiming stays snappy.
    ///
    /// It captures its local offset to the parent at start, then each LateUpdate drives its world
    /// pose toward parent * offset with critically-damped position smoothing and exponential
    /// rotation smoothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoseSmoothingFollow : MonoBehaviour
    {
        [Tooltip("Approx seconds for the position to catch up. Larger = smoother but laggier. 0 = off.")]
        [SerializeField, Min(0f)] float positionSmoothTime = 0.06f;
        [Tooltip("Rotation catch-up speed (higher = snappier). ~15-25 feels responsive, lower = softer.")]
        [SerializeField, Min(0f)] float rotationLerpSpeed = 18f;
        [Tooltip("If the target jumps farther than this (meters) in one frame, snap instead of smooth " +
            "(avoids a long slow glide after a big teleport / first acquisition).")]
        [SerializeField, Min(0f)] float snapDistance = 0.5f;

        Transform parent;
        Vector3 offsetPos;
        Quaternion offsetRot;
        Vector3 velocity;
        bool initialized;

        void OnEnable()
        {
            parent = transform.parent;
            offsetPos = transform.localPosition;
            offsetRot = transform.localRotation;
            initialized = false; // snap to target on first frame
        }

        void LateUpdate()
        {
            if (parent == null)
                return;

            Vector3 targetPos = parent.TransformPoint(offsetPos);
            Quaternion targetRot = parent.rotation * offsetRot;

            if (!initialized || positionSmoothTime <= 0f ||
                (transform.position - targetPos).sqrMagnitude > snapDistance * snapDistance)
            {
                transform.SetPositionAndRotation(targetPos, targetRot);
                velocity = Vector3.zero;
                initialized = true;
                return;
            }

            Vector3 pos = Vector3.SmoothDamp(transform.position, targetPos, ref velocity, positionSmoothTime);
            float t = 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime);
            Quaternion rot = Quaternion.Slerp(transform.rotation, targetRot, t);
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
