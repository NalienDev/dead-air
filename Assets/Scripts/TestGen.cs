using UnityEngine;

/// <summary>
/// Kicks off dungeon generation on Start for testing.
/// </summary>
public class TestGen : MonoBehaviour
{
    void Start()
    {
        GetComponent<DungeonGenerator>().StartGeneration();
    }

}
