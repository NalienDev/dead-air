using UnityEngine;

/// <summary>
/// A connection point between dungeon parts that tracks whether it is already occupied.
/// </summary>
public class EntryPoint : MonoBehaviour
{
    private bool _isOccupied = false;

    public void SetOccupied(bool value = true) => _isOccupied = value;
    public bool IsOccupied() => _isOccupied;
}
