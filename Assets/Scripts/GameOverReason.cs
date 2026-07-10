/// <summary>
/// Shared reason codes for why the run last ended in a Game Over. Stored on
/// QuotaManager.lastGameOverReason (a SyncVar<int>, so it replicates to
/// every client, not just the server) and read back by
/// GameOverStatsDisplay in the GameOver scene to show why the run ended.
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
