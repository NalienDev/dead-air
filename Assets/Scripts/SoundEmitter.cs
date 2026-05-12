using UnityEngine;
using PurrNet;

public class SoundEmitter : NetworkBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private float soundRadius = 10f;

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
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, soundRadius);
    }
}