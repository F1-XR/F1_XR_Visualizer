using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

namespace F1XR.EditorTools
{
    /// <summary>
    /// 씬의 XR Origin 을 통합 프리팹 인스턴스로 교체한다.
    /// 씬의 다른 오브젝트가 리그 내부를 가리키던 참조는 상대 경로로 대응시켜 자동 재연결하고,
    /// 씬마다 필요 없는 기능은 인스턴스 오버라이드로 꺼둔다.
    ///
    /// 리그를 새로 만들 필요는 없다. 프리팹을 고치면 모든 씬에 전파된다.
    /// 이 도구는 씬을 새로 추가했을 때 리그를 붙이는 용도로 쓴다.
    /// </summary>
    public static class XROriginUnifier
    {
        const string PrefabPath =
            "Assets/F1_XR_Visualizer/03_Prefabs/XR Origin/XR Origin (VR) Unified.prefab";
        const string SceneDir = "Assets/F1_XR_Visualizer/01_Scenes/";

        /// <summary>리그가 들어가는 모든 씬.</summary>
        static readonly string[] RigScenes =
        {
            "UI Test", "New Scene", "Play", "SessionSelectSpace",
            "SessionSpace", "SessionSpace 1",
            "HomeSpace", "HomeSpace 1", "Showroom",
        };

        /// <summary>GearShift(ControllerShiftMorph)를 켜두는 씬. 나머지는 끈다.</summary>
        static readonly string[] GearShiftOnScenes = { "UI Test" };

        /// <summary>손 트래킹을 끄는 씬 (컨트롤러 전용).</summary>
        static readonly string[] HandsOffScenes = { "SessionSpace", "SessionSpace 1" };

        /// <summary>AR 구성을 끄는 씬 (VR 전용).</summary>
        static readonly string[] ArOffScenes = { "HomeSpace", "HomeSpace 1", "Showroom" };

        [MenuItem("F1XR/XR Origin/Apply Unified Rig To All Scenes")]
        public static void ApplyToAllScenes()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (asset == null)
            {
                Debug.LogError("[XRUnify] 통합 프리팹을 찾을 수 없습니다: " + PrefabPath);
                return;
            }

            foreach (var name in RigScenes) Replace(name, asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[XRUnify] 전 씬 적용 완료.");
        }

        static void Replace(string sceneName, GameObject prefab)
        {
            var path = SceneDir + sceneName + ".unity";
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            var oldRig = FindRig(scene);
            if (oldRig == null)
            {
                Debug.LogWarning("[XRUnify] " + sceneName + ": XR Origin 없음, 건너뜀");
                return;
            }

            var newRig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            newRig.name = oldRig.name;
            newRig.transform.SetPositionAndRotation(oldRig.transform.position, oldRig.transform.rotation);
            newRig.transform.localScale = oldRig.transform.localScale;
            newRig.transform.SetSiblingIndex(oldRig.transform.GetSiblingIndex());

            ApplySceneOverrides(sceneName, newRig);

            var map = new Dictionary<Object, Object>();
            BuildMap(oldRig.transform, newRig.transform, map);
            int rewired = RemapReferences(scene, oldRig, map);

            Object.DestroyImmediate(oldRig);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(string.Format("[XRUnify] {0}: 교체 완료, 참조 {1}건 재연결", sceneName, rewired));
        }

        // ---------------------------------------------------------- 씬별 오버라이드

        static void ApplySceneOverrides(string sceneName, GameObject rig)
        {
            if (System.Array.IndexOf(GearShiftOnScenes, sceneName) < 0)
                SetComponentsEnabled(rig, "ControllerShiftMorph", false);

            if (System.Array.IndexOf(HandsOffScenes, sceneName) >= 0)
                DisableHandTracking(rig);

            if (System.Array.IndexOf(ArOffScenes, sceneName) >= 0)
                DisableArParts(rig);
        }

        /// <summary>
        /// 손 트래킹을 끈다. HandVisualizer 를 비활성화하는 것만으로는 부족한데,
        /// HandInputModeSwitcher.Awake() 가 handVisualizerRoot 를 다시 켜기 때문이다.
        /// </summary>
        static void DisableHandTracking(GameObject rig)
        {
            var hv = FindDeep(rig.transform, "HandVisualizer");
            if (hv != null)
            {
                hv.gameObject.SetActive(false);
                EditorUtility.SetDirty(hv.gameObject);
            }
            SetComponentsEnabled(rig, "HandInputModeSwitcher", false);
        }

