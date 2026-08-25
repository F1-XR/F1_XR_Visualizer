using System;
using F1XR.RestAPI.Replay.Track.Placement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.XR.ARFoundation;

namespace F1XR.RestAPI.Replay.Track.Build
{
    public enum TrackPlacementMode
    {
        TableAutomatic,
        Free,
        Fixed
    }

    public sealed partial class TrackRevealPlacer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] ARPlanePlacementController placementController;
        [SerializeField] ARAnchorManager anchorManager;
        [SerializeField] GameObject placementPrefab;
        [SerializeField] GameObject trackMapPrefab;
        [SerializeField] float trackMapScale = 1f;
        [SerializeField] bool fitTrackMapToBounds;
        [SerializeField] Vector2 trackMapTargetXZSize;

        [Header("Input")]
        [SerializeField] InputActionProperty placeAction;
        [SerializeField] bool useControllerTriggerFallback = true;
        [SerializeField] float inputArmDelay = 0.5f;

        [Header("Preview")]
        [SerializeField] TrackPlacementMode placementMode = TrackPlacementMode.Free;
        [SerializeField] Material previewMaterial;
        [SerializeField] Color previewColor = new Color(0.2f, 1f, 0.35f, 0.35f);
        [FormerlySerializedAs("verticalOffset")]
        [SerializeField, Tooltip("Final gap in metres between the lowest rendered track point and the detected surface.")]
        float surfaceOffset = 0.005f;
        [SerializeField] bool useHitRotation = true;
        [SerializeField] bool alignLongAxisToSurface = true;
        [SerializeField, Min(1f)] float minimumSurfaceAspectRatio = 1.15f;
        [SerializeField, Min(0f)] float surfaceRotationLockDelay = 0.3f;
        [SerializeField, Range(0f, 45f)] float surfaceRotationStabilityAngle = 12f;
        [SerializeField, Min(0f)] float surfaceRotationUnlockDelay = 0.5f;
        [SerializeField, Min(0f)] float surfaceSwitchDistance = 0.75f;
        [SerializeField, Min(0f)] float surfaceSwitchHeight = 0.2f;
        [SerializeField] bool centerOnSurface = true;
        [SerializeField] bool fitToSurfaceBounds = true;
        [SerializeField, Range(0.1f, 1f)] float surfaceFit = 1f;
        [SerializeField, Min(0f)] float previewPositionSmoothTime = 0.06f;
        [SerializeField, Min(0f)] float previewRotationSpeed = 18f;
        [SerializeField, Min(0f)] float previewScaleSpeed = 10f;

        [Header("Fixed Placement")]
        [SerializeField] Vector3 fixedPosition;
        [SerializeField] Vector3 fixedEulerAngles;
        [SerializeField] Vector3 fixedScale = Vector3.one;

        [Header("Anchor Stabilization")]
        [SerializeField] bool stabilizeAnchor = true;
        [SerializeField, Min(0f)] float anchorPositionSpeed = 12f;
        [SerializeField, Min(0f)] float anchorRotationSpeed = 10f;
        [SerializeField, Min(0f)] float anchorPositionDeadZone = 0.002f;
        [SerializeField, Min(0f)] float anchorRotationDeadZone = 0.2f;

        [Header("Build Animation")]
        [SerializeField] float buildDuration = 2.0f;
        [SerializeField] float buildEdgeWidth = 0.18f;
        [SerializeField, ColorUsage(true, true)]
        Color buildEdgeColor = new Color(4f, 1.8f, 0.1f, 1f);
        [FormerlySerializedAs("restoreOriginalMaterialsAfterBuild")]
        [SerializeField] bool restoreMaterialsAfterBuild = true;
        [SerializeField] bool allowReplaceExisting = false;

        [Header("Placement Persistence")]
        [SerializeField] bool restoreSavedPlacement = true;
        [SerializeField] string persistenceKey = "F1XR.TrackPlacement.v1";
        [SerializeField, Min(0f)] float persistenceSaveDelay = 0.4f;

        [Header("Diagnostics")]
        [SerializeField] bool logPlacementDiagnostics;
        [SerializeField, Min(0.5f)] float placementDiagnosticInterval = 2f;

        public event Action PlacementRevealed;
        public bool HasPlacement => spawnedInstance != null;
        public bool IsPlacementActive => placementActive;
        public bool HasValidSurface =>
            hasCurrentHit &&
            IsPlacementVisualReady();
        public bool IsEditMode => editState != null && editState.IsEditMode;
        public bool CanUndo => editState != null && editState.CanUndo;
        public TrackPlacementMode PlacementMode => placementMode;
        public bool HasSavedPlacement => PlayerPrefs.GetInt(PersistenceName("Valid"), 0) == 1;
        public bool HasTrackMapPrefab => trackMapPrefab != null;
        public Transform PlacementTransform => spawnedInstance != null ? spawnedInstance.transform : null;
        public Transform CarsTransform => spawnedInstance != null ? EnsureCarsRoot(spawnedInstance.transform) : null;

        GameObject previewInstance;
        GameObject spawnedInstance;
        ARAnchor currentAnchor;

        Pose currentPose;
        ARPlane currentPlane;
        AutomaticTableSurface currentAutomaticSurface;
        bool hasCurrentHit;
        bool hasAutomaticSurface;
        ARPlane rotationCandidatePlane;
        Quaternion rotationCandidate;
        float rotationCandidateSince;
        bool hasRotationCandidate;
        ARPlane lockedRotationPlane;
        Quaternion lockedPlacementRotation;
        bool hasLockedPlacementRotation;
        Vector3 lockedSurfaceCenter;
        Quaternion placementFallbackRotation;
        bool hasPlacementFallbackRotation;
        bool surfaceHitMissing;
        bool rotationResetWhileMissing;
        float surfaceHitMissingSince;
        float nextPlacementDiagnosticTime;
        bool hasLoggedPlacementHit;
        bool lastLoggedPlacementHit;

        bool inputsArmed;
        float enableTime;
        bool wasLeftTriggerPressed;
        bool wasRightTriggerPressed;
        bool placementActive = true;

        Material runtimePreviewMaterial;
        TrackEditState editState;
        Vector3 observedPosition;
        Quaternion observedRotation;
        Vector3 observedScale;
        float persistenceSaveTime;
        bool persistenceDirty;
        bool runtimeDebugPlacement;

        static Transform EnsureCarsRoot(Transform placementRoot)
        {
            Transform visualRoot = placementRoot.Find("Visual");
            if (visualRoot == null)
                visualRoot = placementRoot;

            Transform carsRoot = visualRoot.Find("Cars");
            if (carsRoot != null)
                return carsRoot;

            GameObject obj = new GameObject("Cars");
            obj.transform.SetParent(visualRoot, worldPositionStays: false);
            return obj.transform;
        }

        public void SetPlacementPrefab(
            GameObject prefab,
            GameObject mapPrefab = null,
            float mapScale = 1f,
            bool fitMapToBounds = false,
            Vector2 mapTargetXZSize = default)
        {
            placementPrefab = prefab;
            trackMapPrefab = mapPrefab;
            trackMapScale = mapScale > 0f ? mapScale : 1f;
            fitTrackMapToBounds = fitMapToBounds;
            trackMapTargetXZSize = mapTargetXZSize;
            if (gameObject.scene.name == "SessionSpace_fitin")
                return;

            ClearPreview();
            if (HasSavedPlacement && !SavedGeometryMatches())
                ClearSavedPlacement();
            TryRestoreSavedPlacement();

            if (placementMode == TrackPlacementMode.Fixed)
                TryPlaceFixed();
        }

        void Reset()
        {
            placementController = GetComponent<ARPlanePlacementController>();
            anchorManager = GetComponent<ARAnchorManager>();
        }

        void Awake()
        {
            if (placementController == null)
                placementController = GetComponent<ARPlanePlacementController>();

            if (anchorManager == null)
                anchorManager = GetComponent<ARAnchorManager>();
        }

        void OnEnable()
        {
            if (placeAction.action != null)
                placeAction.action.Enable();

            enableTime = Time.time;
            inputsArmed = false;
            wasLeftTriggerPressed = false;
            wasRightTriggerPressed = false;

            if (placementController != null)
                placementController.PlacementRequested += ConfirmPlacement;
        }

        void OnDisable()
        {
            if (placeAction.action != null)
                placeAction.action.Disable();

            if (placementController != null)
                placementController.PlacementRequested -= ConfirmPlacement;

            HidePreview();
        }

        void OnDestroy()
        {
            if (previewInstance != null)
                Destroy(previewInstance);

            if (runtimePreviewMaterial != null)
                Destroy(runtimePreviewMaterial);
        }

        void Update()
        {
            // 이미 하나 배치했고 교체 허용이 꺼져 있으면 더 이상 preview를 보여주지 않음
            if (spawnedInstance != null && !allowReplaceExisting)
            {
                if (!spawnedInstance.activeInHierarchy)
                {
                    ClearSpawned();
                }
                else
                {
                    HidePreview();
                    return;
                }
            }

            UpdatePlacementPersistence();

            if (!placementActive)
            {
                HidePreview();
                return;
            }

            if (spawnedInstance != null && !allowReplaceExisting)
            {
                HidePreview();
                return;
            }

            UpdatePlacementHit();
            UpdatePreview();

            if (!inputsArmed)
            {
                if (Time.time >= enableTime + inputArmDelay && !IsAnyPlacementInputHeld())
                    inputsArmed = true;

                UpdateTriggerStateOnly();
                return;
            }

            bool pressedThisFrame = false;

            if (placeAction.action != null && placeAction.action.WasPressedThisFrame())
                pressedThisFrame = true;

            if (useControllerTriggerFallback && WasTriggerPressedThisFrame())
                pressedThisFrame = true;

            if (pressedThisFrame)
                ConfirmPlacement();
        }

        public void BeginPlacement()
        {
            if (spawnedInstance != null)
                return;

            placementActive = true;
            enableTime = Time.time;
            inputsArmed = false;
            LogPlacementDiagnostic("begin");
        }

        public void SetPlacementMode(TrackPlacementMode mode)
        {
            if (placementMode == mode || spawnedInstance != null)
                return;

            placementMode = mode;
            ClearPreview();
            LogPlacementDiagnostic($"mode={mode}");
        }

        public void CancelPlacement()
        {
            if (spawnedInstance != null)
                return;

            placementActive = false;
            hasCurrentHit = false;
            HidePreview();
            LogPlacementDiagnostic("cancel");
        }

        public void ToggleEditMode()
        {
            if (editState != null)
                editState.SetEditMode(!editState.IsEditMode);
        }

        public void UndoManipulation()
        {
            editState?.Undo();
        }

        public void ResetPlacement()
        {
            ClearSavedPlacement();
            ClearSpawned();
            BeginPlacement();
        }

        void TryRestoreSavedPlacement()
        {
            if (!restoreSavedPlacement ||
                spawnedInstance != null ||
                !IsPlacementVisualReady() ||
                !HasSavedPlacement)
                return;

            GameObject target = Instantiate(placementPrefab);
            target.name = placementPrefab.name;
            ApplyTrackMap(target);
            ConfigurePhysics(target);

            Vector3 position = ReadVector3("Position", Vector3.zero);
            Quaternion rotation = ReadQuaternion("Rotation", Quaternion.identity);
            Vector3 scale = ReadVector3("Scale", Vector3.one);
            if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
                scale = Vector3.one;

            target.transform.SetPositionAndRotation(position, rotation);
            target.transform.localScale = scale;
            spawnedInstance = target;
            placementActive = false;
            ConfigureEditState(target);
            ObserveCurrentTransform();
            PlacementRevealed?.Invoke();
        }

        bool IsPlacementVisualReady()
        {
            if (placementPrefab == null)
                return false;

            if (placementPrefab
                    .GetComponentsInChildren<Renderer>(
                        includeInactive: true)
                    .Length > 0)
            {
                return true;
            }

            return trackMapPrefab != null &&
                trackMapPrefab
                    .GetComponentsInChildren<Renderer>(
                        includeInactive: true)
                    .Length > 0;
        }

        void UpdatePlacementPersistence()
        {
            if (runtimeDebugPlacement || spawnedInstance == null)
                return;

            Transform placement = spawnedInstance.transform;
            bool changed = placement.position != observedPosition ||
                placement.rotation != observedRotation ||
                placement.localScale != observedScale;
            if (changed)
            {
                ObserveCurrentTransform();
                persistenceDirty = true;
                persistenceSaveTime = Time.unscaledTime + persistenceSaveDelay;
            }

            if (persistenceDirty && Time.unscaledTime >= persistenceSaveTime)
                SavePlacement();
        }

        void ObserveCurrentTransform()
        {
            if (spawnedInstance == null)
                return;

            Transform placement = spawnedInstance.transform;
            observedPosition = placement.position;
            observedRotation = placement.rotation;
            observedScale = placement.localScale;
        }

        void SavePlacement()
        {
            if (!restoreSavedPlacement || spawnedInstance == null)
                return;

            Transform placement = spawnedInstance.transform;
            WriteVector3("Position", placement.position);
            WriteQuaternion("Rotation", placement.rotation);
            WriteVector3("Scale", placement.localScale);
            WriteGeometrySignature();
            PlayerPrefs.SetInt(PersistenceName("Valid"), 1);
            PlayerPrefs.Save();
            persistenceDirty = false;
        }

        void ClearSavedPlacement()
        {
            PlayerPrefs.DeleteKey(PersistenceName("Valid"));
            PlayerPrefs.DeleteKey(PersistenceName("Geometry.Valid"));
            PlayerPrefs.Save();
            persistenceDirty = false;
        }

        bool SavedGeometryMatches()
        {
            if (PlayerPrefs.GetInt(PersistenceName("Geometry.Valid"), 0) != 1)
                return false;

            bool savedFit =
                PlayerPrefs.GetInt(PersistenceName("Geometry.FitMap"), 0) == 1;
            int savedMode =
                PlayerPrefs.GetInt(PersistenceName("Geometry.Mode"), -1);
            float savedMapScale =
                PlayerPrefs.GetFloat(PersistenceName("Geometry.MapScale"), -1f);
            Vector2 savedTarget = new(
                PlayerPrefs.GetFloat(PersistenceName("Geometry.Target.x"), -1f),
                PlayerPrefs.GetFloat(PersistenceName("Geometry.Target.y"), -1f));
            string savedMap =
                PlayerPrefs.GetString(PersistenceName("Geometry.Map"), string.Empty);
            string currentMap =
                trackMapPrefab != null ? trackMapPrefab.name : string.Empty;

            return savedFit == fitTrackMapToBounds &&
                savedMode == (int)placementMode &&
                Mathf.Approximately(savedMapScale, trackMapScale) &&
                Vector2.Distance(savedTarget, trackMapTargetXZSize) <= 0.0001f &&
                savedMap == currentMap;
        }

        void WriteGeometrySignature()
        {
            PlayerPrefs.SetInt(PersistenceName("Geometry.Valid"), 1);
            PlayerPrefs.SetInt(
                PersistenceName("Geometry.FitMap"),
                fitTrackMapToBounds ? 1 : 0);
            PlayerPrefs.SetInt(
                PersistenceName("Geometry.Mode"),
                (int)placementMode);
            PlayerPrefs.SetFloat(
                PersistenceName("Geometry.MapScale"),
                trackMapScale);
            PlayerPrefs.SetFloat(
                PersistenceName("Geometry.Target.x"),
                trackMapTargetXZSize.x);
            PlayerPrefs.SetFloat(
                PersistenceName("Geometry.Target.y"),
                trackMapTargetXZSize.y);
            PlayerPrefs.SetString(
                PersistenceName("Geometry.Map"),
                trackMapPrefab != null ? trackMapPrefab.name : string.Empty);
        }

        string PersistenceName(string suffix)
        {
            return $"{persistenceKey}.{suffix}";
        }

        void WriteVector3(string name, Vector3 value)
        {
            PlayerPrefs.SetFloat(PersistenceName(name + ".x"), value.x);
            PlayerPrefs.SetFloat(PersistenceName(name + ".y"), value.y);
            PlayerPrefs.SetFloat(PersistenceName(name + ".z"), value.z);
        }

        Vector3 ReadVector3(string name, Vector3 fallback)
        {
            return new Vector3(
                PlayerPrefs.GetFloat(PersistenceName(name + ".x"), fallback.x),
                PlayerPrefs.GetFloat(PersistenceName(name + ".y"), fallback.y),
                PlayerPrefs.GetFloat(PersistenceName(name + ".z"), fallback.z));
        }

        void WriteQuaternion(string name, Quaternion value)
        {
            PlayerPrefs.SetFloat(PersistenceName(name + ".x"), value.x);
            PlayerPrefs.SetFloat(PersistenceName(name + ".y"), value.y);
            PlayerPrefs.SetFloat(PersistenceName(name + ".z"), value.z);
            PlayerPrefs.SetFloat(PersistenceName(name + ".w"), value.w);
        }

        Quaternion ReadQuaternion(string name, Quaternion fallback)
        {
            Quaternion value = new(
                PlayerPrefs.GetFloat(PersistenceName(name + ".x"), fallback.x),
                PlayerPrefs.GetFloat(PersistenceName(name + ".y"), fallback.y),
                PlayerPrefs.GetFloat(PersistenceName(name + ".z"), fallback.z),
                PlayerPrefs.GetFloat(PersistenceName(name + ".w"), fallback.w));
            float magnitudeSquared = value.x * value.x + value.y * value.y +
                value.z * value.z + value.w * value.w;
            return magnitudeSquared > 0.001f ? Quaternion.Normalize(value) : fallback;
        }

        void UpdatePlacementHit()
        {
            hasCurrentHit = false;
            hasAutomaticSurface = false;
            if (placementController != null)
            {
                if (placementMode ==
                    TrackPlacementMode.TableAutomatic)
                {
                    hasCurrentHit = placementController
                        .TryGetAutomaticTableSurface(
                            out currentAutomaticSurface);
                    if (hasCurrentHit)
                    {
                        currentPose = currentAutomaticSurface.Pose;
                        currentPlane = currentAutomaticSurface.Plane;
                        hasAutomaticSurface = true;
                    }
                }
                else
                {
                    hasCurrentHit =
                        placementController.TryGetPlacementSurface(
                            out currentAutomaticSurface);
                    if (hasCurrentHit)
                    {
                        currentPose = currentAutomaticSurface.Pose;
                        currentPlane = currentAutomaticSurface.Plane;
                        hasAutomaticSurface = true;
                    }
                }
            }

            if (hasCurrentHit)
            {
                surfaceHitMissing = false;
                rotationResetWhileMissing = false;
                LogPlacementDiagnostic("surface");
                return;
            }

            if (!surfaceHitMissing)
            {
                surfaceHitMissing = true;
                rotationResetWhileMissing = false;
                surfaceHitMissingSince = Time.unscaledTime;
                LogPlacementDiagnostic("surface-missing");
                return;
            }

            if (!rotationResetWhileMissing &&
                Time.unscaledTime - surfaceHitMissingSince >= surfaceRotationUnlockDelay)
            {
                ResetPlacementRotation();
                rotationResetWhileMissing = true;
            }
        }

        void ResetPlacementRotation()
        {
            rotationCandidatePlane = null;
            hasRotationCandidate = false;
            lockedRotationPlane = null;
            hasLockedPlacementRotation = false;
            hasPlacementFallbackRotation = false;
        }

        void LogPlacementDiagnostic(string phase)
        {
            if (!logPlacementDiagnostics)
                return;

            float now = Time.unscaledTime;
            bool hitChanged = !hasLoggedPlacementHit ||
                lastLoggedPlacementHit != hasCurrentHit;
            if (!hitChanged && now < nextPlacementDiagnosticTime)
                return;

            hasLoggedPlacementHit = true;
            lastLoggedPlacementHit = hasCurrentHit;
            nextPlacementDiagnosticTime = now + placementDiagnosticInterval;
            string plane = currentPlane == null
                ? "none"
                : $"id={currentPlane.trackableId}, size={currentPlane.size}, " +
                  $"classifications={currentPlane.classifications}";
            Debug.Log(
                $"[TrackPlacement] phase={phase}, active={placementActive}, " +
                $"mode={placementMode}, hit={hasCurrentHit}, " +
                $"automatic={hasAutomaticSurface}, plane={plane}",
                this);
        }

    }
}
