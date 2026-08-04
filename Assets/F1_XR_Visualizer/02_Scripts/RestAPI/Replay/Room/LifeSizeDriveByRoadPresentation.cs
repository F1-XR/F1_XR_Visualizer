using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1XR.RestAPI.Replay.Room
{
    [DisallowMultipleComponent]
    public sealed class LifeSizeDriveByRoadPresentation : MonoBehaviour
    {
        private const int IgnoreRaycastLayer = 2;

        private GameObject roadRoot;
        private Mesh roadMesh;
        private Material roadMaterial;
        private LifeSizeDriveByPlan preparedPlan;
        private Transform sourceStage;
        private readonly List<RendererState> sourceRendererStates = new();

        internal bool IsPrepared =>
            preparedPlan != null &&
            preparedPlan.IsValid &&
            sourceStage != null &&
            roadRoot != null &&
            roadMesh != null;
        internal bool IsCommitted =>
            IsPrepared && roadRoot.activeSelf;

        internal bool TryPrepare(
            LifeSizeDriveByPlan plan,
            Transform stage,
            out string failure)
        {
            Clear();
            failure = "";
            if (plan == null || !plan.IsValid || stage == null)
            {
                failure =
                    "A validated LifeSize drive-by plan is required before road preparation.";
                return false;
            }

            sourceStage = stage;
            CaptureSourceRendererStates();

            roadRoot = new GameObject("LifeSizeDriveByRoad");
            roadRoot.layer = IgnoreRaycastLayer;
            roadRoot.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            roadRoot.transform.localScale = Vector3.one;
            roadRoot.transform.SetParent(transform, true);

            MeshFilter filter = roadRoot.AddComponent<MeshFilter>();
            MeshRenderer renderer =
                roadRoot.AddComponent<MeshRenderer>();
            roadMesh = BuildRoadMesh(plan);
            roadMaterial = CreateRoadMaterial();
            if (roadMesh == null || roadMaterial == null)
            {
                failure =
                    "The LifeSize road mesh or material could not be prepared.";
                Clear();
                return false;
            }

            filter.sharedMesh = roadMesh;
            renderer.sharedMaterial = roadMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            roadRoot.SetActive(false);
            preparedPlan = plan;

            if (!TryValidatePrepared(out failure))
            {
                Clear();
                return false;
            }

            return true;
        }

        internal bool TryCommit(
            LifeSizeDriveByPlan plan,
            out string failure)
        {
            failure = "";
            if (!IsPrepared ||
                !ReferenceEquals(plan, preparedPlan) ||
                !TryValidatePrepared(out failure))
            {
                if (string.IsNullOrEmpty(failure))
                {
                    failure =
                        "Only the validated plan used during preparation can be committed.";
                }
                return false;
            }

            SetSourceEnvironmentHidden(true);
            roadRoot.SetActive(true);
            return true;
        }

        internal void Clear()
        {
            SetSourceEnvironmentHidden(false);
            sourceRendererStates.Clear();
            sourceStage = null;
            preparedPlan = null;
            if (roadRoot != null)
                Destroy(roadRoot);
            roadRoot = null;

            if (roadMaterial != null)
                Destroy(roadMaterial);
            roadMaterial = null;

            if (roadMesh != null)
                Destroy(roadMesh);
            roadMesh = null;
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnDestroy()
        {
            Clear();
        }

        private bool TryValidatePrepared(out string failure)
        {
            failure = "";
            if (roadRoot == null ||
                roadMesh == null ||
                roadMesh.vertexCount < 8 ||
                roadMesh.GetIndexCount(0) < 18)
            {
                failure =
                    "The prepared LifeSize road contains no usable geometry.";
                return false;
            }

            if (roadRoot.GetComponentInChildren<Collider>(true) != null ||
                roadRoot.GetComponentInChildren<Rigidbody>(true) != null)
            {
                failure =
                    "The LifeSize road must remain render-only and non-interactable.";
                return false;
            }

            return true;
        }

        private void CaptureSourceRendererStates()
        {
            sourceRendererStates.Clear();
            if (sourceStage == null)
                return;

            Transform cars = sourceStage.Find("Cars");
            Renderer[] renderers =
                sourceStage.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null ||
                    cars != null && renderer.transform.IsChildOf(cars))
                {
                    continue;
                }

                sourceRendererStates.Add(new RendererState(renderer));
            }
        }

        private void SetSourceEnvironmentHidden(bool hidden)
        {
            for (int i = 0; i < sourceRendererStates.Count; i++)
                sourceRendererStates[i].SetHidden(hidden);
        }

        private readonly struct RendererState
        {
            private readonly Renderer renderer;
            private readonly bool forceRenderingOff;

            public RendererState(Renderer renderer)
            {
                this.renderer = renderer;
                forceRenderingOff = renderer.forceRenderingOff;
            }

            public void SetHidden(bool hidden)
            {
                if (renderer != null)
                {
                    renderer.forceRenderingOff =
                        hidden || forceRenderingOff;
                }
            }
        }

        private static Mesh BuildRoadMesh(LifeSizeDriveByPlan plan)
        {
            IReadOnlyList<Vector3> centerline = plan.Centerline;
            if (centerline == null || centerline.Count < 2)
                return null;

            int pointCount = centerline.Count;
            Vector3[] vertices = new Vector3[pointCount * 4];
            Vector2[] uv = new Vector2[vertices.Length];
            List<int> triangles =
                new((pointCount - 1) * 24);
            float halfWidth = plan.RoadWidth * 0.5f;
            float distance = 0f;

            for (int i = 0; i < pointCount; i++)
            {
                if (i > 0)
                {
                    distance += Vector3.Distance(
                        centerline[i - 1],
                        centerline[i]);
                }

                int before = Mathf.Max(0, i - 1);
                int after = Mathf.Min(pointCount - 1, i + 1);
                Vector3 tangent =
                    centerline[after] - centerline[before];
                tangent = Vector3.ProjectOnPlane(
                    tangent,
                    Vector3.up);
                if (tangent.sqrMagnitude <= 0.000001f)
                    return null;
                tangent.Normalize();

                Vector3 right =
                    Vector3.Cross(Vector3.up, tangent).normalized;
                Vector3 topLeft =
                    centerline[i] - right * halfWidth;
                Vector3 topRight =
                    centerline[i] + right * halfWidth;
                Vector3 down =
                    Vector3.down * plan.RoadThickness;
                int vertex = i * 4;
                vertices[vertex] = topLeft;
                vertices[vertex + 1] = topRight;
                vertices[vertex + 2] = topLeft + down;
                vertices[vertex + 3] = topRight + down;
                uv[vertex] = new Vector2(0f, distance);
                uv[vertex + 1] = new Vector2(1f, distance);
                uv[vertex + 2] = new Vector2(0f, distance);
                uv[vertex + 3] = new Vector2(1f, distance);
            }

            for (int i = 0; i < pointCount - 1; i++)
            {
                int current = i * 4;
                int next = current + 4;

                AddQuad(
                    triangles,
                    current,
                    next,
                    current + 1,
                    next + 1);
                AddQuad(
                    triangles,
                    current + 3,
                    next + 3,
                    current + 2,
                    next + 2);
                AddQuad(
                    triangles,
                    current + 2,
                    next + 2,
                    current,
                    next);
                AddQuad(
                    triangles,
                    current + 1,
                    next + 1,
                    current + 3,
                    next + 3);
            }

            Mesh mesh = new()
            {
                name = "LifeSizeDriveByRoadMesh",
                vertices = vertices,
                uv = uv,
                triangles = triangles.ToArray()
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddQuad(
            List<int> triangles,
            int first,
            int nextFirst,
            int second,
            int nextSecond)
        {
            triangles.Add(first);
            triangles.Add(nextFirst);
            triangles.Add(second);
            triangles.Add(second);
            triangles.Add(nextFirst);
            triangles.Add(nextSecond);
        }

        private static Material CreateRoadMaterial()
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                return null;

            Material material = new(shader)
            {
                name = "LifeSizeDriveByRoadMaterial"
            };
            Color roadColor = new(0.12f, 0.13f, 0.15f, 1f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", roadColor);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", roadColor);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.15f);
            return material;
        }
    }
}
