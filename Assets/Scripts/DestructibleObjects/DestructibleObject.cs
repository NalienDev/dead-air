using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PurrNet;

public class DestructibleObject : NetworkBehaviour
{
    [System.Serializable]
    struct DebrisPrefab{
        public string name;
        public GameObject prefab;
    }

    [Header("Debris")]
    [SerializeField] private DebrisPrefab debrisPrefab;
    [SerializeField] private float forceRequired;
    [SerializeField] private Material ditherMaterial;

    [Header("Despawning")]
    [SerializeField, Range(0, 100)] private int despawnPercentage;
    [SerializeField] private float despawnTime;

    [Header("Audio")]
    [SerializeField] private List<AudioClip> audioClips = new List<AudioClip>();
    [SerializeField, Range(0f, 1f)] private float volume;
    [SerializeField, Range(0f, 0.2f)] private float volumeVariation;
    [SerializeField, Range(0f, 0.5f)] private float pitchVariation;

    private GameObject debris;
    private new Rigidbody rigidbody;

    public void Break(){
        float velocityMagnitude = rigidbody.linearVelocity.magnitude;
        debris.transform.position = transform.position;
        debris.transform.rotation = transform.rotation;
        debris.transform.localScale = transform.localScale;
        debris.SetActive(true);

        for(int i = 0; i < debris.transform.childCount; i++){
            Rigidbody debrisRigidbody = debris.transform.GetChild(i).GetComponent<Rigidbody>();
            Vector3 randomise = new Vector3(Random.Range(0f, velocityMagnitude), Random.Range(0f, velocityMagnitude), Random.Range(0f, velocityMagnitude)) / 2;
            debrisRigidbody.linearVelocity = rigidbody.linearVelocity + randomise;
        }

        debris.GetComponent<Despawn>().SetVariables(despawnPercentage, despawnTime, ditherMaterial, audioClips[Random.Range(0, audioClips.Count)], volume, volumeVariation, pitchVariation);
        Destroy(gameObject);
    }

    void Start(){
        rigidbody = GetComponent<Rigidbody>();
        debris = Instantiate(debrisPrefab.prefab, transform.position, Quaternion.identity);
        debris.SetActive(false);
    }

    void OnCollisionEnter(Collision collision){
        if(collision.relativeVelocity.magnitude > forceRequired){
            Break();
        }
    }
}