        /// <summary>
        /// AR 구성을 끈다. AR 오브젝트는 통째로 비활성화하고, XR Origin 루트와 카메라에서
        /// 벗어날 수 없는 매니저들(ARTrackableManager 계열은 XROrigin, ARCameraManager 는
        /// Camera 를 RequireComponent 로 요구한다)은 컴포넌트 단위로 끈다.
        /// </summary>
        static void DisableArParts(GameObject rig)
        {
            var ar = FindChild(rig.transform, "AR");
            if (ar != null)
            {
                ar.gameObject.SetActive(false);
                EditorUtility.SetDirty(ar.gameObject);
            }

            foreach (var c in rig.GetComponents<Behaviour>())
            {
                if (c == null) continue;
                var n = c.GetType().Name;
                if (n != "ARRaycastManager" && n != "ARPlaneManager" && n != "ARAnchorManager") continue;
                c.enabled = false;
                EditorUtility.SetDirty(c);
            }

            var cam = FindDeep(rig.transform, "Main Camera");
            var acm = cam != null ? cam.GetComponent<ARCameraManager>() : null;
            if (acm != null) { acm.enabled = false; EditorUtility.SetDirty(acm); }
        }

        static void SetComponentsEnabled(GameObject rig, string typeName, bool value)
        {
            foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != typeName) continue;
                mb.enabled = value;
                EditorUtility.SetDirty(mb);
            }
        }

        // ---------------------------------------------------------- 참조 재연결

        static int RemapReferences(Scene scene, GameObject oldRig, Dictionary<Object, Object> map)
        {
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == oldRig) continue;
                foreach (var comp in root.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) continue;
                    var so = new SerializedObject(comp);
                    var p = so.GetIterator();
                    bool changed = false;
                    while (p.Next(true))
                    {
                        if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var v = p.objectReferenceValue;
                        if (v == null) continue;

                        Object nv;
                        if (map.TryGetValue(v, out nv) && nv != null)
                        {
                            p.objectReferenceValue = nv;
                            changed = true;
                            count++;
                        }
                        else if (BelongsTo(v, oldRig))
                        {
                            Debug.LogWarning(string.Format(
                                "[XRUnify] 대응 실패 — {0}.{1} 가 옛 리그의 '{2}' 를 가리켜 끊깁니다.",
                                comp.GetType().Name, p.propertyPath, v.name), comp);
                        }
                    }
                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            return count;
        }

        /// <summary>이름 기준으로 옛 리그와 새 리그의 오브젝트/컴포넌트를 대응시킨다.</summary>
        static void BuildMap(Transform a, Transform b, Dictionary<Object, Object> map)
        {
            map[a.gameObject] = b.gameObject;
            map[a] = b;

            var ca = a.GetComponents<Component>();
            var cb = b.GetComponents<Component>();
            for (int i = 0; i < ca.Length; i++)
            {
                if (ca[i] == null) continue;
                var type = ca[i].GetType();
                int ordinal = 0;
                for (int k = 0; k < i; k++)
                    if (ca[k] != null && ca[k].GetType() == type) ordinal++;

                int seen = 0;
                for (int j = 0; j < cb.Length; j++)
                {
                    if (cb[j] == null || cb[j].GetType() != type) continue;
                    if (seen == ordinal) { map[ca[i]] = cb[j]; break; }
                    seen++;
                }
            }

            var taken = new HashSet<Transform>();
            foreach (Transform childA in a)
            {
                Transform childB = null;
                foreach (Transform cand in b)
                {
                    if (taken.Contains(cand) || cand.name != childA.name) continue;
                    childB = cand;
                    break;
                }
                if (childB == null) continue;
                taken.Add(childB);
                BuildMap(childA, childB, map);
            }
        }

        static bool BelongsTo(Object obj, GameObject rig)
        {
            var go = obj as GameObject;
            if (go == null)
            {
                var c = obj as Component;
                if (c == null) return false;
                go = c.gameObject;
            }
            return go.transform.IsChildOf(rig.transform);
        }

        // ---------------------------------------------------------- 유틸

        static GameObject FindRig(Scene scene)
        {
            return scene.GetRootGameObjects().FirstOrDefault(g => g.name.StartsWith("XR Origin"));
        }

        static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform t in parent) if (t.name == name) return t;
            return null;
        }

        static Transform FindDeep(Transform parent, string name)
        {
            foreach (var t in parent.GetComponentsInChildren<Transform>(true))
                if (t != parent && t.name == name) return t;
            return null;
        }
    }
}
