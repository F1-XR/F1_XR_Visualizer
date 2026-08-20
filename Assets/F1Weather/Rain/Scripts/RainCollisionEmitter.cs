using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(ParticleSystem))]
public class RainCollisionEmitter : MonoBehaviour
{
    [Header("Impact Targets")]
    public ParticleSystem splashSystem;
    public ParticleSystem rippleSystem;

    [Header("Sampling")]
    [Tooltip("Max splash+ripple pairs spawned per frame")]
    [Range(1, 100)]
    public int maxImpactsPerFrame = 35;

    [Tooltip("Probability that each collision event produces a visible impact")]
    [Range(0f, 1f)]
    public float impactChance = 0.35f;

    [Header("Splash")]
    [Tooltip("Number of tiny droplets per impact")]
    [Range(2, 12)]
    public int splashCount = 5;

    [Tooltip("Base ejection speed of splash droplets")]
    public float splashSpeed = 1.2f;

    [Tooltip("Half-angle of the cone around surface normal")]
    [Range(10f, 80f)]
    public float splashConeHalfAngle = 50f;

    [Header("Ripple")]
    [Tooltip("Offset along surface normal to prevent Z-fighting")]
    public float rippleNormalOffset = 0.003f;

    ParticleSystem _rain;
    readonly List<ParticleCollisionEvent> _events = new(64);
    int _frameImpacts;

    void Awake()
    {
        _rain = GetComponent<ParticleSystem>();
        EnsureRippleMesh();
    }

    void LateUpdate() => _frameImpacts = 0;

    void OnParticleCollision(GameObject other)
    {
        int n = _rain.GetCollisionEvents(other, _events);

        for (int i = 0; i < n && _frameImpacts < maxImpactsPerFrame; i++)
        {
            if (Random.value > impactChance) continue;

            Vector3 pos = _events[i].intersection;
            Vector3 norm = _events[i].normal.normalized;

            EmitSplash(pos, norm);
            EmitRipple(pos, norm);
            _frameImpacts++;
        }
    }

    void EmitSplash(Vector3 pos, Vector3 normal)
    {
        if (!splashSystem) return;

        Quaternion basis = Quaternion.LookRotation(normal);
        Vector3 origin = pos + normal * 0.002f;

        for (int i = 0; i < splashCount; i++)
        {
            Vector3 dir = basis * (Quaternion.Euler(
                Random.Range(0f, splashConeHalfAngle),
                Random.Range(0f, 360f), 0f) * Vector3.forward);

            float spd = splashSpeed * Random.Range(0.4f, 1.6f);

            var ep = new ParticleSystem.EmitParams
            {
                position  = origin,
                velocity  = dir * spd,
                startSize = Random.Range(0.006f, 0.018f),
                startLifetime = Random.Range(0.08f, 0.25f),
                startColor = new Color(0.8f, 0.85f, 0.95f,
                                       Random.Range(0.25f, 0.65f))
            };
            splashSystem.Emit(ep, 1);
        }
    }

    void EmitRipple(Vector3 pos, Vector3 normal)
    {
        if (!rippleSystem) return;

        Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal);

        var ep = new ParticleSystem.EmitParams
        {
            position      = pos + normal * rippleNormalOffset,
            velocity      = Vector3.zero,
            rotation3D    = rot.eulerAngles,
            startSize     = Random.Range(0.04f, 0.09f),
            startLifetime = Random.Range(0.2f, 0.4f),
            startColor    = new Color(0.75f, 0.82f, 0.93f,
                                      Random.Range(0.15f, 0.4f))
        };
        rippleSystem.Emit(ep, 1);
    }

    void EnsureRippleMesh()
    {
        if (!rippleSystem) return;
        var rr = rippleSystem.GetComponent<ParticleSystemRenderer>();
        if (rr.renderMode == ParticleSystemRenderMode.Mesh && rr.mesh) return;

        rr.renderMode = ParticleSystemRenderMode.Mesh;
        rr.alignment  = ParticleSystemRenderSpace.Local;
        rr.mesh       = CreateRingMesh();
    }

    static Mesh CreateRingMesh(int seg = 32, float inner = 0.45f, float outer = 0.5f)
    {
        int vc = (seg + 1) * 2;
        var verts = new Vector3[vc];
        var uvs   = new Vector2[vc];
        var tris  = new int[seg * 6];

        for (int i = 0; i <= seg; i++)
        {
            float angle = (float)i / seg * Mathf.PI * 2f;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            verts[i * 2]     = new Vector3(c * inner, 0f, s * inner);
            verts[i * 2 + 1] = new Vector3(c * outer, 0f, s * outer);
            uvs[i * 2]       = new Vector2((float)i / seg, 0f);
            uvs[i * 2 + 1]   = new Vector2((float)i / seg, 1f);
        }

        for (int i = 0; i < seg; i++)
        {
            int b = i * 6, v = i * 2;
            tris[b]   = v;     tris[b+1] = v + 2; tris[b+2] = v + 1;
            tris[b+3] = v + 1; tris[b+4] = v + 2; tris[b+5] = v + 3;
        }

        var mesh = new Mesh { name = "RainRippleRing" };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
