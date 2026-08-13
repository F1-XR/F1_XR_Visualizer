using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.Drone
{
    /// <summary>
    /// A blink. Covers both eyes completely for a tenth of a second so a scale jump and a
    /// teleport can happen where nobody can see them.
    ///
    /// This is scaffolding, not the effect. Showing the map grow the whole way from tabletop
    /// to a thousand times is what makes the transition sickening: the entire field of view
    /// expands continuously while the inner ear reports sitting still. Hiding the worst of
    /// that behind a blink is the cheapest way to find out how much of the nausea is the
    /// scale change itself and how much is simply watching it happen.
    ///
    /// Deliberately kept free of any knowledge of the transition. It exposes "cover", "reveal"
    /// and "are you covered", and the coordinator decides what to do in the dark. When the
    /// fracture eventually takes this job over, breaking geometry replaces this quad and
    /// nothing else has to move.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VRDroneTransitionOccluder : MonoBehaviour
    {
        [Tooltip("Metres in front of the eyes. Just past the near clip plane, so nothing in " +
            "the world can ever be closer.")]
        [SerializeField, Min(0.01f)] float distance = 0.12f;

        [Tooltip("Width and height of the quad at that distance. Generous on purpose: the " +
            "two eye frustums are asymmetric, and a quad sized exactly for one eye leaves a " +
            "visible sliver in the other.")]
        [SerializeField, Min(0.1f)] float size = 3f;

        [SerializeField] Color color = new(0f, 0f, 0f, 1f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Camera targetCamera;
        GameObject quad;
        Material material;
        float alpha;

        public bool IsFullyCovered => alpha >= 0.999f;

        public void Configure(Camera camera)
        {
            targetCamera = camera;
        }

        /// <summary>Fades to fully opaque and does not return until the view is covered.</summary>
        public IEnumerator Cover(float duration)
        {
            if (!EnsureQuad())
            {
                // Silent failure here means the jump it was meant to hide plays out in full
                // view, which looks like the world lurching sideways and tearing.
                Debug.LogError(
                    "[VRDroneOccluder] Could not build the blink quad; the scale jump will be " +
                    "fully visible.",
                    this);
                yield break;
            }

            quad.SetActive(true);
            yield return FadeTo(1f, duration);

            Debug.Log(
                $"[VRDroneOccluder] covered alpha={alpha:F2} active={quad.activeInHierarchy} " +
                $"parent={(quad.transform.parent != null ? quad.transform.parent.name : "none")}",
                this);
        }

        /// <summary>Fades back to clear, then puts the quad away.</summary>
        public IEnumerator Reveal(float duration)
        {
            if (quad == null)
                yield break;

            yield return FadeTo(0f, duration);
            quad.SetActive(false);
        }

        public void HideImmediate()
        {
            alpha = 0f;
            if (quad != null)
                quad.SetActive(false);
        }

        IEnumerator FadeTo(float target, float duration)
        {
            float start = alpha;

            if (duration <= 0f)
            {
                SetAlpha(target);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                SetAlpha(Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            SetAlpha(target);

            // One more frame at the final value. Without it the caller can do its work in the
            // same frame the last alpha was written, before that frame has been presented,
            // and the jump shows through.
            yield return null;
        }

        void SetAlpha(float value)
        {
            alpha = Mathf.Clamp01(value);
            if (material == null)
                return;

            Color next = color;
            next.a = alpha;
            material.SetColor(BaseColorId, next);
            material.SetColor(ColorId, next);
        }

        bool EnsureQuad()
        {
            if (quad != null)
                return true;

            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetCamera == null)
            {
                Debug.LogError("[VRDroneOccluder] No camera to attach to.", this);
                return false;
            }

            Material created = CreateMaterial();
            if (created == null)
                return false;

            material = created;

            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "VR Drone Transition Occluder";

            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            // Parented to the camera rather than positioned every frame: head tracking then
            // carries it for free and it cannot lag behind a fast turn.
            Transform quadTransform = quad.transform;
            quadTransform.SetParent(targetCamera.transform, false);
            quadTransform.localPosition = new Vector3(0f, 0f, distance);
            quadTransform.localRotation = Quaternion.identity;
            quadTransform.localScale = new Vector3(size, size, 1f);

            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            SetAlpha(0f);
            quad.SetActive(false);
            return true;
        }

        Material CreateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogError("[VRDroneOccluder] No unlit shader available.", this);
                return null;
            }

            var created = new Material(shader)
            {
                name = "VR Drone Occluder (Runtime)",

                // Overlay: last thing drawn, so no transparent object in either world can
                // sort in front of it.
                renderQueue = 4000
            };

            created.SetOverrideTag("RenderType", "Transparent");
            if (created.HasProperty("_Surface"))
                created.SetFloat("_Surface", 1f);
            created.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (created.HasProperty("_SrcBlend"))
                created.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (created.HasProperty("_DstBlend"))
                created.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (created.HasProperty("_ZWrite"))
                created.SetFloat("_ZWrite", 0f);

            // Always: the quad sits inside the near clip of nothing in particular, and depth
            // testing it against a world that is about to be rescaled by a thousand invites
            // exactly the one frame of leakage this exists to prevent.
            if (created.HasProperty("_ZTest"))
                created.SetFloat("_ZTest", (float)CompareFunction.Always);

            return created;
        }

        void OnDestroy()
        {
            if (quad != null)
                Destroy(quad);

            if (material != null)
                Destroy(material);
        }
    }
}
