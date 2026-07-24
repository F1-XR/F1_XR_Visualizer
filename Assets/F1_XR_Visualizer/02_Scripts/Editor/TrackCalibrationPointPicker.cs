using System.Collections.Generic;
using F1XR.RestAPI.Replay;
using UnityEditor;
using UnityEngine;

namespace F1XR.Editor
{
    public sealed class TrackCalibrationPointPicker : EditorWindow
    {
        private static readonly SourcePoint[] BahrainSourcePoints =
        {
            new("Turn 1", new Vector2(42.405939f, 8329.202564f)),
            new("Turn 2", new Vector2(820.781621f, 7879.043841f)),
            new("Turn 3", new Vector2(1912.969997f, 8065.442167f)),
            new("Turn 4", new Vector2(7487.745293f, 6790.710716f)),
            new("Turn 5", new Vector2(5820.228443f, 4860.740936f)),
            new("Turn 6", new Vector2(5180.404121f, 4214.598861f)),
            new("Turn 7", new Vector2(4276.202937f, 4156.240631f)),
            new("Turn 8", new Vector2(2490.099011f, 2458.600397f)),
            new("Turn 9", new Vector2(2711.155288f, 5950.242339f)),
            new("Turn 10", new Vector2(2100.132716f, 6613.999851f)),
            new("Turn 11", new Vector2(2120.985221f, -663.995072f)),
            new("Turn 12", new Vector2(4981.251162f, 1603.747622f)),
            new("Turn 13", new Vector2(6665.934072f, 449.275637f)),
            new("Turn 14", new Vector2(-145.412458f, -3472.808548f)),
            new("Turn 15", new Vector2(-552.812606f, -2803.666251f)),
        };

        private TrackCalibration calibration;
        private Transform localRoot;
        private int pointIndex;
        private bool isPicking;
        private bool showSourcePreview = true;
        private bool advanceAfterPick = true;
        private float targetPositionScale = 1000f;
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
            }

            if (GUILayout.Button("Fill Bahrain Source Positions"))
                FillBahrainSourcePositions();

            if (calibration.points == null || calibration.points.Length == 0)
                return;

            showSourcePreview = EditorGUILayout.Toggle("Show Source Preview", showSourcePreview);
            advanceAfterPick = EditorGUILayout.Toggle("Advance After Pick", advanceAfterPick);

            pointIndex = Mathf.Clamp(pointIndex, 0, calibration.points.Length - 1);
            pointIndex = EditorGUILayout.Popup("Point", pointIndex, PointNames());

