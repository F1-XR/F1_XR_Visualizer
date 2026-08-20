#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class RainSystemBuilder
{
    const string MatDir  = "Assets/F1Weather/Rain/Materials";
    const string MeshDir = "Assets/F1Weather/Rain/Meshes";

    [MenuItem("F1Weather/Create Heavy Rain System")]
    static void Create()
    {
        var root = new GameObject("RainSystem");
        Undo.RegisterCreatedObjectUndo(root, "Create Heavy Rain System");
        root.transform.position = new Vector3(0f, 10f, 0f);

        var fallGO   = Child(root, "RainFall");
        var splashGO = Child(root, "RainSplash");
        var rippleGO = Child(root, "RainRipple");

        var fall   = fallGO.AddComponent<ParticleSystem>();
        var splash = splashGO.AddComponent<ParticleSystem>();
        var ripple = rippleGO.AddComponent<ParticleSystem>();

        fall.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ripple.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var rainMat   = LoadOrCreateMat("M_F1_Rain",       new Color(0.75f, 0.8f, 0.9f, 0.45f));
        var splashMat = LoadOrCreateMat("M_F1_RainSplash",  new Color(0.8f, 0.85f, 0.95f, 0.5f));
        var rippleMat = LoadOrCreateMat("M_F1_RainRipple",  new Color(0.75f, 0.82f, 0.93f, 0.35f));

        ConfigureFall(fall, rainMat);
        ConfigureSplash(splash, splashMat);
        ConfigureRipple(ripple, rippleMat);

        var emitter = fallGO.AddComponent<RainCollisionEmitter>();
        emitter.splashSystem = splash;
        emitter.rippleSystem = ripple;

        Selection.activeGameObject = root;
        Debug.Log("[RainSystem] Created. Enter Play mode with colliders in scene to test.");
    }

    // ─── Hierarchy ──────────────────────────────────────────

    static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    // ─── RainFall ───────────────────────────────────────────

    static void ConfigureFall(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.loop           = true;
        main.prewarm        = true;
        main.playOnAwake    = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode    = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles   = 2000;
        main.gravityModifier = 0.3f;

        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed    = 0f;

        main.startSize3D = true;
        main.startSizeX  = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
        main.startSizeY  = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
        main.startSizeZ  = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);

        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.65f, 0.72f, 0.85f, 0.15f),
            new Color(0.85f, 0.88f, 0.95f, 0.55f));

        // Emission
        var em = ps.emission;
        em.enabled      = true;
        em.rateOverTime = 500f;

        // Shape: wide box overhead
        var sh = ps.shape;
        sh.enabled   = true;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale     = new Vector3(20f, 0.1f, 20f);

        // Slight wind for ↘ diagonal look
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(1.5f, 3.0f);
        vel.y = new ParticleSystem.MinMaxCurve(-22f, -18f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

        // Alpha ramp: brief fade-in, fade-out at end
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(0.5f, 0f),
                new GradientAlphaKey(1.0f, 0.1f),
                new GradientAlphaKey(0.7f, 0.75f),
                new GradientAlphaKey(0.0f, 1f) });
        col.color = g;

        // Collision: kill on hit, send messages
        var coll = ps.collision;
        coll.enabled               = true;
        coll.type                  = ParticleSystemCollisionType.World;
        coll.quality               = ParticleSystemCollisionQuality.Medium;
        coll.bounce                = new ParticleSystem.MinMaxCurve(0f);
        coll.lifetimeLoss          = new ParticleSystem.MinMaxCurve(1f);
        coll.dampen                = new ParticleSystem.MinMaxCurve(1f);
        coll.sendCollisionMessages = true;

        // Renderer: Stretched Billboard
        var rr = ps.GetComponent<ParticleSystemRenderer>();
        rr.renderMode          = ParticleSystemRenderMode.Stretch;
        rr.cameraVelocityScale = 0f;
        rr.velocityScale       = 0.06f;
        rr.lengthScale         = 2.0f;
        rr.material            = mat;
        rr.enableGPUInstancing = true;
    }

    // ─── RainSplash ─────────────────────────────────────────

    static void ConfigureSplash(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.loop            = true;
        main.playOnAwake     = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 3000;
        main.gravityModifier = 1.5f;

        main.startLifetime = 0.2f;
        main.startSpeed    = 0f;
        main.startSize     = 0.01f;
        main.startColor    = new Color(0.8f, 0.85f, 0.95f, 0.5f);

        // Emission: script-driven only
        var em = ps.emission;
        em.enabled      = true;
        em.rateOverTime = 0f;

        var sh = ps.shape;
        sh.enabled = false;

        // Alpha fade-out
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(1f,   0f),
                new GradientAlphaKey(0.6f, 0.4f),
                new GradientAlphaKey(0f,   1f) });
        col.color = g;

        var rr = ps.GetComponent<ParticleSystemRenderer>();
        rr.renderMode          = ParticleSystemRenderMode.Billboard;
        rr.material            = mat;
        rr.enableGPUInstancing = true;
    }

    // ─── RainRipple ─────────────────────────────────────────

    static void ConfigureRipple(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.loop            = true;
        main.playOnAwake     = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 2000;
        main.gravityModifier = 0f;
        main.startRotation3D = true;

        main.startLifetime = 0.35f;
        main.startSpeed    = 0f;
        main.startSize     = 0.07f;
        main.startColor    = new Color(0.75f, 0.82f, 0.93f, 0.3f);

        var em = ps.emission;
        em.enabled      = true;
        em.rateOverTime = 0f;

        var sh = ps.shape;
        sh.enabled = false;

        // Ring expands: 1x → 3.5x over lifetime
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(3.5f,
            new AnimationCurve(
                new Keyframe(0f, 0.286f, 0f, 2f),
                new Keyframe(1f, 1f,     0.5f, 0f)));

        // Alpha fade-out
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f) },
            new[] {
                new GradientAlphaKey(1f,   0f),
                new GradientAlphaKey(0.7f, 0.25f),
                new GradientAlphaKey(0f,   1f) });
        col.color = g;

        // Renderer: ring Mesh, Local alignment
        var rr = ps.GetComponent<ParticleSystemRenderer>();
        rr.renderMode          = ParticleSystemRenderMode.Mesh;
        rr.alignment           = ParticleSystemRenderSpace.Local;
        rr.material            = mat;
        rr.enableGPUInstancing = true;

        EnsureDir(MeshDir);
        string meshPath = MeshDir + "/RainRippleRing.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null)
        {
            mesh = CreateRingMesh();
            AssetDatabase.CreateAsset(mesh, meshPath);
        }
        rr.mesh = mesh;
    }

    // ─── Ring Mesh ──────────────────────────────────────────

    static Mesh CreateRingMesh(int seg = 32, float inner = 0.45f, float outer = 0.5f)
    {
        int vc    = (seg + 1) * 2;
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

        var m = new Mesh { name = "RainRippleRing" };
        m.vertices  = verts;
        m.uv        = uvs;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // ─── Material Utility ───────────────────────────────────

    static Material LoadOrCreateMat(string name, Color color)
    {
        string path = $"{MatDir}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[RainSystem] URP Particles/Unlit not found, trying fallback.");
            shader = Shader.Find("Particles/Standard Unlit");
        }

        var mat = new Material(shader) { name = name };
        mat.SetFloat("_Surface", 1f);       // Transparent
        mat.SetFloat("_SrcBlend", 5f);      // SrcAlpha
        mat.SetFloat("_DstBlend", 10f);     // One  (Additive)
        mat.SetFloat("_SrcBlendAlpha", 1f);
        mat.SetFloat("_DstBlendAlpha", 10f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_AlphaClip", 0f);
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color", color);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("SHADOWCASTER", false);

        EnsureDir(MatDir);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureDir(string dir)
    {
        if (AssetDatabase.IsValidFolder(dir)) return;
        var parts = dir.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
#endif
