using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace F1XR.PlayPanel
{
    /// <summary>
    /// Scene-scoped polish for the XR ray visuals in the UI Test scene (does NOT edit the shared XR rig
    /// prefab). At runtime it finds every <see cref="XRInteractorLineVisual"/> in the scene and:
    ///   - thins the ray and makes it subtly transparent so it doesn't dominate the view,
    ///   - gives it a small round reticle dot at the hit point.
    /// Because this component only lives in this scene, other scenes are unaffected.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneRayStyler : MonoBehaviour
    {
        [Header("Ray line")]
        [SerializeField, Min(0.0005f)] float lineWidth = 0.004f;
        [SerializeField] Color lineColor = new Color(0.8f, 0.9f, 1f, 0.35f);
        [SerializeField] Color lineColorEnd = new Color(0.8f, 0.9f, 1f, 0.05f);

        [Header("Reticle dot")]
        [SerializeField] bool addReticle = true;
        [SerializeField, Min(0.001f)] float reticleSize = 0.012f;
        [SerializeField] Color reticleColor = new Color(0.6f, 0.95f, 1f, 1f);

        Material reticleMat;

        void Start() => ApplyToScene();

        void ApplyToScene()
        {
            var visuals = FindObjectsByType<XRInteractorLineVisual>(FindObjectsInactive.Include);
            foreach (var v in visuals)
            {
                if (v == null)
                    continue;

                v.lineWidth = lineWidth;

                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(lineColor, 0f), new GradientColorKey(lineColorEnd, 1f) },
                    new[] { new GradientAlphaKey(lineColor.a, 0f), new GradientAlphaKey(lineColorEnd.a, 1f) });
                v.setLineColorGradient = true;
                v.validColorGradient = grad;
                v.invalidColorGradient = grad;

                if (addReticle && v.reticle == null)
                    v.reticle = CreateReticle();
            }
        }

        GameObject CreateReticle()
        {
            if (reticleMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                reticleMat = new Material(shader);
                reticleMat.SetColor("_BaseColor", reticleColor);
            }

            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "RayReticleDot";
            var col = dot.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            dot.transform.SetParent(transform, false);
            dot.transform.localScale = Vector3.one * reticleSize;
            dot.GetComponent<MeshRenderer>().sharedMaterial = reticleMat;
            dot.SetActive(false);
            return dot;
        }
    }
}
