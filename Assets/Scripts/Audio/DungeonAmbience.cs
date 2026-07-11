using UnityEngine;

/// <summary>
/// Plays a looping ambient track for the LOCAL player only while they are inside the
/// dungeon (driven by <see cref="PlayerManager.isInsideDungeon"/> — the same synced flag
/// the enemies use). Fades in on entry and out on exit via the attached
/// <see cref="CrossfadeLoopPlayer"/>, so there's no snap.
///
/// Setup: put this on any scene object in Main (e.g. an "Audio" object). On the
/// CrossfadeLoopPlayer that gets added, set Spatial Blend to 0 (2D) and tune the fade
/// times/volume there. Assign the ambient clip here. You can also swap clips at
/// runtime — it crossfades.
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
            // Clip was swapped in the inspector / by code — crossfade to the new one.
            _player.PlayLoop(_ambientLoop);
        }
    }
}
