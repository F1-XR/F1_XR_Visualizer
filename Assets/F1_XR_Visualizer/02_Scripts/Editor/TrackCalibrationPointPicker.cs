using System.Collections.Generic;
using F1XR.RestAPI.Replay;
using UnityEditor;
using UnityEngine;

public sealed class TrackCalibrationPointPicker : EditorWindow
{
    private TrackCalibration calibration;
    private Transform localRoot;
    private int pointIndex;
    private bool isPicking;
    private readonly List<MeshCollider> temporaryColliders = new();
    private readonly HashSet<Collider> pickableColliders = new();

    [MenuItem("Tools/F1 XR/Track Calibration Point Picker")]
    private static void Open()
    {
        GetWindow<TrackCalibrationPointPicker>("Track Point Picker");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGui;
        TryUseSelection();
        TryFindLocalRoot();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGui;
        RemoveTemporaryColliders();
    }

    private void OnSelectionChange()
    {
        TryUseSelection();
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Calibration", EditorStyles.boldLabel);
        calibration = (TrackCalibration)EditorGUILayout.ObjectField(
            "Asset",
            calibration,
            typeof(TrackCalibration),
            false
        );

        localRoot = (Transform)EditorGUILayout.ObjectField(
            "Local Root",
            localRoot,
            typeof(Transform),
            true
        );

        if (GUILayout.Button("Find TrackPlacement Root"))
            TryFindLocalRoot();

        EditorGUILayout.Space();

        if (calibration == null)
        {
            EditorGUILayout.HelpBox("Select BahrainTrackCalibration.asset.", MessageType.Info);
            return;
        }

        if (calibration.points == null || calibration.points.Length == 0)
        {
            EditorGUILayout.HelpBox("Calibration has no points.", MessageType.Warning);
            return;
        }

        pointIndex = Mathf.Clamp(pointIndex, 0, calibration.points.Length - 1);
        pointIndex = EditorGUILayout.Popup("Point", pointIndex, PointNames());

        TrackCalibration.Point point = calibration.points[pointIndex];
        EditorGUILayout.Vector2Field("Source", point.sourcePosition);
        EditorGUILayout.Vector3Field("Target Local", point.targetLocalPosition);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(localRoot == null))
        {
            string buttonText = isPicking ? "Picking: click road center in Scene View" : "Pick Scene Point";
            if (GUILayout.Button(buttonText))
                isPicking = !isPicking;
        }

        if (GUILayout.Button("Stop Picking"))
            isPicking = false;

        if (localRoot == null)
            EditorGUILayout.HelpBox("Assign TrackPlacement or click Find TrackPlacement Root.", MessageType.Warning);

