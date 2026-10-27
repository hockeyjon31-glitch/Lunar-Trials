using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
/// <summary>
/// Kongregate client-API facade, shaped after <see cref="CrazyGamesBridge"/> but deliberately
/// far smaller: this project takes no Kreds, so the only surface it needs is identity plus
/// the Statistics API.
///
/// Design rules, all learned from the CrazySDK situation:
///
///   * NOTHING here is compiled out or written behind an SDK - Kongregate's API is a plain
///     JavaScript object the parent page hands us, not a native SDK with its own
///     RuntimeInitializeOnLoadMethod. All gating is done at RUNTIME by <c>LunarKongOnHost()</c>,
///     so a build uploaded to itch.io or CrazyGames evaluates to a permanent no-op without a
///     separate prepare/finish dance.
///   * STATE IS POLLED, not pushed. Same reasoning as FullscreenButton: the player can sign in
///     out-of-band and the browser sees it before we do. So there is no login callback -
///     <see cref="Poll"/> reads username/guest each tick and the caller reacts to changes.
///   * Stats are re-submitted on every load, which Kongregate explicitly asks for, because a
///     dropped connection during a run would otherwise lose the record permanently.
///
/// MONETIZATION SEAM: Virtual Goods (Kreds) is intentionally NOT implemented. If it is ever
/// wanted, this class is where a <c>PurchaseItem</c> / <c>HasItem</c> pair belongs - Kongregate
/// requires emailing developers@kongregate.com for permission before integrating it, and a
/// startup re-check of the player's Kongregate inventory for uncredited purchases. Adding it
/// here keeps the client API surface in one file instead of spreading it across callers.
/// </summary>
public static class KongregateBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int LunarKongOnHost();
    [DllImport("__Internal")] private static extern int LunarKongInit();
    [DllImport("__Internal")] private static extern int LunarKongReady();
    [DllImport("__Internal")] private static extern void LunarKongSubmitStat(string name, int value);
    // A raw byte buffer rather than StringBuilder: the jslib writes UTF-8 with stringToUTF8,
    // and byte[] crossing this boundary has no encoding ambiguity.
    [DllImport("__Internal")] private static extern int LunarKongGetUsername(byte[] buffer, int size);
    [DllImport("__Internal")] private static extern int LunarKongIsGuest();
    [DllImport("__Internal")] private static extern void LunarKongShowRegistration();
#else
    private static int LunarKongOnHost() { return 0; }
    private static int LunarKongInit() { return 0; }
    private static int LunarKongReady() { return 0; }
    private static void LunarKongSubmitStat(string name, int value) { }
    private static int LunarKongGetUsername(byte[] buffer, int size) { return 0; }
    private static int LunarKongIsGuest() { return 0; }
    private static void LunarKongShowRegistration() { }
