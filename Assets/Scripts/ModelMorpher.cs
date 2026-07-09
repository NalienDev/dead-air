using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Crossfades one model into another using the Custom/EchoDissolve shader.
/// Both models sit overlapped under the same parent; the old one dissolves out
/// while the new one dissolves in, with a glowing edge along the burn front.
///
/// Purely visual and local — run it on every client (the Echo triggers it from
/// an ObserversRpc). The models keep their own materials: during the morph each
/// renderer is temporarily switched to a dissolve material that copies its base
/// map/color, and the originals are restored when the morph ends.
/// </summary>
public class ModelMorpher : MonoBehaviour
{
    [Tooltip("The Custom/EchoDissolve shader. Assign it explicitly so it isn't " +
             "stripped from builds; Shader.Find is only a play-mode fallback.")]
    [SerializeField] private Shader _dissolveShader;
    [SerializeField] private float _duration = 1.5f;
    [SerializeField] private AnimationCurve _curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Optional burst played when the morph starts (place it on the model).")]
    [SerializeField] private ParticleSystem _burst;

    private static readonly int DissolveThreshold = Shader.PropertyToID("_DissolveThreshold");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private class Swap
    {
        public Renderer renderer;
        public Material[] originals;
        public Material[] dissolved;
    }

    private readonly List<Swap> _fromSwaps = new();
    private readonly List<Swap> _toSwaps = new();
    private Coroutine _routine;
    private GameObject _from;

    public bool IsMorphing => _routine != null;

    /// <summary>
    /// Dissolves <paramref name="from"/> out while <paramref name="to"/> dissolves
    /// in. Activates both immediately; when done, <paramref name="from"/> is
    /// deactivated and every renderer gets its original materials back.
    /// </summary>
    public void Morph(GameObject from, GameObject to)
    {
        CancelMorph();

        if (_dissolveShader == null)
            _dissolveShader = Shader.Find("Custom/EchoDissolve");

        // No shader available — hard swap so gameplay still reads correctly.
        if (_dissolveShader == null || from == null || to == null)
        {
            if (from != null) from.SetActive(false);
            if (to != null) to.SetActive(true);
            return;
        }

        _from = from;
        from.SetActive(true);
        to.SetActive(true);

        BuildSwaps(from, _fromSwaps, 0f);
        BuildSwaps(to, _toSwaps, 1f);

        if (_burst != null) _burst.Play();

        _routine = StartCoroutine(Run());
    }

    /// <summary>
    /// Stops a running morph and restores original materials. Active states are
    /// left alone — the caller decides which model should be showing.
    /// </summary>
    public void CancelMorph()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
        Restore(_fromSwaps);
        Restore(_toSwaps);
        _from = null;
    }

    private void OnDestroy() => CancelMorph();

    private IEnumerator Run()
    {
        float elapsed = 0f;
        while (elapsed < _duration)
        {
            elapsed += Time.deltaTime;
            float t = _curve.Evaluate(Mathf.Clamp01(elapsed / _duration));
            SetThresholds(_fromSwaps, t);        // burn out
            SetThresholds(_toSwaps, 1f - t);     // burn in
            yield return null;
        }

        GameObject from = _from;
        _routine = null;
        CancelMorph();
        if (from != null) from.SetActive(false);
    }

    private void BuildSwaps(GameObject root, List<Swap> swaps, float startThreshold)
    {
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;

            Material[] originals = r.sharedMaterials;
            Material[] dissolved = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Material mat = new Material(_dissolveShader);
                CopyLook(originals[i], mat);
                mat.SetFloat(DissolveThreshold, startThreshold);
                dissolved[i] = mat;
            }

            r.sharedMaterials = dissolved;
            swaps.Add(new Swap { renderer = r, originals = originals, dissolved = dissolved });
        }
    }

    // Carries the source material's look over to the dissolve material.
    // Handles both URP (_BaseMap/_BaseColor) and legacy (_MainTex/_Color) names.
    private static void CopyLook(Material source, Material target)
    {
        if (source == null) return;

        if (source.HasProperty(BaseMapId) && source.GetTexture(BaseMapId) != null)
            target.SetTexture(BaseMapId, source.GetTexture(BaseMapId));
        else if (source.HasProperty(MainTexId) && source.GetTexture(MainTexId) != null)
            target.SetTexture(BaseMapId, source.GetTexture(MainTexId));

        if (source.HasProperty(BaseColorId))
            target.SetColor(BaseColorId, source.GetColor(BaseColorId));
        else if (source.HasProperty(ColorId))
            target.SetColor(BaseColorId, source.GetColor(ColorId));
    }

    private static void SetThresholds(List<Swap> swaps, float value)
    {
        foreach (Swap swap in swaps)
            foreach (Material mat in swap.dissolved)
                if (mat != null) mat.SetFloat(DissolveThreshold, value);
    }

    private static void Restore(List<Swap> swaps)
    {
        foreach (Swap swap in swaps)
        {
            if (swap.renderer != null)
                swap.renderer.sharedMaterials = swap.originals;
            foreach (Material mat in swap.dissolved)
                if (mat != null) Destroy(mat);
        }
        swaps.Clear();
    }
}
