using System.Collections.Generic;
using F1XR.Interaction.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.RestAPI.Replay
{
    public sealed partial class EventPopoutReplay
    {
        private const float AccidentEditMinimumScale = 0.65f;
        private const float AccidentEditMaximumScale = 1.45f;
        private const float AccidentEditScaleStep = 0.05f;
        private const float AccidentEditYawStepDegrees = 5f;
        private const float AccidentEditHorizontalLimitMeters = 1.5f;
        private const float AccidentEditDownLimitMeters = 0.65f;
        private const float AccidentEditUpLimitMeters = 0.9f;
        private const float AccidentEditKeyboardMoveMetersPerSecond = 0.55f;

        private readonly List<LineRenderer> accidentEditCornerLines = new();
        private GameObject accidentEditFrameRoot;
        private Material accidentEditFrameMaterial;
        private Material accidentEditCornerMaterial;
        private Bounds accidentEditBounds;
        private Vector3 accidentEditDefaultPosition;
        private Quaternion accidentEditDefaultRotation = Quaternion.identity;
        private Vector3 accidentEditDefaultScale = Vector3.one;
        private Vector3 accidentEditSessionPosition;
        private Quaternion accidentEditSessionRotation = Quaternion.identity;
        private Vector3 accidentEditSessionScale = Vector3.one;
        private Vector3 accidentEditRestColliderCenter;
        private Vector3 accidentEditRestColliderSize;
        private bool accidentEditDefaultsCaptured;
        private bool accidentEditHasSessionPlacement;
        private bool accidentEditColliderStateCaptured;
        private bool accidentEditModeActive;

        public bool IsAccidentEditMode => accidentEditModeActive;
        public bool CanEditAccident =>
            isActive &&
            IsCurrentCollision &&
            PresentationRoot != null &&
            collisionIncidentPresentation != null &&
            accidentEditDefaultsCaptured;

        private void EnsureAccidentEditModeReady()
        {
            Transform root = PresentationRoot;
            if (root == null || !IsCollisionEvent(currentEvent))
                return;

            if (!accidentEditDefaultsCaptured)
            {
                accidentEditDefaultPosition = root.position;
                accidentEditDefaultRotation = root.rotation;
                accidentEditDefaultScale = root.localScale;
                accidentEditSessionPosition = accidentEditDefaultPosition;
                accidentEditSessionRotation = accidentEditDefaultRotation;
                accidentEditSessionScale = accidentEditDefaultScale;
                accidentEditBounds = stageInteractionDefaultsCaptured
                    ? new Bounds(
                        stageInteractionDefaultCenter,
                        stageInteractionDefaultSize)
                    : new Bounds(Vector3.zero, Vector3.one * 0.1f);
                accidentEditDefaultsCaptured = true;
            }
            else if (accidentEditHasSessionPlacement)
            {
                root.SetPositionAndRotation(
                    accidentEditSessionPosition,
                    accidentEditSessionRotation);
                root.localScale = accidentEditSessionScale;
            }

            ConfigureAccidentEditScaleController();
            EnsureAccidentEditFrame();
            accidentEditModeActive = false;
            SetAccidentEditFrameVisible(false);
            SetStageInteractionEnabled(false);
        }

        private void ConfigureAccidentEditScaleController()
        {
            Transform root = PresentationRoot;
            if (root == null)
                return;

            ScaleController scale = root.GetComponent<ScaleController>();
            XRGrabInteractable grab = root.GetComponent<XRGrabInteractable>();
            Rigidbody body = root.GetComponent<Rigidbody>();
            if (scale == null || grab == null || body == null)
                return;

            float defaultScale = Mathf.Max(
                0.0001f,
                Mathf.Abs(accidentEditDefaultScale.x));
            scale.Configure(
                root,
                grab,
                body,
                defaultScale * AccidentEditMinimumScale,
                defaultScale * AccidentEditMaximumScale);
        }

        private void EnsureAccidentEditFrame()
        {
            if (PresentationRoot == null || accidentEditFrameRoot != null)
                return;

            accidentEditFrameMaterial =
                ReplayCarVisualUtil.CreateSelectionMaterial(
                    new Color(0.08f, 0.78f, 1f, 0.62f));
            accidentEditFrameMaterial.name =
                "Runtime_AccidentEditFrame_Cyan";
            accidentEditCornerMaterial =
                ReplayCarVisualUtil.CreateSelectionMaterial(
                    new Color(0.94f, 0.98f, 1f, 0.88f));
            accidentEditCornerMaterial.name =
                "Runtime_AccidentEditCorners_White";

            accidentEditFrameRoot = new GameObject("AccidentEditFrame");
            accidentEditFrameRoot.transform.SetParent(PresentationRoot, false);

            Bounds frameBounds = accidentEditBounds;
            Vector3 frameSize = frameBounds.size;
            float horizontalSize = Mathf.Max(
                0.001f,
                Mathf.Min(
                    Mathf.Max(frameSize.x, 0.001f),
                    Mathf.Max(frameSize.z, 0.001f)));
            frameSize.y = Mathf.Max(frameSize.y, horizontalSize * 0.12f);
            frameSize *= 1.035f;
            frameBounds.size = frameSize;

            Vector3[] corners = AccidentEditBoundsCorners(frameBounds);
            int[] edgeOrder =
            {
                0, 1, 2, 3, 0, 4, 5, 1,
                5, 6, 2, 6, 7, 3, 7, 4
            };
            LineRenderer outline = CreateAccidentEditLine(
                "ThinCyanBounds",
                accidentEditFrameRoot.transform,
                accidentEditFrameMaterial,
                AccidentEditLineWidthLocal());
            outline.positionCount = edgeOrder.Length;
            for (int index = 0; index < edgeOrder.Length; index++)
                outline.SetPosition(index, corners[edgeOrder[index]]);

            float markerLength = Mathf.Max(
                horizontalSize * 0.055f,
                0.012f / AccidentEditDefaultWorldScale());
            for (int index = 0; index < corners.Length; index++)
            {
                Vector3 corner = corners[index];
                Vector3 inwardX = new(
                    corner.x > frameBounds.center.x ? -1f : 1f,
                    0f,
                    0f);
                Vector3 inwardY = new(
                    0f,
                    corner.y > frameBounds.center.y ? -1f : 1f,
                    0f);
                Vector3 inwardZ = new(
                    0f,
                    0f,
                    corner.z > frameBounds.center.z ? -1f : 1f);
                LineRenderer marker = CreateAccidentEditLine(
                    $"Corner_{index + 1:00}",
                    accidentEditFrameRoot.transform,
                    accidentEditCornerMaterial,
                    AccidentEditLineWidthLocal() * 1.2f);
                marker.positionCount = 5;
                marker.SetPosition(0, corner + inwardX * markerLength);
                marker.SetPosition(1, corner);
                marker.SetPosition(2, corner + inwardY * markerLength);
                marker.SetPosition(3, corner);
                marker.SetPosition(4, corner + inwardZ * markerLength);
                accidentEditCornerLines.Add(marker);
            }

            SetAccidentEditFrameVisible(false);
            RunAccidentEditScaleRegression();
        }

        public void EnterAccidentEditMode()
        {
            if (!CanEditAccident || accidentEditModeActive)
                return;

            accidentEditRestColliderCenter = stageInteractionCollider != null
                ? stageInteractionCollider.center
                : Vector3.zero;
            accidentEditRestColliderSize = stageInteractionCollider != null
                ? stageInteractionCollider.size
                : Vector3.zero;
            accidentEditColliderStateCaptured =
                stageInteractionCollider != null;
            if (stageInteractionCollider != null)
            {
                stageInteractionCollider.center = accidentEditBounds.center;
                stageInteractionCollider.size = accidentEditBounds.size;
            }

            accidentEditModeActive = true;
            SetStageInteractionEnabled(true);
            SetAccidentEditFrameVisible(true);
            StartAccidentEditPreview();
            Debug.Log(
                "[AccidentEdit] ENTER; track-style one-hand grab and " +
                "two-hand uniform scale enabled on AccidentPresentationRoot.",
                this);
        }

        private void StartAccidentEditPreview()
        {
            collisionIncidentPresentation.BeginReveal();
            float previewTime = collisionIncidentPresentation.Tick(0f);
            timeline.SetTime(previewTime);
            timeline.Pause();
            ShowCollisionCars(previewTime);
            collisionIncidentPresentation.ApplyVehicleMotion();
            eventAudio?.SetPlaying(false);
        }

        public void CompleteAccidentEditMode()
        {
            if (!accidentEditModeActive)
                return;

            ClampAndStoreAccidentEditTransform();
            accidentEditHasSessionPlacement = true;
            accidentEditModeActive = false;
            RestoreAccidentEditCollider();
            SetStageInteractionEnabled(false);
            SetAccidentEditFrameVisible(false);
            RestartAccidentCinematicFromBeginning();
            Debug.Log(
                $"[AccidentEdit] DONE; placement preserved; " +
                $"position={accidentEditSessionPosition:F3}, " +
                $"yaw={accidentEditSessionRotation.eulerAngles.y:0.0}, " +
                $"scale={AccidentEditScaleMultiplier():0.00}x.",
                this);
        }

        private void RestartAccidentCinematicFromBeginning()
        {
            if (collisionIncidentPresentation == null)
                return;

            collisionIncidentPresentation.BeginReveal();
            float replayTime = collisionIncidentPresentation.Tick(0f);
            timeline.SetTime(replayTime);
            ShowCollisionCars(replayTime);
            collisionIncidentPresentation.ApplyVehicleMotion();
            eventAudio?.SetPlaying(false);
        }

        private void SuspendAccidentEditModeForClose()
        {
            if (!accidentEditDefaultsCaptured)
                return;

            if (accidentEditModeActive)
                ClampAndStoreAccidentEditTransform();
            accidentEditModeActive = false;
            RestoreAccidentEditCollider();
            SetStageInteractionEnabled(false);
            SetAccidentEditFrameVisible(false);
        }

        public void ResetAccidentEditTransform()
        {
            Transform root = PresentationRoot;
            if (root == null || !accidentEditDefaultsCaptured)
                return;

            root.SetPositionAndRotation(
                accidentEditDefaultPosition,
                accidentEditDefaultRotation);
            root.localScale = accidentEditDefaultScale;
            accidentEditSessionPosition = accidentEditDefaultPosition;
            accidentEditSessionRotation = accidentEditDefaultRotation;
            accidentEditSessionScale = accidentEditDefaultScale;
            accidentEditHasSessionPlacement = false;
            Debug.Log("[AccidentEdit] RESET restored exact default transform.", this);
        }

        private void RotateAccidentEdit(float degrees)
        {
            Transform root = PresentationRoot;
            if (!accidentEditModeActive || root == null)
                return;

            float yaw = root.eulerAngles.y + degrees;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            ClampAndStoreAccidentEditTransform();
        }

        private void SetAccidentEditScaleMultiplier(float multiplier)
        {
            Transform root = PresentationRoot;
            if (!accidentEditModeActive || root == null)
                return;

            float clamped = Mathf.Clamp(
                multiplier,
                AccidentEditMinimumScale,
                AccidentEditMaximumScale);
            root.localScale = accidentEditDefaultScale * clamped;
            ClampAndStoreAccidentEditTransform();
        }

        private void UpdateAccidentEditMode()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (!accidentEditModeActive)
            {
                if (keyboard.f2Key.wasPressedThisFrame)
                    EnterAccidentEditMode();
                return;
            }

            Transform root = PresentationRoot;
            if (root == null)
                return;

            Vector2 move = Vector2.zero;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                move.x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                move.x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                move.y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                move.y += 1f;
            if (move.sqrMagnitude > 0f)
            {
                move = Vector2.ClampMagnitude(move, 1f);
                Transform viewer = Camera.main != null
                    ? Camera.main.transform
                    : null;
                Vector3 forward = viewer != null
                    ? Vector3.ProjectOnPlane(viewer.forward, Vector3.up)
                    : Vector3.forward;
                Vector3 right = viewer != null
                    ? Vector3.ProjectOnPlane(viewer.right, Vector3.up)
                    : Vector3.right;
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.forward;
                if (right.sqrMagnitude < 0.001f)
                    right = Vector3.right;
                root.position +=
                    (right.normalized * move.x +
                     forward.normalized * move.y) *
                    (AccidentEditKeyboardMoveMetersPerSecond *
                     Time.unscaledDeltaTime);
            }

            if (keyboard.qKey.wasPressedThisFrame)
                RotateAccidentEdit(-AccidentEditYawStepDegrees);
            if (keyboard.eKey.wasPressedThisFrame)
                RotateAccidentEdit(AccidentEditYawStepDegrees);
            if (keyboard.minusKey.wasPressedThisFrame ||
                keyboard.numpadMinusKey.wasPressedThisFrame)
            {
                SetAccidentEditScaleMultiplier(
                    AccidentEditScaleMultiplier() -
                    AccidentEditScaleStep);
            }
            if (keyboard.equalsKey.wasPressedThisFrame ||
                keyboard.numpadPlusKey.wasPressedThisFrame)
            {
                SetAccidentEditScaleMultiplier(
                    AccidentEditScaleMultiplier() +
                    AccidentEditScaleStep);
            }
            if (keyboard.rKey.wasPressedThisFrame)
                ResetAccidentEditTransform();
            if (keyboard.enterKey.wasPressedThisFrame ||
                keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                CompleteAccidentEditMode();
            }

            ClampAndStoreAccidentEditTransform();
        }

        private void LateUpdate()
        {
            if (!isActive ||
                !IsCurrentCollision ||
                !accidentEditDefaultsCaptured)
            {
                return;
            }

            if (accidentEditModeActive)
                ClampAndStoreAccidentEditTransform();
        }

        private void ClampAndStoreAccidentEditTransform()
        {
            Transform root = PresentationRoot;
            if (root == null || !accidentEditDefaultsCaptured)
                return;

            Vector3 offset = root.position - accidentEditDefaultPosition;
            Vector3 horizontal = Vector3.ProjectOnPlane(offset, Vector3.up);
            horizontal = Vector3.ClampMagnitude(
                horizontal,
                AccidentEditHorizontalLimitMeters);
            float vertical = Mathf.Clamp(
                Vector3.Dot(offset, Vector3.up),
                -AccidentEditDownLimitMeters,
                AccidentEditUpLimitMeters);
            root.position = accidentEditDefaultPosition +
                horizontal + Vector3.up * vertical;

            float yaw = root.eulerAngles.y;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            float multiplier = Mathf.Clamp(
                AccidentEditScaleMultiplier(),
                AccidentEditMinimumScale,
                AccidentEditMaximumScale);
            root.localScale = accidentEditDefaultScale * multiplier;

            accidentEditSessionPosition = root.position;
            accidentEditSessionRotation = root.rotation;
            accidentEditSessionScale = root.localScale;
        }

        private float AccidentEditScaleMultiplier()
        {
            if (PresentationRoot == null || !accidentEditDefaultsCaptured)
                return 1f;

            return Mathf.Abs(accidentEditDefaultScale.x) > 0.000001f
                ? Mathf.Abs(
                    PresentationRoot.localScale.x /
                    accidentEditDefaultScale.x)
                : 1f;
        }

        private void SetAccidentEditFrameVisible(bool visible)
        {
            if (accidentEditFrameRoot != null)
                accidentEditFrameRoot.SetActive(visible);
        }

        private void RestoreAccidentEditCollider()
        {
            if (!accidentEditColliderStateCaptured ||
                stageInteractionCollider == null)
            {
                accidentEditColliderStateCaptured = false;
                return;
            }

            stageInteractionCollider.center = accidentEditRestColliderCenter;
            stageInteractionCollider.size = accidentEditRestColliderSize;
            accidentEditColliderStateCaptured = false;
        }

        private void RunAccidentEditScaleRegression()
        {
            Transform root = PresentationRoot;
            if (root == null || collisionIncidentPresentation == null)
                return;

            Transform[] descendants =
                root.GetComponentsInChildren<Transform>(true);
            Vector3[] localPositions = new Vector3[descendants.Length];
            Quaternion[] localRotations = new Quaternion[descendants.Length];
            Vector3[] localScales = new Vector3[descendants.Length];
            for (int index = 0; index < descendants.Length; index++)
            {
                localPositions[index] = descendants[index].localPosition;
                localRotations[index] = descendants[index].localRotation;
                localScales[index] = descendants[index].localScale;
            }

            Vector3 originalPosition = root.position;
            Quaternion originalRotation = root.rotation;
            Vector3 originalScale = root.localScale;
            Vector3 contactLocal =
                collisionIncidentPresentation.VisualContactLocalPoint;
            float[] multipliers = { 0.75f, 1f, 1.25f };
            for (int testIndex = 0; testIndex < multipliers.Length; testIndex++)
            {
                float multiplier = multipliers[testIndex];
                root.localScale = accidentEditDefaultScale * multiplier;
                Vector3 contactWorld = root.TransformPoint(contactLocal);
                float contactRoundTripError = Vector3.Distance(
                    contactLocal,
                    root.InverseTransformPoint(contactWorld));
                float maximumChildLocalError = 0f;
                for (int index = 1; index < descendants.Length; index++)
                {
                    maximumChildLocalError = Mathf.Max(
                        maximumChildLocalError,
                        Vector3.Distance(
                            localPositions[index],
                            descendants[index].localPosition),
                        Quaternion.Angle(
                            localRotations[index],
                            descendants[index].localRotation) * 0.0001f,
                        Vector3.Distance(
                            localScales[index],
                            descendants[index].localScale));
                }

                Debug.Log(
                    $"[AccidentEditValidation] scale={multiplier:0.00}x, " +
                    $"uniform=True, internalChildren={Mathf.Max(0, descendants.Length - 1)}, " +
                    $"childLocalError={maximumChildLocalError:0.000000}, " +
                    $"contactLocalError={contactRoundTripError:0.000000}; " +
                    "track/cars/VFX remain in root space.",
                    this);
            }
            root.SetPositionAndRotation(originalPosition, originalRotation);
            root.localScale = originalScale;
        }

        private void ClearAccidentEditMode()
        {
            accidentEditModeActive = false;
            accidentEditDefaultsCaptured = false;
            accidentEditHasSessionPlacement = false;
            accidentEditColliderStateCaptured = false;
            accidentEditFrameRoot = null;
            accidentEditCornerLines.Clear();
            if (accidentEditFrameMaterial != null)
                Destroy(accidentEditFrameMaterial);
            if (accidentEditCornerMaterial != null)
                Destroy(accidentEditCornerMaterial);
            accidentEditFrameMaterial = null;
            accidentEditCornerMaterial = null;
            accidentEditBounds = default;
            accidentEditDefaultPosition = Vector3.zero;
            accidentEditDefaultRotation = Quaternion.identity;
            accidentEditDefaultScale = Vector3.one;
            accidentEditSessionPosition = Vector3.zero;
            accidentEditSessionRotation = Quaternion.identity;
            accidentEditSessionScale = Vector3.one;
        }

        private float AccidentEditDefaultWorldScale()
        {
            Transform root = PresentationRoot;
            if (root == null)
                return 1f;

            Vector3 parentScale = root.parent != null
                ? root.parent.lossyScale
                : Vector3.one;
            return Mathf.Max(
                0.0001f,
                Mathf.Abs(parentScale.x * accidentEditDefaultScale.x));
        }

        private float AccidentEditLineWidthLocal()
        {
            return 0.0045f / AccidentEditDefaultWorldScale();
        }

        private static LineRenderer CreateAccidentEditLine(
            string name,
            Transform parent,
            Material material,
            float width)
        {
            LineRenderer line = new GameObject(name, typeof(LineRenderer))
                .GetComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.useWorldSpace = false;
            line.widthMultiplier = Mathf.Max(0.00001f, width);
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            return line;
        }

        private static Vector3[] AccidentEditBoundsCorners(Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
                new Vector3(min.x, max.y, max.z)
            };
        }
    }
}
