using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// White silhouette shown while the local player is looking at an interactable.
/// Added automatically by <see cref="Interactor"/> on first hover — nothing to place
/// by hand. Purely local/cosmetic (each client outlines only what THEY look at).
///
/// On first highlight it builds one shell per mesh under the object: a child with the
/// same mesh rendered by Custom/InteractableOutline (inverted hull, so only a thin rim
/// shows). Works for static meshes and skinned meshes. The shells are toggled on/off
/// on hover enter/exit.
///
/// Configure it by creating a material from Custom/InteractableOutline at
/// Assets/Resources/InteractableOutline.mat (color + thickness in the inspector);
/// that asset also keeps the shader in builds. Without it, a plain white material is
/// built from the shader — then the shader must be in Always Included Shaders.
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

        // Preferred: a material asset at Assets/Resources/InteractableOutline.mat.
        // Lets you tweak color/thickness in the inspector, and the asset reference
        // keeps the shader in builds without touching Always Included Shaders.
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
