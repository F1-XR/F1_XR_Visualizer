using System.Collections.Generic;
using F1XR.PlayPanel;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.PlayPanel.Editor
{
    /// <summary>
    /// Procedurally builds the vertical "PlayPanel" glass card and drops it into the active scene, plus
    /// scene ambiance (floor grid, bloom, ray polish). Re-runnable; mirrors the project's other prefab
    /// builders (see OriginalKnobPrefabBuilder).
    ///
    /// v2 layout (9:16 card, top-down hierarchy):
    ///   Top    - a floating 3D play triangle (protrudes toward the viewer, gentle idle motion)
    ///   Middle - "PLAY" title + a small grey description line
    ///   Bottom - a closed capsule "pill" button: "START" + a small arrow glyph, neon border that flows
    ///            and brightens on hover, easing forward when a hand/ray approaches.
    ///
    /// Orientation: the UI Test camera looks down +Z, so the user sees the panel's -Z face. Everything is
    /// authored on that -Z face with an identity root rotation (no 180 flip -> text/glyph not mirrored).
    /// </summary>
    public static class PlayPanelBuilder
    {
        const string RootFolder = "Assets/F1_XR_Visualizer/PlayPanel";
        const string PrefabPath = RootFolder + "/Prefabs/PlayPanel.prefab";
        const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        const string InstanceName = "PlayPanel";

        // ---- Card: 3:4 (W:H), with real body depth. ----
        const float PanelH = 0.38f;
        const float PanelW = 0.285f; // 3:4 -> 0.285 : 0.38
        const float PanelD = 0.034f, PanelCorner = 0.032f;
        const int CornerSeg = 8;
        static float HalfDepth => PanelD * 0.5f;

        // -Z offsets (more negative = closer to the user). The body is opaque; a thin glass cover sits
        // just in front of it, and all parts protrude in front of the glass.
        static float ZGlass => -(HalfDepth + 0.001f);   // glass cover centre (front ~ -HalfDepth-0.005)
        static float ZNeon => -(HalfDepth + 0.009f);
        static float ZShadow => -(HalfDepth + 0.0075f);
        static float ZIcon => -(HalfDepth + 0.006f);    // thin plate sitting almost flush on the glass
        static float ZMount => -(HalfDepth + 0.032f);   // black mount block bridging icon -> body
        static float ZPillFill => -(HalfDepth + 0.016f);
        static float ZPillBorder => -(HalfDepth + 0.027f);
        static float ZPillText => -(HalfDepth + 0.03f);

        // ---- Layout Y (panel-local). Icon + pill centred on the panel axis (x = 0). ----
        const float IconY = 0.085f;
        const float ShadowY = 0.0f;

        // ---- Pill (centred, slightly smaller) ----
        const float PillW = 0.135f, PillH = 0.05f;
        const float PillX = 0f, PillY = -0.12f;
        static float PillCorner => PillH * 0.5f;

        // ---- Neon ----
        const float NeonHalfWidth = 0.0036f;
        const float FlowWavelength = 0.22f;
        const float LeftLineX = -0.12f; // vertical accent line near the left edge

        [MenuItem("Tools/F1 XR/PlayPanel/Build Prefab")]
        public static void BuildPrefab()
        {
            var root = BuildInstance();
            EnsureFolder(RootFolder, "Prefabs");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[PlayPanelBuilder] Built prefab: {PrefabPath}");
        }

        [MenuItem("Tools/F1 XR/PlayPanel/Build & Place In Active Scene")]
        public static void BuildAndPlace()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;

            var existing = FindSceneObject(InstanceName);
            if (existing != null)
                Object.DestroyImmediate(existing);

            var root = BuildInstance();
            root.name = InstanceName;
            root.transform.position = new Vector3(0f, 1.35f, 0.6f);
            root.transform.rotation = Quaternion.identity;
            SceneManager.MoveGameObjectToScene(root, scene);

            SetupSceneAmbiance(scene);

            Selection.activeGameObject = root;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[PlayPanelBuilder] Placed '{InstanceName}' + ambiance in scene: {scene.path}");
        }

        static GameObject BuildInstance()
        {
            EnsureFolders();

            // Opaque dark body (the bulk) + a thin translucent glossy glass cover in front of it.
            Material bodyMat = CreateLit(Mat("Body"), new Color(0.028f, 0.028f, 0.033f), 0.72f);
            bodyMat.SetFloat("_Metallic", 0.05f); EditorUtility.SetDirty(bodyMat);
            Material glassMat = CreateGlassMaterial(Mat("Glass"), new Color(0.03f, 0.03f, 0.05f, 0.35f), 0.9f);
            // Triangle: off-white glassy part (high smoothness, only a whisper of emission - not a flat glow).
            Material triMat = CreateEmissiveLit(Mat("Triangle"), new Color(0.86f, 0.86f, 0.90f), 0.85f,
                new Color(0.05f, 0.05f, 0.06f));
            // Pill front: glossy near-black plastic.
            Material pillFillMat = CreateLit(Mat("PillFill"), new Color(0.02f, 0.02f, 0.025f), 0.85f);
            pillFillMat.SetFloat("_Metallic", 0.1f); EditorUtility.SetDirty(pillFillMat);
            Material neonMat = CreateNeonMaterial(Mat("Neon"));
            Material shadowMat = CreateShadowMaterial(Mat("Shadow"));
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            var root = new GameObject("PlayPanel");

            // ---- Opaque body ----
            var body = NewChild("Body", root.transform, Vector3.zero);
            body.AddComponent<MeshFilter>().sharedMesh =
                SaveMesh(BuildRoundedRectPrism(PanelW, PanelH, PanelD, PanelCorner, CornerSeg), Mesh("Body"), "PlayPanel_Body");
            body.AddComponent<MeshRenderer>().sharedMaterial = bodyMat;
            var cardCol = body.AddComponent<BoxCollider>();
            cardCol.size = new Vector3(PanelW, PanelH, PanelD);

            // ---- Thin translucent glass cover, slightly inset and protruding in front of the body ----
            var glass = NewChild("GlassCover", root.transform, new Vector3(0f, 0f, ZGlass));
            glass.AddComponent<MeshFilter>().sharedMesh = SaveMesh(
                BuildRoundedRectPrism(PanelW - 0.012f, PanelH - 0.012f, 0.008f, Mathf.Max(0.004f, PanelCorner - 0.004f), CornerSeg),
                Mesh("Glass"), "PlayPanel_Glass");
            var glassR = glass.AddComponent<MeshRenderer>();
            glassR.sharedMaterial = glassMat;

            // ---- Left neon accent line: runs down the left edge and curves into the pill button ----
            var leftPath = BuildLeftLinePath();
            var leftLine = NewChild("LeftNeonLine", root.transform, new Vector3(0f, 0f, ZNeon));
            leftLine.AddComponent<MeshFilter>().sharedMesh =
                SaveMesh(BuildRibbonMesh(leftPath, NeonHalfWidth), Mesh("LeftLine"), "PlayPanel_LeftLine");
            var leftLineR = AddRibbonRenderer(leftLine, neonMat);

            // ---- Top: glassy 3D play part on a black mount, protruding toward the user + cast shadow ----
            CreateQuad("IconShadow", root.transform, new Vector3(0.012f, ShadowY, ZShadow),
                new Vector3(0.11f, 0.19f, 1f), shadowMat);

            var icon = NewChild("PlayIcon", root.transform, new Vector3(0f, IconY, ZIcon));
            AssignPrismMesh(icon, SaveMesh(BuildTriangularPrism(0.088f, 0.108f, 0.012f), Mesh("Icon"), "PlayPanel_Icon"),
                triMat, triMat);
            // Upright and parallel to the panel (no forward lean); depth reads from the thickness alone.
            icon.transform.localRotation = Quaternion.identity;

            // ---- Bottom: capsule pill button ("START", no arrow). Moves/brightens on hover. ----
            // ButtonGroup's transform sits AT the pill, so the interactable's attach point (which the XR
            // ray snaps to on select) is on the button - NOT at the panel centre. Children are relative.
            var buttonGroup = NewChild("ButtonGroup", root.transform, new Vector3(PillX, PillY, ZPillFill));

            var pillFill = NewChild("PillFill", buttonGroup.transform, Vector3.zero);
            pillFill.AddComponent<MeshFilter>().sharedMesh =
                SaveMesh(BuildRoundedRectPrism(PillW, PillH, 0.018f, PillCorner, 10), Mesh("Pill"), "PlayPanel_Pill");
            pillFill.AddComponent<MeshRenderer>().sharedMaterial = pillFillMat;

            var pillOutline = Close(BuildRoundedRectOutline(PillW, PillH, PillCorner, 10)); // centred on origin
            var pillBorder = NewChild("PillBorderNeon", buttonGroup.transform, new Vector3(0f, 0f, ZPillBorder - ZPillFill));
            pillBorder.AddComponent<MeshFilter>().sharedMesh =
                SaveMesh(BuildRibbonMesh(pillOutline, NeonHalfWidth), Mesh("PillBorder"), "PlayPanel_PillBorder");
            var pillBorderR = AddRibbonRenderer(pillBorder, neonMat);

            CreateText("PillLabel", buttonGroup.transform, new Vector3(0f, 0f, ZPillText - ZPillFill), "START",
                font, 0.085f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center,
                new Vector2(0.13f, 0.05f), new Vector2(0.5f, 0.5f));

            var btnCol = buttonGroup.AddComponent<BoxCollider>();
            btnCol.center = new Vector3(0f, 0f, -0.01f);
            btnCol.size = new Vector3(PillW + 0.02f, PillH + 0.025f, 0.06f);
            var interactable = buttonGroup.AddComponent<XRSimpleInteractable>();

            // ---- Controller wiring: left line + pill flow together; the pill eases forward on hover ----
            var controller = root.AddComponent<NeonFlowController>();
            SetRef(controller, "interactable", interactable);
            SetRef(controller, "buttonGroup", buttonGroup.transform);
            SetRef(controller, "playIcon", icon.transform);
            SetFloatField(controller, "iconLocalZ", ZIcon);
            // One gradient pass each: the left line reads blue (top) -> purple (down); the pill stays purple.
            SetRibbons(controller, new (Renderer r, float phase, float repeat)[]
            {
                (leftLineR, 0f, 1f),
                (pillBorderR, 0.4f, 1f),
            });

            return root;
        }

        // ---------- Scene ambiance (floor grid + bloom + ray polish) ----------

        // The panel is self-contained. We only clean up any ambiance helpers that earlier builds added
        // (floor grid, post volume, ray styler) so re-running Build & Place doesn't leave them behind.
        // Nothing is created here: this scene runs in MR (passthrough) and needs no filler geometry, and
        // URP post-processing would break the camera's transparent passthrough background.
        static void SetupSceneAmbiance(Scene scene)
        {
            ReplaceSceneObject("PlayPanel_Floor", scene);
            ReplaceSceneObject("PlayPanel_PostVolume", scene);
            ReplaceSceneObject("PlayPanel_RayStyler", scene);

            // Product-style lighting (safe in MR - lights only affect the virtual panel, not passthrough).
            ReplaceSceneObject("PlayPanel_KeyLight", scene);
            ReplaceSceneObject("PlayPanel_RimLight", scene);
            var target = new Vector3(0f, 1.35f, 0.585f); // panel front centre
            CreateSpot("PlayPanel_KeyLight", scene, new Vector3(-0.4f, 1.85f, -0.15f), target,
                new Color(1f, 0.98f, 0.95f), 8f, 65f, true);
            CreateSpot("PlayPanel_RimLight", scene, new Vector3(0.5f, 1.5f, 1.15f), target,
                new Color(0.55f, 0.35f, 1f), 9f, 65f, false);
        }

        static void CreateSpot(string name, Scene scene, Vector3 pos, Vector3 target, Color color,
            float intensity, float angle, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation((target - pos).normalized, Vector3.up);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = color;
            l.intensity = intensity;
            l.range = 4f;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.5f;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            SceneManager.MoveGameObjectToScene(go, scene);
        }

        // ---------- Neon paths ----------

        // Vertical accent line down the left edge that curves at the bottom into the pill's top-left cap.
        static List<Vector2> BuildLeftLinePath()
        {
            var pts = new List<Vector2>();
            float x = LeftLineX;
            pts.Add(new Vector2(x, 0.15f));
            pts.Add(new Vector2(x, -0.06f));

            const float r = 0.03f;                       // rounded corner: down -> right
            Vector2 c = new Vector2(x + r, -0.06f);
            const int seg = 10;
            for (int i = 1; i <= seg; i++)
            {
                float a = Mathf.Lerp(180f, 270f, i / (float)seg) * Mathf.Deg2Rad;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            // short connector into the pill's top-left cap
            pts.Add(new Vector2(PillX - PillW * 0.5f + PillCorner, PillY + PillH * 0.5f));
            return pts;
        }

        static List<Vector2> BuildRoundedRectOutline(float w, float h, float r, int segPerCorner)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            float hw = w * 0.5f, hh = h * 0.5f;
            var pts = new List<Vector2>();
            Vector2[] centers =
            {
                new Vector2(hw - r, hh - r),
                new Vector2(-(hw - r), hh - r),
                new Vector2(-(hw - r), -(hh - r)),
                new Vector2(hw - r, -(hh - r))
            };
            for (int c = 0; c < 4; c++)
            {
                float baseAng = c * 90f;
                for (int i = 0; i <= segPerCorner; i++)
                {
                    float a = (baseAng + 90f * i / segPerCorner) * Mathf.Deg2Rad;
                    pts.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            }
            return pts;
        }

        static List<Vector2> Close(List<Vector2> pts)
        {
            if (pts.Count > 0) pts.Add(pts[0]); // duplicate start so the flow gradient wraps seamlessly
            return pts;
        }

        static float PathLength(List<Vector2> pts)
        {
            float len = 0f;
            for (int i = 1; i < pts.Count; i++)
                len += Vector2.Distance(pts[i], pts[i - 1]);
            return len;
        }

        static MeshRenderer AddRibbonRenderer(GameObject go, Material mat)
        {
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        static Mesh BuildRibbonMesh(List<Vector2> pts, float halfWidth)
        {
            int n = pts.Count;
            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var norms = new Vector3[n * 2];
            var tris = new List<int>((n - 1) * 6);

            var cum = new float[n];
            for (int i = 1; i < n; i++)
                cum[i] = cum[i - 1] + Vector2.Distance(pts[i], pts[i - 1]);
            float total = Mathf.Max(cum[n - 1], 1e-5f);

            for (int i = 0; i < n; i++)
            {
                Vector2 dir = i == 0 ? (pts[1] - pts[0])
                    : i == n - 1 ? (pts[n - 1] - pts[n - 2])
                    : (pts[i + 1] - pts[i - 1]);
                dir = dir.sqrMagnitude < 1e-10f ? Vector2.up : dir.normalized;
                Vector2 nrm = new Vector2(-dir.y, dir.x);
                float t = cum[i] / total;

                Vector2 l = pts[i] + nrm * halfWidth;
                Vector2 r = pts[i] - nrm * halfWidth;
                verts[i * 2] = new Vector3(l.x, l.y, 0f);
                verts[i * 2 + 1] = new Vector3(r.x, r.y, 0f);
                uvs[i * 2] = new Vector2(t, 1f);
                uvs[i * 2 + 1] = new Vector2(t, 0f);
                norms[i * 2] = Vector3.back;
                norms[i * 2 + 1] = Vector3.back;
            }

            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = (i + 1) * 2, d = (i + 1) * 2 + 1;
                tris.Add(a); tris.Add(c); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(b);
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- Triangular play prism (2 submeshes: -Z face, then +Z face + sides) ----------

        static Mesh BuildTriangularPrism(float width, float height, float thick)
        {
            float apexX = width * 0.62f, baseX = -width * 0.38f, hh = height * 0.5f, hd = thick * 0.5f;
            Vector2[] outline = { new Vector2(apexX, 0f), new Vector2(baseX, hh), new Vector2(baseX, -hh) };
            int n = outline.Length;

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var faceTris = new List<int>();
            var otherTris = new List<int>();

            int fc = verts.Count; verts.Add(new Vector3(0, 0, -hd)); norms.Add(Vector3.back);
            int fs = verts.Count;
            for (int i = 0; i < n; i++) { verts.Add(new Vector3(outline[i].x, outline[i].y, -hd)); norms.Add(Vector3.back); }
            for (int i = 0; i < n; i++) { int a = fs + i, b = fs + (i + 1) % n; faceTris.Add(fc); faceTris.Add(b); faceTris.Add(a); }

            int bc = verts.Count; verts.Add(new Vector3(0, 0, hd)); norms.Add(Vector3.forward);
            int bs = verts.Count;
            for (int i = 0; i < n; i++) { verts.Add(new Vector3(outline[i].x, outline[i].y, hd)); norms.Add(Vector3.forward); }
            for (int i = 0; i < n; i++) { int a = bs + i, b = bs + (i + 1) % n; otherTris.Add(bc); otherTris.Add(a); otherTris.Add(b); }

            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = outline[i], p1 = outline[(i + 1) % n];
                Vector2 edge = (p1 - p0).normalized;
                Vector3 normal = new Vector3(edge.y, -edge.x, 0f);
                int vi = verts.Count;
                verts.Add(new Vector3(p0.x, p0.y, hd)); norms.Add(normal);
                verts.Add(new Vector3(p1.x, p1.y, hd)); norms.Add(normal);
                verts.Add(new Vector3(p1.x, p1.y, -hd)); norms.Add(normal);
                verts.Add(new Vector3(p0.x, p0.y, -hd)); norms.Add(normal);
                otherTris.Add(vi); otherTris.Add(vi + 2); otherTris.Add(vi + 1);
                otherTris.Add(vi); otherTris.Add(vi + 3); otherTris.Add(vi + 2);
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(faceTris, 0);
            mesh.SetTriangles(otherTris, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AssignPrismMesh(GameObject go, Mesh mesh, Material faceMat, Material sideMat)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { faceMat, sideMat };
        }

        // ---------- Rounded-rect prism ----------

        static Mesh BuildRoundedRectPrism(float w, float h, float d, float r, int seg)
        {
            var outline = BuildRoundedRectOutline(w, h, r, seg);
            int n = outline.Count;
            float hd = d * 0.5f;

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();

            int frontCenter = verts.Count;
            verts.Add(new Vector3(0, 0, hd)); norms.Add(Vector3.forward);
            int frontStart = verts.Count;
            for (int i = 0; i < n; i++) { verts.Add(new Vector3(outline[i].x, outline[i].y, hd)); norms.Add(Vector3.forward); }
            for (int i = 0; i < n; i++) { int a = frontStart + i, b = frontStart + (i + 1) % n; tris.Add(frontCenter); tris.Add(a); tris.Add(b); }

            int backCenter = verts.Count;
            verts.Add(new Vector3(0, 0, -hd)); norms.Add(Vector3.back);
            int backStart = verts.Count;
            for (int i = 0; i < n; i++) { verts.Add(new Vector3(outline[i].x, outline[i].y, -hd)); norms.Add(Vector3.back); }
            for (int i = 0; i < n; i++) { int a = backStart + i, b = backStart + (i + 1) % n; tris.Add(backCenter); tris.Add(b); tris.Add(a); }

            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = outline[i], p1 = outline[(i + 1) % n];
                Vector2 edge = (p1 - p0).normalized;
                Vector3 normal = new Vector3(edge.y, -edge.x, 0f);
                int vi = verts.Count;
                verts.Add(new Vector3(p0.x, p0.y, hd)); norms.Add(normal);
                verts.Add(new Vector3(p1.x, p1.y, hd)); norms.Add(normal);
                verts.Add(new Vector3(p1.x, p1.y, -hd)); norms.Add(normal);
                verts.Add(new Vector3(p0.x, p0.y, -hd)); norms.Add(normal);
                tris.Add(vi); tris.Add(vi + 2); tris.Add(vi + 1);
                tris.Add(vi); tris.Add(vi + 3); tris.Add(vi + 2);
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- Text ----------

        static void CreateText(string name, Transform parent, Vector3 localPos, string text, TMP_FontAsset font,
            float fontSize, FontStyles style, Color color, TextAlignmentOptions align, Vector2 size, Vector2 pivot)
        {
            var go = NewChild(name, parent, localPos);
            var tmp = go.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = align;
            tmp.enableAutoSizing = false;
            tmp.rectTransform.pivot = pivot;
            tmp.rectTransform.sizeDelta = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; }
        }

        // ---------- Materials ----------

        static Material CreateLit(string path, Color color, float smoothness)
        {
            var mat = LoadOrCreate(path, "Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material CreateEmissiveLit(string path, Color color, float smoothness, Color emission)
        {
            var mat = CreateLit(path, color, smoothness);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", emission);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material CreateGlassMaterial(string path, Color color, float smoothness)
        {
            var mat = LoadOrCreate(path, "Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material CreateNeonMaterial(string path)
        {
            var mat = LoadOrCreate(path, "F1XR/NeonFlowRibbon");
            // Purple base with a blue lean near the top; no harsh magenta (matches the reference render).
            mat.SetColor("_Col0", new Color(0.32f, 0.48f, 1.00f)); // blue (top of the line)
            mat.SetColor("_Col1", new Color(0.45f, 0.35f, 1.00f));
            mat.SetColor("_Col2", new Color(0.58f, 0.26f, 1.00f)); // purple
            mat.SetColor("_Col3", new Color(0.62f, 0.22f, 1.00f));
            mat.SetColor("_Col4", new Color(0.58f, 0.26f, 1.00f));
            mat.SetColor("_Col5", new Color(0.48f, 0.32f, 1.00f));
            mat.SetFloat("_Repeat", 1f);
            mat.SetFloat("_FlowSpeed", 0.02f); // slow, subtle
            mat.SetFloat("_Glow", 1f);
            mat.SetFloat("_Glow", 1f);
            mat.SetFloat("_EdgeSoftness", 0.65f);
            mat.SetFloat("_CapSoftness", 0f); // closed loop -> no end caps
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material CreateShadowMaterial(string path)
        {
            var mat = LoadOrCreate(path, "F1XR/SoftShadowBlob");
            mat.SetColor("_Color", Color.black);
            mat.SetFloat("_Strength", 0.62f);
            mat.SetFloat("_Softness", 0.8f);
            mat.SetFloat("_VerticalBias", 0.3f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreate(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            return mat;
        }

        // ---------- Utilities ----------

        static string Mat(string name) => RootFolder + "/Materials/PlayPanel_" + name + ".mat";
        static string Mesh(string name) => RootFolder + "/Meshes/PlayPanel_" + name + ".asset";

        static GameObject CreateQuad(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPos;
            quad.transform.localScale = localScale;
            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            return quad;
        }

        static Mesh SaveMesh(Mesh mesh, string path, string name)
        {
            mesh.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            existing.subMeshCount = mesh.subMeshCount;
            existing.SetVertices(new List<Vector3>(mesh.vertices));
            existing.SetNormals(new List<Vector3>(mesh.normals));
            if (mesh.uv != null && mesh.uv.Length > 0)
                existing.SetUVs(0, new List<Vector2>(mesh.uv));
            for (int s = 0; s < mesh.subMeshCount; s++)
                existing.SetTriangles(mesh.GetTriangles(s), s);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static GameObject NewChild(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop != null) { prop.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
            else Debug.LogWarning($"[PlayPanelBuilder] Field '{field}' not found on {target.GetType().Name}");
        }

        static void SetFloatField(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop != null) { prop.floatValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        static void SetRibbons(Object target, (Renderer r, float phase, float repeat)[] ribbons)
        {
            var so = new SerializedObject(target);
            var arr = so.FindProperty("ribbons");
            arr.arraySize = ribbons.Length;
            for (int i = 0; i < ribbons.Length; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("renderer").objectReferenceValue = ribbons[i].r;
                el.FindPropertyRelative("phaseOffset").floatValue = ribbons[i].phase;
                el.FindPropertyRelative("repeat").floatValue = ribbons[i].repeat;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets/F1_XR_Visualizer", "PlayPanel");
            EnsureFolder(RootFolder, "Prefabs");
            EnsureFolder(RootFolder, "Materials");
            EnsureFolder(RootFolder, "Meshes");
        }

        static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
                AssetDatabase.CreateFolder(parent, child);
        }

        static void ReplaceSceneObject(string name, Scene scene)
        {
            foreach (var rootGo in scene.GetRootGameObjects())
                if (rootGo.name == name)
                    Object.DestroyImmediate(rootGo);
        }

        static GameObject FindSceneObject(string name)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return null;
            foreach (var rootGo in scene.GetRootGameObjects())
                if (rootGo.name == name) return rootGo;
            return null;
        }
    }
}
