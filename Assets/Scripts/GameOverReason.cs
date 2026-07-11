/// <summary>
/// Shared reason codes for why a run ended in a game over.
/// </summary>
public static class GameOverReason
{
    public const int None = 0;
    public const int QuotaNotMet = 1;
    public const int TeamWiped = 2;

    public static string ToDisplayText(int reason)
    {
        switch (reason)
        {
            case QuotaNotMet: return "QUOTA NOT MET";
            case TeamWiped: return "TEAM WIPED";
            default: return "SIGNAL LOST";
        }
    }
}
