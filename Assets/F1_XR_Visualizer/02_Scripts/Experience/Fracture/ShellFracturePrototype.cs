using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.Experience.Fracture
{
    /// <summary>
    /// Standalone test rig for the eggshell break, with a single flat surface and no
    /// connection to Passthrough, the mode manager or the room proxies. It exists to
    /// answer one question: does the break look right.
    ///
    /// The surface lies in local XY with its normal along local +Z, so pointing the root
    /// at a wall, the ceiling or the floor is just a rotation. <see cref="surfaceKind"/>
    /// picks the travel distances that surface should use, which is what makes the wall,
    /// ceiling and floor tests possible without a headset.
    ///
    /// All of the actual break logic lives in <see cref="ShellFractureRig"/>, shared with
    /// the room shell controller, so tuning here is tuning there.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShellFracturePrototype : MonoBehaviour
    {
        public enum SurfaceKind
        {
            Wall,
            Ceiling,
            Floor
        }

        [Header("Surface")]
        [SerializeField] Vector2 surfaceSize = new(1.2f, 0.9f);

        [Tooltip("Where the crack starts, in surface local space. (0,0) is the middle.")]
        [SerializeField] Vector2 fractureOrigin = Vector2.zero;

        [Tooltip("Chooses the travel distances. Rotate the root to match: a wall faces the " +
            "viewer, a ceiling faces down, a floor faces up.")]
        [SerializeField] SurfaceKind surfaceKind = SurfaceKind.Wall;

        [Header("Fracture")]
        [SerializeField] ShellFractureRig.Settings fractureSettings =
            ShellFractureRig.Settings.Default;

        [Header("Per surface travel")]
        [SerializeField, Min(0f)] float wallFallDistance = 1.4f;
        [SerializeField, Min(0f)] float ceilingFallDistance = 1.8f;
        [SerializeField, Min(0f)] float floorFallDistance = 0.35f;

        [Header("Dust")]
        [SerializeField] ShellDustEmitter dust;
        [SerializeField] bool spawnDustIfMissing = true;

        [Header("Look")]
        [Tooltip("Alpha below 1 keeps the shell translucent, so it reads as a temporary " +
            "stand-in. The final look replaces this with a passthrough mask.")]
        [SerializeField] Color surfaceColor = new(0.82f, 0.80f, 0.75f, 0.55f);

        [Tooltip("Stand-in for the incoming world. Sits behind the shell so the reveal is " +
            "visible in the Game view: as the fragments fall or fade, this shows through.")]
        [SerializeField] bool showBackdrop = true;
        [SerializeField] Color backdropColor = new(0.10f, 0.35f, 0.55f, 1f);

        ShellFractureRig rig;
        GameObject testSurface;
        GameObject backdrop;
        Mesh surfaceMesh;
        Mesh backdropMesh;
        Material sharedSurfaceMaterial;
        Material backdropMaterial;
        Coroutine breakRoutine;

        public int FragmentCount => rig != null ? rig.FragmentCount : 0;
        public int OwnedMeshCount => rig != null ? rig.OwnedMeshCount : 0;
        public bool IsBuilt => rig != null && rig.Root != null;
        public bool IsBreaking => breakRoutine != null;
        public float TotalBreakSeconds => ActiveSettings().TotalSeconds();

        void Start()
        {
            if (testSurface == null)
                BuildSurface();
        }

        void OnDestroy()
        {
            rig?.Dispose();
            DestroySafely(surfaceMesh);
            DestroySafely(backdropMesh);
            DestroySafely(sharedSurfaceMaterial);
            DestroySafely(backdropMaterial);
        }

        // ---------------------------------------------------------------- public API

        [ContextMenu("Build Surface")]
        public void BuildSurface()
        {
            ResetSurface();

            if (testSurface != null)
                DestroySafely(testSurface);

            DestroySafely(surfaceMesh);

            List<Vector2> boundary = BuildBoundary();
            surfaceMesh = BuildSurfaceMesh(boundary);

            testSurface = new GameObject("TestSurface");
            testSurface.transform.SetParent(transform, false);
            testSurface.AddComponent<MeshFilter>().sharedMesh = surfaceMesh;

            MeshRenderer renderer = testSurface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetSharedMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            BuildBackdrop();

            Debug.Log(
                $"[ShellFracture] Surface built {surfaceSize.x:F2}m x {surfaceSize.y:F2}m " +
                $"as {surfaceKind}, verts={surfaceMesh.vertexCount}",
                this);
        }

        void BuildBackdrop()
        {
            if (backdrop != null)
                DestroySafely(backdrop);

            DestroySafely(backdropMesh);

            if (!showBackdrop)
                return;

            // A plain quad, twice the surface size, set behind the shell (local -Z, the far
            // side from the viewer). Stands in for the world that should already be there.
            float w = surfaceSize.x;
            float h = surfaceSize.y;
            var boundary = new List<Vector2>
            {
                new(-w, -h), new(w, -h), new(w, h), new(-w, h)
            };
            backdropMesh = BuildSurfaceMesh(boundary);

            backdrop = new GameObject("IncomingBackdrop");
            backdrop.transform.SetParent(transform, false);
            backdrop.transform.localPosition = new Vector3(0f, 0f, -0.5f);
            backdrop.AddComponent<MeshFilter>().sharedMesh = backdropMesh;

            MeshRenderer renderer = backdrop.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetBackdropMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        [ContextMenu("Play Shell Break")]
        public void PlayBreak()
        {
            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("[ShellFracture] Component is not active; cannot animate.", this);
                return;
            }

            rig?.Dispose();
            rig = new ShellFractureRig();

            ShellDustEmitter emitter = GetDust();
            if (emitter != null)
                rig.FragmentReleased += emitter.EmitAt;

            List<Vector2> boundary = BuildBoundary();
            if (!rig.Build(boundary, fractureOrigin, transform, GetSharedMaterial(),
                    ActiveSettings(), "Fragments"))
            {
                Debug.LogWarning("[ShellFracture] Shatter produced no cells.", this);
                return;
            }

            if (testSurface != null)
                testSurface.SetActive(false);

            if (breakRoutine != null)
                StopCoroutine(breakRoutine);

            breakRoutine = StartCoroutine(BreakRoutine());

            Debug.Log(
                $"[ShellFracture] Breaking {rig.FragmentCount} fragments as {surfaceKind}, " +
                $"fall={ActiveSettings().fallDistance:F2}m over {TotalBreakSeconds:F2}s.",
                this);
        }

        [ContextMenu("Reset Shell")]
        public void ResetSurface()
        {
            if (breakRoutine != null)
            {
                StopCoroutine(breakRoutine);
                breakRoutine = null;
            }

            rig?.Dispose();
            rig = null;

            if (testSurface != null)
                testSurface.SetActive(true);
        }

        [ContextMenu("Log Fracture Stats")]
        public void LogStats()
        {
            ShellFractureRig.Settings settings = ActiveSettings();
            Debug.Log(
                $"[ShellFracture] kind={surfaceKind} fragments={FragmentCount} " +
                $"ownedMeshes={OwnedMeshCount} lift={settings.liftDistance:F3}m " +
                $"fall={settings.fallDistance:F2}m lateral={settings.lateralDistance:F3}m " +
                $"settle={settings.settleDistance:F3}m " +
                $"rotation=+/-({settings.rotationRange.x},{settings.rotationRange.y}," +
                $"{settings.rotationRange.z}) total={TotalBreakSeconds:F2}s",
                this);

            if (rig != null)
                Debug.Log(
                    $"[ShellFracture] delay spread: first {rig.MinDelay:F3}s, " +
                    $"last {rig.MaxDelay:F3}s",
                    this);
        }

        // ---------------------------------------------------------------- internals

        ShellFractureRig.Settings ActiveSettings()
        {
            ShellFractureRig.Settings settings = fractureSettings;
            switch (surfaceKind)
            {
                case SurfaceKind.Ceiling:
                    settings.fallDistance = ceilingFallDistance;
                    settings.settleDistance = 0f;
                    settings.liftDistance = 0.003f;
                    break;

                case SurfaceKind.Floor:
                    settings.fallDistance = floorFallDistance;
                    settings.settleDistance = 0f;
                    settings.lateralDistance = Mathf.Min(settings.lateralDistance, 0.04f);
                    break;

                default:
                    settings.fallDistance = wallFallDistance;
                    break;
            }

            return settings;
        }

        IEnumerator BreakRoutine()
        {
            float total = TotalBreakSeconds;
            float elapsed = 0f;

            while (elapsed < total)
            {
                elapsed += Time.deltaTime;
                rig.Step(elapsed);
                yield return null;
            }

            breakRoutine = null;
        }

        List<Vector2> BuildBoundary()
        {
            float halfWidth = surfaceSize.x * 0.5f;
            float halfHeight = surfaceSize.y * 0.5f;
            return new List<Vector2>
            {
                new(-halfWidth, -halfHeight),
                new(halfWidth, -halfHeight),
                new(halfWidth, halfHeight),
                new(-halfWidth, halfHeight)
            };
        }

        static Mesh BuildSurfaceMesh(IReadOnlyList<Vector2> polygon)
        {
            var vertices = new Vector3[polygon.Count];
            var normals = new Vector3[polygon.Count];
            var uvs = new Vector2[polygon.Count];

            for (int i = 0; i < polygon.Count; i++)
            {
                vertices[i] = new Vector3(polygon[i].x, polygon[i].y, 0f);
                normals[i] = Vector3.forward;
                uvs[i] = polygon[i];
            }

            var triangles = new int[(polygon.Count - 2) * 3];
            for (int i = 0; i < polygon.Count - 2; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            var mesh = new Mesh { name = "ShellTestSurface" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            return mesh;
        }

        ShellDustEmitter GetDust()
        {
            if (dust != null || !spawnDustIfMissing)
                return dust;

            var host = new GameObject("ShellDust");
            host.transform.SetParent(transform, false);
            dust = host.AddComponent<ShellDustEmitter>();
            return dust;
        }

        Material GetSharedMaterial()
        {
            if (sharedSurfaceMaterial != null)
                return sharedSurfaceMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogWarning("[ShellFracture] No shader available for the surface.", this);
                return null;
            }

            sharedSurfaceMaterial = FractureMaterial.Create(shader, surfaceColor,
                "ShellFractureSurface");
            return sharedSurfaceMaterial;
        }

        Material GetBackdropMaterial()
        {
            if (backdropMaterial != null)
                return backdropMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            // Opaque, so it reads as a solid world behind the shell.
            backdropMaterial = new Material(shader) { name = "ShellFractureBackdrop" };
            backdropMaterial.SetColor("_BaseColor", backdropColor);
            backdropMaterial.color = backdropColor;
            if (backdropMaterial.HasProperty("_Smoothness"))
                backdropMaterial.SetFloat("_Smoothness", 0.2f);
            return backdropMaterial;
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

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawSphere(new Vector3(fractureOrigin.x, fractureOrigin.y, 0f), 0.02f);

            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(
                Vector3.zero, new Vector3(surfaceSize.x, surfaceSize.y, 0.001f));
        }
    }
}