#endif

    private const float PollInterval = 0.5f;
    private const int UsernameBuffer = 128;

    private static bool enabled;          // we are literally on kongregate.com
    private static bool ready;            // the API object has arrived
    private static bool statsSent;        // load-time stat submission is done
    private static string adoptedFrom;    // the Kongregate name we last applied to the player
    private static float nextPollAt;
    private static string lastUsername = "";
    private static bool lastGuest;

    private static readonly byte[] nameBuffer = new byte[UsernameBuffer];

    /// <summary>True when the game is running on Kongregate's own host.</summary>
    public static bool Enabled => enabled;

    /// <summary>True once the client API object is available and stat submission will land.</summary>
    public static bool Ready => ready;

    /// <summary>Player as Kongregate knows them ("" until the API is up, or when not hosted).</summary>
    public static string Username { get; private set; } = "";

    /// <summary>Signed-out visitors. False while the API is unknown, so callers never prompt a
    /// guest button they cannot yet justify.</summary>
    public static bool IsGuest { get; private set; }

    /// <summary>Short status for a build footer, mirroring CrazyGamesBridge.StateLabel.</summary>
    public static string StateLabel =>
        !enabled ? "off" :
        !ready ? "connecting" :
        statsSent ? (IsGuest ? "guest" : "signed in") : "loading";

    /// <summary>Kick off detection. Cheap and idempotent - safe from any Awake.</summary>
    public static void Init()
    {
        if (LunarKongOnHost() != 0) enabled = true;
        if (enabled) LunarKongInit();
    }

    /// <summary>Rate-limited refresh. Drives init, identity adoption and the load-time stat
    /// burst. Returns early when the host is not Kongregate, so a foreign build spends
    /// nothing per tick.</summary>
    public static void Poll()
    {
        if (!enabled || Time.unscaledTime < nextPollAt) return;
        nextPollAt = Time.unscaledTime + PollInterval;

        if (!ready)
        {
            if (LunarKongReady() == 0) return;
            ready = true;
        }

        // Identity: adopt the platform's name, but never clobber a callsign the player typed.
        // Re-checked every tick rather than latched, so a guest who registers mid-session gets
        // the new name; the guard is that we only ever replace a name this code itself applied,
        // or a generated "PILOT-1234" the player never chose.
        System.Array.Clear(nameBuffer, 0, nameBuffer.Length);
        int bytes = LunarKongGetUsername(nameBuffer, nameBuffer.Length);
        if (bytes > 0)
        {
            Username = Encoding.UTF8.GetString(nameBuffer, 0, Mathf.Min(bytes, nameBuffer.Length));
            if (!string.IsNullOrWhiteSpace(Username) && Username != adoptedFrom
                && (IsGeneratedCallsign(GameFlow.Username) || GameFlow.Username == adoptedFrom))
            {
                adoptedFrom = Username;
                GameFlow.Username = Username;
            }
        }

        bool guest = LunarKongIsGuest() != 0;
        if (!statsSent)
        {
            statsSent = true;
            SubmitStats();
        }
        else if (guest != lastGuest || Username != lastUsername)
        {
            // A guest who registers mid-session must not lose their progress: the platform
            // name is re-applied, and the stat burst runs again so their records follow them.
            SubmitStats();
        }

        lastGuest = guest;
        lastUsername = Username;
        IsGuest = guest;
    }

    /// <summary>Raised by the register control when it is clicked.</summary>
    public static void ShowRegistrationBox()
    {
        if (ready) LunarKongShowRegistration();
    }

    // ------------------------------------------------------------------- stats
    // Kongregate's boards only sort descending and have no per-stat direction, so every
    // metric reported here is a COMPLETION COUNTER (higher = further along), not a time.
    // That is also what makes them good Badge material later: they move at clear,
    // measurable moments rather than on every score.

    /// <summary>Submits the whole completion set. Called on first connect (Kongregate asks for
    /// stats on every load), and again whenever the player's identity changes.</summary>
    public static void SubmitStats()
    {
        if (!ready) return;

        // Required by the submission checklist: Kongregate filter player ratings through it.
        Submit("initialized", 1);

        // Every counter below is deliberately reported ONLY once the player has actually
        // achieved it. The underlying saves use -1 for "never", and clamping that to 0 would
        // make a fresh profile claim BRONZE it never earned - a false record on a public board.
        // Leaving an unearned stat unsubmitted keeps it simply absent for that player.
        //
        // NOTE the harvest grade is intentionally absent from this set: GetHarvestGrade is the
        // difficulty of the NEXT run (1 = fresh field, defaults to 1 on untouched worlds), so it
        // reports what the player has yet to do, not what they did. Harvest progression is
        // already covered by harvest_stars and worlds_harvested.
        int harvestStars = 0, worldsHarvested = 0;
        int bestRaceTier = 0, bestRaceGrade = 0;
        int ridgeBronze = 0, ridgeSilver = 0, ridgeGold = 0, ridgePlat = 0;
        int bestRaceTimeCs = 0;
        bool anyTier = false, anyRaceGrade = false;

        for (int w = 0; w < LevelManager.PlanetCount; w++)
        {
            // One site = one harvest star (Levels.cs), capped per world, so this is the real
            // measure of harvest depth across the campaign.
            harvestStars += LevelManager.GetHarvestSitesPassed(w);
            if (LevelManager.IsHarvestCleared(w)) worldsHarvested++;

            int tier = LevelManager.GetRaceBestTier(w);
            if (tier >= 0)
            {
                anyTier = true;
                bestRaceTier = Mathf.Max(bestRaceTier, tier);
            }

            int grade = LevelManager.GetRaceBestGrade(w);
            if (grade >= 0)
            {
                anyRaceGrade = true;
                bestRaceGrade = Mathf.Max(bestRaceGrade, grade);
            }

            // Ridge ladder: GameFlow.RidgeGradeNames = { BRONZE, SILVER, GOLD, PLATINUM },
            // so -1 = none, 0 = Bronze, 3 = Platinum. Counting "worlds at or above this grade"
            // gives a monotonic ladder of completion counters with no off-by-one at zero.
            int r = LevelManager.GetRidgeBestGrade(w);
            if (r >= 0) ridgeBronze++;
            if (r >= 1) ridgeSilver++;
            if (r >= 2) ridgeGold++;
            if (r >= 3) ridgePlat++;

            // Fastest race time: Lunar Race is currently the Canyon course only (Ridge is
            // Survival now and records no times). The leaderboard already keeps each world's
            // best, so this is a pure read of the campaign-wide minimum.
            int courseBest = Leaderboard.BestRaceWorld(w, "canyon");
            if (courseBest > 0 && (bestRaceTimeCs == 0 || courseBest < bestRaceTimeCs)) bestRaceTimeCs = courseBest;
        }

        Submit("harvest_stars", harvestStars);
        Submit("worlds_harvested", worldsHarvested);
        if (bestRaceTimeCs > 0) Submit("best_race_time_cs", bestRaceTimeCs);   // centiseconds, lower = faster (portal: prefer minimum)
        if (anyTier) Submit("best_race_tier", bestRaceTier + 1);   // 1=BRONZE .. 3=GOLD
        if (anyRaceGrade) Submit("best_race_grade", bestRaceGrade + 1);   // 1..5 grades

        Submit("ridge_bronze_worlds", ridgeBronze);
        Submit("ridge_silver_worlds", ridgeSilver);
        Submit("ridge_gold_worlds", ridgeGold);
        Submit("ridge_platinum_worlds", ridgePlat);
    }

    /// <summary>Called from the game's own progress-recording paths so a newly earned tier
    /// reaches the board immediately instead of at the next load.</summary>
    public static void OnProgressChanged()
    {
        if (ready) SubmitStats();
    }

    private static void Submit(string stat, int value)
    {
        if (!ready) return;
        LunarKongSubmitStat(stat, value);
    }

    /// <summary>True when the stored callsign is one the game generated ("PILOT-1234"), i.e.
    /// the player has not chosen their own. Only then may we substitute the Kongregate name.</summary>
    public static bool IsGeneratedCallsign(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        if (!name.StartsWith("PILOT-", System.StringComparison.Ordinal)) return false;
        int n;
        return int.TryParse(name.Substring("PILOT-".Length), out n);
    }
}
