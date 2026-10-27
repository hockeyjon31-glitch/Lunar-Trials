using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Newgrounds CloudSave for the whole save. Mirrors the CrazyGames cloud path inside
/// <see cref="SaveBridge"/>: the local PlayerPrefs store stays the live target (so guests and
/// offline runs never lose anything), and when a player is logged in we sync a single JSON blob
/// to Newgrounds cloud slot <see cref="SlotId"/> so progress follows the account across devices.
///
///   * Guest / no user / off Newgrounds: every entry point is a no-op; only PlayerPrefs is used.
///   * On login: read the slot. If it holds data the cloud is authoritative and is applied to
///     PlayerPrefs (with the SAME PlayerPrefs type per key, see SaveBridge.TypeOf). If the slot
///     is empty the current local progress is pushed up so it follows the account.
///   * After that: every local write marks the blob dirty and a debounced flush pushes it back
///     (3s of quiet, so a burst of writes is one request).
///
/// The blob is a flat list of (key, type, value) entries built from SaveBridge.EnumerateSaveKeys,
/// serialized with JsonUtility (no Newtonsoft dependency). The 2 MB slot cap is enforced before
/// sending; an oversized blob is skipped with a warning rather than truncated.
/// </summary>
public static class NewgroundsCloud
{
    private const int SlotId = 1;
    private const float FlushDelay = 3f;              // seconds of quiet before pushing
    private const int MaxBytes = 2 * 1024 * 1024;     // CloudSave slot cap

    private static bool loading;
    private static bool loaded;
    private static bool saving;
    private static bool dirty;
    private static float dirtyAt;

    /// <summary>True once a logged-in user's slot has been read (or found empty) and the game
    /// is safe to treat as cloud-backed.</summary>
    public static bool Ready => loaded && NewgroundsBridge.Ready;

    /// <summary>Starts the one-time load once a user exists. Safe to call repeatedly.</summary>
    public static void EnsureLoaded()
    {
        if (loaded || loading) return;
        if (!NewgroundsBridge.HasUser || NGIO.ngioCore == null) return;
        loading = true;
        if (!NewgroundsBridge.Run(Load())) loading = false;
    }

    /// <summary>Signals that local progress changed and should be pushed. No-op until a user
    /// is logged in.</summary>
    public static void MarkDirty()
    {
        if (!NewgroundsBridge.Enabled || !NewgroundsBridge.HasUser) return;
        dirty = true;
        dirtyAt = Time.unscaledTime;
    }

    /// <summary>Per-frame pump from <see cref="NewgroundsBridge.Tick"/>: flushes the blob once
    /// the write burst has gone quiet.</summary>
    public static void Tick()
    {
        if (!Ready || saving || !dirty) return;
        if (Time.unscaledTime - dirtyAt < FlushDelay) return;
        saving = true;
        if (!NewgroundsBridge.Run(Save())) saving = false;
    }

    // ------------------------------------------------------------------ load
    private static IEnumerator Load()
    {
        NewgroundsIO.objects.SaveSlot slot = null;

        var comp = new NewgroundsIO.components.CloudSave.loadSlot();
        comp.id = SlotId;
        yield return NGIO.ngioCore.ExecuteComponent(comp, response =>
        {
            if (response == null || !response.success) return;
            var res = response.result as NewgroundsIO.results.CloudSave.loadSlot;
            if (res != null && res.slot != null && res.slot.hasData) slot = res.slot;
        });

        if (slot != null)
        {
            string data = null;
            yield return slot.GetData(s => data = s);
            if (!string.IsNullOrEmpty(data)) Apply(data);
            dirty = false;   // cloud is authoritative; nothing to push back
            Debug.Log("Newgrounds: loaded cloud save from slot " + SlotId + ".");
        }
        else
        {
            // No cloud save yet - push the current local progress up so it follows the account.
            dirty = true;
            dirtyAt = Time.unscaledTime - FlushDelay;   // flush on the very next Tick
            Debug.Log("Newgrounds: no cloud save yet; local progress will be uploaded.");
        }

        loaded = true;
        loading = false;
    }

    // ------------------------------------------------------------------ save
    private static IEnumerator Save()
    {
        string json = Serialize();
        if (string.IsNullOrEmpty(json))
        {
            dirty = false;
            saving = false;
            yield break;
        }
        if (json.Length > MaxBytes)
        {
            Debug.LogWarning("Newgrounds: cloud save is " + json.Length + " bytes, over the 2 MB slot cap - skipped.");
            dirty = false;
            saving = false;
            yield break;
        }

        var comp = new NewgroundsIO.components.CloudSave.setData();
        comp.id = SlotId;
        comp.data = json;
        yield return NGIO.ngioCore.ExecuteComponent(comp, response =>
        {
            if (response != null && response.success)
                Debug.Log("Newgrounds: cloud save pushed (" + json.Length + " bytes).");
            else
                Debug.LogWarning("Newgrounds: cloud save push failed.");
        });

        dirty = false;
        saving = false;
    }

    // ------------------------------------------------------------------ serialization
    [System.Serializable]
    private class Entry
    {
        public string k;   // key
        public string t;   // type: "i" / "f" / "s"
        public string v;   // value
    }

    [System.Serializable]
    private class Blob
    {
        public List<Entry> e = new List<Entry>();
    }

    private static string Serialize()
    {
        var blob = new Blob();
        List<string> keys = SaveBridge.EnumerateSaveKeys();
        for (int i = 0; i < keys.Count; i++)
        {
            string k = keys[i];
            string v = SaveBridge.ReadRaw(k);
            if (v == null) continue;   // key never set locally
            blob.e.Add(new Entry { k = k, t = SaveBridge.TypeOf(k).ToString(), v = v });
        }
        return JsonUtility.ToJson(blob);
    }

    private static void Apply(string json)
    {
        Blob blob;
        try { blob = JsonUtility.FromJson<Blob>(json); }
        catch { blob = null; }
        if (blob == null || blob.e == null) return;

        for (int i = 0; i < blob.e.Count; i++)
        {
            Entry e = blob.e[i];
            if (e == null || string.IsNullOrEmpty(e.k)) continue;
            SaveBridge.WriteRaw(e.k, ParseType(e.t), e.v);
        }
        PlayerPrefs.Save();
        Debug.Log("Newgrounds: applied " + blob.e.Count + " save key(s) from the cloud.");
    }

    private static char ParseType(string t) => string.IsNullOrEmpty(t) ? 'i' : t[0];
}
