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

        private PitWallPortalEditState pitWallEditState;
        private Transform pitWallEditedStage;
        private Vector3 pitWallEditBasePortalPosition;
        private Vector2 pitWallEditBasePortalSize;
        private Vector3 pitWallEditBaseStagePosition;
        private Quaternion pitWallEditBaseStageRotation;
        private Vector3 pitWallEditBaseStageScale;

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

            pitWallEditState.SetEditMode(
                !pitWallEditState.IsEditMode);
            return true;
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
            Vector2 effectiveSize)
        {
            if (entrySurface == null ||
                effectiveSize.x <= 0f ||
                effectiveSize.y <= 0f)
            {
                return;
            }

            entryPosition = position;
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
            pitWallEditedStage.SetPositionAndRotation(
                position + stageOffset * uniformScale,
                pitWallEditBaseStageRotation);
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
            pitWallEditBasePortalSize = entryPortalSize;
            pitWallEditBaseStagePosition = stage.position;
            pitWallEditBaseStageRotation = stage.rotation;
            pitWallEditBaseStageScale = stage.localScale;

            GameObject surface = entrySurface.gameObject;
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
            grab.matchAttachRotation = false;
            grab.trackRotation = false;
            grab.trackScale = false;
            grab.snapToColliderVolume = false;
            grab.attachEaseInTime = 0f;
            grab.throwOnDetach = false;

            surface.AddComponent<WorldGrabTarget>();
            WorldGrabPolicy grabPolicy =
                surface.AddComponent<WorldGrabPolicy>();
            grabPolicy.UseGrabPoint(grab, entrySurface);

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
                editCollider);
        }

        private void ClearPitWallEditor()
        {
            if (pitWallEditState != null)
                pitWallEditState.Release();
            pitWallEditState = null;
            pitWallEditedStage = null;
            pitWallEditBasePortalPosition = Vector3.zero;
            pitWallEditBasePortalSize = Vector2.zero;
            pitWallEditBaseStagePosition = Vector3.zero;
            pitWallEditBaseStageRotation = Quaternion.identity;
            pitWallEditBaseStageScale = Vector3.one;
        }
    }
}
