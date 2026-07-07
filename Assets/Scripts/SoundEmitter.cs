using UnityEngine;
using PurrNet;

public class SoundEmitter : NetworkBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private float soundRadius = 10f;

    [Tooltip("How loud this world sound is to the blind Conductor (0..1). " +
             "0 = the Conductor cannot hear it.")]
    [SerializeField, Range(0f, 1f)] private float _conductorLoudness = 0f;

    private bool _wasPlaying = false;

    private void Update()
    {
        if (!isServer) return;

        // Debug — remove antes de entregar
        if (Input.GetKeyDown(KeyCode.T)) audioSource.Play();

        bool isPlaying = audioSource.isPlaying;
        // Só dispara uma vez na transição false → true
        if (isPlaying && !_wasPlaying)
            OnSoundStarted();

        _wasPlaying = isPlaying;
    }

    private void OnSoundStarted()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, soundRadius);

        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<ISoundListener>(out var listener))
                listener.OnHearSound(transform.position);
        }

        // Also feed the blind Conductor, if this emitter is meant to be audible to it.
        if (_conductorLoudness > 0f)
            NoiseEvents.Report(transform.position, _conductorLoudness);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, soundRadius);
    }
}