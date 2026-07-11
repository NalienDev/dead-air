using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Repeatedly spawns a prefab overhead once per second.
/// </summary>
public class Spawner : MonoBehaviour
{
    public GameObject spawned;

    void Start()
    {
        StartCoroutine(Spawn());
    }

    IEnumerator Spawn(){
        while(true){
            Instantiate(spawned, new Vector3(0f, 10f, 0f), Quaternion.identity);
            yield return new WaitForSeconds(1f);
        }
    }
}
