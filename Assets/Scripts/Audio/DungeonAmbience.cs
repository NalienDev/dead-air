using UnityEngine;

/// <summary>
/// Fades a looping ambient track in and out for the local player while they are inside the dungeon.
/// </summary>
[RequireComponent(typeof(CrossfadeLoopPlayer))]
public class DungeonAmbience : MonoBehaviour
{
    [Tooltip("Looping ambient played while the local player is inside the dungeon.")]
    [SerializeField] private AudioClip _ambientLoop;

    private CrossfadeLoopPlayer _player;
    private bool _playing;

    private void Awake() => _player = GetComponent<CrossfadeLoopPlayer>();

    private void Update()
    {
        bool inDungeon = PlayerManager.Local != null && PlayerManager.Local.IsInsideDungeon();

        if (inDungeon && !_playing)
        {
            _player.PlayLoop(_ambientLoop);
            _playing = true;
        }
        else if (!inDungeon && _playing)
        {
            _player.StopLoop();
            _playing = false;
        }
        else if (inDungeon && _playing && _player.CurrentClip != _ambientLoop)
        {
            // Clip was swapped, so crossfade to the new one.
            _player.PlayLoop(_ambientLoop);
        }
    }
}
