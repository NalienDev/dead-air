#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Tools
{
    /// <summary>
    /// Editor-only helper that captures camera waypoints and exports them as a C# array.
    /// </summary>
    public class CameraPathRecorder : MonoBehaviour
    {
        [System.Serializable]
        public struct RecordedWaypoint
        {
            public Vector3 position;
            public Vector3 rotation;
        }

        [Tooltip("Transform to read from when capturing. Defaults to this GameObject's Transform.")]
        public Transform target;

        [Tooltip("Captured waypoints, in order.")]
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

        // Formats the captured waypoints as a C# Waypoint array literal.
        public string BuildCodeSnippet()
        {
            // InvariantCulture so a locale using ',' as the decimal separator doesn't emit
            // "1,23f" instead of "1.23f", which would break compilation.
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
