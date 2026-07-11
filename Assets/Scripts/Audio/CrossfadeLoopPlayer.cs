using UnityEngine;

/// <summary>
/// Plays looping audio with smooth fades and crossfades between loops using two internal sources.
/// </summary>
public class CrossfadeLoopPlayer : MonoBehaviour
{
    [Header("Fades (seconds)")]
    [Tooltip("Fade-in when a loop starts from silence.")]
    [SerializeField, Min(0f)] private float _fadeInSeconds = 0.2f;
    [Tooltip("Fade-out when the loop stops.")]
    [SerializeField, Min(0f)] private float _fadeOutSeconds = 0.3f;
    [Tooltip("Crossfade length when switching between two loops.")]
    [SerializeField, Min(0f)] private float _crossfadeSeconds = 0.6f;

    [Header("Source Settings")]
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;
    [Tooltip("0 = 2D, 1 = fully 3D positional.")]
    [SerializeField, Range(0f, 1f)] private float _spatialBlend = 1f;

    private readonly AudioSource[] _sources = new AudioSource[2];
    private readonly float[] _targetVolume = new float[2];
    private readonly float[] _fadeRate = new float[2]; // volume units per second
    private int _active = -1;

    private void Awake()
    {
        for (int i = 0; i < 2; i++)
        {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = _spatialBlend;
            src.volume = 0f;
            _sources[i] = src;
        }
    }

    // Current loop clip, or null when silent or stopping.
    public AudioClip CurrentClip =>
        _active >= 0 && _targetVolume[_active] > 0f ? _sources[_active].clip : null;

    // Starts or crossfades to a loop; passing null fades everything out.
    public void PlayLoop(AudioClip clip)
    {
        if (clip == null) { StopLoop(); return; }

        // Already playing this clip and not fading out: nothing to do.
        if (_active >= 0 && _sources[_active].clip == clip && _targetVolume[_active] > 0f)
            return;

        bool somethingAudible = _active >= 0 && _sources[_active].isPlaying
                                             && _sources[_active].volume > 0.001f;
        float seconds = somethingAudible ? _crossfadeSeconds : _fadeInSeconds;

        int next = _active >= 0 ? 1 - _active
                 : !_sources[0].isPlaying ? 0
                 : !_sources[1].isPlaying ? 1 : 0;

        // Fade everything else down, fade the new loop up.
        for (int i = 0; i < 2; i++)
            if (i != next && _sources[i].isPlaying)
                FadeTo(i, 0f, seconds);

        _sources[next].clip = clip;
        if (!_sources[next].isPlaying) _sources[next].Play();
        FadeTo(next, _volume, seconds);

        _active = next;
    }

    public void StopLoop()
    {
        for (int i = 0; i < 2; i++)
            if (_sources[i].isPlaying)
                FadeTo(i, 0f, _fadeOutSeconds);
        _active = -1;
    }

    private void FadeTo(int index, float target, float seconds)
    {
        _targetVolume[index] = target;
        float diff = Mathf.Abs(_sources[index].volume - target);
        _fadeRate[index] = seconds <= 0f ? float.MaxValue
                                         : Mathf.Max(0.0001f, diff) / seconds;
    }

    private void Update()
    {
        for (int i = 0; i < 2; i++)
        {
            AudioSource src = _sources[i];
            if (!src.isPlaying) continue;

            src.volume = Mathf.MoveTowards(src.volume, _targetVolume[i], _fadeRate[i] * Time.deltaTime);

            // Fully faded out: stop the source.
            if (_targetVolume[i] <= 0f && src.volume <= 0.0001f)
            {
                src.Stop();
                src.volume = 0f;
            }
        }
    }
}
