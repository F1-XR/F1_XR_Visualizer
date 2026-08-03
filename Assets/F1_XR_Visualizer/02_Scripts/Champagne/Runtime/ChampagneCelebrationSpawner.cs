using UnityEngine;

namespace F1XR.Champagne
{
    [DisallowMultipleComponent]
    public sealed class ChampagneCelebrationSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ChampagneBottleController champagneInstance;
        [SerializeField] private ChampagneBottleController champagnePrefab;
        [SerializeField] private GameObject tableInstance;
        [SerializeField] private GameObject tablePrefab;
        [SerializeField] private Transform xrOriginTransform;
        [SerializeField] private Transform playerHeadTransform;
        [SerializeField] private Transform testFallbackSpawnTransform;

        [Header("Spawn")]
        [SerializeField] private float spawnDistance = 0.5f;
        [SerializeField] private float spawnHeightOffset = 0.8f;
        [SerializeField] private float headFallbackHeightOffset = -0.45f;
        [SerializeField] private bool faceBottleTowardPlayer = true;
        [SerializeField] private Vector3 spawnRotationOffset;
        [SerializeField] private Vector3 bottleLocalOffset;
        [SerializeField] private Vector3 tableLocalOffset = new Vector3(0f, -0.08f, 0f);

        [Header("Lifecycle")]
        [SerializeField] private bool usePreloadedInstance = true;
        [SerializeField] private bool spawnTableWithBottle = true;
        [SerializeField] private bool hideOnCelebrationEnd = true;
        [SerializeField] private bool resetOnCelebrationEnd = true;
        [SerializeField] private bool disableRigidbodyBeforeSpawn = true;

        [Header("Spawn Validation")]
        [SerializeField] private bool validateSpawnPosition = true;
        [SerializeField] private float spawnValidationRadius = 0.2f;
        [SerializeField] private LayerMask spawnObstacleLayerMask = ~0;
        [SerializeField] private int maximumFallbackAttempts = 4;
        [SerializeField] private float fallbackSideOffset = 0.35f;
        [SerializeField] private float fallbackForwardOffset = 0.25f;

        [Header("Runtime Debug")]
        [SerializeField] private bool debugLogging;
        [SerializeField] private bool hasSpawnedForCurrentCelebration;

        private Rigidbody champagneBody;

        private void Awake()
        {
            ResolvePlayerReferences();
            PrepareHiddenObjects();
        }

        public void OnCheckeredFlagShown()
        {
            if (hasSpawnedForCurrentCelebration)
                return;

            ResolvePlayerReferences();

            ChampagneBottleController bottle = ResolveBottle();
            if (bottle == null)
            {
                Debug.LogWarning("[ChampagneSpawner] Champagne bottle instance or prefab is missing.", this);
                return;
            }

            if (!TryGetPlayerPose(out Vector3 playerPosition, out Vector3 playerForward))
            {
                Debug.LogWarning("[ChampagneSpawner] Player reference is missing. Assign XR Origin, player head, or test fallback transform.", this);
                return;
            }

            Quaternion spawnRotation = BuildSpawnRotation(playerForward);
            Vector3 spawnPosition = ResolveSpawnPosition(playerPosition, playerForward);
            Vector3 bottlePosition = spawnPosition + spawnRotation * bottleLocalOffset;

            GameObject table = ResolveTable();
            if (table != null)
            {
                table.transform.SetPositionAndRotation(spawnPosition + spawnRotation * tableLocalOffset, spawnRotation);
                table.SetActive(true);
            }

            bottle.ShowBottle();
            bottle.ResetBottle();
            bottle.transform.SetPositionAndRotation(bottlePosition, spawnRotation);
            bottle.SetInteractionEnabled(true);
            SetBottleBodySimulated(bottle, true);

            hasSpawnedForCurrentCelebration = true;

            if (debugLogging)
                Debug.Log($"[ChampagneSpawner] Spawned at {bottlePosition} with table={(table != null)}", this);
        }

        public void EndCelebration()
        {
            if (champagneInstance != null)
            {
                champagneInstance.SetInteractionEnabled(false);
                if (resetOnCelebrationEnd)
                    champagneInstance.ResetBottle();

                SetBottleBodySimulated(champagneInstance, false);

                if (hideOnCelebrationEnd)
                    champagneInstance.HideBottle();
            }

            if (tableInstance != null && hideOnCelebrationEnd)
                tableInstance.SetActive(false);
        }

        public void ResetCelebration()
        {
            hasSpawnedForCurrentCelebration = false;
            EndCelebration();
        }

        [ContextMenu("Simulate Checkered Flag Shown")]
        private void SimulateCheckeredFlagShown()
        {
            OnCheckeredFlagShown();
        }