            TrackCalibration.Point point = calibration.points[pointIndex];
            EditorGUILayout.Vector2Field("Source", point.sourcePosition);
            EditorGUILayout.Vector3Field("Target Local", point.targetLocalPosition);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(calibration.points.Length < 2))
            {
                if (GUILayout.Button("Insert Mid Point After Selected"))
                    InsertMidPointAfterSelected();
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!CanUseSourcePreview()))
            {
                if (GUILayout.Button("Set Selected Target To Source Preview"))
                    SetSelectedTargetToSourcePreview();

                if (GUILayout.Button("Set All Targets To Source Preview"))
                    SetAllTargetsToSourcePreview();
            }

            EditorGUILayout.Space();

            targetPositionScale = EditorGUILayout.FloatField("Target Scale", targetPositionScale);

            using (new EditorGUI.DisabledScope(Mathf.Approximately(targetPositionScale, 0f)))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Multiply Selected Target"))
                    ScaleSelectedTarget(targetPositionScale);

                if (GUILayout.Button("Divide Selected Target"))
                    ScaleSelectedTarget(1f / targetPositionScale);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Multiply All Targets"))
                    ScaleAllTargets(targetPositionScale);

                if (GUILayout.Button("Divide All Targets"))
                    ScaleAllTargets(1f / targetPositionScale);
                EditorGUILayout.EndHorizontal();
            }

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

            if (advanceAfterPick && pointIndex < calibration.points.Length - 1)
                pointIndex++;

            SceneView.RepaintAll();
            Repaint();
        }

        private bool CanUseSourcePreview()
        {
            if (calibration == null || calibration.points == null || calibration.points.Length == 0)
                return false;

            if (pointIndex < 0 || pointIndex >= calibration.points.Length)
                return false;

            return calibration.active;
        }

        private void SetSelectedTargetToSourcePreview()
        {
            if (!CanUseSourcePreview())
                return;

            TrackCalibration.Point[] points = calibration.points;
            TrackCalibration.Point point = points[pointIndex];

            if (!calibration.TryMapGlobalPreview(point.sourcePosition, out Vector3 mappedLocalPosition))
            {
                Debug.LogWarning($"{calibration.name}: source preview failed for {point.name}.");
                return;
            }

            Undo.RecordObject(calibration, "Set Selected Target To Source Preview");

            point.targetLocalPosition = mappedLocalPosition;
            points[pointIndex] = point;
            calibration.points = points;

            EditorUtility.SetDirty(calibration);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Repaint();

            Debug.Log($"{calibration.name}: {point.name} targetLocalPosition set to source preview {mappedLocalPosition}.");
        }

        private void SetAllTargetsToSourcePreview()
        {
            if (!CanUseSourcePreview())
                return;

            TrackCalibration.Point[] oldPoints = calibration.points;
            Vector3[] mappedPositions = new Vector3[oldPoints.Length];

            for (int i = 0; i < oldPoints.Length; i++)
            {
                if (!calibration.TryMapGlobalPreview(oldPoints[i].sourcePosition, out mappedPositions[i]))
                {
                    Debug.LogWarning($"{calibration.name}: source preview failed for {oldPoints[i].name}.");
                    return;
                }
            }

            Undo.RecordObject(calibration, "Set All Targets To Source Preview");

            TrackCalibration.Point[] points = calibration.points;
            for (int i = 0; i < points.Length; i++)
            {
                TrackCalibration.Point point = points[i];
                point.targetLocalPosition = mappedPositions[i];
                points[i] = point;
            }

            calibration.points = points;
            EditorUtility.SetDirty(calibration);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Repaint();

            Debug.Log($"{calibration.name}: set {points.Length} target positions to source preview.");
        }

        private void ScaleSelectedTarget(float scale)
        {
            if (calibration == null || calibration.points == null || calibration.points.Length == 0)
                return;

            Undo.RecordObject(calibration, "Scale Selected Target Position");

            TrackCalibration.Point[] points = calibration.points;
            TrackCalibration.Point point = points[pointIndex];
            point.targetLocalPosition = ScaleTargetPosition(point.targetLocalPosition, scale);
            points[pointIndex] = point;
            calibration.points = points;

            EditorUtility.SetDirty(calibration);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Repaint();

            Debug.Log($"{calibration.name}: scaled {point.name} targetLocalPosition by {scale}.");
        }

        private void ScaleAllTargets(float scale)
        {
            if (calibration == null || calibration.points == null || calibration.points.Length == 0)
                return;

            Undo.RecordObject(calibration, "Scale All Target Positions");

            TrackCalibration.Point[] points = calibration.points;
            for (int i = 0; i < points.Length; i++)
            {
                TrackCalibration.Point point = points[i];
                point.targetLocalPosition = ScaleTargetPosition(point.targetLocalPosition, scale);
                points[i] = point;
            }

            calibration.points = points;
            EditorUtility.SetDirty(calibration);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Repaint();

            Debug.Log($"{calibration.name}: scaled {points.Length} targetLocalPositions by {scale}.");
        }

        private static Vector3 ScaleTargetPosition(Vector3 position, float scale)
        {
            return new Vector3(position.x * scale, position.y, position.z * scale);
        }

        private void InsertMidPointAfterSelected()
        {
            if (calibration == null || calibration.points == null || calibration.points.Length < 2)
                return;

            pointIndex = Mathf.Clamp(pointIndex, 0, calibration.points.Length - 1);

            TrackCalibration.Point[] oldPoints = calibration.points;
            int nextIndex = pointIndex + 1 < oldPoints.Length ? pointIndex + 1 : 0;
            int insertIndex = pointIndex + 1;

            TrackCalibration.Point selected = oldPoints[pointIndex];
            TrackCalibration.Point next = oldPoints[nextIndex];
            TrackCalibration.Point midPoint = new TrackCalibration.Point
            {
                name = MakeMidPointName(selected.name, next.name),
                sourcePosition = Vector2.Lerp(selected.sourcePosition, next.sourcePosition, 0.5f),
                sourceHeight = Mathf.Lerp(selected.sourceHeight, next.sourceHeight, 0.5f),
                targetLocalPosition = Vector3.Lerp(selected.targetLocalPosition, next.targetLocalPosition, 0.5f)
            };

            TrackCalibration.Point[] newPoints = new TrackCalibration.Point[oldPoints.Length + 1];
            for (int i = 0; i < insertIndex; i++)
                newPoints[i] = oldPoints[i];

            newPoints[insertIndex] = midPoint;

            for (int i = insertIndex; i < oldPoints.Length; i++)
                newPoints[i + 1] = oldPoints[i];

            Undo.RecordObject(calibration, "Insert Track Calibration Mid Point");

            calibration.points = newPoints;
            pointIndex = insertIndex;

            EditorUtility.SetDirty(calibration);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Repaint();

            Debug.Log(
                $"{calibration.name}: inserted {midPoint.name} at index {insertIndex}. " +
                "Use Pick Scene Point to set its targetLocalPosition on the road."
            );
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

                if (!showSourcePreview)
                    continue;

                if (!calibration.TryMapGlobalPreview(point.sourcePosition, out Vector3 mappedLocalPosition))
                    continue;

                Vector3 mappedWorldPosition = localRoot.TransformPoint(mappedLocalPosition);
                float mappedSize = HandleUtility.GetHandleSize(mappedWorldPosition) * 0.045f;
                float error = Vector3.Distance(
                    new Vector3(point.targetLocalPosition.x, 0f, point.targetLocalPosition.z),
                    new Vector3(mappedLocalPosition.x, 0f, mappedLocalPosition.z)
                );

                Handles.color = i == pointIndex ? Color.magenta : new Color(1f, 0f, 1f, 0.55f);
                Handles.CubeHandleCap(0, mappedWorldPosition, Quaternion.identity, mappedSize, EventType.Repaint);
                Handles.DrawLine(worldPosition, mappedWorldPosition);
                Handles.Label(mappedWorldPosition, $"{point.name} source\nerr {error:0.###}");
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

        private void FillBahrainSourcePositions()
        {
            if (calibration == null)
                return;

            Undo.RecordObject(calibration, "Fill Bahrain Source Positions");

            TrackCalibration.Point[] existingPoints = calibration.points ?? new TrackCalibration.Point[0];
            TrackCalibration.Point[] points = new TrackCalibration.Point[BahrainSourcePoints.Length];

            for (int i = 0; i < BahrainSourcePoints.Length; i++)
            {
                SourcePoint source = BahrainSourcePoints[i];
                TrackCalibration.Point existingPoint = FindExistingPoint(existingPoints, source.name);

                points[i] = new TrackCalibration.Point
                {
                    name = source.name,
                    sourcePosition = source.position,
                    sourceHeight = existingPoint.sourceHeight,
                    targetLocalPosition = existingPoint.targetLocalPosition
                };
            }

            calibration.circuitKey = 63;
            calibration.circuitName = "Bahrain";
            calibration.points = points;
            pointIndex = Mathf.Clamp(pointIndex, 0, points.Length - 1);

            EditorUtility.SetDirty(calibration);
            AssetDatabase.SaveAssets();
            Repaint();

            Debug.Log($"{calibration.name}: filled Bahrain source positions for {points.Length} turns.");
        }

        private static TrackCalibration.Point FindExistingPoint(TrackCalibration.Point[] points, string name)
        {
            foreach (TrackCalibration.Point point in points)
            {
                if (point.name == name)
                    return point;
            }

            return default;
        }

        private static string MakeMidPointName(string fromName, string toName)
        {
            if (string.IsNullOrWhiteSpace(fromName))
                fromName = "Point";

            if (string.IsNullOrWhiteSpace(toName))
                toName = "Next";

            return $"{fromName}-{toName} Mid";
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

        private readonly struct SourcePoint
        {
            public readonly string name;
            public readonly Vector2 position;

            public SourcePoint(string name, Vector2 position)
            {
                this.name = name;
                this.position = position;
            }
        }
    }
}
