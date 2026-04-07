using UnityEngine;

public class TestGen : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        GetComponent<DungeonGenerator>().StartGeneration();
    }

}
