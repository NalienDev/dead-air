using UnityEngine;

/// <summary>
/// Marks a connection point between two dungeon parts.
/// Tracks whether this point is already connected to another part.
/// </summary>
public class EntryPoint : MonoBehaviour
{
    private bool _isOccupied = false;

    public void SetOccupied(bool value = true) => _isOccupied = value;
    public bool IsOccupied() => _isOccupied;
}
