#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class MiniCloudBuilder
{
    const string MatDir = "Assets/F1Weather/Cloud/Materials";
    const string TexDir = "Assets/F1Weather/Cloud/Textures";

    [MenuItem("F1Weather/Create Mini Weather Cloud")]
    static void Create()
    {
        var cloudTex = CreateOrUpdateCloudPuffTexture();
        var cloudMat = LoadOrCreateCloudMat("M_CloudPuff", cloudTex);

        var root = new GameObject("CloudRoot");
        Undo.RegisterCreatedObjectUndo(root, "Create Mini Weather Cloud");
        root.transform.position = new Vector3(0f, 3f, 0f);

        // ─── Cloud_MainPuffs ────────────────────────────────
        var mainGO = Child(root, "Cloud_MainPuffs");
        var mainPS = mainGO.AddComponent<ParticleSystem>();
        mainPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ConfigureMainPuffs(mainPS, cloudMat);

        // ─── Cloud_EdgePuffs ────────────────────────────────
        var edgeGO = Child(root, "Cloud_EdgePuffs");
        var edgePS = edgeGO.AddComponent<ParticleSystem>();
        edgePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ConfigureEdgePuffs(edgePS, cloudMat);

        // ─── Cloud_BottomPuffs ──────────────────────────────
        var bottomGO = Child(root, "Cloud_BottomPuffs");
        bottomGO.transform.localPosition = new Vector3(0f, -0.3f, 0f);
        var bottomPS = bottomGO.AddComponent<ParticleSystem>();
        bottomPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ConfigureBottomPuffs(bottomPS, cloudMat);

        // ─── RainEmitter (placeholder) ──────────────────────
        Child(root, "RainEmitter");

        // ─── WeatherCloud ───────────────────────────────────
        var wc = root.AddComponent<WeatherCloud>();
        wc.cloudMain   = mainPS;
        wc.cloudEdge   = edgePS;
        wc.cloudBottom = bottomPS;

        Selection.activeGameObject = root;
        Debug.Log("[CloudRoot] Burst cloud created. Enter Play mode to see.");
    }

    // ─── Main Puffs: dense opaque center body ───────────────

    static void ConfigureMainPuffs(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.loop            = false;
        main.playOnAwake     = true;
        main.prewarm         = false;
        main.startLifetime   = 1000f;
        main.startSpeed      = 0f;
        main.startSize       = new ParticleSystem.MinMaxCurve(1.5f, 2.3f);
        main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor      = new Color(1f, 1f, 1f, 0.9f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode     = ParticleSystemScalingMode.Local;
        main.maxParticles    = 15;

        var em = ps.emission;
        em.enabled         = true;
        em.rateOverTime    = 0f;
        em.rateOverDistance = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

        var sh = ps.shape;
        sh.enabled         = true;
        sh.shapeType       = ParticleSystemShapeType.Sphere;
        sh.scale           = new Vector3(4.0f, 0.4f, 2.5f);
        sh.radiusThickness = 1f;

        DisableAllMovement(ps);
        ConfigureRenderer(ps, mat);
    }

    // ─── Edge Puffs: bumpy silhouette around main ───────────

    static void ConfigureEdgePuffs(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.loop            = false;
        main.playOnAwake     = true;
        main.prewarm         = false;
        main.startLifetime   = 1000f;
        main.startSpeed      = 0f;
        main.startSize       = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
        main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor      = new Color(1f, 1f, 1f, 0.7f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode     = ParticleSystemScalingMode.Local;
        main.maxParticles    = 12;

        var em = ps.emission;
        em.enabled         = true;
        em.rateOverTime    = 0f;
        em.rateOverDistance = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });

        var sh = ps.shape;
        sh.enabled         = true;
        sh.shapeType       = ParticleSystemShapeType.Sphere;
        sh.scale           = new Vector3(5.5f, 0.8f, 3.2f);
        sh.radiusThickness = 0.25f;

        DisableAllMovement(ps);
        ConfigureRenderer(ps, mat);
    }

    // ─── Bottom Puffs: gray underside for depth ─────────────

    static void ConfigureBottomPuffs(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.loop            = false;
        main.playOnAwake     = true;
        main.prewarm         = false;
        main.startLifetime   = 1000f;
        main.startSpeed      = 0f;
        main.startSize       = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
        main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor      = new Color(0.75f, 0.75f, 0.78f, 0.6f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode     = ParticleSystemScalingMode.Local;
        main.maxParticles    = 8;

        var em = ps.emission;
        em.enabled         = true;
        em.rateOverTime    = 0f;
        em.rateOverDistance = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });

        var sh = ps.shape;
        sh.enabled         = true;
        sh.shapeType       = ParticleSystemShapeType.Sphere;
        sh.scale           = new Vector3(2.5f, 0.3f, 1.8f);
        sh.radiusThickness = 1f;

        DisableAllMovement(ps);
        ConfigureRenderer(ps, mat);
    }

    // ─── Shared Helpers ─────────────────────────────────────

    static void DisableAllMovement(ParticleSystem ps)
    {
        var vel      = ps.velocityOverLifetime;      vel.enabled = false;
        var force    = ps.forceOverLifetime;          force.enabled = false;
        var noise    = ps.noise;                      noise.enabled = false;
        var rot      = ps.rotationOverLifetime;       rot.enabled = false;
        var limitVel = ps.limitVelocityOverLifetime;  limitVel.enabled = false;
        var inherit  = ps.inheritVelocity;            inherit.enabled = false;
        var ext      = ps.externalForces;             ext.enabled = false;
    }

    static void ConfigureRenderer(ParticleSystem ps, Material mat)
    {
        var rr = ps.GetComponent<ParticleSystemRenderer>();
        rr.renderMode          = ParticleSystemRenderMode.Billboard;
        rr.sortMode            = ParticleSystemSortMode.Distance;
        rr.material            = mat;
        rr.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
        rr.receiveShadows      = false;
        rr.enableGPUInstancing = true;
    }

    // ─── Procedural Cloud Puff Texture ──────────────────────
    // Solid white center (50% radius plateau), smoothstep
    // falloff with Perlin noise for fluffy irregular edges.

    static Texture2D CreateOrUpdateCloudPuffTexture()
    {
        const string path = TexDir + "/T_CloudPuff.asset";
        const int size = 128;

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        bool isNew = !tex;

        if (isNew)
        {
            tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name       = "T_CloudPuff",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        float half = size * 0.5f;
        var pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - half) / half;
            float dy = (y - half) / half;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);

            const float plateau = 0.5f;
            float alpha;
            if (dist < plateau)
            {
                alpha = 1f;
            }
            else if (dist < 1f)
            {
                float t = (dist - plateau) / (1f - plateau);
                t = t * t * (3f - 2f * t);
                alpha = 1f - t;
            }
            else
            {
                alpha = 0f;
            }

            float noiseMask = Mathf.Clamp01((dist - 0.3f) / 0.5f);
            float n1 = Mathf.PerlinNoise(x * 0.08f + 137f, y * 0.08f + 241f);
            float n2 = Mathf.PerlinNoise(x * 0.15f + 53f,  y * 0.15f + 89f);
            float noise = n1 * 0.55f + n2 * 0.45f;
            alpha *= Mathf.Lerp(1f, Mathf.Clamp01(noise + 0.15f), noiseMask * 0.6f);

            pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);

        if (isNew)
        {
            EnsureDir(TexDir);
            AssetDatabase.CreateAsset(tex, path);
        }
        else
        {
            EditorUtility.SetDirty(tex);
        }

        return tex;
    }

    // ─── Cloud Material ─────────────────────────────────────

    static Material LoadOrCreateCloudMat(string name, Texture2D tex)
    {
        string path = $"{MatDir}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing)
        {
            if (existing.mainTexture != tex)
                existing.mainTexture = tex;
            return existing;
        }

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (!shader) shader = Shader.Find("Particles/Standard Unlit");

        var mat = new Material(shader) { name = name };
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_SrcBlend", 5f);
        mat.SetFloat("_DstBlend", 6f);
        mat.SetFloat("_SrcBlendAlpha", 1f);
        mat.SetFloat("_DstBlendAlpha", 6f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_AlphaClip", 0f);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetColor("_Color", Color.white);
        mat.SetTexture("_BaseMap", tex);
        mat.SetTexture("_MainTex", tex);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("SHADOWCASTER", false);

        EnsureDir(MatDir);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ─── Utility ────────────────────────────────────────────

    static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
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
