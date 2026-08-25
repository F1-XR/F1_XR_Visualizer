using System.IO;
using UnityEditor;
using UnityEngine;

namespace F1XR.EditorTools
{
    /// <summary>
    /// 도리토스 FBX 모델에서 물리 프리팹을 생성한다.
    /// 칩(Chip_XX)에는 Rigidbody + Convex MeshCollider,
    /// 봉지(Bag, Bag_Inside)에는 Concave MeshCollider를 붙여
    /// 봉지를 뒤집으면 칩이 쏟아지도록 구성한다.
    /// Bag_ChipFill 은 시각용 채움 메시라 비활성화한다.
    /// </summary>
    public static class DoritosPhysicsPrefabBuilder
    {
        const string ModelPath =
            "Assets/F1_XR_Visualizer/05_Models/Doritos/Doritos.fbx";
        const string PrefabDir =
            "Assets/F1_XR_Visualizer/03_Prefabs/Doritos";
        const string PrefabPath = PrefabDir + "/Doritos.prefab";

        [MenuItem("F1XR/Doritos/Create Physics Prefab")]
        public static void CreatePhysicsPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"도리토스 모델을 찾을 수 없습니다: {ModelPath}");
                return;
            }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(
                root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            root.name = "Doritos";

            // 봉지 루트: 손으로 잡거나 애니메이션으로 움직여도 물리가 따라오도록 kinematic
            var rootRb = root.AddComponent<Rigidbody>();
            rootRb.isKinematic = true;

            foreach (Transform child in root.transform)
            {
                var go = child.gameObject;
                if (child.name.StartsWith("Chip_"))
                {
                    var col = go.AddComponent<MeshCollider>();
                    col.convex = true;

                    var rb = go.AddComponent<Rigidbody>();
                    rb.mass = 0.03f;
                    rb.linearDamping = 0.3f;
                    rb.angularDamping = 0.8f;
                    // 얇은 칩이 봉지 벽을 뚫지 않도록 연속 충돌 감지
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                }
                else if (child.name == "Bag" || child.name == "Bag_Inside")
                {
                    go.AddComponent<MeshCollider>(); // concave — kinematic 루트에 붙으므로 허용
                }
                else if (child.name == "Bag_ChipFill")
                {
                    go.SetActive(false);
                }
            }

            if (!Directory.Exists(PrefabDir))
            {
                Directory.CreateDirectory(PrefabDir);
                AssetDatabase.Refresh();
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
            Debug.Log($"도리토스 물리 프리팹 생성 완료: {PrefabPath}");
        }
    }
}
