using UnityEngine;

namespace F1XR.Driving
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VehicleMeshBoundsMask : MonoBehaviour
    {
        void OnEnable()
        {
            RestoreMeshObjects();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += RemoveInEditor;
                return;
            }
#endif
            Destroy(this);
        }

        void OnValidate()
        {
            RestoreMeshObjects();
        }

        void RestoreMeshObjects()
        {
            Transform modelRoot = FindChildTransform(transform, "Cl22glb");
            if (modelRoot == null)
                return;

            foreach (MeshFilter meshFilter in modelRoot.GetComponentsInChildren<MeshFilter>(true))
                SetMeshActive(meshFilter.gameObject, true);
        }

        void SetMeshActive(GameObject meshObject, bool active)
        {
            if (meshObject.activeSelf == active)
                return;

            meshObject.SetActive(active);
#if UNITY_EDITOR
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(meshObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

#if UNITY_EDITOR
        void RemoveInEditor()
        {
            if (this != null)
                DestroyImmediate(this);
        }
#endif

        static Transform FindChildTransform(Transform root, string childName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                    return child;
            }

            return null;
        }

    }
}
