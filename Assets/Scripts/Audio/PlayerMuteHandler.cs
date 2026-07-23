using Dissonance;
using UnityEngine;

public class PlayerMuteHandler : MonoBehaviour
{
    private bool _isMuted;
    private DissonanceComms _dissonanceComms;

    private void Start()
    {
        _isMuted = false;
        _dissonanceComms = FindFirstObjectByType<DissonanceComms>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.M))
        {
            _isMuted = !_isMuted;
            ToggleMute(_isMuted);
        }

    }

    private void ToggleMute(bool mute)
    {
        _dissonanceComms.IsMuted = mute;
    }
}
