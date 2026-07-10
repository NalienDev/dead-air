using PurrNet;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// First-person volumetric flashlight. The owner toggles it with F; the on/off
/// state and the look pitch are synced so teammates see the beam too.
///
/// The light + beam live on a mount built at runtime under the player root (which
/// stays active for everyone), aimed from the owner's camera. Yaw comes free from
/// the player transform (already networked); only pitch is synced.
///
/// Assign PlayerCameraRoot to _cameraRoot and a material using Custom/FlashlightBeam
/// to _beamMaterial.
/// </summary>
public class PlayerFlashlight : NetworkBehaviour
{
    [Header("Aim")]
    [SerializeField] private Transform _cameraRoot;          // PlayerCameraRoot
    [SerializeField] private Vector3 _localPosition = new Vector3(0f, 1.5f, 0.15f);

    [Header("Light")]
    [SerializeField] private Color _color = new Color(1f, 0.96f, 0.85f);
    [SerializeField] private float _range = 30f;
    [SerializeField] private float _spotAngle = 50f;
    [SerializeField] private float _intensity = 60f;
    [SerializeField] private bool _shadows = true;
    [SerializeField] private Texture _cookie;

    [Header("Beam")]
    [SerializeField] private Material _beamMaterial;         // Custom/FlashlightBeam
    [SerializeField, Range(6, 48)] private int _beamSegments = 24;
    [SerializeField, Range(0.2f, 1f)] private float _beamLengthScale = 0.9f;

    [Header("Input")]
    [SerializeField] private KeyCode _toggleKey = KeyCode.F;

    private SyncVar<bool> _isOn = new SyncVar<bool>(false, ownerAuth: true);
    private SyncVar<float> _pitch = new SyncVar<float>(0f, 0.05f, ownerAuth: true);

    private Transform _mount;
    private GameObject _rig;

    /// <summary>True while this player's flashlight is switched on (synced).</summary>
    public bool IsOn => _isOn.value;

    /// <summary>
    /// True if the flashlight is on and <paramref name="worldPoint"/> falls inside
    /// the beam cone. Range is checked unless <paramref name="ignoreRange"/> is set
    /// — the beam is angular, so it can still "hit" a distant point it's aimed at.
    /// Works on the server too — the mount follows the synced pitch there.
    /// Occlusion is the caller's problem.
    /// </summary>
    public bool IsIlluminating(Vector3 worldPoint, bool ignoreRange = false)
    {
        if (!_isOn.value || _mount == null) return false;

        Vector3 toPoint = worldPoint - _mount.position;
        if (!ignoreRange && toPoint.sqrMagnitude > _range * _range) return false;

        return Vector3.Angle(_mount.forward, toPoint) <= _spotAngle * 0.5f;
    }

    private void Awake() => BuildRig();

    protected override void OnSpawned(bool asServer)
    {
        _isOn.onChanged += OnToggled;
        OnToggled(_isOn.value);
    }

    protected override void OnDespawned(bool asServer) => _isOn.onChanged -= OnToggled;

    private void Update()
    {
        if (!isOwner) return;

        if (Input.GetKeyDown(_toggleKey))
            _isOn.value = !_isOn.value;

        if (_cameraRoot != null)
            _pitch.value = _cameraRoot.localEulerAngles.x;
    }

    private void LateUpdate()
    {
        if (_mount == null) return;

        if (isOwner && _cameraRoot != null)
        {
            _mount.localRotation = _cameraRoot.localRotation;
        }
        else
        {
            Quaternion target = Quaternion.Euler(_pitch.value, 0f, 0f);
            _mount.localRotation = Quaternion.Slerp(_mount.localRotation, target, 1f - Mathf.Exp(-15f * Time.deltaTime));
        }
    }

    private void OnToggled(bool on)
    {
        if (_rig != null) _rig.SetActive(on);
    }

    // ── Rig ────────────────────────────────────────────────────────────────

    private void BuildRig()
    {
        _mount = new GameObject("FlashlightMount").transform;
        _mount.SetParent(transform, false);
        _mount.localPosition = _localPosition;

        _rig = new GameObject("FlashlightRig");
        _rig.transform.SetParent(_mount, false);

        Light light = _rig.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = _color;
        light.range = _range;
        light.spotAngle = _spotAngle;
        light.innerSpotAngle = _spotAngle * 0.6f;
        light.intensity = _intensity;
        light.shadows = _shadows ? LightShadows.Soft : LightShadows.None;
        light.cookie = _cookie;

        if (_beamMaterial != null)
            BuildBeam();

        _rig.SetActive(false);
    }

    private void BuildBeam()
    {
        float length = _range * _beamLengthScale;
        float radius = length * Mathf.Tan(_spotAngle * 0.5f * Mathf.Deg2Rad);

        var beam = new GameObject("Beam");
        beam.transform.SetParent(_rig.transform, false);

        MeshFilter mf = beam.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildCone(length, radius, _beamSegments);

        MeshRenderer mr = beam.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        Material mat = new Material(_beamMaterial);
        mat.SetFloat("_ConeLength", length);
        mr.sharedMaterial = mat;
    }

    // Open cone, apex at origin, opening toward +Z.
    private static Mesh BuildCone(float length, float radius, int segments)
    {
        var verts = new Vector3[segments + 1];
        var normals = new Vector3[segments + 1];
        var uvs = new Vector2[segments + 1];

        verts[0] = Vector3.zero;
        normals[0] = Vector3.forward;
        uvs[0] = new Vector2(0.5f, 0f);

        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i + 1] = new Vector3(c * radius, s * radius, length);
            normals[i + 1] = new Vector3(c, s, 0f).normalized;
            uvs[i + 1] = new Vector2((float)i / segments, 1f);
        }

        var tris = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % segments + 1;
        }

        var mesh = new Mesh { name = "FlashlightCone" };
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}
