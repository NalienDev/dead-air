using UnityEngine;

public class PickableObject : MonoBehaviour
{
    private Rigidbody _rb;
    private Transform _pickupPosTransform;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public void Grab(Transform pickupPosTransform)
    {
        _pickupPosTransform = pickupPosTransform;
        _rb.useGravity = false;
        _rb.isKinematic = true;
        _rb.excludeLayers = LayerMask.GetMask("Player");
    }

    public void Drop()
    {
        _pickupPosTransform = null;
        _rb.useGravity = true;
        _rb.isKinematic = false;
        _rb.excludeLayers &= ~LayerMask.GetMask("Player");
    }

    private void FixedUpdate()
    {
        if (_pickupPosTransform != null)
        {
            float lerpSpeed = 10f;
            Vector3 newPos = Vector3.Lerp(transform.position, _pickupPosTransform.position, Time.deltaTime * lerpSpeed);
            _rb.MovePosition(newPos); 
        }
    }
}
