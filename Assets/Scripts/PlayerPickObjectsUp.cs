using UnityEngine;

public class PlayerPickObjectsUp : MonoBehaviour
{
    [SerializeField] private Transform _cameraCenter;
    [SerializeField] private Transform _pickupPos;
    private bool _objectInHand = false;
    private GameObject _pickedUpObject;

    private void FixedUpdate()
    {
        RaycastHit hit;
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (_objectInHand)
            {
                _pickedUpObject.transform.SetParent(null, false);
                _objectInHand = false;
            } else
            {
                if (Physics.Raycast(_cameraCenter.position, _cameraCenter.forward, out hit, 100.0f))
                {
                    Debug.DrawRay(_cameraCenter.position, _cameraCenter.forward * 10, Color.yellow, 2);

                    if (hit.collider.gameObject.CompareTag("PickableObject"))
                    {
                        print("Found an object - distance: " + hit.distance);
                        _pickedUpObject = hit.collider.gameObject;
                        _pickedUpObject.transform.SetParent(_pickupPos.transform, false);
                        _pickedUpObject.transform.localPosition = Vector3.zero;
                        _objectInHand = true;

                    }
                }
            }
                
        }
        
    }
}
