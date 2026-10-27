using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Single save store for the whole game, mirroring the PlayerPrefs API exactly so call
/// sites just swap `PlayerPrefs` for `SaveBridge`.
///
///   - Outside CrazyGames (editor / plain web builds): straight passthrough to PlayerPrefs
///     (Unity's IndexedDB-backed local storage) - current behaviour, untouched.
///   - On a curateCrazyGames build (URL opts in, SDK initialised): all reads/writes go to
///     the CrazyGames Data module instead (localStorage for guests, cloud-synced to the
///     logged-in account - the "reaccess progress on any device" layer). One-time
///     migration copies any existing local progress from the same browser into the cloud
///     store so nothing is lost on the switch.
///
/// Ints/floats are stored as invariant-culture strings in the cloud store (the Data
/// module API only accepts strings), matching PlayerPrefs semantics otherwise.
/// </summary>
public static class SaveBridge
{
    private const string MigrationSentinel = "lunar.cg.migrated";
    private static bool migrationAttempted;

    /// <summary>Type of every key written this session ('i' int, 'f' float, 's' string). The
    /// Newgrounds cloud blob has to restore an int key AS an int (a string-restored int reads
    /// back as 0), so the type is captured at write time; keys never written this session fall
    /// back to the name-based guess in <see cref="TypeOf"/>.</summary>
    private static readonly Dictionary<string, char> writeTypes = new Dictionary<string, char>();

    /// <summary>True once the cloud store is the live save target.</summary>
    public static bool OnCloud => CrazyGamesBridge.Ready;

    // -------------------------------------------------------------- shared helpers
    private static bool CloudHas(string key)
    {
        return CrazyGamesBridge.HasKey(key);
    }

    private static string CloudGet(string key)
    {
        return CrazyGamesBridge.GetString(key);
    }

    private static void CloudSet(string key, string value)
    {
        CrazyGamesBridge.SetString(key, value);
    }

