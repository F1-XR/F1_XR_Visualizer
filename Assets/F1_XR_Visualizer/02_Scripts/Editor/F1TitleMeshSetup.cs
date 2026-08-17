using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class F1TitleMeshSetup
{
    private const string ScenePath = "Assets/F1_XR_Visualizer/01_Scenes/HomeSpace 1.unity";
    private const string MeshPath = "Assets/F1_XR_Visualizer/05_Models/MyLittleGrandPrix.obj";
    private const string MaterialPath = "Assets/F1_XR_Visualizer/08_Materials/F1_Logo_Metal.mat";
    private const string DoubleSidedMaterialPath = "Assets/F1_XR_Visualizer/08_Materials/F1_Title_Metal.mat";
    private const string ObjectName = "MyLittleGrandPrixText";
    private const string BackFaceName = "BackFace";

    [MenuItem("F1 XR/Setup My Little Grand Prix Title %#m")]
    public static void Setup()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var logo = GameObject.Find("F1_Logo");
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (logo == null || mesh == null || material == null)
        {
            Debug.LogError("[F1TitleMeshSetup] F1_Logo, title mesh, or F1 logo material could not be found.");
            return;
        }

        var title = GameObject.Find(ObjectName);
        if (title == null)
        {
            title = new GameObject(ObjectName, typeof(MeshFilter), typeof(MeshRenderer));
        }

        var filter = title.GetComponent<MeshFilter>();
        var renderer = title.GetComponent<MeshRenderer>();
        filter.sharedMesh = mesh;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;

        var logoRenderer = logo.GetComponentInChildren<Renderer>();
        if (logoRenderer == null)
        {
            Debug.LogError("[F1TitleMeshSetup] F1_Logo has no Renderer.");
            return;
        }

        title.transform.rotation = logoRenderer.transform.rotation;
        title.transform.localScale = Vector3.one;
        var logoBounds = logoRenderer.bounds;
        var titleBounds = renderer.bounds;
        var currentPosition = title.transform.position;
        var boundsOffset = titleBounds.center - currentPosition;
        title.transform.position = new Vector3(
            logoBounds.center.x - boundsOffset.x,
            logoBounds.min.y - 0.12f - titleBounds.extents.y - boundsOffset.y,
            logoBounds.center.z - boundsOffset.z);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[F1TitleMeshSetup] Created MyLittleGrandPrixText below F1_Logo using F1_Logo_Metal.");
    }

    [MenuItem("F1 XR/Apply Double-Sided Title Material %#d")]
    public static void ApplyDoubleSidedTitleMaterial()
    {
        UseMatchedDoubleSidedTitle();
    }

    [MenuItem("F1 XR/Set Title Depth to Default %#t")]
    public static void SetTitleDepthToDefault()
    {
        var title = GameObject.Find(ObjectName);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (title == null || material == null)
        {
            Debug.LogError("[F1TitleMeshSetup] Title object or source material could not be found.");
            return;
        }

        var scale = title.transform.localScale;
        title.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        title.GetComponent<MeshRenderer>().sharedMaterial = material;
        EditorSceneManager.MarkSceneDirty(title.scene);
        EditorSceneManager.SaveScene(title.scene);
        Debug.Log("[F1TitleMeshSetup] Restored F1_Logo_Metal and title depth scale to 1.");
    }

    [MenuItem("F1 XR/Match Title Depth to F1 Logo %#g")]
    public static void MatchTitleDepthToLogo()
    {
        var logo = GameObject.Find("F1_Logo");
        var title = GameObject.Find(ObjectName);
        if (logo == null || title == null)
        {
            Debug.LogError("[F1TitleMeshSetup] F1_Logo or title object could not be found.");
            return;
        }

        var logoRenderer = logo.GetComponentInChildren<Renderer>();
        var filter = title.GetComponent<MeshFilter>();
        if (logoRenderer == null || filter.sharedMesh == null)
        {
            Debug.LogError("[F1TitleMeshSetup] Logo renderer or title mesh could not be found.");
            return;
        }

        var titleDepth = filter.sharedMesh.bounds.size.z;
        if (titleDepth <= 0f)
        {
            Debug.LogError("[F1TitleMeshSetup] Title mesh has no depth.");
            return;
        }

        var logoDepth = ProjectedBoundsSize(logoRenderer.bounds, title.transform.forward);
        var scale = title.transform.localScale;
        title.transform.localScale = new Vector3(scale.x, scale.y, logoDepth / titleDepth);
        EditorSceneManager.MarkSceneDirty(title.scene);
        EditorSceneManager.SaveScene(title.scene);
        Debug.Log($"[F1TitleMeshSetup] Matched title depth scale to {title.transform.localScale.z:F4}.");
    }

    private static float ProjectedBoundsSize(Bounds bounds, Vector3 direction)
    {
        direction.Normalize();
        var extents = bounds.extents;
        var center = bounds.center;
        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;

        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var point = center + Vector3.Scale(extents, new Vector3(x, y, z));
            var projection = Vector3.Dot(point, direction);
            minimum = Mathf.Min(minimum, projection);
            maximum = Mathf.Max(maximum, projection);
        }

        return maximum - minimum;
    }

    [MenuItem("F1 XR/Use Matched Double-Sided Title %#b")]
    public static void UseMatchedDoubleSidedTitle()
    {
        var title = GameObject.Find(ObjectName);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (title == null || material == null)
        {
            Debug.LogError("[F1TitleMeshSetup] Title object or source material could not be found.");
            return;
        }

        var filter = title.GetComponent<MeshFilter>();
        var renderer = title.GetComponent<MeshRenderer>();
        if (filter.sharedMesh == null || renderer == null)
        {
            Debug.LogError("[F1TitleMeshSetup] Title mesh or renderer could not be found.");
            return;
        }

        renderer.sharedMaterial = material;

        var backFace = title.transform.Find(BackFaceName);
        if (backFace == null)
        {
            backFace = new GameObject(BackFaceName, typeof(MeshFilter), typeof(MeshRenderer)).transform;
            backFace.SetParent(title.transform, false);
        }

        backFace.localPosition = new Vector3(0f, 0f, filter.sharedMesh.bounds.size.z);
        backFace.localRotation = Quaternion.identity;
        backFace.localScale = new Vector3(1f, 1f, -1f);

        var backFilter = backFace.GetComponent<MeshFilter>();
        var backRenderer = backFace.GetComponent<MeshRenderer>();
        backFilter.sharedMesh = filter.sharedMesh;
        backRenderer.sharedMaterial = material;
        backRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        backRenderer.receiveShadows = true;

        EditorSceneManager.MarkSceneDirty(title.scene);
        EditorSceneManager.SaveScene(title.scene);
        Debug.Log("[F1TitleMeshSetup] Added a reverse-winding title copy with F1_Logo_Metal.");
    }
}
