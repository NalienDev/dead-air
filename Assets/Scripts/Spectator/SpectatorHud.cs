using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spectator HUD showing who the local dead player is watching and a list of all dead players.
/// </summary>
public class SpectatorHud : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("Parent object toggled with spectator mode. Keep this script on a separate always-active object.")]
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

    // Rebuild only when the set of dead players changes, to avoid churning UI objects and sprites.
    private void RebuildDeadListIfChanged()
    {
        if (_deadListParent == null || _deadEntryPrefab == null) return;

        List<PlayerManager> dead = new();
        foreach (PlayerManager pm in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
            if (pm != null && pm.IsDead) dead.Add(pm);
        dead.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        // Resolve avatars up front; they load asynchronously, so a late arrival changes
        // the signature and rebuilds the list.
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
                avatar.enabled = false; // no avatar available, so hide the image
            }
        }
    }

    // Prefer a child named "Avatar", then any non-root Image, falling back to the root image.
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