        EditorGUILayout.HelpBox(
            "Click the road center in Scene View. The hit point is saved as Local Root localPosition.",
            MessageType.None
        );
    }

    private void OnSceneGui(SceneView sceneView)
    {
        DrawConfiguredPoints();

        if (!isPicking || calibration == null || localRoot == null)
            return;

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        if (Event.current.type == EventType.Layout)
            HandleUtility.AddDefaultControl(controlId);

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(12f, 12f, 360f, 44f), EditorStyles.helpBox);
        GUILayout.Label($"Pick {calibration.points[pointIndex].name}: click road center");
        GUILayout.EndArea();
        Handles.EndGUI();

        Event evt = Event.current;
        if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
        {
            GUIUtility.hotControl = 0;
            evt.Use();
            return;
        }

        if (evt.type != EventType.MouseDown || evt.button != 0 || evt.alt)
            return;

        GUIUtility.hotControl = controlId;
        EnsurePickableColliders();
        Physics.SyncTransforms();

        Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
        if (!TryRaycastPickable(ray, out RaycastHit hit))
        {
            if (TryHitLocalGroundPlane(ray, out Vector3 planeHit))
            {
                SaveTargetPosition(planeHit, "local ground plane");
                evt.Use();
                return;
            }

            Debug.LogWarning(
                $"Track point pick failed. pickableColliders={pickableColliders.Count}, " +
                $"mouse={evt.mousePosition}, root={localRoot.name}"
            );
            evt.Use();
            return;
        }

        SaveTargetPosition(hit.point, hit.collider.name);
        evt.Use();
    }

    private void SaveTargetPosition(Vector3 worldPosition, string hitSource)
    {
        Vector3 localPosition = localRoot.InverseTransformPoint(worldPosition);

        Undo.RecordObject(calibration, "Pick Track Calibration Point");

        TrackCalibration.Point[] points = calibration.points;
        TrackCalibration.Point point = points[pointIndex];
        point.targetLocalPosition = localPosition;
        points[pointIndex] = point;
        calibration.points = points;

        EditorUtility.SetDirty(calibration);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"{calibration.name}: {point.name} targetLocalPosition={localPosition} " +
            $"world={worldPosition}, root={localRoot.name}, hit={hitSource}"
        );

        if (pointIndex < calibration.points.Length - 1)
            pointIndex++;

        SceneView.RepaintAll();
        Repaint();
    }

    private void DrawConfiguredPoints()
    {
        if (calibration == null || localRoot == null || calibration.points == null)
            return;

        for (int i = 0; i < calibration.points.Length; i++)
        {
            TrackCalibration.Point point = calibration.points[i];
            Vector3 worldPosition = localRoot.TransformPoint(point.targetLocalPosition);
            float size = HandleUtility.GetHandleSize(worldPosition) * 0.035f;

            Handles.color = i == pointIndex ? Color.yellow : Color.cyan;
            Handles.SphereHandleCap(0, worldPosition, Quaternion.identity, size, EventType.Repaint);
            Handles.Label(worldPosition, point.name);
        }
    }

    private void EnsurePickableColliders()
    {
        pickableColliders.Clear();

        MeshFilter[] meshFilters = localRoot.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter.sharedMesh == null)
                continue;

            MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled)
                continue;

            if (meshFilter.name == "Cube")
                continue;

            Collider existingCollider = meshFilter.GetComponent<Collider>();
            if (existingCollider != null)
            {
                pickableColliders.Add(existingCollider);
                continue;
            }

            MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.sharedMesh;
            collider.hideFlags = HideFlags.DontSave;
            temporaryColliders.Add(collider);
            pickableColliders.Add(collider);
        }
    }

    private bool TryRaycastPickable(Ray ray, out RaycastHit bestHit)
    {
        bestHit = default;
        RaycastHit[] hits = Physics.RaycastAll(ray, 10000f);

        float bestDistance = float.MaxValue;
        bool found = false;

        foreach (RaycastHit hit in hits)
        {
            if (!pickableColliders.Contains(hit.collider))
                continue;

            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            bestHit = hit;
            found = true;
        }

        return found;
    }

    private bool TryHitLocalGroundPlane(Ray ray, out Vector3 worldPosition)
    {
        worldPosition = default;

        Vector3 planeNormal = localRoot.TransformDirection(Vector3.up);
        Vector3 planePoint = localRoot.TransformPoint(Vector3.zero);
        Plane plane = new Plane(planeNormal, planePoint);

        if (!plane.Raycast(ray, out float distance))
            return false;

        worldPosition = ray.GetPoint(distance);
        return true;
    }

    private void RemoveTemporaryColliders()
    {
        foreach (MeshCollider collider in temporaryColliders)
        {
            if (collider != null)
                DestroyImmediate(collider);
        }

        temporaryColliders.Clear();
        pickableColliders.Clear();
    }

    private void TryUseSelection()
    {
        if (Selection.activeObject is TrackCalibration selectedCalibration)
            calibration = selectedCalibration;
    }

    private void TryFindLocalRoot()
    {
        GameObject root = GameObject.Find("TrackPlacement");
        if (root == null)
            root = GameObject.Find("Bahrain");
        if (root == null)
            root = GameObject.Find("BahrainTrack");

        if (root != null)
            localRoot = root.transform;
    }

    private string[] PointNames()
    {
        string[] names = new string[calibration.points.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string pointName = calibration.points[i].name;
            names[i] = string.IsNullOrWhiteSpace(pointName) ? $"Point {i}" : pointName;
        }

        return names;
    }
}
