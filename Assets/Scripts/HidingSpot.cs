using PurrNet;
using UnityEngine;

public class HidingSpot : Interactable
{
    [Header("Refs")]
    [SerializeField] private Transform _hideAnchor;
    [SerializeField] private Transform _exitPoint; // onde ele reaparece; se null, usa a posição pré-hide
    [SerializeField] private AudioSource _audioSource; // sons de abrir/fechar — igual ao SoundEmitter

    [Header("Sounds")]
    [Tooltip("Som reproduzido em todos os clientes ao entrar no locker.")]
    [SerializeField] private AudioClip _enterSound;
    [Tooltip("Som reproduzido em todos os clientes ao sair do locker.")]
    [SerializeField] private AudioClip _exitSound;

    [Header("Noise (Conductor)")]
    [Tooltip("Loudness (0..1) do som ao entrar. Deve ficar acima do sprint mas abaixo do " +
             "chase-spike threshold do Conductor.")]
    [SerializeField, Range(0f, 1f)] private float _enterNoiseLoudness = 0.6f;
    [Tooltip("Loudness (0..1) do som ao sair. Pode ser igual ou ligeiramente menor.")]
    [SerializeField, Range(0f, 1f)] private float _exitNoiseLoudness = 0.6f;

    public PlayerManager Occupant { get; private set; }
    public bool IsOccupied => Occupant != null;

    private Vector3 _preHidePosition;

    // ── Interactable ──────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="Interactor"/> when the local player presses E while
    /// aiming at this HidingSpot. Enters the spot if free, exits if the local
    /// player is already the occupant.
    /// </summary>
    public override InteractionType OnInteract(GameObject user)
    {
        // O Interactor pode estar num child do player — GetComponentInParent
        // garante que encontramos o PlayerManager no root independentemente disso.
        PlayerManager local = user.GetComponentInParent<PlayerManager>();
        if (local == null) return InteractionType.NONE;

        if (IsOccupied)
        {
            if (Occupant == local) ServerExit(local);
        }
        else
        {
            ServerEnter(local);
        }

        return InteractionType.PRESS;
    }

    // ── Server ───────────────────────────────────────────────────────────────

    [ServerRpc(requireOwnership: false)]
    private void ServerEnter(PlayerManager player)
    {
        if (IsOccupied || player == null) return;

        Occupant = player;
        _preHidePosition = player.transform.position;

        TeleportPlayer(player, _hideAnchor.position, _hideAnchor.rotation);
        player.SetHiding(true);

        RpcPlaySound(entering: true);
        NoiseEvents.Report(transform.position, _enterNoiseLoudness);
        Debug.Log($"[HidingSpot] '{player.name}' entrou no locker.");
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerExit(PlayerManager player)
    {
        if (Occupant != player) return;

        Vector3 pos = _exitPoint != null ? _exitPoint.position : _preHidePosition;
        Quaternion rot = _exitPoint != null ? _exitPoint.rotation : player.transform.rotation;

        TeleportPlayer(player, pos, rot);
        player.SetHiding(false);

        RpcPlaySound(entering: false);
        NoiseEvents.Report(transform.position, _exitNoiseLoudness);

        Occupant = null;
        Debug.Log($"[HidingSpot] '{player.name}' saiu do locker.");
    }

    // Usado pelo Conductor quando abre o locker à força.
    public void ForceEject()
    {
        if (!isServer || Occupant == null) return;

        Vector3 pos = _exitPoint != null ? _exitPoint.position : _preHidePosition;
        TeleportPlayer(Occupant, pos, Occupant.transform.rotation);
        Occupant.SetHiding(false);

        RpcPlaySound(entering: false);
        NoiseEvents.Report(transform.position, _exitNoiseLoudness);

        Occupant = null;
    }

    // ── Audio (todos os clientes) ─────────────────────────────────────────────

    /// <summary>
    /// Reproduz o som de abrir/fechar em todos os clientes com áudio 3D espacial,
    /// exactamente como <see cref="SoundEmitter"/> faz para objectos do mundo.
    /// </summary>
    [ObserversRpc(runLocally: true)]
    private void RpcPlaySound(bool entering)
    {
        if (_audioSource == null) return;
        AudioClip clip = entering ? _enterSound : _exitSound;
        if (clip != null) _audioSource.PlayOneShot(clip);
    }

    private void TeleportPlayer(PlayerManager player, Vector3 pos, Quaternion rot)
    {
        // O CharacterController tem de estar desligado por um frame, senão ele "luta"
        // contra o teleport e a posição não cola.
        if (player.TryGetComponent(out CharacterController cc))
            cc.enabled = false;

        player.transform.SetPositionAndRotation(pos, rot);

        if (cc != null) cc.enabled = true;
    }
}