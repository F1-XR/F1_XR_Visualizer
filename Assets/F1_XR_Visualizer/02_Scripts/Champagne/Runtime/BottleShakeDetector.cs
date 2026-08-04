using UnityEngine;

namespace F1XR.Champagne
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BottleShakeDetector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Rigidbody targetBody;

        [Header("Shake Detection")]
        [SerializeField] float shakeAccelerationThreshold = 12f;
        [SerializeField] float shakeAngularVelocityThreshold = 5f;
        [SerializeField] float minimumVelocityForDirectionChange = 0.25f;
        [SerializeField, Range(-1f, 1f)] float directionChangeDotThreshold = -0.35f;
        [SerializeField, Min(1)] int requiredShakeDirectionChanges = 2;
        [SerializeField, Min(0.05f)] float directionChangeTimeWindow = 0.35f;
        [SerializeField] float pressureGainRate = 0.7f;
        [SerializeField] float pressureDecayRate = 0.12f;
        [SerializeField, Range(0f, 1f)] float smoothingFactor = 0.35f;
        [SerializeField] float maximumAccelerationSample = 60f;
        [SerializeField] bool onlyDetectShakeWhileGrabbed = true;

        [Header("Pressure")]
        [SerializeField, Range(0f, 1f)] float minimumPopPressure = 0.65f;
        [SerializeField, Range(0f, 1f)] float currentPressure;

        [Header("Debug")]
        [SerializeField] bool debugLogs;
        [SerializeField] float currentShakeStrength;
        [SerializeField] int recentDirectionReversalCount;
        [SerializeField] bool currentlyGrabbed;
        [SerializeField] bool canAccumulatePressure = true;

        Vector3 previousVelocity;
        Vector3 previousDirection;
        float smoothedAcceleration;
        float directionWindowStartedAt = -1f;

        public float CurrentPressure => currentPressure;
        public float CurrentShakeStrength => currentShakeStrength;
        public int RecentDirectionReversalCount => recentDirectionReversalCount;
        public bool CurrentlyGrabbed => currentlyGrabbed;
        public float MinimumPopPressure => minimumPopPressure;

        void Reset()
        {
            targetBody = GetComponent<Rigidbody>();
        }

        void Awake()
        {
            if (targetBody == null)
                targetBody = GetComponent<Rigidbody>();

            previousVelocity = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
        }

        void FixedUpdate()
        {
            if (targetBody == null)
                return;

            var velocity = targetBody.linearVelocity;
            var acceleration = (velocity - previousVelocity) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            var accelerationMagnitude = Mathf.Min(acceleration.magnitude, maximumAccelerationSample);
            smoothedAcceleration = Mathf.Lerp(smoothedAcceleration, accelerationMagnitude, smoothingFactor);

            UpdateDirectionReversals(velocity);
            UpdateShakeStrength(velocity);
            UpdatePressure();

            previousVelocity = velocity;
        }

        void UpdateDirectionReversals(Vector3 velocity)
        {
            if (velocity.magnitude < minimumVelocityForDirectionChange)
                return;

            var currentDirection = velocity.normalized;
            if (previousDirection.sqrMagnitude > 0.0001f &&
                Vector3.Dot(previousDirection, currentDirection) <= directionChangeDotThreshold)
            {
                if (directionWindowStartedAt < 0f || Time.time - directionWindowStartedAt > directionChangeTimeWindow)
                {
                    directionWindowStartedAt = Time.time;
                    recentDirectionReversalCount = 1;
                }
                else
                {
                    recentDirectionReversalCount++;
                }
            }

            if (directionWindowStartedAt >= 0f && Time.time - directionWindowStartedAt > directionChangeTimeWindow)
            {
                directionWindowStartedAt = -1f;
                recentDirectionReversalCount = 0;
            }

            previousDirection = currentDirection;
        }

        void UpdateShakeStrength(Vector3 velocity)
        {
            currentShakeStrength = 0f;

            if (!canAccumulatePressure)
                return;

            if (onlyDetectShakeWhileGrabbed && !currentlyGrabbed)
                return;

            if (velocity.magnitude < minimumVelocityForDirectionChange)
                return;

            if (smoothedAcceleration < shakeAccelerationThreshold)
                return;

            if (targetBody.angularVelocity.magnitude > shakeAngularVelocityThreshold &&
                recentDirectionReversalCount < requiredShakeDirectionChanges)
            {
                return;
            }

            if (recentDirectionReversalCount < requiredShakeDirectionChanges)
                return;

            currentShakeStrength = Mathf.InverseLerp(
                shakeAccelerationThreshold,
                maximumAccelerationSample,
                smoothedAcceleration);
        }

        void UpdatePressure()
        {
            var delta = currentShakeStrength > 0f
                ? currentShakeStrength * pressureGainRate * Time.fixedDeltaTime
                : -pressureDecayRate * Time.fixedDeltaTime;

            currentPressure = Mathf.Clamp01(currentPressure + delta);
        }

        public void SetGrabbed(bool grabbed)
        {
            currentlyGrabbed = grabbed;
            if (debugLogs)
                Debug.Log($"[ChampagneShake] grabbed={grabbed}", this);
        }

        public void SetCanAccumulatePressure(bool enabled)
        {
            canAccumulatePressure = enabled;
            if (!enabled)
                currentShakeStrength = 0f;
        }

        public void SetPressure(float normalizedPressure)
        {
            currentPressure = Mathf.Clamp01(normalizedPressure);
        }

        public void ResetPressure()
        {
            currentPressure = 0f;
            currentShakeStrength = 0f;
            recentDirectionReversalCount = 0;
            directionWindowStartedAt = -1f;
            previousDirection = Vector3.zero;
            previousVelocity = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
        }
    }
}
