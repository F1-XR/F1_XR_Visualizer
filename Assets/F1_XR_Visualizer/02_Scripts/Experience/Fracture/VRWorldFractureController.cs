using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace F1XR.Experience.Fracture
{
    /// <summary>
    /// The return to MR, phase A: the VR world is what breaks.
    ///
    /// This replaces <see cref="RoomShellFractureController"/> on the way home, and only on
    /// the way home. Breaking the physical room shell is right going into VR, where the user
    /// is looking at the real room; coming back they are looking at the VR world, and
    /// cracking the shape of the real walls in front of it puts the outgoing world and the
    /// thing that breaks in two different places.
    ///
    /// Nothing is captured and nothing is masked. The VR scene renders live for the whole
    /// break; a full-screen pass reads its depth, recovers where each pixel is in the VR
    /// world, and writes framebuffer alpha 0 for the pixels the fracture field has taken. The
    /// field is anchored to a world position, so the hole belongs to the VR world rather than
    /// to the screen: leaning left shows the same hole from a new angle instead of dragging
    /// it along.
    ///
    /// Phase A stops at the hole. No fragments, no dust, nothing falls. Whether a
    /// world-locked hole appears at all is the question worth answering first, and debris
    /// built on a field that swims would be wasted.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VRWorldFractureController : MonoBehaviour
    {
        [SerializeField] ExperienceModeManager experienceManager;
        [SerializeField] PassthroughTransitionController passthrough;
        [SerializeField] Camera xrCamera;

        [Tooltip("Assign the return hook on the mode manager. Turn off to leave the mode " +
            "change to whatever else claims it.")]
        [SerializeField] bool driveReturnToMR = true;

        [Header("Where it starts")]
        [Tooltip("Metres in front of the head, at the moment the return begins, for the point " +
            "the crack opens from. The point is then fixed in the VR world and does not " +
            "follow the head.")]
        [SerializeField, Range(0.5f, 8f)] float originDistance = 3f;

        [Tooltip("Metres below eye level for that same point, so the break does not open " +
            "exactly on the horizon line.")]
        [SerializeField, Range(-2f, 2f)] float originHeightOffset = -0.4f;

        [Header("How it grows")]
        [Tooltip("How far the break reaches by the end. It has to comfortably exceed the " +
            "distance to the furthest VR surface, or a ring of VR is left standing.")]
        [SerializeField, Min(1f)] float finalRadius = 30f;

        [SerializeField, Min(0.1f)] float duration = 2.5f;

        [Tooltip("Growth shape. Slow at first reads as a crack opening rather than a wipe.")]
        [SerializeField] AnimationCurve growth = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Crack shape")]
        [Tooltip("How irregular the edge is, in metres of wander. Zero gives an expanding " +
            "sphere, which reads as a dissolve rather than a fracture.")]
        [SerializeField, Range(0f, 4f)] float noiseStrength = 0.9f;

        [Tooltip("Size of the irregularity. Higher is finer, more shattered detail.")]
        [SerializeField, Range(0.05f, 8f)] float noiseFrequency = 1.1f;

        [Tooltip("Thickness of the dark line at the boundary. Kept thin on purpose: the " +
            "target is a crack in the world, not a glowing sci-fi dissolve.")]
        [SerializeField, Range(0.001f, 1f)] float edgeWidth = 0.08f;

        [SerializeField] Color edgeColor = new(0.02f, 0.02f, 0.03f, 1f);

        [Tooltip("Seconds to hold after the last VR pixel is gone, before the mode manager " +
            "settles MR and switches the VR environment off.")]
        [SerializeField, Min(0f)] float tailSeconds = 0.15f;

        static readonly int FractureOriginId = Shader.PropertyToID("_FractureOrigin");
        static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        static readonly int NoiseFrequencyId = Shader.PropertyToID("_NoiseFrequency");
        static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");

        Material material;
        Mesh mesh;
        GameObject overlay;

        UniversalAdditionalCameraData cameraData;
        bool previousRequiresDepthTexture;
        bool depthTextureForced;

        void Awake()
        {
            if (experienceManager == null)
                experienceManager = FindAnyObjectByType<ExperienceModeManager>();

            if (passthrough == null)
                passthrough = FindAnyObjectByType<PassthroughTransitionController>();
        }

        void OnEnable()
        {
            if (!driveReturnToMR || experienceManager == null)
            {
                Debug.LogWarning(
                    $"[VR2MR][world] NOT claiming the return: driveReturnToMR=" +
                    $"{driveReturnToMR} manager={(experienceManager != null ? "found" : "NULL")}. " +
                    "The old room shell fracture will run instead.",
                    this);
                return;
            }

            experienceManager.RebuildSequence = PlayReturnSequence;
            Debug.Log(
                "[VR2MR][world] CLAIMED the return hook. Return MR will break the VR world.",
                this);
        }

        void OnDisable()
        {
            if (experienceManager != null &&
                experienceManager.RebuildSequence == PlayReturnSequence)
            {
                experienceManager.RebuildSequence = null;
            }

            Teardown();
        }

        public IEnumerator PlayReturnSequence() => RunFracture();

        IEnumerator RunFracture()
        {
            Debug.Log("[VR2MR][world] return requested, VR world fracture.", this);

            if (!ResolveCamera())
            {
                Debug.LogError(
                    "[VR2MR][FATAL VISUAL] No camera for the VR world fracture; the return " +
                    "continues without it.",
                    this);
                yield break;
            }

            Shader shader = Shader.Find("F1XR/VRWorldFractureField");
            if (shader == null)
            {
                Debug.LogError(
                    "[VR2MR][FATAL VISUAL] F1XR/VRWorldFractureField shader missing. If this " +
                    "only happens in a headset build it has been stripped; add it to Always " +
                    "Included Shaders.",
                    this);
                yield break;
            }

            // The compositor underlay is switched on but the camera stays fully opaque, so
            // the real room is not revealed anywhere yet. It can only be seen through the
            // alpha holes this pass punches.
            if (passthrough == null || !passthrough.PrepareMRIncoming())
            {
                Debug.LogError(
                    "[VR2MR][FATAL VISUAL] Passthrough could not be prepared; refusing to " +
                    "break the VR world into nothing.",
                    this);
                yield break;
            }

            // Per camera rather than on the pipeline asset: the asset is shared with every
            // other camera and with the editor, and this is a two second effect.
            previousRequiresDepthTexture = cameraData.requiresDepthTexture;
            cameraData.requiresDepthTexture = true;
            depthTextureForced = true;

            // Fixed in the VR world the instant the return starts. Read from the head once
            // and never again: if this kept tracking the head the break would follow the
            // eyes, which is the exact failure this whole route exists to avoid.
            Transform head = xrCamera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : head.forward;
            Vector3 origin = head.position
                + forward * originDistance
                + Vector3.up * originHeightOffset;

            BuildOverlay(shader, origin);

            Debug.Log(
                $"[VR2MR][world] origin={origin} finalRadius={finalRadius} " +
                $"duration={duration} depthTexture was={previousRequiresDepthTexture}.",
                this);

            try
            {
                float elapsed = 0f;
                bool loggedFirstHole = false;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float threshold =
                        growth.Evaluate(Mathf.Clamp01(elapsed / duration)) * finalRadius;

                    material.SetFloat(ThresholdId, threshold);

                    if (!loggedFirstHole && threshold > 0f)
                    {
                        loggedFirstHole = true;
                        Debug.Log("[VR2MR][world] FirstMRHoleRevealed", this);
                    }

                    yield return null;
                }

                // Hold the field wide open so no VR survives into the handover.
                material.SetFloat(ThresholdId, finalRadius);
                Debug.Log("[VR2MR][world] FractureComplete", this);

                if (tailSeconds > 0f)
                    yield return new WaitForSeconds(tailSeconds);
            }
            finally
            {
                // The manager settles MR and switches the VR environment off after this
                // returns, so the overlay and the depth texture go now, whatever happened.
                Teardown();
            }
        }

        void BuildOverlay(Shader shader, Vector3 origin)
        {
            material = new Material(shader) { name = "VRWorldFractureField" };
            material.SetVector(FractureOriginId, origin);
            material.SetFloat(ThresholdId, 0f);
            material.SetFloat(EdgeWidthId, edgeWidth);
            material.SetFloat(NoiseFrequencyId, noiseFrequency);
            material.SetFloat(NoiseStrengthId, noiseStrength);
            material.SetColor(EdgeColorId, edgeColor);

            mesh = BuildClipSpaceTriangle();

            overlay = new GameObject("VRWorldFractureOverlay");
            overlay.transform.SetParent(transform, false);
            overlay.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = overlay.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void Teardown()
        {
            if (depthTextureForced && cameraData != null)
                cameraData.requiresDepthTexture = previousRequiresDepthTexture;

            depthTextureForced = false;

            DestroySafely(overlay);
            DestroySafely(material);
            DestroySafely(mesh);

            overlay = null;
            material = null;
            mesh = null;
        }

        bool ResolveCamera()
        {
            if (xrCamera == null)
                xrCamera = Camera.main;

            if (xrCamera == null)
                return false;

            cameraData = xrCamera.GetUniversalAdditionalCameraData();
            return cameraData != null;
        }

        /// <summary>
        /// A triangle whose vertex positions are already clip-space coordinates, used by the
        /// shader untransformed. Bounds are made enormous so frustum culling, which has no
        /// idea this ignores its transform, never removes it.
        /// </summary>
        static Mesh BuildClipSpaceTriangle()
        {
            var built = new Mesh { name = "VRWorldFractureTriangle" };
            built.SetVertices(new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(3f, -1f, 0f),
                new Vector3(-1f, 3f, 0f)
            });
            built.SetTriangles(new[] { 0, 1, 2 }, 0, false);
            built.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            return built;
        }

        static void DestroySafely(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
