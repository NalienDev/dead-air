using UnityEngine;

public class EnergyCellTrigger : MonoBehaviour
{
    private MeshRenderer _meshRenderer;
    private Collider _collider;
    [SerializeField] private Transform _shieldTransform;

    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        _collider = GetComponent<Collider>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<GrabbableObject>() != null)
        {
            other.GetComponent<GrabbableObject>().ForceDrop();
            Destroy(other.gameObject);
            _meshRenderer.enabled = true;
            _collider.isTrigger = false;

            _shieldTransform.localScale += new Vector3(10, 10, 10);
        }
    }
}
