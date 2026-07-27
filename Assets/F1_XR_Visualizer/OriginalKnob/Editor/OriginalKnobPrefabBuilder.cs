using System.Collections.Generic;
using F1XR.OriginalKnob;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.OriginalKnob.Editor
{
    /// <summary>
    /// Procedurally builds the OrIginalKnob rotary control (mesh, materials, glow ring, wiring) and can
    /// drop it into the active scene. Re-runnable; mirrors the project's other prefab builders.
    /// </summary>
    public static class OriginalKnobPrefabBuilder
    {
        const string RootFolder = "Assets/F1_XR_Visualizer/OriginalKnob";
        const string PrefabPath = RootFolder + "/Prefabs/OrIginalKnob.prefab";
        const string MeshPath = RootFolder + "/Meshes/OrIginalKnob_Panel.asset";
        const string PanelMatPath = RootFolder + "/Materials/OrIginalKnob_Panel.mat";
        const string KnobMatPath = RootFolder + "/Materials/OrIginalKnob_Knob.mat";
        const string MarkerMatPath = RootFolder + "/Materials/OrIginalKnob_Marker.mat";
        const string RingMatPath = RootFolder + "/Materials/OrIginalKnob_Ring.mat";
        const string InstanceName = "OrIginalKnob";

        // Dimensions (metres). Recommended starting values from the spec.
        const float PanelW = 0.26f, PanelH = 0.26f, PanelD = 0.025f, PanelCorner = 0.045f;
        const int CornerSegments = 6;
        const float KnobRadius = 0.072f, KnobThickness = 0.032f, PanelKnobGap = 0.024f;
        const float RingQuadSize = 0.22f;

        static float PanelHalfDepth => PanelD * 0.5f;
        static float KnobCenterZ => PanelHalfDepth + PanelKnobGap + KnobThickness * 0.5f;
        static float RingZ => PanelHalfDepth + 0.002f;

        // Glow-ring depth stack (local Z, forward = toward the user). BaseRing near the panel; the three
        // glow rings step forward across the knob's side so they read as a 3D coil.
        static float RingBaseZ => PanelHalfDepth + 0.002f;   // faint groove, near the panel
        const float GlowRing1Z = 0.030f;                     // first glow ring (near the knob back)
        const float GlowRingDepthStep = 0.015f;              // gap between successive rings

        [MenuItem("Tools/F1 XR/OriginalKnob/Build Prefab")]
        public static void BuildPrefab()
        {
            var root = BuildInstance();
            EnsureFolder(RootFolder, "Prefabs");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[OriginalKnobBuilder] Built prefab: {PrefabPath}");
        }

        [MenuItem("Tools/F1 XR/OriginalKnob/Build & Place In Active Scene")]
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
            root.transform.position = new Vector3(0f, 1.2f, 0.55f);
            root.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // front (+Z local) faces the user
            SceneManager.MoveGameObjectToScene(root, scene);
            Selection.activeGameObject = root;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[OriginalKnobBuilder] Placed '{InstanceName}' in scene: {scene.path}");
        }

        static GameObject BuildInstance()
        {
            EnsureFolders();

            Material panelMat = CreateLitMaterial(PanelMatPath, new Color(0.03f, 0.03f, 0.035f), 0f, 0.25f);
            Material knobMat = CreateLitMaterial(KnobMatPath, new Color(0.11f, 0.11f, 0.13f), 0f, 0.4f);
            Material markerMat = CreateMarkerMaterial(MarkerMatPath);
            Material ringMat = CreateRingMaterial(RingMatPath);

            var root = new GameObject("RotaryKnobPanel");

            // ---- Panel body ----
            var panelBody = NewChild("PanelBody", root.transform, Vector3.zero);
            var panelMesh = NewChild("PanelMesh", panelBody.transform, Vector3.zero);
            var mf = panelMesh.AddComponent<MeshFilter>();
            mf.sharedMesh = CreateOrLoadPanelMesh();
            panelMesh.AddComponent<MeshRenderer>().sharedMaterial = panelMat;
            var panelCol = panelBody.AddComponent<BoxCollider>();
            panelCol.size = new Vector3(PanelW, PanelH, PanelD);
            panelCol.center = Vector3.zero;

            // ---- Glow ring: same-diameter arcs STACKED IN DEPTH (Z) to form a 3D coil. ----
            // BaseRing sits at the back (near the panel); the three glow rings step forward toward the
            // knob face, each a fixed gap in front of the previous one.
            var ringRoot = NewChild("RingRoot", root.transform, Vector3.zero);
            var baseRing = CreateRingQuad("BaseRing", ringRoot.transform, ringMat, RingBaseZ);
            var mainGlowArc = CreateRingQuad("MainGlowArc", ringRoot.transform, ringMat, GlowRing1Z);
            var trailArc01 = CreateRingQuad("TrailArc01", ringRoot.transform, ringMat, GlowRing1Z + GlowRingDepthStep);
            var trailArc02 = CreateRingQuad("TrailArc02", ringRoot.transform, ringMat, GlowRing1Z + GlowRingDepthStep * 2f);

            // ---- Knob pivot (the only thing that rotates) ----
            var knobPivot = NewChild("KnobPivot", root.transform, new Vector3(0f, 0f, KnobCenterZ));

            var knobMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            knobMesh.name = "KnobMesh";
            knobMesh.transform.SetParent(knobPivot.transform, false);
            knobMesh.transform.localPosition = Vector3.zero;
            knobMesh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis Y -> Z
            knobMesh.transform.localScale = new Vector3(KnobRadius * 2f, KnobThickness * 0.5f, KnobRadius * 2f);
            knobMesh.GetComponent<MeshRenderer>().sharedMaterial = knobMat;
            Object.DestroyImmediate(knobMesh.GetComponent<Collider>());

            var knobCollider = NewChild("KnobCollider", knobPivot.transform, Vector3.zero);
            var capsule = knobCollider.AddComponent<CapsuleCollider>();
            capsule.direction = 2; // Z
            capsule.radius = KnobRadius + 0.008f;
            capsule.height = KnobThickness + 0.04f;

            var marker = NewChild("RotationMarker", knobPivot.transform, Vector3.zero);
            float markerZ = KnobThickness * 0.5f + 0.003f;
            // Three EQUAL-size dots arranged as a short diagonal streak near the top of the knob face,
            // descending to the right, matching the reference.
            const float dotSize = 0.0055f;
            const float dotStep = 0.013f;
            Vector3 dotCenter = new Vector3(0f, 0.05f, markerZ);
            Vector3 dotDir = new Vector3(-0.8f, -0.6f, 0f).normalized; // reads as descending-right on screen (panel is mirrored)
            CreateMarkerDot("Dot1", marker.transform, dotCenter - dotDir * dotStep, dotSize, markerMat);
            CreateMarkerDot("Dot2", marker.transform, dotCenter, dotSize, markerMat);
            CreateMarkerDot("Dot3", marker.transform, dotCenter + dotDir * dotStep, dotSize, markerMat);

            // ---- Interaction + logic components on the pivot / root ----
            var interactable = knobPivot.AddComponent<XRSimpleInteractable>();

            var knobController = root.AddComponent<RotaryKnobController>();
            var rotaryInput = root.AddComponent<ControllerRotaryInput>();
            var ringVisual = root.AddComponent<RotaryRingVisualController>();
            var haptic = root.AddComponent<ControllerHapticController>();
            var rayHider = root.AddComponent<KnobRayLineHider>();
            var dotLights = root.AddComponent<MarkerDotTurnLights>();

            SetRef(knobController, "knobPivot", knobPivot.transform);

            SetRef(rotaryInput, "interactable", interactable);
            SetRef(rotaryInput, "knob", knobController);

            SetRef(ringVisual, "knob", knobController);
            SetRef(ringVisual, "interactable", interactable);
            SetRef(ringVisual, "baseRing.renderer", baseRing.GetComponent<MeshRenderer>());
            SetRef(ringVisual, "mainGlowArc.renderer", mainGlowArc.GetComponent<MeshRenderer>());
            SetRef(ringVisual, "trailArc01.renderer", trailArc01.GetComponent<MeshRenderer>());
            SetRef(ringVisual, "trailArc02.renderer", trailArc02.GetComponent<MeshRenderer>());

            SetRef(haptic, "interactable", interactable);
            SetRef(haptic, "knob", knobController);

            SetRef(rayHider, "interactable", interactable);

            SetRef(dotLights, "knob", knobController);
            SetRef(dotLights, "rotationMarker", marker.transform);

            return root;
        }

        // ---------- Ring / marker helpers ----------

        static GameObject CreateRingQuad(string name, Transform parent, Material ringMat, float zOffset)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0f, 0f, zOffset);
            // Unity's Quad faces -Z; the panel is rotated 180 deg to face the user, so we'd view the quad
            // from behind (UV mirrored in X). Flip the quad 180 deg about Y so its front faces the user and
            // the arc angle convention reads un-mirrored.
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            quad.transform.localScale = new Vector3(RingQuadSize, RingQuadSize, 1f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = ringMat;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            return quad;
        }

        static void CreateMarkerDot(string name, Transform parent, Vector3 localPos, float diameter, Material mat)
        {
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = name;
            dot.transform.SetParent(parent, false);
            dot.transform.localPosition = localPos;
            dot.transform.localScale = Vector3.one * diameter;
            dot.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Object.DestroyImmediate(dot.GetComponent<Collider>());
        }

        // ---------- Panel mesh ----------

        static Mesh CreateOrLoadPanelMesh()
        {
            var mesh = BuildRoundedRectPrism(PanelW, PanelH, PanelD, PanelCorner, CornerSegments);
            mesh.name = "OrIginalKnob_Panel";

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, MeshPath);
            }
            else
            {
                existing.Clear();
                existing.vertices = mesh.vertices;
                existing.normals = mesh.normals;
                existing.triangles = mesh.triangles;
                existing.RecalculateBounds();
                mesh = existing;
                EditorUtility.SetDirty(existing);
            }
            return mesh;
        }

        static Mesh BuildRoundedRectPrism(float w, float h, float d, float r, int seg)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            float hw = w * 0.5f, hh = h * 0.5f, hd = d * 0.5f;

            // Rounded-rect outline (CCW when viewed from +Z).
            var outline = new List<Vector2>();
            Vector2[] centers =
            {
                new Vector2(hw - r, hh - r),   // top-right   0..90
                new Vector2(-(hw - r), hh - r),// top-left    90..180
                new Vector2(-(hw - r), -(hh - r)),// bottom-left 180..270
                new Vector2(hw - r, -(hh - r)) // bottom-right 270..360
            };
            for (int c = 0; c < 4; c++)
            {
                float baseAng = c * 90f;
                for (int i = 0; i <= seg; i++)
                {
                    float a = (baseAng + 90f * i / seg) * Mathf.Deg2Rad;
                    outline.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            }
            int n = outline.Count;

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();

            // Front cap (+Z)
            int frontCenter = verts.Count;
            verts.Add(new Vector3(0, 0, hd)); norms.Add(Vector3.forward);
            int frontStart = verts.Count;
            for (int i = 0; i < n; i++) { verts.Add(new Vector3(outline[i].x, outline[i].y, hd)); norms.Add(Vector3.forward); }
            for (int i = 0; i < n; i++)
            {
                int a = frontStart + i, b = frontStart + (i + 1) % n;
                tris.Add(frontCenter); tris.Add(a); tris.Add(b);
            }

            // Back cap (-Z)
            int backCenter = verts.Count;
            verts.Add(new Vector3(0, 0, -hd)); norms.Add(Vector3.back);
            int backStart = verts.Count;
            for (int i = 0; i < n; i++) { verts.Add(new Vector3(outline[i].x, outline[i].y, -hd)); norms.Add(Vector3.back); }
            for (int i = 0; i < n; i++)
            {
                int a = backStart + i, b = backStart + (i + 1) % n;
                tris.Add(backCenter); tris.Add(b); tris.Add(a);
            }

            // Side walls (flat per-edge normals)
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = outline[i], p1 = outline[(i + 1) % n];
                Vector2 edge = (p1 - p0).normalized;
                Vector3 normal = new Vector3(edge.y, -edge.x, 0f); // outward for CCW outline
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

        // ---------- Materials ----------

        static Material CreateLitMaterial(string path, Color color, float metallic, float smoothness)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material CreateMarkerMaterial(string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.5f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", Color.white * 0.8f); // subtle - stays below the ring glow
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material CreateRingMaterial(string path)
        {
            var shader = Shader.Find("F1XR/RotaryRingArc");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_RingColor", Color.white);
            mat.SetFloat("_Radius", 0.79f);
            mat.SetFloat("_RadiusOffset", 0f);
            mat.SetFloat("_LineWidth", 0.02f);
            mat.SetFloat("_Softness", 0.006f);
            mat.SetFloat("_StartAngle", 90f);
            mat.SetFloat("_RotationOffset", 0f);
            mat.SetFloat("_ArcLength", 0f);
            // Default OFF: the ring is only lit when the RotaryRingVisualController drives it via MPB
            // (i.e. while grabbed/turning). Without MPB (edit mode / first frame) it shows nothing.
            mat.SetFloat("_Intensity", 0f);
            mat.SetFloat("_Alpha", 1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------- Utilities ----------

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
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning($"[OriginalKnobBuilder] Field '{field}' not found on {target.GetType().Name}");
            }
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets/F1_XR_Visualizer", "OriginalKnob");
            EnsureFolder(RootFolder, "Prefabs");
            EnsureFolder(RootFolder, "Materials");
            EnsureFolder(RootFolder, "Meshes");
        }

        static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
                AssetDatabase.CreateFolder(parent, child);
        }

        static GameObject FindSceneObject(string name)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return null;
            foreach (var rootGo in scene.GetRootGameObjects())
                if (rootGo.name == name)
                    return rootGo;
            return null;
        }
    }
}
