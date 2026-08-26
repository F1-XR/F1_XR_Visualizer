using F1XR.Debugging;
using UnityEditor;
using UnityEngine;

namespace F1XR.EditorTools
{
    [CustomEditor(typeof(SessionSpaceDebugger))]
    public sealed class SessionSpaceDebuggerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            var debugger = (SessionSpaceDebugger)target;
            if (Application.isPlaying)
            {
                if (GUILayout.Button("Enter VR Drone Now (Skip Placement)"))
                    debugger.EnterVrDroneWithoutPlacement();

                if (GUILayout.Button("Load DrivingTest"))
                    debugger.LoadDrivingTest();
            }
            else
            {
                if (GUILayout.Button("Enable VR Drone Bypass Start"))
                {
                    Undo.RecordObject(debugger, "Enable VR Drone Bypass Start");
                    debugger.EnableVrDroneBypassStart();
                    EditorUtility.SetDirty(debugger);
                }

                EditorGUILayout.HelpBox(
                    "Skip Spatial Setup On Play와 Enter Vr Drone On Play를 함께 체크하면 다음 Play에서 평면 인식 없이 드론 모드로 진입합니다.",
                    MessageType.Info);
            }
        }
    }

}
