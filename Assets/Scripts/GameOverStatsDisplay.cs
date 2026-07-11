using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Lives in the GameOver scene, alongside GameOverHandler and
/// GameOverCameraSequence. Reads QuotaManager's stats once at Start -
/// synchronously, before GameOverHandler's multi-second delayed reset wipes
/// them back to day 1 - and writes them into world-space TextMeshPro signs
/// placed by hand on buildings in the scene, the same numbers QuotaUI.cs
/// already shows during normal play, just rendered as 3D text in the world
/// instead of a screen overlay, so the flythrough camera passes by them.
///
/// Setup for each sign: Hierarchy > right-click > 3D Object > Text -
/// TextMeshPro (NOT the UI version - that needs a Canvas and won't sit
/// naturally on a building facade). Position/rotate/scale it to sit flush
/// against a wall, then drag it into the matching slot below. Leave any
/// slot empty to skip it.
/// </summary>
public class GameOverStatsDisplay : MonoBehaviour
{
    [Header("World-space text on buildings (TextMeshPro 3D, not UGUI)")]
    [SerializeField] private TextMeshPro daySurvivedText;
    [SerializeField] private TextMeshPro reasonText;
    [SerializeField] private TextMeshPro extractedText;

    [Header("Format")]
    [SerializeField] private string dayFormat = "SURVIVED {0} DAY(S)";
    [SerializeField] private string extractedFormat = "TOTAL EXTRACTED: {0}";

    [Header("Reveal")]
    [Tooltip("Types each sign out character by character (like ConnectedText.cs) instead of popping in all at once.")]
    [SerializeField] private bool useTypewriterEffect = true;
    [SerializeField] private float typewriterCharsPerSecond = 20f;
    [Tooltip("Pause between each sign starting its reveal, so they don't all type at once.")]
    [SerializeField] private float staggerBetweenSigns = 1.5f;

    private void Start()
    {
        int day = 1;
        int reason = GameOverReason.None;
        int extracted = 0;

        if (QuotaManager.Instance != null)
        {
            day = QuotaManager.Instance.currentDay.value;
            reason = QuotaManager.Instance.lastGameOverReason.value;
            extracted = QuotaManager.Instance.totalBandwidth.value;
        }

        string dayMessage = string.Format(dayFormat, day);
        string reasonMessage = GameOverReason.ToDisplayText(reason);
        string extractedMessage = string.Format(extractedFormat, extracted);

        if (useTypewriterEffect)
            StartCoroutine(RevealSequence(dayMessage, reasonMessage, extractedMessage));
        else
        {
            SetText(daySurvivedText, dayMessage);
            SetText(reasonText, reasonMessage);
            SetText(extractedText, extractedMessage);
        }
    }

    private IEnumerator RevealSequence(string dayMessage, string reasonMessage, string extractedMessage)
    {
        yield return Typewriter(daySurvivedText, dayMessage);
        if (staggerBetweenSigns > 0f) yield return new WaitForSeconds(staggerBetweenSigns);

        yield return Typewriter(reasonText, reasonMessage);
        if (staggerBetweenSigns > 0f) yield return new WaitForSeconds(staggerBetweenSigns);

        yield return Typewriter(extractedText, extractedMessage);
    }

    private IEnumerator Typewriter(TextMeshPro label, string message)
    {
        if (label == null) yield break;

        label.text = "";
        float delay = typewriterCharsPerSecond > 0f ? 1f / typewriterCharsPerSecond : 0f;

        foreach (char c in message)
        {
            label.text += c;
            if (delay > 0f) yield return new WaitForSeconds(delay);
        }
    }

    private static void SetText(TextMeshPro label, string message)
    {
        if (label != null) label.text = message;
    }
}
