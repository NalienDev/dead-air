using PurrNet;
using UnityEngine;

public class PlayerPickObjectsUp : MonoBehaviour
{
    [SerializeField] private Transform _cameraCenterTransform;
    [SerializeField] private Transform _pickupPosTransform;
    
    private PickableObject _pickedUpObject;

    private void Update()
    {
        RaycastHit hit;
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (_pickedUpObject == null)
            {
                if (Physics.Raycast(_cameraCenterTransform.position, _cameraCenterTransform.forward, out hit, 2f))
                {
                    Debug.DrawRay(_cameraCenterTransform.position, _cameraCenterTransform.forward * 10, Color.yellow, 2);
                    if (hit.transform.TryGetComponent(out _pickedUpObject))
                    {
                        NetworkTransform objNetTransform = _pickedUpObject.GetComponent<NetworkTransform>();
                        if (!objNetTransform.isOwner)
                        {
                            NetworkTransform selfNetTransform = gameObject.GetComponent<NetworkTransform>();
                            objNetTransform.GiveOwnership(selfNetTransform.localPlayer.Value);
                        }
                        _pickedUpObject.Grab(_pickupPosTransform);
                    }
                }
            } else
            {
                _pickedUpObject.Drop();
                _pickedUpObject = null;
            }
          
        }
    }
}
