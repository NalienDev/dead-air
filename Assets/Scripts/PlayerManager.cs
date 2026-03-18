using PurrNet;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public SyncVar<int> maxHealth = new(100);
    public SyncVar<int> currentHealth = new(100);

    public SyncVar<int> maxOxygen = new(360);
    public SyncVar<int> currentOxygen = new(360);
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.D))
        {
            Damage(10);
        }

        // Oxigénio baixa 1 por segundo
        /**
        currentOxygen -= Time.deltaTime;
        currentOxygen = Mathf.Clamp(currentOxygen, 0, maxOxygen);

        if (Input.GetKeyDown(KeyCode.F))
        {
            currentOxygen += 5;
            currentOxygen = Mathf.Clamp(currentOxygen, 0, maxOxygen);
        }
        */
    }

    public void Damage(int damage)
    {
        currentHealth.value -= damage;
        if (currentHealth.value <= 0)
            currentHealth.value = 0;
        EventManager.OnPlayerHit(currentHealth.value);
    }
}