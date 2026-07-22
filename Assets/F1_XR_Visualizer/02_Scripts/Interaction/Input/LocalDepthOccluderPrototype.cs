using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace F1XR.Interaction.Input
{
    public enum ExperimentalInputVisualMode
    {
        None,
        ContextualIndicatorsOnly,
        LocalDepthOccluder
    }

    [DisallowMultipleComponent]
    public sealed class LocalDepthOccluderPrototype : MonoBehaviour
    {
        [SerializeField] ExperimentalInputVisualMode mode = ExperimentalInputVisualMode.ContextualIndicatorsOnly;
        [SerializeField] ContextualInputFeedback contextualFeedback;
        [SerializeField] Material depthOnlyMaterial;
        [SerializeField] Handedness testHand = Handedness.Left;
        [SerializeField, Min(0.005f)] float fingertipRadius = 0.012f;
        [SerializeField, Range(0f, 0.004f)] float expansion;
        [SerializeField] bool showValidationCube = true;
        [SerializeField, Min(0.1f)] float validationDistance = 0.65f;
        [SerializeField, Min(0.05f)] float validationCubeSize = 0.22f;

        XRHandSubsystem handSubsystem;
        Transform viewTransform;
        GameObject fingertipProxy;
        MeshRenderer proxyRenderer;
        GameObject validationCube;
        MeshRenderer validationRenderer;
        Material validationMaterial;
        ExperimentalInputVisualMode? appliedMode;
        float nextSubsystemCheck;

        void Awake()
        {
            Camera mainCamera = Camera.main;
            viewTransform = mainCamera != null ? mainCamera.transform : transform;
            CreateFingertipProxy();
            CreateValidationCube();
            ResolveHandSubsystem();
            ApplyMode();
        }

        void OnValidate()
        {
            if (Application.isPlaying)
                ApplyMode();
        }

        void Update()
        {
            if (appliedMode != mode)
                ApplyMode();

            if (mode != ExperimentalInputVisualMode.LocalDepthOccluder)
                return;

            if (handSubsystem == null || !handSubsystem.running)
            {
                if (Time.unscaledTime >= nextSubsystemCheck)
                {
                    ResolveHandSubsystem();
                    nextSubsystemCheck = Time.unscaledTime + 1f;
                }

                proxyRenderer.enabled = false;
                return;
            }

            XRHand hand = testHand == Handedness.Right
                ? handSubsystem.rightHand
                : handSubsystem.leftHand;

            if (!hand.isTracked ||
                !XRHandInput.TryGetJointPoint(hand, XRHandJointID.IndexTip, out Vector3 localFingertip))
            {
                proxyRenderer.enabled = false;
                return;
            }

            fingertipProxy.transform.position = transform.TransformPoint(localFingertip);
            float diameter = (fingertipRadius + expansion) * 2f;
            fingertipProxy.transform.localScale = Vector3.one * diameter;
            proxyRenderer.enabled = true;
        }

        void OnDisable()
        {
            if (proxyRenderer != null)
                proxyRenderer.enabled = false;
            if (validationCube != null)
                validationCube.SetActive(false);
        }

        void OnDestroy()
        {
            if (fingertipProxy != null)
                Destroy(fingertipProxy);
            if (validationCube != null)
                Destroy(validationCube);
            if (validationMaterial != null)
                Destroy(validationMaterial);
        }

        void ResolveHandSubsystem()
        {
            handSubsystem = XRHandInput.FindRunningSubsystem();
        }

        void ApplyMode()
        {
            if (contextualFeedback != null)
            {
                contextualFeedback.Mode = mode == ExperimentalInputVisualMode.ContextualIndicatorsOnly
                    ? InputFeedbackMode.RayAndContextualIndicators
                    : InputFeedbackMode.RayOnly;
            }

            bool showDepthTest = mode == ExperimentalInputVisualMode.LocalDepthOccluder;
            if (proxyRenderer != null)
                proxyRenderer.enabled = false;
            if (validationCube != null)
                validationCube.SetActive(showDepthTest && showValidationCube);

            if (showDepthTest && appliedMode != mode)
                PlaceValidationCube();

            appliedMode = mode;
        }

        void CreateFingertipProxy()
        {
            fingertipProxy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fingertipProxy.name = testHand == Handedness.Right
                ? "Right Fingertip Depth Proxy"
                : "Left Fingertip Depth Proxy";
            fingertipProxy.transform.SetParent(transform, false);

            Collider proxyCollider = fingertipProxy.GetComponent<Collider>();
            if (proxyCollider != null)
                proxyCollider.enabled = false;

            proxyRenderer = fingertipProxy.GetComponent<MeshRenderer>();
            proxyRenderer.sharedMaterial = depthOnlyMaterial;
            ConfigureRenderer(proxyRenderer);
            proxyRenderer.enabled = false;
        }

        void CreateValidationCube()
        {
            validationCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            validationCube.name = "Local Depth Validation Cube";

            Collider cubeCollider = validationCube.GetComponent<Collider>();
            if (cubeCollider != null)
                cubeCollider.enabled = false;

            validationRenderer = validationCube.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            validationMaterial = new Material(shader)
            {
                name = "Local Depth Validation Cube (Runtime)",
                color = new Color(0.08f, 0.35f, 0.9f, 1f),
                hideFlags = HideFlags.DontSave
            };
            validationRenderer.sharedMaterial = validationMaterial;
            ConfigureRenderer(validationRenderer);
            validationCube.SetActive(false);
        }

        void PlaceValidationCube()
        {
            validationCube.transform.position = viewTransform.position + viewTransform.forward * validationDistance;
            validationCube.transform.rotation = Quaternion.LookRotation(viewTransform.forward, viewTransform.up);
            validationCube.transform.localScale = Vector3.one * validationCubeSize;
        }

        static void ConfigureRenderer(Renderer target)
        {
            target.shadowCastingMode = ShadowCastingMode.Off;
            target.receiveShadows = false;
            target.lightProbeUsage = LightProbeUsage.Off;
            target.reflectionProbeUsage = ReflectionProbeUsage.Off;
            target.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }
    }
}
