using System.Collections;
using UnityEngine;

namespace F1XR.Experience
{
    public enum PassthroughState
    {
        MR,
        TransitionToVR,
        VR,
        TransitionToMR
    }

    /// <summary>
    /// Fades Meta Passthrough in and out. This is the only responsibility of this
    /// component: no room destruction, no replay, no experience mode logic.
    ///
    /// How the fade works, and why:
    ///
    /// The project renders Passthrough through Unity OpenXR Meta, not the legacy
    /// Meta XR SDK, so there is no OVRPassthroughLayer to set an opacity on.
    /// The scene owns a "Passthrough Layer" GameObject whose CompositionLayer uses
    /// PassthroughLayerData, and that type carries no opacity field. Animating the
    /// layer through ColorScaleBiasExtension does not work either, because
    /// MetaOpenXRPassthroughLayer only builds the extension chain inside
    /// CreateNativeLayer, and its ModifyNativeLayer returns false while the layer is
    /// enabled, so runtime value changes never reach the compositor.
    ///
    /// What remains, and what the project already relies on, is the camera's
    /// transparent background: alpha 0 lets the real room show through, alpha 1 hides
    /// it completely. This component animates that alpha and toggles the Passthrough
    /// layer GameObject at the endpoints.
    ///
    /// ARCameraManager is deliberately never enabled or disabled here. Toggling it
    /// restarts the camera subsystem, and the layer-plus-background structure above is
    /// the one already proven in this project (see VRDroneCoordinator).
    ///
    /// Device test still required: alpha 0 and 1 are known good on Quest 3, but the
    /// intermediate values used during the fade are not yet verified on-device.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PassthroughTransitionController : MonoBehaviour
    {
        const string PassthroughLayerName = "Passthrough Layer";

        [SerializeField] Camera xrCamera;
        [SerializeField] GameObject passthroughLayer;
        [SerializeField, Min(0f)] float defaultDuration = 2f;

        [Tooltip("Background colour once Passthrough is fully hidden. The alpha of " +
            "this colour is driven by the fade and is ignored.")]
        [SerializeField] Color vrBackgroundColor = new(0.015f, 0.02f, 0.04f, 1f);

        PassthroughState state = PassthroughState.MR;
        Coroutine fadeRoutine;

        // 0 = Passthrough fully visible (MR), 1 = Passthrough fully hidden (VR).
        float hideAmount;

        public PassthroughState State => state;

        public bool IsTransitioning =>
            state == PassthroughState.TransitionToVR ||
            state == PassthroughState.TransitionToMR;

        public float DefaultDuration => defaultDuration;

        void Awake()
        {
            ResolveReferences();
        }

        void Start()
        {
            // The authored scene state is MR, so make the runtime state agree with it.
            ApplyMRImmediate();
        }

        /// <summary>Fade the real world out. Safe to call mid-transition.</summary>
        public void EnterVR(float duration)
        {
            StartFade(1f, duration, PassthroughState.TransitionToVR, PassthroughState.VR);
        }

        /// <summary>Fade the real world back in. Safe to call mid-transition.</summary>
        public void EnterMR(float duration)
        {
            StartFade(0f, duration, PassthroughState.TransitionToMR, PassthroughState.MR);
        }

        public void EnterVR() => EnterVR(defaultDuration);

        public void EnterMR() => EnterMR(defaultDuration);

        /// <summary>Snap to fully visible Passthrough, cancelling any fade.</summary>
        public void ApplyMRImmediate()
        {
            StopFade();
            hideAmount = 0f;
            ApplyHideAmount(hideAmount);
            state = PassthroughState.MR;
        }

        /// <summary>Snap to fully hidden Passthrough, cancelling any fade.</summary>
        public void ApplyVRImmediate()
        {
            StopFade();
            hideAmount = 1f;
            ApplyHideAmount(hideAmount);
            state = PassthroughState.VR;
        }

        void StartFade(
            float target,
            float duration,
            PassthroughState transitionState,
            PassthroughState finalState)
        {
            if (!ResolveReferences())
            {
                Debug.LogError(
                    "[Passthrough] Fade aborted: camera or Passthrough Layer could not be " +
                    "resolved.",
                    this);
                return;
            }

            Debug.Log(
                $"[Passthrough] StartFade target={target} duration={duration} " +
                $"from={hideAmount:F2} state={state}",
                this);

            StopFade();

            if (Mathf.Approximately(hideAmount, target) || duration <= 0f)
            {
                hideAmount = target;
                ApplyHideAmount(hideAmount);
                state = finalState;
                return;
            }

            state = transitionState;
            fadeRoutine = StartCoroutine(FadeRoutine(target, duration, finalState));
        }

        void StopFade()
        {
            if (fadeRoutine == null)
                return;

            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        IEnumerator FadeRoutine(float target, float duration, PassthroughState finalState)
        {
            // Resume from wherever the previous fade left off so a reversal mid-fade
            // continues smoothly instead of snapping back to an endpoint. The remaining
            // travel is scaled so a full fade always takes the requested duration.
            float start = hideAmount;
            float travel = Mathf.Abs(target - start);
            float scaledDuration = duration * travel;
            float elapsed = 0f;

            while (elapsed < scaledDuration)
            {
                elapsed += Time.deltaTime;
                hideAmount = Mathf.Lerp(start, target, elapsed / scaledDuration);
                ApplyHideAmount(hideAmount);
                yield return null;
            }

            hideAmount = target;
            ApplyHideAmount(hideAmount);
            state = finalState;
            fadeRoutine = null;

            Debug.Log(
                $"[Passthrough] Fade complete state={state} hideAmount={hideAmount:F2} " +
                $"cameraAlpha={(xrCamera != null ? xrCamera.backgroundColor.a : -1f):F2} " +
                $"layerActive={(passthroughLayer != null && passthroughLayer.activeSelf)}",
                this);
        }

        void ApplyHideAmount(float amount)
        {
            if (passthroughLayer != null)
            {
                // Keep the layer alive for every partially visible value; only drop it
                // once it can no longer contribute anything to the image.
                bool layerContributes = amount < 1f;
                if (passthroughLayer.activeSelf != layerContributes)
                    passthroughLayer.SetActive(layerContributes);
            }

            if (xrCamera == null)
                return;

            // Solid Color is kept for the whole range, including VR. Switching to
            // Skybox at the endpoint would pop, and the VR environment supplies its own
            // visuals anyway.
            xrCamera.clearFlags = CameraClearFlags.SolidColor;
            xrCamera.backgroundColor = new Color(
                vrBackgroundColor.r,
                vrBackgroundColor.g,
                vrBackgroundColor.b,
                amount);
        }

        bool ResolveReferences()
        {
            if (xrCamera == null)
                xrCamera = Camera.main;

            if (passthroughLayer == null)
                passthroughLayer = GameObject.Find(PassthroughLayerName);

            if (xrCamera == null)
            {
                Debug.LogError(
                    "[Passthrough] No camera assigned and Camera.main is missing.",
                    this);
                return false;
            }

            if (passthroughLayer == null)
            {
                Debug.LogError(
                    $"[Passthrough] '{PassthroughLayerName}' GameObject not found in " +
                    "the scene.",
                    this);
                return false;
            }

            return true;
        }
    }
}
