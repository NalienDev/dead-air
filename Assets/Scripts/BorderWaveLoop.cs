using UnityEngine;
using UnityEngine.UI;

public class BorderWaveLoop : MonoBehaviour
{
    [SerializeField] private Sprite[] _frames;
    [SerializeField] private float _framesPerSecond = 12f;

    private Image _image;
    private float _timer;
    private int _index;

    private void Awake() => _image = GetComponent<Image>();

    private void Update()
    {
        if (_frames == null || _frames.Length == 0) return;

        _timer += Time.deltaTime;
        float frameDuration = 1f / _framesPerSecond;

        if (_timer >= frameDuration)
        {
            _timer -= frameDuration;
            _index = (_index + 1) % _frames.Length;
            _image.sprite = _frames[_index];
        }
    }
}