using UnityEditor;
using UnityEngine;

namespace Tools
{
    [CustomEditor(typeof(CameraPathRecorder))]
    public class CameraPathRecorderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var recorder = (CameraPathRecorder)target;

            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox(
                "1. Fly the Scene view to frame the next shot.\n" +
                "2. GameObject > Align With View (Ctrl+Shift+F) to snap the target to it.\n" +
                "3. Capture Waypoint Here.\n" +
                "4. Repeat, then Copy Waypoints As Code and paste into your script.",
                MessageType.Info);

            EditorGUILayout.Space(6);

            if (GUILayout.Button("Capture Waypoint Here", GUILayout.Height(30)))
            {
                Undo.RecordObject(recorder, "Capture Waypoint");
                recorder.CaptureWaypoint();
                EditorUtility.SetDirty(recorder);
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Remove Last"))
            {
                Undo.RecordObject(recorder, "Remove Last Waypoint");
                recorder.RemoveLastWaypoint();
                EditorUtility.SetDirty(recorder);
            }

            if (GUILayout.Button("Clear All"))
            {
                if (EditorUtility.DisplayDialog("Clear All Waypoints",
                        "Remove every captured waypoint?", "Clear", "Cancel"))
                {
                    Undo.RecordObject(recorder, "Clear Waypoints");
                    recorder.ClearAllWaypoints();
                    EditorUtility.SetDirty(recorder);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            using (new EditorGUI.DisabledScope(recorder.waypoints.Count == 0))
            {
                if (GUILayout.Button($"Copy Waypoints As Code ({recorder.waypoints.Count})", GUILayout.Height(28)))
                {
                    EditorGUIUtility.systemCopyBuffer = recorder.BuildCodeSnippet();
                    Debug.Log($"[CameraPathRecorder] Copied {recorder.waypoints.Count} waypoints to the clipboard - paste them straight into a Waypoint[] array.");
                }
            }
        }
    }
}
