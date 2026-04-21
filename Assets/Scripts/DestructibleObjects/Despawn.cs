using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using PurrNet;

public class Despawn : NetworkBehaviour 
{
    private int despawnPercentage;
    private float despawnTime;
    private Material ditherMaterial;
    private AudioClip clip;
    private float volume;
    private float volumeVariation;
    private float pitchVariation;
    private AudioSource audioSource;

    [SerializeField] private float fadeDuration = 2f;

    public void SetVariables(int despawnPercentage, float despawnTime, Material ditherMaterial, AudioClip clip, float volume, float volumeVariation, float pitchVariation){
        this.despawnPercentage = despawnPercentage;
        this.despawnTime = despawnTime;
        this.ditherMaterial = ditherMaterial;
        this.clip = clip;
        this.volume = volume;
        this.volumeVariation = volumeVariation;
        this.pitchVariation = pitchVariation;
    }

    void Start(){
        audioSource = GetComponent<AudioSource>();
        audioSource.pitch = 1f + Random.Range(-pitchVariation/2, pitchVariation/2);
        audioSource.PlayOneShot(clip, volume + Random.Range(-volumeVariation, volumeVariation));
        StartCoroutine(DespawnCoroutine());
    }

    private IEnumerator DespawnCoroutine(){
        yield return new WaitForSeconds(despawnTime);

        int despawnCount = transform.childCount * despawnPercentage / 100;
        List<Material> instancedMats = new List<Material>();

        for(int i = transform.childCount - 1; i >= transform.childCount - despawnCount; i--){
            Transform t = transform.GetChild(i);
            Renderer r = t.GetComponent<Renderer>();
            if(r != null){
                Texture originalTexture = r.material.mainTexture;
                Material instance = new Material(ditherMaterial);
                instance.SetTexture("_BaseTexture", originalTexture);
                instance.SetColor("_BaseColor", Color.white);
                r.material = instance;
                instancedMats.Add(instance);
            }
        }

        float elapsed = 0f;
        while(elapsed < fadeDuration){
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);

            foreach(Material mat in instancedMats){
                mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            }
            yield return null;
        }

        for(int i = transform.childCount - 1; i >= transform.childCount - despawnCount; i--){
            Destroy(transform.GetChild(i).gameObject);
        }
    }
}