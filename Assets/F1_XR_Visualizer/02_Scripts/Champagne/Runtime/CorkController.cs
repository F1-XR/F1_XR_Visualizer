using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace F1XR.Champagne
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CorkController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Rigidbody corkBody;
        [SerializeField] Collider[] corkColliders;

        [Header("Launch")]
        [SerializeField] float corkLaunchImpulse = 1.6f;
        [SerializeField] float corkLaunchRandomness = 0.08f;
        [SerializeField] float corkSpinImpulse = 0.15f;
        [SerializeField] float corkMass = 0.035f;
        [SerializeField] float corkDrag = 0.08f;
        [SerializeField] float corkAngularDrag = 0.15f;
        [SerializeField, Range(0f, 1f)] float corkBounciness = 0.25f;
        [SerializeField] float corkLifetime = 8f;
        [SerializeField] bool destroyOrDisableCorkAfterLifetime;
        [SerializeField] bool useBottleRecoil = true;
        [SerializeField] float bottleRecoilImpulse = 0.08f;
        [SerializeField] float ignoreBottleCollisionDuration = 0.25f;
        [SerializeField] LayerMask corkCollisionLayerMask = ~0;

        [Header("Debug")]
        [SerializeField] bool debugLogs;

        readonly List<(Collider cork, Collider bottle)> ignoredPairs = new();
        Transform initialParent;
        Vector3 initialLocalPosition;
        Quaternion initialLocalRotation;
        bool initialColliderEnabled = true;
        Coroutine restoreCollisionRoutine;
        Coroutine lifetimeRoutine;

        void Reset()
        {
            corkBody = GetComponent<Rigidbody>();
            corkColliders = GetComponentsInChildren<Collider>(includeInactive: true);
        }

        void Awake()
        {
            if (corkBody == null)
                corkBody = GetComponent<Rigidbody>();

            if (corkColliders == null || corkColliders.Length == 0)
                corkColliders = GetComponentsInChildren<Collider>(includeInactive: true);

            initialParent = transform.parent;
            initialLocalPosition = transform.localPosition;
            initialLocalRotation = transform.localRotation;
            initialColliderEnabled = corkColliders == null || corkColliders.Length == 0 || corkColliders[0].enabled;
            ConfigureBody();
            ResetCork();
        }

        void ConfigureBody()
        {
            if (corkBody == null)
                return;

            corkBody.mass = Mathf.Max(0.001f, corkMass);
            corkBody.linearDamping = Mathf.Max(0f, corkDrag);
            corkBody.angularDamping = Mathf.Max(0f, corkAngularDrag);
            corkBody.interpolation = RigidbodyInterpolation.Interpolate;
            corkBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        public void Launch(Vector3 launchDirection, Rigidbody bottleBody, Collider[] bottleColliders)
        {
            if (corkBody == null)
                return;

            ConfigureBody();
            gameObject.SetActive(true);
            transform.SetParent(null, true);
            SetCollidersEnabled(true);
            IgnoreBottleCollision(bottleColliders, true);

            corkBody.isKinematic = false;
            corkBody.linearVelocity = Vector3.zero;
            corkBody.angularVelocity = Vector3.zero;

            var direction = launchDirection.sqrMagnitude > 0.0001f ? launchDirection.normalized : transform.forward;
            direction = (direction + Random.insideUnitSphere * corkLaunchRandomness).normalized;
            corkBody.AddForce(direction * corkLaunchImpulse, ForceMode.Impulse);
            corkBody.AddTorque(Random.insideUnitSphere * corkSpinImpulse, ForceMode.Impulse);

            if (useBottleRecoil && bottleBody != null)
                bottleBody.AddForce(-direction * bottleRecoilImpulse, ForceMode.Impulse);

            if (restoreCollisionRoutine != null)
                StopCoroutine(restoreCollisionRoutine);
            restoreCollisionRoutine = StartCoroutine(RestoreCollisionAfterDelay());

            if (lifetimeRoutine != null)
                StopCoroutine(lifetimeRoutine);
            if (corkLifetime > 0f)
                lifetimeRoutine = StartCoroutine(HandleLifetime());

            if (debugLogs)
                Debug.Log($"[ChampagneCork] launched direction={direction}", this);
        }

        IEnumerator RestoreCollisionAfterDelay()
        {
            yield return new WaitForSeconds(ignoreBottleCollisionDuration);
            IgnoreBottleCollision(null, false);
            restoreCollisionRoutine = null;
        }

        IEnumerator HandleLifetime()
        {
            yield return new WaitForSeconds(corkLifetime);

            if (destroyOrDisableCorkAfterLifetime)
                gameObject.SetActive(false);

            lifetimeRoutine = null;
        }

        void IgnoreBottleCollision(Collider[] bottleColliders, bool ignore)
        {
            if (!ignore)
            {
                foreach (var pair in ignoredPairs)
                {
                    if (pair.cork != null && pair.bottle != null)
                        Physics.IgnoreCollision(pair.cork, pair.bottle, false);
                }

                ignoredPairs.Clear();
                return;
            }

            if (corkColliders == null || bottleColliders == null)
                return;

            foreach (var corkCollider in corkColliders)
            {
                if (corkCollider == null)
                    continue;

                foreach (var bottleCollider in bottleColliders)
                {
                    if (bottleCollider == null)
                        continue;

                    Physics.IgnoreCollision(corkCollider, bottleCollider, true);
                    ignoredPairs.Add((corkCollider, bottleCollider));
                }
            }
        }

        public void ResetCork()
        {
            if (restoreCollisionRoutine != null)
            {
                StopCoroutine(restoreCollisionRoutine);
                restoreCollisionRoutine = null;
            }

            if (lifetimeRoutine != null)
            {
                StopCoroutine(lifetimeRoutine);
                lifetimeRoutine = null;
            }

            IgnoreBottleCollision(null, false);
            gameObject.SetActive(true);
            transform.SetParent(initialParent, false);
            transform.localPosition = initialLocalPosition;
            transform.localRotation = initialLocalRotation;

            if (corkBody != null)
            {
                corkBody.isKinematic = true;
                corkBody.linearVelocity = Vector3.zero;
                corkBody.angularVelocity = Vector3.zero;
            }

            SetCollidersEnabled(initialColliderEnabled);
        }

        void SetCollidersEnabled(bool enabled)
        {
            if (corkColliders == null)
                return;

            foreach (var corkCollider in corkColliders)
            {
                if (corkCollider == null)
                    continue;

                corkCollider.enabled = enabled;
                corkCollider.sharedMaterial ??= new PhysicsMaterial("Champagne Cork Runtime")
                {
                    bounciness = corkBounciness,
                    dynamicFriction = 0.45f,
                    staticFriction = 0.5f,
                    bounceCombine = PhysicsMaterialCombine.Average
                };
            }
        }
    }
}
