using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Newgrounds.io client-API facade, shaped after <see cref="KongregateBridge"/> but built on the
/// official <c>io.newgrounds.unity</c> library (embedded under Packages/).
///
/// Design rules, matching the other portal bridges:
///
///   * NOTHING is compiled out or written behind a compile-time define - the package is present
///     in every build, and all gating happens at RUNTIME in <see cref="Init"/>. Off Newgrounds
///     (itch.io, CrazyGames, Kongregate, editor) the host check fails, <see cref="Enabled"/>
///     stays false and every entry point becomes a permanent no-op, so one binary is correct
///     everywhere with no separate prepare/finish dance.
///   * Every call that needs a logged-in user is guarded by <see cref="HasUser"/>; a guest can
///     play the whole game and simply banks no medals or scores. Medal-ladder state is derived
///     fresh from the save on each call, so a guest who logs in mid-session is back-filled at
///     once; secret awards are remembered locally and flushed the moment a user exists.
///   * Score posting mirrors <see cref="CrazyBoard"/>: only a NEW PERSONAL BEST is sent (the
///     leaderboard calls us from its rank==1 path), which keeps the request count low and matches
///     the other portals' behaviour. Newgrounds keeps the best of what it receives anyway.
/// </summary>
public static class NewgroundsBridge
{
    // Secret-award bits. These double as the local persistence mask (see AchievedKey).
    public const int SecretProspectorPerfect = 0;
    public const int SecretHarvestingHero    = 1;
    public const int SecretLastGasp          = 2;
    public const int SecretRaggedEnd         = 3;

    private const string AchievedKey = "lunar.ng.secrets";         // secret bits earned locally
    private const string PostedKey   = "lunar.ng.secrets.posted";  // secret bits already sent to NG

    private static MonoBehaviour runner;                          // provided by NewgroundsSession
    private static bool inited;
    private static bool enabled;
    private static readonly HashSet<int> attemptedMedals = new HashSet<int>();
    private static string adoptedFrom = "";                       // last NG name copied into the callsign

    /// <summary>True when the game is really running on Newgrounds.</summary>
    public static bool Enabled => enabled;

    /// <summary>True once the API is initialised, connected and its items preloaded.</summary>
    public static bool Ready => enabled && NGIO.isInitialized && NGIO.isReady;

    /// <summary>True once a player is logged in (medals/scores can actually post).</summary>
    public static bool HasUser => Ready && NGIO.hasUser;

    /// <summary>True while the login page is open in another tab.</summary>
    public static bool LoginOpen => enabled && NGIO.loginPageOpen;

    /// <summary>The logged-in player's Newgrounds name ("" until logged in).</summary>
    public static string Username => HasUser && NGIO.user != null ? NGIO.user.name : "";

    /// <summary>
    /// Gives the facade a host so it can run the library's coroutines. Called once by
    /// <see cref="NewgroundsSession"/> on the sessions that are actually on Newgrounds.
    /// </summary>
    public static void Attach(MonoBehaviour host) => runner = host;

    /// <summary>Runs a coroutine on the session host. Returns false when no host is attached
    /// (off Newgrounds / before the session Awake); used by the CloudSave layer.</summary>
    public static bool Run(IEnumerator routine)
    {
        if (runner == null) return false;
        runner.StartCoroutine(routine);
        return true;
    }

    /// <summary>
    /// Initialises the library and decides - at runtime - whether this host is Newgrounds.
    /// Idempotent and cheap; safe to call from any Awake.
    /// </summary>
    public static void Init()
    {
        if (inited) return;
        inited = true;

        var options = new Dictionary<string, object>
        {
            { "version",            NewgroundsConfig.Version },
            { "debugMode",          false },
            { "checkHostLicense",   false },
            { "autoLogNewView",     false },
            { "preloadMedals",      true },
            { "preloadScoreBoards", true },
            { "preloadSaveSlots",   true },
        };
        NGIO.Init(NewgroundsConfig.AppID, NewgroundsConfig.AesKey, options);

        // The library reads the page URL, so we can tell the host without any jslib. A Newgrounds
        // build is served from www.newgrounds.com or uploads.ungrounded.net, and the page always
        // appends an ngio_session_id query param - either signal is enough.
        NewgroundsIO.Core core = NGIO.ngioCore;
        string host = core != null ? core.host : "";
        bool hostIsNewgrounds = !string.IsNullOrEmpty(host)
            && (host.Contains("newgrounds.com") || host.Contains("ungrounded.net"));
        bool hasSession = core != null && core.GetUriParam("ngio_session_id") != null;
        enabled = hostIsNewgrounds || hasSession;
    }

