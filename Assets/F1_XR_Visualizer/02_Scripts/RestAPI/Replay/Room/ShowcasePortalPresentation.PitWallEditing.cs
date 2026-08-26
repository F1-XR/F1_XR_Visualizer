using F1XR.Interaction.World;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.RestAPI.Replay.Room
{
    public sealed partial class ShowcasePortalPresentation
    {
        private const float PitEditMinimumScale = 0.55f;
        private const float PitEditMaximumScale = 1.1f;
        private const float PitEditHandleDepth = 0.08f;
        private const float PitEditHandleHeight = 0.18f;
        private const float PitEditMinimumGrabDistance = 0.3f;
        private const float PitImmersiveReferenceSpeed = 0.7f;

        private PitWallPortalEditState pitWallEditState;
        private GameObject pitWallEditOutline;
        private Transform pitWallEditedStage;
        private Vector3 pitWallEditBasePortalPosition;
        private Vector2 pitWallEditBasePortalSize;
        private Vector3 pitWallEditBaseStagePosition;
        private Quaternion pitWallEditBaseStageRotation;
        private Vector3 pitWallEditBaseStageScale;
        private Quaternion pitWallEditBasePortalRotation;
        private Vector3 pitWallImmersiveEyeOffset;
        private bool pitWallImmersiveEyeOffsetValid;

        public bool IsPitWallEditMode =>
            pitWallEditState != null &&
            pitWallEditState.IsEditMode;

        public bool CanUndoPitWallEdit =>
            pitWallEditState != null &&
            pitWallEditState.CanUndo;

        public bool IsPitWallManipulating =>
            pitWallEditState != null &&
            pitWallEditState.IsManipulating;

        internal bool TogglePitWallEditMode()
        {
            if (!IsPitStopConfigured || pitWallEditState == null)
                return false;

            if (!pitWallEditState.IsEditMode)
            {
                pitWallEditState.SetEditMode(true);
                return true;
            }

            if (pitWallEditState.IsManipulating)
                return false;

            pitWallEditState.SetEditMode(false);
            return true;
        }

        internal void AdjustPitWallImmersiveReference(
            Vector2 axis,
            float deltaTime)
        {
            if (!IsPitWallEditMode ||
                pitReplayViewMode != PitReplayViewMode.Immersive ||
                viewerCamera == null ||
                entrySurface == null ||
                entryCamera == null ||
                axis.sqrMagnitude <= 0f ||
                deltaTime <= 0f)
            {
                return;
            }

            Vector3 viewerPosition = viewerCamera.transform.position;
            Vector3 eye = pitWallImmersiveEyeOffsetValid
                ? viewerPosition + pitWallImmersiveEyeOffset
                : viewerPosition;
            Vector3 surfaceForward = entrySurface.forward.normalized;
            if (!portalCameraForwardSigns.TryGetValue(
                    entryCamera,
                    out float cameraForwardSign))
            {
                cameraForwardSign = Vector3.Dot(
                    entrySurface.position - eye,
                    surfaceForward) >= 0f
                    ? 1f
                    : -1f;
                portalCameraForwardSigns[entryCamera] =
                    cameraForwardSign;
            }

            Vector3 cameraForward =
                surfaceForward * cameraForwardSign;
            Vector3 cameraRight = Vector3.Cross(
                entrySurface.up,
                cameraForward).normalized;
            Vector3 cameraUp = Vector3.Cross(
                cameraForward,
                cameraRight).normalized;
            Vector2 input = Vector2.ClampMagnitude(axis, 1f);
            Vector3 candidateEye = eye +
                (cameraRight * input.x +
                 cameraUp * input.y) *
                PitImmersiveReferenceSpeed * deltaTime;

            pitWallImmersiveEyeOffset =
                candidateEye - viewerPosition;
            pitWallImmersiveEyeOffsetValid =
                IsFinite(pitWallImmersiveEyeOffset);
            RefreshPortalViews();
        }

        private Vector3 ResolvePitWallImmersiveEye(
            Camera portalCamera,
            Vector3 viewerPosition)
        {
            return pitStopOnly &&
                   pitReplayViewMode == PitReplayViewMode.Immersive &&
                   portalCamera == entryCamera &&
                   pitWallImmersiveEyeOffsetValid
                ? viewerPosition + pitWallImmersiveEyeOffset
                : viewerPosition;
        }

        internal void SetPitWallEditOutlineVisible(bool visible)
        {
            if (pitWallEditOutline != null)
                pitWallEditOutline.SetActive(visible);
        }

        internal bool UndoPitWallEdit()
        {
            if (!IsPitWallEditMode ||
                pitWallEditState == null ||
                !pitWallEditState.CanUndo)
            {
                return false;
            }

            pitWallEditState.Undo();
            return true;
        }

        internal bool ResetPitWallEdit()
        {
            if (!IsPitWallEditMode || pitWallEditState == null)
                return false;

            pitWallEditState.ResetToAutomatic();
            return true;
        }

        internal void UpdatePitWallEditedPortal(
            Vector3 position,
            Quaternion rotation,
            Vector2 effectiveSize)
        {
            if (entrySurface == null ||
                effectiveSize.x <= 0f ||
                effectiveSize.y <= 0f)
            {
                return;
            }

            entryPosition = position;
            entryInward = rotation * Vector3.forward;
            entryPortalRight = rotation * Vector3.right;
            entryPortalSize = effectiveSize;
            if (pitWallEditedStage == null ||
                pitWallEditBasePortalSize.x <= 0.0001f)
            {
                return;
            }

            float uniformScale = Mathf.Clamp(
                effectiveSize.x / pitWallEditBasePortalSize.x,
                PitEditMinimumScale,
                PitEditMaximumScale);
            Vector3 stageOffset =
                pitWallEditBaseStagePosition -
                pitWallEditBasePortalPosition;
            Quaternion rotationDelta =
                rotation *
                Quaternion.Inverse(pitWallEditBasePortalRotation);
            pitWallEditedStage.SetPositionAndRotation(
                position +
                rotationDelta * stageOffset * uniformScale,
                rotationDelta * pitWallEditBaseStageRotation);
            pitWallEditedStage.localScale =
                pitWallEditBaseStageScale * uniformScale;
        }

        private void CreatePitWallEditor(
            ShowcaseWallFrame wall,
            Transform stage)
        {
            ClearPitWallEditor();
            if (entrySurface == null ||
                stage == null ||
                !wall.IsValid)
                return;

            pitWallEditedStage = stage;
            pitWallEditBasePortalPosition = entrySurface.position;
            pitWallEditBasePortalRotation = entrySurface.rotation;
            pitWallEditBasePortalSize = entryPortalSize;
            pitWallEditBaseStagePosition = stage.position;
            pitWallEditBaseStageRotation = stage.rotation;
            pitWallEditBaseStageScale = stage.localScale;
            pitWallImmersiveEyeOffset = Vector3.zero;
            pitWallImmersiveEyeOffsetValid = false;

            GameObject surface = entrySurface.gameObject;
            CreatePitWallEditOutline(
                entrySurface,
                entryPortalSize);
            BoxCollider editCollider =
                surface.AddComponent<BoxCollider>();
            editCollider.center = Vector3.zero;
            editCollider.size = new Vector3(
                entryPortalSize.x,
                PitEditHandleHeight,
                PitEditHandleDepth);

            Rigidbody body = surface.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            XRGrabInteractable grab =
                surface.AddComponent<XRGrabInteractable>();
            grab.colliders.Clear();
            grab.colliders.Add(editCollider);
            grab.interactionLayers =
                InteractionLayerMask.GetMask("Default");
            grab.selectMode = InteractableSelectMode.Single;
            grab.useDynamicAttach = true;
            grab.matchAttachPosition = true;
            bool freePlacement =
                entrySurface.gameObject.scene.name ==
                "SessionSpace_fitin";
            grab.matchAttachRotation = freePlacement;
            grab.trackRotation = freePlacement;
            grab.trackScale = false;
            grab.snapToColliderVolume = false;
            grab.attachEaseInTime = 0f;
            grab.throwOnDetach = false;

            surface.AddComponent<WorldGrabTarget>();
            WorldGrabPolicy grabPolicy =
                surface.AddComponent<WorldGrabPolicy>();
            grabPolicy.UseGrabPoint(grab, entrySurface);
            grabPolicy.ConfigurePreservedFarMovement(
                PitEditMinimumGrabDistance);

            ScaleController scaleController =
                surface.AddComponent<ScaleController>();
            scaleController.Configure(
                entrySurface,
                grab,
                body,
                PitEditMinimumScale,
                PitEditMaximumScale);

            pitWallEditState =
                surface.AddComponent<PitWallPortalEditState>();
            pitWallEditState.Configure(
                this,
                wall,
                entryPortalSize,
                grab,
                scaleController,
                grabPolicy,
                editCollider,
                freePlacement);
        }

        private void CreatePitWallEditOutline(
            Transform parent,
            Vector2 size)
        {
            float width = Mathf.Clamp(
                Mathf.Min(size.x, size.y) * 0.012f,
                0.018f,
                0.035f);
            Material material = CreatePortalFrameMaterial(
                "PitStopEditOutline",
                new Color(0.16f, 0.82f, 1f, 0.9f),
                3140);
            if (material == null)
                return;

            pitWallEditOutline = new GameObject(
                "PitStopEditOutline");
            pitWallEditOutline.layer = PortalSurfaceLayer;
            pitWallEditOutline.transform.SetParent(parent, false);
            pitWallEditOutline.transform.localPosition =
                new Vector3(0f, 0f, -0.02f);
            MeshFilter filter =
                pitWallEditOutline.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateRectangularPitPortalFrameMesh(
                size,
                width,
                "Runtime_PitStopEditOutline");
            MeshRenderer renderer =
                pitWallEditOutline.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 22;
            pitWallEditOutline.SetActive(false);
        }

        private void ClearPitWallEditor()
        {
            SetPitWallEditOutlineVisible(false);
            if (pitWallEditState != null)
                pitWallEditState.Release();
            pitWallEditState = null;
            pitWallEditOutline = null;
            pitWallEditedStage = null;
            pitWallEditBasePortalPosition = Vector3.zero;
            pitWallEditBasePortalSize = Vector2.zero;
            pitWallEditBaseStagePosition = Vector3.zero;
            pitWallEditBaseStageRotation = Quaternion.identity;
            pitWallEditBaseStageScale = Vector3.one;
            pitWallEditBasePortalRotation = Quaternion.identity;
            pitWallImmersiveEyeOffset = Vector3.zero;
            pitWallImmersiveEyeOffsetValid = false;
        }
    }
}
