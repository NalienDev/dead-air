/// <summary>
/// Carries the local player's lobby identity across the scene change from the lobby
/// into the game. <see cref="PurrLobby.LobbyManager"/> only exists in the lobby scene,
/// so we snapshot the local user there (see <see cref="LobbyLocalUserCapture"/>) into
/// these statics, which survive scene loads. <see cref="PlayerIdentity"/> reads them
/// in-game to publish the player's Steam id + name.
/// </summary>
public static class LobbyLocalUser
{
    /// <summary>Local user's Steam id (matches LobbyUser.Id). Empty until captured.</summary>
    public static string Id = "";

    /// <summary>Local user's Steam display name. Empty until captured.</summary>
    public static string DisplayName = "";
}
