using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Writes the run's final stats into world-space text signs in the game-over scene.
/// </summary>
public class GameOverStatsDisplay : MonoBehaviour
{
    [Header("World-space Text")]
    [SerializeField] private TextMeshPro daySurvivedText;
    [SerializeField] private TextMeshPro reasonText;
    [SerializeField] private TextMeshPro extractedText;

    [Header("Format")]
    [SerializeField] private string dayFormat = "SURVIVED {0} DAY(S)";
    [SerializeField] private string extractedFormat = "TOTAL EXTRACTED: {0}";

    [Header("Reveal")]
    [Tooltip("Type each sign out character by character instead of popping in at once.")]
    [SerializeField] private bool useTypewriterEffect = true;
    [SerializeField] private float typewriterCharsPerSecond = 20f;
    [Tooltip("Pause between each sign starting its reveal.")]
    [SerializeField] private float staggerBetweenSigns = 1.5f;

    private void Start()
    {
        // Read the stats now, before GameOverHandler's delayed reset wipes them.
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
