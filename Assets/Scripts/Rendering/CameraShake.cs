using UnityEngine;

/// <summary>
/// Trauma-based camera shake. Attach to a dedicated "shake rig" node that sits
/// BETWEEN the camera follow target and the Camera itself, and whose local pose is
/// (0,0,0)/identity — this component drives that local pose, so it never fights the
/// look/follow rig above it.
///
/// The blind Conductor calls <see cref="ShakeFromWorld"/> whenever it takes a heavy
/// step; the closer its footstep lands to the local player, the harder the screen
/// rattles. Trauma is squared before it becomes motion, so light taps stay subtle
/// while a nearby stomp is violent, and it always decays smoothly back to still.
/// </summary>
[DisallowMultipleComponent]
public class CameraShake : MonoBehaviour
{
    [Header("Shake Shape")]
    [Tooltip("Max positional offset (metres) at full trauma.")]
    [SerializeField] private float _maxOffset = 0.15f;
    [Tooltip("Max rotational kick (degrees) at full trauma.")]
    [SerializeField] private float _maxRoll = 2.5f;
    [Tooltip("How fast the Perlin noise scrolls — higher = jitterier.")]
    [SerializeField] private float _frequency = 22f;
    [Tooltip("Trauma lost per second. Higher = shorter shakes.")]
    [SerializeField, Range(0.1f, 5f)] private float _recovery = 1.6f;

    private float _trauma;
    private float _seed;

    // The local player's shake rig registers itself here so world-space callers can
    // reach the one camera that actually belongs to this client.
    private static CameraShake s_local;

    private Camera _camera;

    private void Awake()
    {
        _seed = Random.value * 100f;
        _camera = GetComponentInChildren<Camera>();
    }

    private void OnDisable()
    {
        if (s_local == this) s_local = null;
    }

    private void LateUpdate()
    {
        // Only the client's active (main) camera counts as "local". Remote players'
        // camera rigs exist in the scene but are not Camera.main.
        if (_camera != null && _camera == Camera.main)
            s_local = this;
        else if (s_local == this)
            s_local = null;

        if (_trauma <= 0f)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            return;
        }

        float shake = _trauma * _trauma;
        float t = Time.time * _frequency;

        float offX = (Mathf.PerlinNoise(_seed, t) * 2f - 1f) * _maxOffset * shake;
        float offY = (Mathf.PerlinNoise(_seed + 1f, t) * 2f - 1f) * _maxOffset * shake;
        float roll = (Mathf.PerlinNoise(_seed + 2f, t) * 2f - 1f) * _maxRoll * shake;

        transform.localPosition = new Vector3(offX, offY, 0f);
        transform.localRotation = Quaternion.Euler(0f, 0f, roll);

        _trauma = Mathf.Clamp01(_trauma - _recovery * Time.deltaTime);
    }

    /// <summary>Add trauma directly to this rig (0..1, accumulates and clamps).</summary>
    public void AddTrauma(float amount) => _trauma = Mathf.Clamp01(_trauma + amount);

    /// <summary>
    /// Shake the local player's camera based on a world-space event. Intensity falls
    /// off linearly to zero at <paramref name="radius"/>. Safe to call on every
    /// client — only the client whose camera is nearby feels anything.
    /// </summary>
    public static void ShakeFromWorld(Vector3 worldPos, float intensity, float radius)
    {
        if (s_local == null || radius <= 0f) return;

        float dist = Vector3.Distance(s_local.transform.position, worldPos);
        float falloff = 1f - Mathf.Clamp01(dist / radius);
        if (falloff <= 0f) return;

        s_local.AddTrauma(intensity * falloff);
    }
}