    // ------------------------------------------------------------------ migration
    /// <summary>Copies existing local (PlayerPrefs) progress into the cloud store once,
    /// on the first run where the cloud store becomes available. Safe to call repeatedly.
    /// Runs lazily from any cloud access; also called explicitly at boot.</summary>
    public static void EnsureMigrated()
    {
        if (!CrazyGamesBridge.Ready) { migrationAttempted = false; return; }
        if (migrationAttempted) return;
        migrationAttempted = true;
        if (CloudHas(MigrationSentinel)) return;

        var keys = EnumerateSaveKeys();

        int copied = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            string k = keys[i];
            if (CloudHas(k)) continue;
            if (!PlayerPrefs.HasKey(k)) continue;
            CloudSet(k, PlayerPrefs.GetString(k, ""));
            copied++;
        }
        CloudSet(MigrationSentinel, "1");
        Debug.Log("[SaveBridge] Migrated " + copied + " save key(s) into the CrazyGames data module.");
    }

    /// <summary>Every save key this build can write. PlayerPrefs has no key iterator, so the
    /// finite list is spelled out here; it drives both the CrazyGames migration and the
    /// Newgrounds cloud blob.</summary>
    public static List<string> EnumerateSaveKeys()
    {
        var keys = new List<string>
        {
            GameFlow.UsernameKey,
            GameFlow.SaveVersionKey,
            "lunar.muted",
            "lunar.intro.seen",
            "lunar.tokens",
            "lunar.racelast.boostnext",
            // AdCoordinator's rewarded-revive cadence counter (offer on every 3rd death).
            "lunar.revive.deathcount",
            // TutorialCoach shown-flags (CrazyGames first-run onboarding; one per tip).
            "lunar.tut.harvest.intro",
            "lunar.tut.harvest.hull",
            "lunar.tut.harvest.pod",
            "lunar.tut.harvest.ore",
            "lunar.tut.prospector.intro",
            "lunar.tut.prospector.ore",
            "lunar.tut.ridge.intro",
            "lunar.tut.ridge.gap",
            // One-shot bonus flags: without these a save that already claimed them locally
            // could re-claim them after migrating to cloud (a duplicate 100/150 each).
            "lunar.firstmedalbonus",
            "lunar.prospector.firstclearbonus",
            // Newgrounds secret-medal bookkeeping (which secrets were already awarded/uploaded).
            "lunar.ng.secrets",
            "lunar.ng.secrets.posted",
        };
        AddLevelsKeys(keys);
        // Vehicle ownership + the equipped choice. Without these a local save that already owns
        // a rover or the sprint buggy arrives on cloud as if it had bought nothing.
        string[] vehicleKeys = VehicleSystem.SaveKeys;
        for (int i = 0; i < vehicleKeys.Length; i++) keys.Add(vehicleKeys[i]);
        for (int w = 0; w < LevelManager.PlanetCount; w++)
        {
            keys.Add(string.Format("lunar.racebest.w{0}", w));
            // The best race grade cleared is what gates the sprint buggy, so it has to survive
            // migration or a returning player would have to re-beat Gold to unlock it again.
            keys.Add(string.Format("lunar.racegrade.w{0}", w));
            keys.Add(string.Format("lunar.ridgebest.w{0}", w));
            keys.Add(string.Format("lunar.ridgeclear.w{0}", w));
            keys.Add(string.Format("lunar.harvestbest.w{0}", w));
            keys.Add(string.Format("lunar.prospectorbest.w{0}", w));
            keys.Add(string.Format("lunar.prospectorgrade.w{0}", w));
            keys.Add(string.Format("lunar.prospectorsites.w{0}", w));
            keys.Add(string.Format("lunar.harvestclear.w{0}", w));
            keys.Add(string.Format("lunar.prospectorclear.w{0}", w));
            keys.Add(string.Format("lunar.leaderboard.harvest.w{0}.v2", w));
            keys.Add(string.Format("lunar.leaderboard.race.w{0}.canyon.v2", w));
            keys.Add(string.Format("lunar.leaderboard.race.w{0}.ridge.v2", w));
            keys.Add(string.Format("lunar.leaderboard.race.w{0}.v1", w));
            keys.Add(string.Format("lunar.leaderboard.ridge.w{0}.v1", w));
        }
        return keys;
    }

    // ----------------------------------------------------- Newgrounds cloud typing
    /// <summary>The PlayerPrefs type of a key: the recorded write type when known, else a
    /// name-based guess (the known string keys / leaderboards are strings, everything else int).</summary>
    public static char TypeOf(string key)
    {
        char t;
        if (writeTypes.TryGetValue(key, out t)) return t;
        if (key == GameFlow.UsernameKey || key == GameFlow.SaveVersionKey
            || key == "lunar.muted" || key == "lunar.vehicle.selected"
            || key.StartsWith("lunar.leaderboard."))
            return 's';
        return 'i';
    }

    /// <summary>Raw value of a local key, formatted by its type; null when the key is unset.</summary>
    public static string ReadRaw(string key)
    {
        if (!PlayerPrefs.HasKey(key)) return null;
        switch (TypeOf(key))
        {
            case 'f': return PlayerPrefs.GetFloat(key, 0f).ToString("R", CultureInfo.InvariantCulture);
            case 's': return PlayerPrefs.GetString(key, "");
            default: return PlayerPrefs.GetInt(key, 0).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Restores a cloud value into PlayerPrefs using the type it was saved as.</summary>
    public static void WriteRaw(string key, char type, string value)
    {
        writeTypes[key] = type;
        switch (type)
        {
            case 'f':
                float f;
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) PlayerPrefs.SetFloat(key, f);
                break;
            case 's':
                PlayerPrefs.SetString(key, value ?? "");
                break;
            default:
                int i;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) PlayerPrefs.SetInt(key, i);
                break;
        }
    }

    private static void AddLevelsKeys(System.Collections.Generic.List<string> keys)
    {
        string[] ids = { "engine", "boost", "oxygen", "hull", "jump" };
        for (int i = 0; i < ids.Length; i++) keys.Add("lunar.upg." + ids[i]);
    }

    // ---------------------------------------------------------------- PlayerPrefs parity
    public static int GetInt(string key, int defaultValue = 0)
    {
        if (OnCloud)
        {
            EnsureMigrated();
            string v = CloudGet(key);
            int parsed;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : defaultValue;
        }
        return PlayerPrefs.GetInt(key, defaultValue);
    }

    public static void SetInt(string key, int value)
    {
        writeTypes[key] = 'i';
        if (OnCloud) { EnsureMigrated(); CloudSet(key, value.ToString(CultureInfo.InvariantCulture)); }
        else PlayerPrefs.SetInt(key, value);
        NewgroundsCloud.MarkDirty();
    }

    public static float GetFloat(string key, float defaultValue = 0f)
    {
        if (OnCloud)
        {
            EnsureMigrated();
            string v = CloudGet(key);
            float parsed;
            return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : defaultValue;
        }
        return PlayerPrefs.GetFloat(key, defaultValue);
    }

    public static void SetFloat(string key, float value)
    {
        writeTypes[key] = 'f';
        if (OnCloud) { EnsureMigrated(); CloudSet(key, value.ToString("R", CultureInfo.InvariantCulture)); }
        else PlayerPrefs.SetFloat(key, value);
        NewgroundsCloud.MarkDirty();
    }

    public static string GetString(string key, string defaultValue = "")
    {
        if (OnCloud)
        {
            EnsureMigrated();
            string v = CloudGet(key);
            return string.IsNullOrEmpty(v) ? defaultValue : v;
        }
        return PlayerPrefs.GetString(key, defaultValue);
    }

    public static void SetString(string key, string value)
    {
        writeTypes[key] = 's';
        if (OnCloud) { EnsureMigrated(); CloudSet(key, value ?? ""); }
        else PlayerPrefs.SetString(key, value);
        NewgroundsCloud.MarkDirty();
    }

    public static bool HasKey(string key)
    {
        if (OnCloud) { EnsureMigrated(); return CloudHas(key); }
        return PlayerPrefs.HasKey(key);
    }

    public static void DeleteKey(string key)
    {
        if (OnCloud)
        {
            EnsureMigrated();
            CrazyGamesBridge.DeleteKey(key);
        }
        // Deliberate, matching DeleteAll's "clear both" philosophy: ALSO drop the local copy
        // so a future cloud switch can never resurrect a deleted value from PlayerPrefs.
        PlayerPrefs.DeleteKey(key);
        NewgroundsCloud.MarkDirty();
    }

    /// <summary>Clears BOTH stores so a cloud switch can never resurrect stale local
    /// data and vice-versa.</summary>
    public static void DeleteAll()
    {
        if (CrazyGamesBridge.Enabled)
        {
            CrazyGamesBridge.DeleteAll();
            migrationAttempted = false;
        }
        PlayerPrefs.DeleteAll();
        NewgroundsCloud.MarkDirty();
    }

    public static void Save()
    {
        PlayerPrefs.Save(); // cloud store is debounced by the SDK automatically
        NewgroundsCloud.MarkDirty();
    }
}