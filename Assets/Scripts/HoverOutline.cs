using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local inverted-hull outline shown while the player looks at an interactable.
/// </summary>
public class HoverOutline : MonoBehaviour
{
    private static Material s_material;

    private readonly List<GameObject> _shells = new();
    private bool _built;
    private bool _visible;

    private static Material GetMaterial()
    {
        if (s_material != null) return s_material;

        // Preferred: a material asset in Resources, which also keeps the shader in builds.
        s_material = Resources.Load<Material>("InteractableOutline");
        if (s_material != null) return s_material;

        // Fallback: build one from the shader with its default properties.
        Shader shader = Shader.Find("Custom/InteractableOutline");
        if (shader == null)
        {
            Debug.LogError("[HoverOutline] No Resources/InteractableOutline material and " +
                           "shader 'Custom/InteractableOutline' not found (stripped from build?). " +
                           "Create the material or add the shader to Always Included Shaders.");
            return null;
        }
        s_material = new Material(shader);
        return s_material;
    }

    public void SetHighlight(bool on)
    {
        if (on == _visible) return;
        _visible = on;

        if (on && !_built) Build();

        foreach (GameObject shell in _shells)
            if (shell != null) shell.SetActive(on);
    }

    private void Build()
    {
        _built = true;

        Material mat = GetMaterial();
        if (mat == null) return;

        // Static meshes.
        foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<MeshRenderer>() == null) continue;

            GameObject shell = CreateShell(mf.transform);
            shell.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            MeshRenderer mr = shell.AddComponent<MeshRenderer>();
            SetupRenderer(mr, mat);
        }

        // Skinned meshes (follow the same bones so the outline animates too).
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.sharedMesh == null) continue;

            GameObject shell = CreateShell(smr.transform);
            SkinnedMeshRenderer copy = shell.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = smr.sharedMesh;
            copy.bones = smr.bones;
            copy.rootBone = smr.rootBone;
            SetupRenderer(copy, mat);
        }
    }

    private GameObject CreateShell(Transform source)
    {
        var shell = new GameObject("HoverOutline");
        shell.transform.SetParent(source, false); // identity local transform = perfect overlap
        shell.layer = source.gameObject.layer;
        shell.SetActive(false);
        _shells.Add(shell);
        return shell;
    }

    private static void SetupRenderer(Renderer r, Material mat)
    {
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private void OnDestroy()
    {
        foreach (GameObject shell in _shells)
            if (shell != null) Destroy(shell);
        _shells.Clear();
    }
}
