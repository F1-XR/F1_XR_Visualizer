using System.Collections.Generic;
using UnityEngine;

namespace F1XR.Driving
{
    [DisallowMultipleComponent]
    public sealed class SuzukaCircuitMeshColliders : MonoBehaviour
    {
        [SerializeField] string circuitRootName = "SuzukaCircuit";
        [SerializeField] bool includeInactiveMeshes = true;

        readonly List<MeshCollider> runtimeColliders = new();

        void Awake()
        {
            GameObject circuitRoot = GameObject.Find(circuitRootName);
            if (circuitRoot == null)
            {
                Debug.LogWarning($"[DrivingTest] Circuit not found: {circuitRootName}.", this);
                return;
            }

            foreach (MeshFilter meshFilter in circuitRoot.GetComponentsInChildren<MeshFilter>(includeInactiveMeshes))
            {
                if (meshFilter.sharedMesh == null || meshFilter.GetComponent<Collider>() != null)
                    continue;

                MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = meshFilter.sharedMesh;
                collider.convex = false;
                runtimeColliders.Add(collider);
            }

            Physics.SyncTransforms();
            Debug.Log($"[DrivingTest] Suzuka mesh colliders added: {runtimeColliders.Count}.", this);
        }

        void OnDisable()
        {
            foreach (MeshCollider collider in runtimeColliders)
            {
                if (collider != null)
                    Destroy(collider);
            }

            runtimeColliders.Clear();
        }
    }
}
