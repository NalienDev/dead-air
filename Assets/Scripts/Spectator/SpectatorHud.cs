using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// On-screen HUD for the local dead player while spectating:
///   • the name of the player you're currently watching ("person" if unknown), and
///   • a list of every dead player with their Steam name + avatar.
///
/// Place on (or under) a Canvas in the game scene and wire the fields. The whole thing
/// is shown only while the local player is spectating. Names/avatars come from each
/// player's <see cref="PlayerIdentity"/>; the local spectator is found via
/// <see cref="PlayerManager.Local"/>.
/// </summary>
public class SpectatorHud : MonoBehaviour
{
    [Header("Root (shown only while spectating)")]
    [Tooltip("Parent object toggled on/off with spectator mode. Keep this script on a " +
             "SEPARATE always-active object so it can turn the root back on.")]
    [SerializeField] private GameObject _root;

    [Header("Now watching")]
    [SerializeField] private TMP_Text _watchingNameLabel;

    [Header("Dead players list")]
    [Tooltip("Container for the entries (e.g. a Vertical Layout Group).")]
    [SerializeField] private Transform _deadListParent;
    [Tooltip("Entry prefab with a TMP_Text (name) and an Image (avatar) in its children.")]
    [SerializeField] private GameObject _deadEntryPrefab;

    [SerializeField] private float _refreshInterval = 0.5f;

    private readonly List<GameObject> _entries = new();
    private readonly List<Sprite> _entrySprites = new();
    private string _deadSignature = "";
    private float _timer;

    private void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = _refreshInterval;

        SpectatorController spectator = ResolveLocalSpectator();
        bool spectating = spectator != null && spectator.IsSpectating;

        if (_root != null) _root.SetActive(spectating);
        if (!spectating)
        {
            ClearEntries();
            _deadSignature = "";
            return;
        }

        if (_watchingNameLabel != null)
            _watchingNameLabel.text = ResolveName(spectator.CurrentlyWatching);

        RebuildDeadListIfChanged();
    }

    private static SpectatorController ResolveLocalSpectator()
    {
        PlayerManager local = PlayerManager.Local;
        return local != null ? local.GetComponent<SpectatorController>() : null;
    }

    private static string ResolveName(PlayerManager pm)
    {
        if (pm == null) return "person";
        PlayerIdentity id = pm.GetComponent<PlayerIdentity>();
        return id != null ? id.DisplayNameOrDefault : "person";
    }

    // Rebuild only when the set of dead players changes, so we don't churn UI objects
    // (and leak sprites) every refresh.
    private void RebuildDeadListIfChanged()
    {
        if (_deadListParent == null || _deadEntryPrefab == null) return;

        List<PlayerManager> dead = new();
        foreach (PlayerManager pm in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
            if (pm != null && pm.IsDead) dead.Add(pm);
        dead.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        // Resolve avatars up front — they load asynchronously from Steam, so one that
        // arrives after the list was built must change the signature and rebuild it.
        List<Texture2D> avatars = new(dead.Count);
        foreach (PlayerManager pm in dead)
        {
            PlayerIdentity id = pm.GetComponent<PlayerIdentity>();
            avatars.Add(id != null ? id.ResolveAvatar() : null);
        }

        string signature = BuildSignature(dead, avatars);
        if (signature == _deadSignature) return;
        _deadSignature = signature;

        ClearEntries();

        for (int i = 0; i < dead.Count; i++)
        {
            PlayerManager pm = dead[i];
            GameObject entry = Instantiate(_deadEntryPrefab, _deadListParent);
            _entries.Add(entry);

            TMP_Text label = entry.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = ResolveName(pm);

            Image avatar = FindAvatarImage(entry);
            if (avatar == null) continue;

            Texture2D tex = avatars[i];
            if (tex != null)
            {
                Sprite sprite = Sprite.Create(
                    tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                _entrySprites.Add(sprite);
                avatar.sprite = sprite;
                avatar.enabled = true;
            }
            else
            {
                avatar.enabled = false; // no avatar available — hide the image
            }
        }
    }

    // The entry root itself may be an Image (row background), and GetComponentInChildren
    // would return that first — so prefer a child named "Avatar", then any Image that
    // is NOT the root, and only fall back to the root image if there's nothing else.
    private static Image FindAvatarImage(GameObject entry)
    {
        Image[] images = entry.GetComponentsInChildren<Image>(true);

        foreach (Image img in images)
            if (img.gameObject.name.IndexOf("avatar", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return img;

        foreach (Image img in images)
            if (img.gameObject != entry)
                return img;

        return images.Length > 0 ? images[0] : null;
    }

    private static string BuildSignature(List<PlayerManager> dead, List<Texture2D> avatars)
    {
        StringBuilder sb = new();
        for (int i = 0; i < dead.Count; i++)
            sb.Append(dead[i].GetInstanceID())
              .Append(avatars[i] != null ? '+' : '-')
              .Append(',');
        return sb.ToString();
    }

    private void ClearEntries()
    {
        foreach (GameObject e in _entries) if (e != null) Destroy(e);
        _entries.Clear();

        foreach (Sprite s in _entrySprites) if (s != null) Destroy(s);
        _entrySprites.Clear();
    }
}