    /// <summary>
    /// Per-frame pump. The library only fires its status callback on an actual change, so this
    /// costs a couple of coroutine starts per frame and nothing off Newgrounds.
    /// </summary>
    public static void Tick()
    {
        if (!enabled || runner == null) return;
        runner.StartCoroutine(NGIO.GetConnectionStatus(OnStatusChanged));
        runner.StartCoroutine(NGIO.KeepSessionAlive());
        NewgroundsCloud.Tick();
    }

    private static void OnStatusChanged(string status) => Refresh();

    /// <summary>
    /// Re-evaluates the whole medal ladder from saved progress and flushes any pending secret
    /// awards. Idempotent and cheap; called on every status change and every progress event.
    /// </summary>
    public static void Refresh()
    {
        if (!HasUser) return;
        AdoptUsername();
        UnlockLadder();
        FlushSecrets();
        NewgroundsCloud.EnsureLoaded();
    }

    /// <summary>Anchor for the same call sites that use <see cref="KongregateBridge.OnProgressChanged"/>.</summary>
    public static void OnProgressChanged() => Refresh();

    /// <summary>
    /// Adopts the Newgrounds account name as the in-game callsign, but only when the player has
    /// never chosen one of their own (the stored callsign is still the generated "PILOT-1234").
    /// Mirrors <see cref="KongregateBridge"/> so the local board and the account agree on a name;
    /// a custom callsign the player typed is never clobbered. Re-checked on every refresh rather
    /// than latched, so a name that changes server-side follows along (the guard also allows
    /// re-adopting over a name this code itself applied).
    /// </summary>
    private static void AdoptUsername()
    {
        string name = Username;                       // NG account name, "" until logged in
        if (string.IsNullOrWhiteSpace(name) || name == adoptedFrom) return;
        if (IsGeneratedCallsign(GameFlow.Username) || GameFlow.Username == adoptedFrom)
        {
            adoptedFrom = name;
            GameFlow.Username = name;
            Debug.Log("Newgrounds: adopted account callsign '" + name + "'.");
        }
    }

    /// <summary>True when the stored callsign is one the game generated ("PILOT-1234"), i.e. the
    /// player has not chosen their own. Only then may we substitute the Newgrounds name.</summary>
    private static bool IsGeneratedCallsign(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        if (!name.StartsWith("PILOT-", StringComparison.Ordinal)) return false;
        int n;
        return int.TryParse(name.Substring("PILOT-".Length), out n);
    }

    // ------------------------------------------------------------------ score submission

    /// <summary>Posts the player's best Harvester token haul for a world (message from the
    /// leaderboard's own-best path). No-op for guests, unconfigured boards or non-positive scores.</summary>
    public static void SubmitHarvest(int world, int tokens) => SubmitBoard(NewgroundsConfig.HarvestBoards, world, tokens);

    /// <summary>Posts the player's best Ridge Survival distance (metres) for a world.</summary>
    public static void SubmitSurvival(int world, int metres) => SubmitBoard(NewgroundsConfig.SurvivalBoards, world, metres);

    private static void SubmitBoard(int[] boards, int world, int value)
    {
        if (!HasUser || runner == null || value <= 0) return;
        if (boards == null || world < 0 || world >= boards.Length) return;
        int id = boards[world];
        if (!NewgroundsConfig.Configured(id)) return;
        runner.StartCoroutine(NGIO.PostScore(id, value));
        Debug.Log("Newgrounds: posted " + value + " to board " + id + " (world " + world + ").");
    }

    // ------------------------------------------------------------------ secret awards

    /// <summary>Every nugget hauled back on a Prospector run.</summary>
    public static void AwardProspectorPerfect() => AwardSecret(SecretProspectorPerfect);
    /// <summary>80% of a Harvester world's ore collected.</summary>
    public static void AwardHarvestingHero() => AwardSecret(SecretHarvestingHero);
    /// <summary>Reached the landing pad on an empty tank.</summary>
    public static void AwardLastGasp() => AwardSecret(SecretLastGasp);
    /// <summary>Reached the landing pad under 5% hull.</summary>
    public static void AwardRaggedEnd() => AwardSecret(SecretRaggedEnd);

    private static void AwardSecret(int bit)
    {
        int mask = 1 << bit;
        int achieved = SaveBridge.GetInt(AchievedKey, 0);
        if ((achieved & mask) == 0)
        {
            SaveBridge.SetInt(AchievedKey, achieved | mask);
            SaveBridge.Save();
            Debug.Log("Newgrounds: secret " + bit + " earned locally.");
        }
        // A logged-in player gets it immediately; a guest's bit waits here until they log in.
        FlushSecrets();
    }

