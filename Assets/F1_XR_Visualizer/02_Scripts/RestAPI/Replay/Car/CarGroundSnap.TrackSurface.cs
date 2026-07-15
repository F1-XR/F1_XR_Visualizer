using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    public partial class CarGroundSnap
    {
        private void EnsureTrackSurfaceColliders(Transform root)
        {
            if (root == null)
            {
                ClearSurfaceCache();
                return;
            }

            if (trackSurfaceRoot != root)
            {
                ClearSurfaceCache();
                trackSurfaceRoot = root;
            }

            PruneTrackSurfaceColliders(root);

            if (colliderReadyRoots.Contains(root))
                return;

            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null)
                    continue;

                if (meshFilter.GetComponentInParent<ReplayCarView>() != null)
                    continue;

                MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                MeshCollider existingMeshCollider = meshFilter.GetComponent<MeshCollider>();
                if (existingMeshCollider != null)
                {
                    if (existingMeshCollider.enabled)
                        trackSurfaceColliders.Add(existingMeshCollider);

                    continue;
                }

                if (meshFilter.GetComponent<Collider>() != null)
                    continue;

                MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = meshFilter.sharedMesh;
                trackSurfaceColliders.Add(collider);
            }

            colliderReadyRoots.Add(root);
        }

        private void PruneTrackSurfaceColliders(Transform root)
        {
            trackSurfaceColliders.RemoveWhere(collider =>
                collider == null ||
                root == null ||
                !collider.transform.IsChildOf(root));
        }

        private bool IsTrackSurfaceCollider(Collider collider)
        {
            if (collider == null)
                return false;

            if (trackSurfaceRoot == null)
                return true;

            if (!collider.transform.IsChildOf(trackSurfaceRoot))
                return false;

            if (!trackSurfaceColliders.Contains(collider))
                return false;

            return true;
        }
    }
}
