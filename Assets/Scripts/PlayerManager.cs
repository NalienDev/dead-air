using PurrNet;
using UnityEngine;

public class PlayerManager : NetworkIdentity
{
    public SyncVar<int> currentHealth = new(100);
    public SyncVar<int> maxHealth = new(100);

    public SyncVar<int> maxOxygen = new(360);
    public SyncVar<int> currentOxygen = new(360);
    public static PlayerManager Local { get; private set; }

    private float _oxygenTimer = 0f;

    protected override void OnSpawned(bool asServer)
    {
        if (isOwner)
            Local = this;
        DontDestroyOnLoad(gameObject);
    }

    public int GetCurrentHealth()
    {
        return currentHealth.value;
    }

    public int GetMaxHealth()
    {
        return maxHealth.value;
    }

    public int GetMaxOxygen()
    {
        return maxOxygen.value;
    }

    public int GetCurrentOxygen()
    {
        return currentOxygen.value;
    }

    void Update()
    {
        if (!isOwner) return;

        _oxygenTimer += Time.deltaTime;
        if (_oxygenTimer >= 1f)
        {
            _oxygenTimer = 0f;
            DrainOxygen(1);
        }

        if (Input.GetKeyDown(KeyCode.F)) Damage(10);
        if (Input.GetKeyDown(KeyCode.X)) GainOxygen(10);
    }

    [ServerRpc]
    public void DrainOxygen(int amount)
    {
        currentOxygen.value = Mathf.Clamp(currentOxygen.value - amount, 0, maxOxygen.value);
    }

    [ServerRpc]
    public void Damage(int damage)
    {
        currentHealth.value -= damage;
        if (currentHealth.value <= 0)
            currentHealth.value = 0;
    }

    [ServerRpc]
    public void GainOxygen(int oxygen)
    {
        currentOxygen.value += oxygen;
        currentOxygen.value = Mathf.Clamp(currentOxygen, 0, maxOxygen);
    }
}