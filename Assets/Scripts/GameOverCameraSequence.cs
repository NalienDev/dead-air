using System.Collections;
using UnityEngine;

/// <summary>
/// Lives in the GameOver scene, alongside GameOverHandler. On Start, snaps the
/// camera to the first waypoint and then eases through the rest of them in
/// order, slowly and smoothly, ending on (and staying at) the last one - a
/// slow flythrough of the burning city, finishing on a wide top-down shot.
///
/// Waypoints are hardcoded position/rotation values (captured directly from
/// the Scene view while framing each shot), not Transform references -
/// nothing to wire up in the Inspector, just tweak the numbers here if a shot
/// needs adjusting.
/// </summary>
public class GameOverCameraSequence : MonoBehaviour
{
    [System.Serializable]
    private struct Waypoint
    {
        public Vector3 position;
        public Vector3 rotation;

        public Waypoint(Vector3 position, Vector3 rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }
    }

    [Header("Camera")]
    [Tooltip("Camera that gets moved. Defaults to Camera.main if left empty.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Timing")]
    [Tooltip("Average seconds per pair of waypoints - total flythrough time is this times (waypoint count - 1).")]
    [SerializeField] private float segmentDuration = 4f;
    [Tooltip("Pause before the very first move starts.")]
    [SerializeField] private float startDelay = 0f;
    [Tooltip("Eases speed in/out only at the very start and end of the WHOLE path - not per waypoint, so the camera never stops at the points in between, just flows through them.")]
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Waypoints (in order)")]
    [SerializeField]
    private Waypoint[] waypoints =
    {
        new Waypoint(new Vector3(-60.39466f, 2.304298f, -28.82319f), new Vector3(353.5735f, 142.4696f, 2.845287f)),
        new Waypoint(new Vector3(-58.35f, 2.68f, -31.49f),           new Vector3(352.9792f, 164.2364f, 0.266113f)),
        new Waypoint(new Vector3(-57.17f, 3.22f, -35.69f),           new Vector3(352.9792f, 164.2364f, 0.266113f)),
        new Waypoint(new Vector3(-52.6f, 3.6f, -38.43f),             new Vector3(16.45725f, 97.08026f, 1.428814f)),
        new Waypoint(new Vector3(-47f, 1.9f, -40.34f),               new Vector3(16.45725f, 97.08026f, 1.428814f)),
        new Waypoint(new Vector3(-34.28f, 1.79f, -40.39f),           new Vector3(16.45725f, 97.08026f, 1.428814f)),
        new Waypoint(new Vector3(-23.69f, 2.13f, -40.22f),           new Vector3(16.45725f, 97.08026f, 1.428814f)),
        new Waypoint(new Vector3(-13.85f, 2.35f, -40.31f),           new Vector3(16.45725f, 97.08026f, 1.428814f)),
        new Waypoint(new Vector3(-0.64f, 3.55f, -40.2f),             new Vector3(16.45725f, 97.08026f, 2.672749f)),
        new Waypoint(new Vector3(7.72f, 3.38f, -40.19f),             new Vector3(16.45725f, 97.08026f, 2.672749f)),
        new Waypoint(new Vector3(14.44f, 5.07f, -40.06f),            new Vector3(16.45725f, 97.08026f, 2.672749f)),
        new Waypoint(new Vector3(21.14f, 9.41f, -39.55f),            new Vector3(32.84606f, 265.6413f, 0.919199f)),
        new Waypoint(new Vector3(39.6f, 21.36f, -38.14f),            new Vector3(32.84606f, 265.6413f, 0.919199f)),
    };

    private void Awake()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void Start()
    {
        if (cameraTransform == null || waypoints == null || waypoints.Length == 0) return;

        StartCoroutine(PlaySequence());
    }

    private IEnumerator PlaySequence()
    {
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        if (waypoints.Length == 1)
        {
            SetCameraTo(waypoints[0].position, waypoints[0].rotation);
            yield break;
        }

        SetCameraTo(waypoints[0].position, waypoints[0].rotation);

        int segments = waypoints.Length - 1;
        float totalDuration = segments * segmentDuration;
        int lastIndex = waypoints.Length - 1;

        float t = 0f;
        while (t < totalDuration)
        {
            t += Time.deltaTime;

            // moveCurve only shapes the very start/end of the ENTIRE path -
            // everything in between moves at a steady pace, so there's no
            // stop-start at each waypoint like separate per-segment lerps
            // would produce.
            float eased = moveCurve.Evaluate(Mathf.Clamp01(t / totalDuration));
            float scaled = eased * segments;

            int segIndex = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, segments - 1);
            float localT = scaled - segIndex;

            // Catmull-Rom needs one point before and one after the current
            // segment's pair - clamp at the ends so the path doesn't try to
            // read past the first/last waypoint.
            Vector3 p0 = waypoints[Mathf.Max(segIndex - 1, 0)].position;
            Vector3 p1 = waypoints[segIndex].position;
            Vector3 p2 = waypoints[Mathf.Min(segIndex + 1, lastIndex)].position;
            Vector3 p3 = waypoints[Mathf.Min(segIndex + 2, lastIndex)].position;

            cameraTransform.position = CatmullRom(p0, p1, p2, p3, localT);

            Quaternion r1 = Quaternion.Euler(waypoints[segIndex].rotation);
            Quaternion r2 = Quaternion.Euler(waypoints[Mathf.Min(segIndex + 1, lastIndex)].rotation);
            cameraTransform.rotation = Quaternion.Slerp(r1, r2, localT);

            yield return null;
        }

        // Land exactly on the last waypoint (city from above) and stay -
        // GameOverHandler takes over the timing for when the scene actually
        // resets/reloads.
        SetCameraTo(waypoints[lastIndex].position, waypoints[lastIndex].rotation);
    }

    // Standard uniform Catmull-Rom spline segment - passes exactly through
    // p1 (t=0) and p2 (t=1), curving smoothly based on the neighboring
    // points p0/p3, with continuous velocity across segment boundaries.
    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private void SetCameraTo(Vector3 pos, Vector3 euler)
    {
        cameraTransform.position = pos;
        cameraTransform.rotation = Quaternion.Euler(euler);
    }
}
