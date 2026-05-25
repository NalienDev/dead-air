using PurrNet;
using UnityEngine;

public class PlayerMovement : NetworkIdentity
{
    [SerializeField] private float _speed = 3f;


    protected override void OnSpawned()
    {
        base.OnSpawned();
        enabled = isOwner;
    }

    private void Update()
    {
        Vector3 moveVector = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));

        transform.position += moveVector * (Time.deltaTime * _speed);
    }
}