        private void ResolvePlayerReferences()
        {
            if (playerHeadTransform == null && Camera.main != null)
                playerHeadTransform = Camera.main.transform;

            if (xrOriginTransform == null && playerHeadTransform != null && playerHeadTransform.root != playerHeadTransform)
                xrOriginTransform = playerHeadTransform.root;
        }

        private void PrepareHiddenObjects()
        {
            if (champagneInstance != null)
            {
                champagneInstance.SetInteractionEnabled(false);
                SetBottleBodySimulated(champagneInstance, false);
                champagneInstance.HideBottle();
            }

            if (tableInstance != null)
                tableInstance.SetActive(false);
        }

        private ChampagneBottleController ResolveBottle()
        {
            if (champagneInstance != null)
                return champagneInstance;

            if (champagnePrefab == null || usePreloadedInstance)
                return null;

            champagneInstance = Instantiate(champagnePrefab);
            champagneInstance.name = champagnePrefab.name;
            champagneInstance.SetInteractionEnabled(false);
            SetBottleBodySimulated(champagneInstance, false);
            champagneInstance.HideBottle();
            return champagneInstance;
        }

        private GameObject ResolveTable()
        {
            if (!spawnTableWithBottle)
                return null;

            if (tableInstance != null)
                return tableInstance;

            if (tablePrefab == null || usePreloadedInstance)
                return null;

            tableInstance = Instantiate(tablePrefab);
            tableInstance.name = tablePrefab.name;
            tableInstance.SetActive(false);
            return tableInstance;
        }

        private bool TryGetPlayerPose(out Vector3 playerPosition, out Vector3 playerForward)
        {
            Transform forwardReference = playerHeadTransform != null ? playerHeadTransform : xrOriginTransform;
            if (forwardReference == null)
                forwardReference = testFallbackSpawnTransform;

            if (forwardReference == null)
            {
                playerPosition = Vector3.zero;
                playerForward = Vector3.forward;
                return false;
            }

            Vector3 forward = Vector3.ProjectOnPlane(forwardReference.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f && xrOriginTransform != null)
                forward = Vector3.ProjectOnPlane(xrOriginTransform.forward, Vector3.up);

            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            playerForward = forward.normalized;

            if (xrOriginTransform != null)
                playerPosition = xrOriginTransform.position + Vector3.up * spawnHeightOffset;
            else if (playerHeadTransform != null)
                playerPosition = playerHeadTransform.position + Vector3.up * headFallbackHeightOffset;
            else
                playerPosition = testFallbackSpawnTransform.position;

            return true;
        }

        private Vector3 ResolveSpawnPosition(Vector3 playerPosition, Vector3 playerForward)
        {
            Vector3 right = Vector3.Cross(Vector3.up, playerForward).normalized;
            Vector3 preferred = playerPosition - playerForward * spawnDistance;

            if (IsSpawnPositionClear(preferred))
                return preferred;

            Vector3[] fallbacks =
            {
                preferred - right * fallbackSideOffset,
                preferred + right * fallbackSideOffset,
                preferred + playerForward * fallbackForwardOffset,
                preferred - playerForward * fallbackForwardOffset,
                preferred - right * fallbackSideOffset + playerForward * fallbackForwardOffset,
                preferred + right * fallbackSideOffset + playerForward * fallbackForwardOffset
            };

            int attempts = Mathf.Min(maximumFallbackAttempts, fallbacks.Length);
            for (int i = 0; i < attempts; i++)
            {
                if (IsSpawnPositionClear(fallbacks[i]))
                    return fallbacks[i];
            }

            if (testFallbackSpawnTransform != null)
                return testFallbackSpawnTransform.position;

            return preferred;
        }

        private bool IsSpawnPositionClear(Vector3 position)
        {
            if (!validateSpawnPosition)
                return true;

            return !Physics.CheckSphere(
                position,
                Mathf.Max(0.01f, spawnValidationRadius),
                spawnObstacleLayerMask,
                QueryTriggerInteraction.Ignore);
        }

        private Quaternion BuildSpawnRotation(Vector3 playerForward)
        {
            Vector3 forward = faceBottleTowardPlayer ? playerForward : -playerForward;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            return Quaternion.LookRotation(forward.normalized, Vector3.up) * Quaternion.Euler(spawnRotationOffset);
        }

        private void SetBottleBodySimulated(ChampagneBottleController bottle, bool simulated)
        {
            if (!disableRigidbodyBeforeSpawn || bottle == null)
                return;

            if (champagneBody == null || champagneBody.gameObject != bottle.gameObject)
                champagneBody = bottle.GetComponent<Rigidbody>();

            if (champagneBody == null)
                return;

            if (!simulated)
            {
                champagneBody.linearVelocity = Vector3.zero;
                champagneBody.angularVelocity = Vector3.zero;
            }

            champagneBody.isKinematic = !simulated;
            champagneBody.detectCollisions = simulated;
        }
    }
}