    private static void FlushSecrets()
    {
        if (!HasUser || runner == null) return;
        int achieved = SaveBridge.GetInt(AchievedKey, 0);
        int posted   = SaveBridge.GetInt(PostedKey, 0);
        int pending  = achieved & ~posted;
        if (pending == 0) return;

        int nowPosted = posted;
        if (TryPostSecret(pending, SecretProspectorPerfect, NewgroundsConfig.MedalProspectorPerfection)) nowPosted |= 1 << SecretProspectorPerfect;
        if (TryPostSecret(pending, SecretHarvestingHero,    NewgroundsConfig.MedalHarvestingHero))       nowPosted |= 1 << SecretHarvestingHero;
        if (TryPostSecret(pending, SecretLastGasp,          NewgroundsConfig.MedalLastGasp))             nowPosted |= 1 << SecretLastGasp;
        if (TryPostSecret(pending, SecretRaggedEnd,         NewgroundsConfig.MedalRaggedEnd))            nowPosted |= 1 << SecretRaggedEnd;

        if (nowPosted != posted)
        {
            SaveBridge.SetInt(PostedKey, nowPosted);
            SaveBridge.Save();
        }
    }

    private static bool TryPostSecret(int pending, int bit, int medalId)
    {
        if ((pending & (1 << bit)) == 0) return false;
        // Keep the bit pending until the medal has an ID, so nothing is silently lost.
        if (!NewgroundsConfig.Configured(medalId)) return false;
        Unlock(medalId);
        return true;
    }

    // ------------------------------------------------------------------ medal ladder

    private static void UnlockLadder()
    {
        int planets = LevelManager.PlanetCount;

        bool allBronze   = planets > 0;   // any placing on every world
        bool allPlatinum = planets > 0;   // Platinum on every world
        int bestProspector = 0;

        for (int w = 0; w < planets; w++)
        {
            int ridge = LevelManager.GetRidgeBestGrade(w);
            if (ridge >= 0) Unlock(NewgroundsConfig.MedalRidgeRookie);   // any placing, any world
            if (ridge >= 2) Unlock(NewgroundsConfig.MedalRidgeRunner);   // Gold, any world
            if (ridge >= 3) Unlock(NewgroundsConfig.MedalRidgeLegend);   // Platinum, any world
            if (ridge < 0)  allBronze = false;
            if (ridge < 3)  allPlatinum = false;

            // Per-world ladders: the medal for world w is earned by clearing grade (w + 1) there,
            // i.e. one more clear than the world before it (1,2,3,4,5 clears for Moon..Titan).
            // The clear count is the site counter, and a world's grade is always that count + 1.
            int need = w + 1;
            int harvestSites    = LevelManager.GetHarvestSitesPassed(w);
            int prospectorSites = LevelManager.GetProspectorSitesPassed(w);
            bestProspector = Mathf.Max(bestProspector, prospectorSites);

            if (harvestSites >= need && w < NewgroundsConfig.HarvestMedalsByWorld.Length)
                Unlock(NewgroundsConfig.HarvestMedalsByWorld[w]);
            // Index 0 is 0 (no Prospector medal for the Moon); Unlock(0) is a safe no-op.
            if (prospectorSites >= need && w < NewgroundsConfig.ProspectorMedalsByWorld.Length)
                Unlock(NewgroundsConfig.ProspectorMedalsByWorld[w]);
        }

        if (allBronze)   Unlock(NewgroundsConfig.MedalRidgeRaider);     // Bronze+ on all five worlds
        if (allPlatinum) Unlock(NewgroundsConfig.MedalRidgeAscendant);  // Platinum on all five worlds

        if (bestProspector >= 1) Unlock(NewgroundsConfig.MedalFirstClaim);
    }

    private static void Unlock(int medalId)
    {
        if (!HasUser || runner == null) return;
        if (!NewgroundsConfig.Configured(medalId)) return;
        if (attemptedMedals.Contains(medalId)) return;

        NewgroundsIO.objects.Medal medal = NGIO.GetMedal(medalId);
        if (medal != null && medal.unlocked)
        {
            // Already held on the server; remember so we never ask again this session.
            attemptedMedals.Add(medalId);
            return;
        }

        attemptedMedals.Add(medalId);
        runner.StartCoroutine(NGIO.UnlockMedal(medalId));
        Debug.Log("Newgrounds: unlocking medal " + medalId + ".");
    }
}
