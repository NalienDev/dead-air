using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Carves a breathable fog-free volume into the volumetric fog shader, its transform defining the shape.
/// </summary>
[ExecuteAlways]
public class FogClearingZone : MonoBehaviour
{
    // Active zones, for the static containment query.
    private static readonly List<FogClearingZone> s_active = new();
    public enum Shape { Sphere, Box }

    [SerializeField] private Shape _shape = Shape.Sphere;

    [Tooltip("Boundary wall thickness, as a fraction of the zone size.")]
    [SerializeField, Range(0.02f, 0.5f)] private float _edgeThickness = 0.1f;

    [Tooltip("How solid/opaque the boundary wall of fog is.")]
    [SerializeField] private float _edgeDensity = 6f;

    [ColorUsage(true, true)]
    [SerializeField] private Color _edgeColor = new Color(0.4f, 0.9f, 1f, 1f);

    private static readonly int Matrix = Shader.PropertyToID("_FogClearMatrix");
    private static readonly int Enabled = Shader.PropertyToID("_FogClearEnabled");
    private static readonly int ShapeId = Shader.PropertyToID("_FogClearShape");
    private static readonly int Edge = Shader.PropertyToID("_FogClearEdge");
    private static readonly int EdgeDensity = Shader.PropertyToID("_FogClearEdgeDensity");
    private static readonly int EdgeColor = Shader.PropertyToID("_FogClearEdgeColor");

    private void LateUpdate() => Apply();

    private void OnEnable()
    {
        Apply();
        if (!s_active.Contains(this)) s_active.Add(this);
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(Enabled, 0f);
        s_active.Remove(this);
    }

    public bool Contains(Vector3 worldPos)
    {
        // The transform is the volume, so containment is a check in local coordinates.
        Vector3 local = transform.InverseTransformPoint(worldPos);
        return _shape == Shape.Sphere
            ? local.sqrMagnitude <= 0.25f // radius 0.5 in local space
            : Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f && Mathf.Abs(local.z) <= 0.5f;
    }

    // True if the position is inside any active zone.
    public static bool ContainsPoint(Vector3 worldPos)
    {
        foreach (FogClearingZone zone in s_active)
            if (zone != null && zone.isActiveAndEnabled && zone.Contains(worldPos))
                return true;
        return false;
    }

    private void Apply()
    {
        Shader.SetGlobalMatrix(Matrix, transform.worldToLocalMatrix);
        Shader.SetGlobalFloat(Enabled, 1f);
        Shader.SetGlobalFloat(ShapeId, _shape == Shape.Box ? 1f : 0f);
        Shader.SetGlobalFloat(Edge, _edgeThickness);
        Shader.SetGlobalFloat(EdgeDensity, _edgeDensity);
        Shader.SetGlobalColor(EdgeColor, _edgeColor);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = _edgeColor;
        Gizmos.matrix = transform.localToWorldMatrix;
        if (_shape == Shape.Sphere) Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
        else Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}
