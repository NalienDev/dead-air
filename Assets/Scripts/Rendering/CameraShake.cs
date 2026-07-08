using System.Collections.Generic;
using Cinemachine;
using UnityEngine;

/// <summary>
/// Trauma-based camera shake driven through Cinemachine's Perlin noise.
///
/// It drives the Amplitude/Frequency gains on EVERY vcam's
/// <see cref="CinemachineBasicMultiChannelPerlin"/> — only the live one actually
/// moves the camera, so this sidesteps having to figure out which vcam is active
/// (which is what made the previous versions silently do nothing).
///
/// SETUP (once, on the PlayerFollowCamera vcam in the player prefab):
///   Inspector → Noise → "Basic Multi Channel Perlin" → Noise Profile → "6D Shake".
///   Leave Amplitude/Frequency Gain at 0 — this script owns them.
///
/// No scene object required (it self-hosts a singleton). Put it on one persistent
/// object only if you want to tune the fields in the Inspector.
/// </summary>
public class CameraShake : MonoBehaviour
{
    [Tooltip("Perlin amplitude gain at full trauma.")]
    [SerializeField] private float _maxAmplitude = 2f;
    [Tooltip("Perlin frequency gain at full trauma.")]
    [SerializeField] private float _maxFrequency = 2f;
    [Tooltip("Trauma lost per second. Higher = shorter shakes.")]
    [SerializeField] private float _recovery = 1.4f;

    private static CameraShake _instance;

    private float _trauma;
    private readonly List<CinemachineBasicMultiChannelPerlin> _perlins = new();
    private bool _warned;

    private static CameraShake Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("[CameraShake]");
                _instance = go.AddComponent<CameraShake>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    /// <summary>Adds trauma (0..1) directly.</summary>
    public static void Shake(float intensity01) => Instance.AddTrauma(intensity01);

    /// <summary>
    /// Adds trauma from a world-space event: full at the event's position, fading to
    /// nothing at <paramref name="radius"/> from the local player's camera.
    /// </summary>
    public static void ShakeFromWorld(Vector3 worldPos, float intensity01, float radius)
    {
        Camera cam = Camera.main;
        if (cam == null || radius <= 0f) return;

        float dist = Vector3.Distance(cam.transform.position, worldPos);
        if (dist >= radius) return;

        Instance.AddTrauma(intensity01 * (1f - dist / radius));
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;
    }

    private void AddTrauma(float amount)
    {
        _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, amount));
        if (_perlins.Count == 0) RefreshPerlins();
    }

    private void RefreshPerlins()
    {
        _perlins.Clear();
        foreach (var p in FindObjectsByType<CinemachineBasicMultiChannelPerlin>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (p.m_NoiseProfile != null) _perlins.Add(p);
        }

        if (_perlins.Count == 0 && !_warned)
        {
            _warned = true;
            Debug.LogWarning("[CameraShake] No Cinemachine vcam has a Basic Multi Channel Perlin " +
                             "with a Noise Profile. On your PlayerFollowCamera vcam: Noise → " +
                             "Basic Multi Channel Perlin → Noise Profile '6D Shake' (leave gains at 0).");
        }
    }

    private void LateUpdate()
    {
        float shake = _trauma * _trauma;

        for (int i = _perlins.Count - 1; i >= 0; i--)
        {
            var p = _perlins[i];
            if (p == null) { _perlins.RemoveAt(i); continue; }
            p.m_AmplitudeGain = shake * _maxAmplitude;
            p.m_FrequencyGain = shake * _maxFrequency;
        }

        if (_trauma > 0f)
            _trauma = Mathf.MoveTowards(_trauma, 0f, _recovery * Time.deltaTime);
    }
}
