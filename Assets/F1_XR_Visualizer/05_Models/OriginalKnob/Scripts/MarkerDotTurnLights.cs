using UnityEngine;

namespace F1XR.OriginalKnob
{
    /// <summary>
    /// Lights up the marker dots as a turn counter: every full turn of the knob, one more dot switches
    /// from grey to glowing white, starting from the OUTERMOST dot (furthest from the knob centre) and
    /// working inward. Unwinding turns it back off. It only drives each dot's colour/emission through a
    /// MaterialPropertyBlock - it never touches the dot transforms (positions/sizes are author-set).
    ///
    /// The dot order is derived from their current local positions (distance from centre), so it adapts to
    /// however the dots are arranged.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class MarkerDotTurnLights : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] RotaryKnobController knob;
        [Tooltip("Parent of the dot renderers (RotationMarker). If empty, found under the knob pivot.")]
        [SerializeField] Transform rotationMarker;

        [Header("Counter")]
        [Tooltip("Turns needed to light each additional dot.")]
        [SerializeField, Min(0.05f)] float turnsPerDot = 1f;

        [Header("Look")]
        [Tooltip("Dot colour when off (grey).")]
        [SerializeField] Color offColor = new Color(0.32f, 0.32f, 0.35f, 1f);
        [Tooltip("Dot colour when lit (white).")]
        [SerializeField] Color onColor = Color.white;
        [Tooltip("Emission strength when lit (HDR, for bloom).")]
        [SerializeField, Min(0f)] float onEmission = 2.4f;
        [Tooltip("Fade time (s) as a dot turns on/off.")]
        [SerializeField, Min(0f)] float fadeTime = 0.12f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        MeshRenderer[] dots; // ordered outermost -> innermost
        float[] lit;
        MaterialPropertyBlock mpb;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            Collect();
        }

        void OnEnable()
        {
            if (mpb == null)
                mpb = new MaterialPropertyBlock();
            Collect();
        }

        void Collect()
        {
            if (knob == null)
                knob = GetComponentInParent<RotaryKnobController>();

            Transform mk = rotationMarker;
            if (mk == null && knob != null && knob.Pivot != null)
                mk = knob.Pivot.Find("RotationMarker");

            if (mk == null)
            {
                dots = new MeshRenderer[0];
                lit = new float[0];
                return;
            }

            dots = mk.GetComponentsInChildren<MeshRenderer>();
            // Outermost first: sort by squared distance from the centre (local XY), descending.
            System.Array.Sort(dots, (a, b) => SqrDist(b).CompareTo(SqrDist(a)));
            lit = new float[dots.Length];
        }

        static float SqrDist(MeshRenderer r)
        {
            Vector3 p = r.transform.localPosition;
            return p.x * p.x + p.y * p.y;
        }

        void LateUpdate()
        {
            if (dots == null || dots.Length == 0)
                return;
            if (mpb == null)
                mpb = new MaterialPropertyBlock();

            float dt = Mathf.Max(Application.isPlaying ? Time.deltaTime : 0.016f, 1e-5f);
            float s = fadeTime <= 0f ? 1f : 1f - Mathf.Exp(-dt / Mathf.Max(fadeTime, 1e-4f));

            float turns = knob != null ? Mathf.Abs(knob.VisualAngle) / 360f : 0f;
            float per = Mathf.Max(turnsPerDot, 0.05f);

            for (int i = 0; i < dots.Length; i++)
            {
                float target = turns >= (i + 1) * per ? 1f : 0f; // dot i lights after (i+1) full turns
                lit[i] = Mathf.Lerp(lit[i], target, s);
                Apply(dots[i], lit[i]);
            }
        }

        void Apply(MeshRenderer r, float t)
        {
            if (r == null)
                return;

            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, Color.Lerp(offColor, onColor, t));
            mpb.SetColor(EmissionColorId, onColor * (onEmission * t));
            r.SetPropertyBlock(mpb);
        }
    }
}
