#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using System.Collections;
using System.Collections.Generic;
using Dissonance;
using PurrLobby;
using PurrNet;
using UnityEngine;

/// <summary>
/// Publishes the player's Dissonance voice name and Steam identity so others can mute them by team and show their name and avatar.
/// </summary>
[RequireComponent(typeof(PlayerManager))]
public class PlayerIdentity : NetworkBehaviour
{
    public SyncVar<string> voiceName = new("");     // Dissonance LocalPlayerName
    public SyncVar<string> steamId = new("");       // lobby member id
    public SyncVar<string> displayName = new("");   // lobby display name

    // Display name, or "person" when we don't have one.
    public string DisplayNameOrDefault =>
        string.IsNullOrEmpty(displayName.value) ? "person" : displayName.value;

    // Avatars resolved once per Steam id, shared by every PlayerIdentity on this client.
    private static readonly Dictionary<string, Texture2D> s_avatarCache = new();

    private bool _loggedAvatarFailure;

    protected override void OnSpawned(bool asServer)
    {
        if (isOwner) StartCoroutine(ResolveAndPublish());
    }

    private IEnumerator ResolveAndPublish()
    {
        // Steam id + display name come from the local user captured back in the lobby.
        string localId = LobbyLocalUser.Id ?? "";
        string name = LobbyLocalUser.DisplayName ?? "";

        if (string.IsNullOrEmpty(localId))
            Debug.LogWarning("[PlayerIdentity] LobbyLocalUser.Id is empty; avatar and name will fall back.");

        // Fall back to the lobby member list if the name wasn't captured.
        if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(localId))
            name = LookupDisplayName(localId);

        ServerSetIdentity(localId, name);

        // Dissonance assigns LocalPlayerName only once it has started, so wait for it.
        DissonanceComms comms = FindFirstObjectByType<DissonanceComms>();
        while (comms == null || string.IsNullOrEmpty(comms.LocalPlayerName))
        {
            yield return null;
            if (comms == null) comms = FindFirstObjectByType<DissonanceComms>();
        }
        ServerSetVoiceName(comms.LocalPlayerName);
    }

    [ServerRpc]
    private void ServerSetIdentity(string id, string name)
    {
        steamId.value = id;
        displayName.value = name;
    }

    [ServerRpc]
    private void ServerSetVoiceName(string v) => voiceName.value = v;

    // This player's Steam avatar, or null; tries the lobby snapshot then Steam, and may
    // return null for a few refreshes while Steam fetches the image, so callers retry.
    public Texture2D ResolveAvatar()
    {
        string id = steamId.value;
        if (string.IsNullOrEmpty(id))
        {
            LogAvatarFailureOnce("steamId is empty (lobby capture missing?)");
            return null;
        }

        if (s_avatarCache.TryGetValue(id, out Texture2D cached) && cached != null)
            return cached;

        // 1. The lobby snapshot carried into the game.
        Texture2D tex = LookupHolderAvatar(id);

        // 2. Straight from Steam (it has every lobby member's avatar cached by now).
        if (tex == null)
            tex = LoadAvatarFromSteam(id);

        if (tex != null)
        {
            s_avatarCache[id] = tex;
            return tex;
        }

        LogAvatarFailureOnce("no avatar in the lobby snapshot and Steam hasn't delivered one (yet)");
        return null;
    }

    private static Texture2D LookupHolderAvatar(string id)
    {
        LobbyDataHolder holder = FindFirstObjectByType<LobbyDataHolder>();
        if (holder == null || holder.CurrentLobby.Members == null) return null;

        foreach (LobbyUser m in holder.CurrentLobby.Members)
            if (m.Id == id) return m.Avatar;
        return null;
    }

    private void LogAvatarFailureOnce(string reason)
    {
        if (_loggedAvatarFailure) return;
        _loggedAvatarFailure = true;
        Debug.Log($"[PlayerIdentity] No avatar for '{DisplayNameOrDefault}': {reason}.");
    }

#if STEAMWORKS_NET && !DISABLESTEAMWORKS
    private static Steamworks.Callback<Steamworks.AvatarImageLoaded_t> s_avatarLoadedCallback;

    private static Texture2D LoadAvatarFromSteam(string id)
    {
        if (!ulong.TryParse(id, out ulong raw)) return null;

        try { Steamworks.InteropHelp.TestIfAvailableClient(); }
        catch { return null; } // Steam not running (e.g. editor without Steam)

        var steamId = new Steamworks.CSteamID(raw);

        // -1 = not cached yet; Steam starts the download and fires AvatarImageLoaded_t.
        // Register the callback so the texture lands in the cache for the next refresh.
        int handle = Steamworks.SteamFriends.GetLargeFriendAvatar(steamId);
        if (handle == -1)
        {
            s_avatarLoadedCallback ??=
                Steamworks.Callback<Steamworks.AvatarImageLoaded_t>.Create(OnSteamAvatarLoaded);
            return null;
        }
        if (handle == 0) return null; // user has no avatar

        return BuildTexture(handle);
    }

    private static void OnSteamAvatarLoaded(Steamworks.AvatarImageLoaded_t data)
    {
        if (data.m_iImage <= 0) return;
        Texture2D tex = BuildTexture(data.m_iImage);
        if (tex != null) s_avatarCache[data.m_steamID.m_SteamID.ToString()] = tex;
    }

    private static Texture2D BuildTexture(int handle)
    {
        if (!Steamworks.SteamUtils.GetImageSize(handle, out uint w, out uint h)) return null;

        byte[] buffer = new byte[w * h * 4];
        if (!Steamworks.SteamUtils.GetImageRGBA(handle, buffer, buffer.Length)) return null;

        var tex = new Texture2D((int)w, (int)h, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(buffer);
        FlipVertically(tex);
        tex.Apply();
        return tex;
    }

    // Steam gives the image top-to-bottom; Unity textures are bottom-to-top.
    private static void FlipVertically(Texture2D texture)
    {
        Color[] pixels = texture.GetPixels();
        int w = texture.width, h = texture.height;
        for (int y = 0; y < h / 2; y++)
        for (int x = 0; x < w; x++)
        {
            (pixels[y * w + x], pixels[(h - 1 - y) * w + x]) =
                (pixels[(h - 1 - y) * w + x], pixels[y * w + x]);
        }
        texture.SetPixels(pixels);
    }
#else
    private static Texture2D LoadAvatarFromSteam(string id) => null;
#endif

    private static string LookupDisplayName(string id)
    {
        LobbyDataHolder holder = FindFirstObjectByType<LobbyDataHolder>();
        if (holder == null || holder.CurrentLobby.Members == null) return "";

        foreach (LobbyUser m in holder.CurrentLobby.Members)
            if (m.Id == id) return m.DisplayName;
        return "";
    }
}
