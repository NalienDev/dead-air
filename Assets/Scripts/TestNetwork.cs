using PurrNet;
using UnityEngine;

/// <summary>
/// Networking test object that syncs health and reacts to debug key presses.
/// </summary>
public class TestNetwork : NetworkIdentity
{
    [SerializeField] private Color _color;
    [SerializeField] private Renderer _renderer;
    [SerializeField] private TextMesh _healthText;
    [SerializeField] private SyncVar<int> _health = new(100);

    [SerializeField] private int _localHealth = 100;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            SetHealth(_localHealth - 10);
        }
        if (Input.GetKeyDown(KeyCode.S))
        {
            TakeDamage(10);
        }
    }

    private void Awake()
    {
        _health.onChanged += OnHealthChanged;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        _health.onChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int newValue)
    {
        _healthText.text = newValue.ToString();
    }

    [ServerRpc]
    private void TakeDamage(int damage)
    {
        _health.value -= damage;
    }

    [ObserversRpc(bufferLast: true)]
    private void SetHealth(int health)
    {
        _localHealth = health;
    }

    protected override void OnObserverAdded(PlayerID player)
    {
        base.OnObserverAdded(player);
    }

    // bufferLast replays the last call for clients that join after it ran.
    [ObserversRpc(bufferLast: true)]
    private void SetColor(Color color)
    {
        _renderer.material.color = color;
    }
}
