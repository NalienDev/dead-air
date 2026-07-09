using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExplodableMonitor : MonoBehaviour
{
    [SerializeField]
    private GameObject screenExplosionParticleSystem;
    [SerializeField]
    private GameObject screenOff;
    [SerializeField]
    private GameObject screenOn;
    [SerializeField]
    private GameObject shards;
    private bool broken;

    private Transform[] _shardTransforms;
    private Vector3[] _shardLocalPositions;
    private Quaternion[] _shardLocalRotations;

    // Start is called before the first frame update
    void Start()
    {
        // Cache the shards' original pose so Repair() can put them back
        // exactly where they started, instead of wherever physics left them.
        Rigidbody[] shardRBs = shards.GetComponentsInChildren<Rigidbody>(true);
        _shardTransforms = new Transform[shardRBs.Length];
        _shardLocalPositions = new Vector3[shardRBs.Length];
        _shardLocalRotations = new Quaternion[shardRBs.Length];

        for (int i = 0; i < shardRBs.Length; i++)
        {
            _shardTransforms[i] = shardRBs[i].transform;
            _shardLocalPositions[i] = shardRBs[i].transform.localPosition;
            _shardLocalRotations[i] = shardRBs[i].transform.localRotation;
        }
    }

    void OnCollisionEnter(Collision col){
        if ((col.gameObject.tag == "bullet") && (!broken))
        {
            broken = true;
            screenOff.SetActive(false);
            screenOn.SetActive(false);
            shards.SetActive(true);
            Rigidbody[] shardRBs = GetComponentsInChildren<Rigidbody>();
            screenExplosionParticleSystem.SetActive(true);
            foreach (Rigidbody shardRB in shardRBs){
                float randomForce = Random.Range(1,5);
                float randomRotationX = Random.Range(-20,20);
                float randomRotationY = Random.Range(-20,20);
                float randomRotationZ = Random.Range(-20,20);
                shardRB.transform.Rotate(randomRotationX,randomRotationY,randomRotationZ);
                shardRB.AddRelativeForce(Vector3.forward * randomForce,ForceMode.Impulse);
            }
        }

    }

    /// <summary>
    /// Puts the monitor back to its intact state (screenOn showing, shards
    /// hidden and reset to their original pose) so it can shatter again the
    /// next time a "bullet" hits it. Safe to call even if it never broke.
    /// </summary>
    public void Repair()
    {
        broken = false;

        if (screenExplosionParticleSystem != null)
            screenExplosionParticleSystem.SetActive(false);

        if (shards != null)
            shards.SetActive(false);

        if (screenOff != null)
            screenOff.SetActive(false);

        if (screenOn != null)
            screenOn.SetActive(true);

        if (_shardTransforms != null)
        {
            for (int i = 0; i < _shardTransforms.Length; i++)
            {
                if (_shardTransforms[i] == null) continue;

                _shardTransforms[i].localPosition = _shardLocalPositions[i];
                _shardTransforms[i].localRotation = _shardLocalRotations[i];

                Rigidbody rb = _shardTransforms[i].GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }
    }
}
