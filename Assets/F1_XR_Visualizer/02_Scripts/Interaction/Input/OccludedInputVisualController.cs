using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.Interaction.Input
{
    public enum OccludedInputVisualMode
    {
        IndicatorsOnly,
        OccludedSoftSilhouette,
        OccludedOutline,
        OccludedSoftSilhouetteWithIndicators,
        DebugAlwaysVisible,
        DebugOccludedBright
    }

    [DisallowMultipleComponent]
    public sealed class OccludedInputVisualController : MonoBehaviour
    {
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");
        static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        static readonly int CullId = Shader.PropertyToID("_Cull");

        [SerializeField] OccludedInputVisualMode mode = OccludedInputVisualMode.IndicatorsOnly;
        [SerializeField] ContextualInputFeedback contextualFeedback;
        [SerializeField] Material occludedMaterial;

        [Header("Tracked visual sources")]
        [SerializeField] GameObject leftHandRoot;
        [SerializeField] GameObject rightHandRoot;
        [SerializeField] GameObject leftHandRay;
        [SerializeField] GameObject rightHandRay;
        [SerializeField] GameObject leftController;
        [SerializeField] GameObject rightController;
        [SerializeField] SkinnedMeshRenderer leftHandSource;
        [SerializeField] SkinnedMeshRenderer rightHandSource;
        [SerializeField] MeshRenderer[] leftControllerSources = System.Array.Empty<MeshRenderer>();
        [SerializeField] MeshRenderer[] rightControllerSources = System.Array.Empty<MeshRenderer>();

        [Header("White silhouette")]
        [SerializeField, Range(0f, 1f)] float idleOpacity = 0.18f;
        [SerializeField, Range(0f, 1f)] float hoverOpacity = 0.24f;
        [SerializeField, Range(0f, 1f)] float activeOpacity = 0.36f;
        [SerializeField, Min(0f)] float opacitySpeed = 8f;
        [SerializeField, Range(0f, 1f)] float softRimStrength = 0.2f;
        [SerializeField, Min(0.1f)] float softRimPower = 2f;
        [SerializeField, Min(0.1f)] float outlineRimPower = 5f;

        readonly InputVisual leftHandVisual = new InputVisual();
        readonly InputVisual rightHandVisual = new InputVisual();
        readonly InputVisual leftControllerVisual = new InputVisual();
        readonly InputVisual rightControllerVisual = new InputVisual();

        XRBaseInteractor[] leftHandInteractors = System.Array.Empty<XRBaseInteractor>();
        XRBaseInteractor[] rightHandInteractors = System.Array.Empty<XRBaseInteractor>();
        XRBaseInteractor[] leftControllerInteractors = System.Array.Empty<XRBaseInteractor>();
        XRBaseInteractor[] rightControllerInteractors = System.Array.Empty<XRBaseInteractor>();
        OccludedInputVisualMode appliedMode = (OccludedInputVisualMode)(-1);
        Material runtimeMaterial;
        bool setupComplete;
        float nextSetupTime;

        public OccludedInputVisualMode Mode
        {
            get => mode;
            set => mode = value;
        }

        sealed class InputVisual
        {
            public string name;
            public Renderer renderer;
            public MaterialPropertyBlock properties;
            public Mesh ownedMesh;
            public float opacity;
            public bool activeLogged;
        }

        void Awake()
        {
            ResolveReferences();
            if (occludedMaterial != null)
            {
                runtimeMaterial = new Material(occludedMaterial)
                {
                    name = occludedMaterial.name + " (Runtime)",
                    hideFlags = HideFlags.DontSave
                };
            }

            setupComplete = CreateVisuals();
            CacheInteractors();
            ApplyMode();
        }

        void OnValidate()
        {
            hoverOpacity = Mathf.Max(hoverOpacity, idleOpacity);
            activeOpacity = Mathf.Max(activeOpacity, hoverOpacity);
        }

        void Update()
        {
            if (!setupComplete && Time.unscaledTime >= nextSetupTime)
            {
                ResolveReferences();
                setupComplete = CreateVisuals();
                if (setupComplete)
                    CacheInteractors();
                nextSetupTime = Time.unscaledTime + 1f;
            }

            ApplyMode();
            UpdateVisual(leftHandVisual, leftHandRoot, leftHandInteractors);
            UpdateVisual(rightHandVisual, rightHandRoot, rightHandInteractors);
            UpdateVisual(leftControllerVisual, leftController, leftControllerInteractors);
            UpdateVisual(rightControllerVisual, rightController, rightControllerInteractors);
        }

        void OnDisable()
        {
            SetEnabled(leftHandVisual, false);
            SetEnabled(rightHandVisual, false);
            SetEnabled(leftControllerVisual, false);
            SetEnabled(rightControllerVisual, false);
        }

        void OnDestroy()
        {
            DestroyOwnedMesh(leftControllerVisual);
            DestroyOwnedMesh(rightControllerVisual);
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        void ResolveReferences()
        {
            contextualFeedback ??= GetComponent<ContextualInputFeedback>();

            foreach (Transform item in GetComponentsInChildren<Transform>(true))
            {
                if (leftHandRoot == null && item.name == "Left Hand Tracking")
                    leftHandRoot = item.gameObject;
                else if (rightHandRoot == null && item.name == "Right Hand Tracking")
                    rightHandRoot = item.gameObject;
                else if (leftHandRay == null && item.name == "LeftHand")
                    leftHandRay = item.gameObject;
                else if (rightHandRay == null && item.name == "RightHand")
                    rightHandRay = item.gameObject;
                else if (leftController == null && item.name == "Left Controller")
                    leftController = item.gameObject;
                else if (rightController == null && item.name == "Right Controller")
                    rightController = item.gameObject;
            }

            if (!IsTrackedHandSource(leftHandSource, leftHandRoot))
                leftHandSource = FindHandSource(leftHandRoot);
            if (!IsTrackedHandSource(rightHandSource, rightHandRoot))
                rightHandSource = FindHandSource(rightHandRoot);

            if (leftControllerSources.Length == 0)
                leftControllerSources = FindControllerSources(leftController);
            if (rightControllerSources.Length == 0)
                rightControllerSources = FindControllerSources(rightController);
        }

        bool CreateVisuals()
        {
            if (runtimeMaterial == null)
                return false;

            CreateHandVisual(leftHandSource, leftHandVisual, "Left Occluded Hand Visual");
            CreateHandVisual(rightHandSource, rightHandVisual, "Right Occluded Hand Visual");
            CreateControllerVisual(leftController, leftControllerSources, leftControllerVisual, "Left Occluded Controller Visual");
            CreateControllerVisual(rightController, rightControllerSources, rightControllerVisual, "Right Occluded Controller Visual");

            bool handVisualsReady =
                (leftHandRoot == null || leftHandVisual.renderer != null) &&
                (rightHandRoot == null || rightHandVisual.renderer != null);

            return handVisualsReady &&
                leftControllerVisual.renderer != null &&
                rightControllerVisual.renderer != null;
        }

        void CreateHandVisual(SkinnedMeshRenderer source, InputVisual visual, string visualName)
        {
            if (source == null || visual.renderer != null)
                return;

            if (source.sharedMesh == null || source.bones.Length == 0)
            {
                Debug.LogWarning(
                    $"[OccludedInputVisual] {source.name} has no tracked mesh or bones.",
                    source);
                return;
            }

            var visualObject = new GameObject(visualName);
            visualObject.layer = source.gameObject.layer;
            visualObject.transform.SetParent(source.transform.parent, false);
            visualObject.transform.localPosition = source.transform.localPosition;
            visualObject.transform.localRotation = source.transform.localRotation;
            visualObject.transform.localScale = source.transform.localScale;
            visualObject.AddComponent<OccludedInputVisualMarker>();

            SkinnedMeshRenderer renderer = visualObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = source.sharedMesh;
            renderer.rootBone = source.rootBone;
            renderer.bones = source.bones;
            renderer.localBounds = source.localBounds;
            renderer.quality = source.quality;
            renderer.updateWhenOffscreen = true;
            ConfigureRenderer(renderer);
            visual.name = visualName;
            visual.renderer = renderer;
            visual.properties = new MaterialPropertyBlock();

            Debug.Log(
                $"[OccludedInputVisual] Created {visualName}: mesh={source.sharedMesh.name}, bones={source.bones.Length}.",
                visualObject);
        }

        void CreateControllerVisual(
            GameObject root,
            MeshRenderer[] sources,
            InputVisual visual,
            string visualName)
        {
            if (root == null || sources.Length == 0 || visual.renderer != null)
                return;

            var combine = new List<CombineInstance>();
            Matrix4x4 worldToRoot = root.transform.worldToLocalMatrix;

            foreach (MeshRenderer source in sources)
            {
                if (source == null || !source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                    continue;

                for (int subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; ++subMesh)
                {
                    combine.Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh,
                        subMeshIndex = subMesh,
                        transform = worldToRoot * filter.transform.localToWorldMatrix
                    });
                }
            }

            if (combine.Count == 0)
                return;

            var visualObject = new GameObject(visualName);
            visualObject.layer = root.layer;
            visualObject.transform.SetParent(root.transform, false);
            visualObject.AddComponent<OccludedInputVisualMarker>();

            var mesh = new Mesh
            {
                name = visualName + " Mesh",
                indexFormat = IndexFormat.UInt32
            };
            mesh.CombineMeshes(combine.ToArray(), true, true, false);
            mesh.UploadMeshData(true);

            visualObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = visualObject.AddComponent<MeshRenderer>();
            ConfigureRenderer(renderer);
            visual.name = visualName;
            visual.renderer = renderer;
            visual.properties = new MaterialPropertyBlock();
            visual.ownedMesh = mesh;
        }

        void ConfigureRenderer(Renderer renderer)
        {
            renderer.sharedMaterial = runtimeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.enabled = false;
        }

        void CacheInteractors()
        {
            leftHandInteractors = CombineInteractors(leftHandRoot, leftHandRay);
            rightHandInteractors = CombineInteractors(rightHandRoot, rightHandRay);
            leftControllerInteractors = GetInteractors(leftController);
            rightControllerInteractors = GetInteractors(rightController);
        }

        void ApplyMode()
        {
            if (appliedMode == mode)
                return;

            if (contextualFeedback != null)
            {
                bool showIndicators = mode == OccludedInputVisualMode.IndicatorsOnly ||
                    mode == OccludedInputVisualMode.OccludedSoftSilhouetteWithIndicators;
                contextualFeedback.Mode = showIndicators
                    ? InputFeedbackMode.RayAndContextualIndicators
                    : InputFeedbackMode.RayOnly;
            }

            if (runtimeMaterial != null)
            {
                bool alwaysVisible = mode == OccludedInputVisualMode.DebugAlwaysVisible;
                bool occludedDebug = mode == OccludedInputVisualMode.DebugOccludedBright;
                bool debugMode = alwaysVisible || occludedDebug;
                CompareFunction occludedCompare = SystemInfo.usesReversedZBuffer
                    ? CompareFunction.Less
                    : CompareFunction.Greater;
                runtimeMaterial.SetFloat(
                    ZTestId,
                    (float)(alwaysVisible ? CompareFunction.Always : occludedCompare));
                runtimeMaterial.SetFloat(
                    CullId,
                    (float)(debugMode ? CullMode.Off : CullMode.Back));
            }

            appliedMode = mode;
        }

        void UpdateVisual(InputVisual visual, GameObject trackedRoot, XRBaseInteractor[] interactors)
        {
            if (visual.renderer == null)
                return;

            bool showSilhouette = mode != OccludedInputVisualMode.IndicatorsOnly;
            bool tracked = trackedRoot != null && trackedRoot.activeInHierarchy;
            bool visible = showSilhouette && tracked;
            SetEnabled(visual, visible);

            if (!visible)
            {
                visual.opacity = idleOpacity;
                return;
            }

            if (!visual.activeLogged)
            {
                Debug.Log(
                    $"[OccludedInputVisual] Active {visual.name}: mode={mode}, rootActive={tracked}, " +
                    $"rendererEnabled={visual.renderer.enabled}, shader={runtimeMaterial.shader.name}, " +
                    $"shaderSupported={runtimeMaterial.shader.isSupported}, layer={visual.renderer.gameObject.layer}, " +
                    $"reversedZ={SystemInfo.usesReversedZBuffer}, graphicsApi={SystemInfo.graphicsDeviceType}.",
                    visual.renderer);
                visual.activeLogged = true;
            }

            bool debugMode = mode == OccludedInputVisualMode.DebugAlwaysVisible ||
                mode == OccludedInputVisualMode.DebugOccludedBright;
            float targetOpacity = debugMode
                ? 0.8f
                : GetTargetOpacity(interactors);
            visual.opacity = Mathf.MoveTowards(
                visual.opacity,
                targetOpacity,
                opacitySpeed * Time.unscaledDeltaTime);

            bool outline = mode == OccludedInputVisualMode.OccludedOutline;
            visual.properties.SetFloat(OpacityId, visual.opacity);
            visual.properties.SetFloat(RimStrengthId, outline ? 1f : softRimStrength);
            visual.properties.SetFloat(RimPowerId, outline ? outlineRimPower : softRimPower);
            visual.renderer.SetPropertyBlock(visual.properties);
        }

        float GetTargetOpacity(XRBaseInteractor[] interactors)
        {
            bool hovered = false;
            foreach (XRBaseInteractor interactor in interactors)
            {
                if (interactor == null)
                    continue;
                if (interactor.hasSelection)
                    return activeOpacity;
                hovered |= interactor.hasHover;
            }

            return hovered ? hoverOpacity : idleOpacity;
        }

        static SkinnedMeshRenderer FindHandSource(GameObject root)
        {
            if (root == null)
                return null;

            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.GetComponentInParent<OccludedInputVisualMarker>() == null)
                    return renderer;
            }

            return null;
        }

        static bool IsTrackedHandSource(SkinnedMeshRenderer source, GameObject root)
        {
            return source != null &&
                root != null &&
                source.gameObject.scene.IsValid() &&
                source.transform.IsChildOf(root.transform);
        }

        static MeshRenderer[] FindControllerSources(GameObject root)
        {
            if (root == null)
                return System.Array.Empty<MeshRenderer>();

            var sources = new List<MeshRenderer>();
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.GetComponentInParent<OccludedInputVisualMarker>() == null &&
                    renderer.TryGetComponent(out MeshFilter filter) &&
                    filter.sharedMesh != null)
                {
                    sources.Add(renderer);
                }
            }

            return sources.ToArray();
        }

        static XRBaseInteractor[] CombineInteractors(GameObject first, GameObject second)
        {
            var interactors = new List<XRBaseInteractor>();
            if (first != null)
                interactors.AddRange(first.GetComponentsInChildren<XRBaseInteractor>(true));
            if (second != null)
                interactors.AddRange(second.GetComponentsInChildren<XRBaseInteractor>(true));
            return interactors.ToArray();
        }

        static XRBaseInteractor[] GetInteractors(GameObject root)
        {
            return root != null
                ? root.GetComponentsInChildren<XRBaseInteractor>(true)
                : System.Array.Empty<XRBaseInteractor>();
        }

        static void SetEnabled(InputVisual visual, bool enabled)
        {
            if (visual.renderer != null && visual.renderer.enabled != enabled)
                visual.renderer.enabled = enabled;
        }

        static void DestroyOwnedMesh(InputVisual visual)
        {
            if (visual.ownedMesh != null)
                Destroy(visual.ownedMesh);
        }
    }
}
