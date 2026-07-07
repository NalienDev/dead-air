using UnityEngine;

/// <summary>
/// Carves a fog-free bubble into Custom/VolumetricFog, with a thick glowing wall
/// at its boundary. Put this on an empty GameObject and move/scale it to shape the
/// clearing — its transform IS the volume (scale = size, rotation orients the box).
///
/// Drives the fog shader through global properties, so the fog material needs no
/// wiring. Only one zone is active at a time (last one enabled wins).
/// </summary>
[ExecuteAlways]
public class FogClearingZone : MonoBehaviour
{
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
    private void OnEnable() => Apply();
    private void OnDisable() => Shader.SetGlobalFloat(Enabled, 0f);

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
