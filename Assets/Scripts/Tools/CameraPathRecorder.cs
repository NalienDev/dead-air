#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Tools
{
    /// <summary>
    /// Editor-only authoring helper: lets you fly the Scene view camera around
    /// by hand, snap the target Transform to match it (GameObject > Align With
    /// View, Ctrl+Shift+F), then click "Capture Waypoint" in the custom
    /// Inspector (see CameraPathRecorderEditor.cs) to record that exact
    /// position/rotation. Once you've captured a full path, click "Copy As
    /// Code" to get a ready-to-paste C# waypoint array on your clipboard - the
    /// same format GameOverCameraSequence.cs (and similar scripts) expect.
    ///
    /// Typical flow for a shot:
    ///   1. Select this object (or the camera itself, if Target is empty).
    ///   2. In the Scene view, fly/orbit to frame the next shot.
    ///   3. GameObject > Align With View (Ctrl+Shift+F) - snaps Target to match.
    ///   4. Click "Capture Waypoint Here" in the Inspector.
    ///   5. Repeat for every shot, in order.
    ///   6. Click "Copy Waypoints As Code" and paste into the target script.
    ///
    /// Wrapped entirely in #if UNITY_EDITOR - it's a level-design tool, not
    /// something that needs to exist in an actual build.
    /// </summary>
    public class CameraPathRecorder : MonoBehaviour
    {
        [System.Serializable]
        public struct RecordedWaypoint
        {
            public Vector3 position;
            public Vector3 rotation;
        }

        [Tooltip("Transform to read from when capturing. Leave empty to use this GameObject's own Transform.")]
        public Transform target;

        [Tooltip("Captured so far, in order. Editable by hand too - reorder, tweak or delete entries directly here.")]
        public List<RecordedWaypoint> waypoints = new List<RecordedWaypoint>();

        public Transform ResolvedTarget => target != null ? target : transform;

        public void CaptureWaypoint()
        {
            Transform t = ResolvedTarget;
            waypoints.Add(new RecordedWaypoint
            {
                position = t.position,
                rotation = t.eulerAngles,
            });
        }

        public void RemoveLastWaypoint()
        {
            if (waypoints.Count > 0)
                waypoints.RemoveAt(waypoints.Count - 1);
        }

        public void ClearAllWaypoints()
        {
            waypoints.Clear();
        }

        /// <summary>
        /// Formats every captured waypoint as a C# array literal matching the
        /// private Waypoint struct pattern used in scripts like
        /// GameOverCameraSequence.cs - paste straight over their waypoints array.
        /// </summary>
        public string BuildCodeSnippet()
        {
            // InvariantCulture matters here - on a system whose locale uses ','
            // as the decimal separator (e.g. Portuguese), plain ToString()
            // would emit "1,23f" instead of "1.23f", which breaks compilation
            // (the comma reads as an extra Vector3 argument).
            var sb = new StringBuilder();
            for (int i = 0; i < waypoints.Count; i++)
            {
                var w = waypoints[i];
                sb.Append("    new Waypoint(new Vector3(")
                  .Append(w.position.x.ToString("0.######", CultureInfo.InvariantCulture)).Append("f, ")
                  .Append(w.position.y.ToString("0.######", CultureInfo.InvariantCulture)).Append("f, ")
                  .Append(w.position.z.ToString("0.######", CultureInfo.InvariantCulture)).Append("f), new Vector3(")
                  .Append(w.rotation.x.ToString("0.######", CultureInfo.InvariantCulture)).Append("f, ")
                  .Append(w.rotation.y.ToString("0.######", CultureInfo.InvariantCulture)).Append("f, ")
                  .Append(w.rotation.z.ToString("0.######", CultureInfo.InvariantCulture)).Append("f)),\n");
            }
            return sb.ToString();
        }
    }
}
#endif
