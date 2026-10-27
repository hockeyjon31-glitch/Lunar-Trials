using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>The playable modes reached from the main menu. Harvester is the
/// planet-by-planet campaign, PROSPECTOR RUN is the canyon slot (a harvesting descent through the
/// world's carved canyon, played in the dark), Race is the fair stock arcade on each world's
/// carved canyon, and Survival is the Ridge played for distance (no rivals, no gates, no time
/// board). Prospector is appended last so the earlier enum values (and the editor playtest
/// SessionState ints) keep their meaning; see LevelManager.ModeOffset for its chain slot.</summary>
public enum GameMode
{
    Harvester,
    Race,
    Survival,
    Prospector,
}

/// <summary>Which course variant the current race uses. Canyon is the default downhill
/// slot-carve; Ridge is a narrow raised crest with collapsing sections.</summary>
public enum RaceCourse
{
    Canyon,
    Ridge,
}

/// <summary>
/// Cross-mode session state and scene routing for the menu-driven flow:
///   Menu scene -> (Harvester | Race) -> Main scene (3D world) -> game over ->
///   Leaderboard scene -> back to Menu.
/// Holds the player's chosen username, the accumulating Harvester score across the
/// current run, and the result handed to the leaderboard screen.
/// </summary>
public static class GameFlow
{
    public const string UsernameKey = "lunar.username";
    public const string SaveVersionKey = "lunar.save.version";
    // Shown as a tiny footer on the menu and leaderboard so a stale/cached build is obvious.
    public const string BuildTag = "v3.18f-CGSDK";
    private const string MenuScene = "Menu";
    private const string GameScene = "Main";
    private const string LeaderboardScene = "Leaderboard";
    // Bump when the save schema or game rules change: the stale account (wallet, unlocks,
    // boards, username) is wiped so the player restarts fresh under the current rules.
    // Bumped to 3.0 for v3.15c: the Ridge is no longer a race - its old race-time boards are
    // gone and the fresh best-distance-survived boards take their place.
    // Bumped to 3.1 for v3.15j (release prep): removes the test-account's fake 999,999-token
    // wallet and all-worlds-unlocked state banked during development playtesting, so the first
    // released build opens with a genuinely fresh from-scratch start.
    // Bumped to 3.2 for v3.15u (one-currency economy): clears the old points-era wallet and
    // unlocks so the token rebalance is assessed from a clean slate.
    // Bumped to 3.3 for v3.15v (check-point unlock chain): progression moved away from token
    // thresholds. Current unlocks are derived from saved clear flags, and existing saves are
    // preserved as the chain evolves.
    private const string SaveVersion = "3.3";

    /// <summary>Which mode a fresh Main-scene load should build.</summary>
    public static GameMode Mode { get; set; } = GameMode.Harvester;

    /// <summary>Which course variant the current race uses (Canyon or Ridge).</summary>
    public static RaceCourse CurrentCourse { get; set; } = RaceCourse.Canyon;

    // True only when the Main (game) scene was legitimately reached through the menu. If the
    // Main scene is loaded without this flag (e.g. the editor opens Main.unity directly and
    // presses Play), the game routes back to the Menu instead of dropping the player straight
    // into a run.
    public static bool IsLaunchAuthorized { get; private set; }

    // ------------------------------------------------------------------- username
    public static string Username
    {
        get { return SaveBridge.GetString(UsernameKey, ""); }
        set { SaveBridge.SetString(UsernameKey, Sanitize(value)); SaveBridge.Save(); }
    }

    /// <summary>If no callsign was ever entered, assigns a fresh random pilot name so every run
    /// always leads to a named leaderboard entry (never the anonymous 'username' fallback).</summary>
    public static void EnsureUsername()
    {
        if (!string.IsNullOrWhiteSpace(Username)) return;
        Username = "PILOT-" + Random.Range(1000, 10000);
    }

    /// <summary>Name shown on the leaderboard; falls back to 'username' when none was entered.</summary>
    public static string DisplayName
    {
        get
        {
            string u = Username;
            return string.IsNullOrWhiteSpace(u) ? "username" : u;
        }
    }

    // ---------------------------------------- Harvester scoring.
    // Harvester tokens reset every level and that run's total is banked on the world's own
    // leaderboard, so there is no cross-level session total anymore. The score IS the run's
    // tokens (nuggets 1:1 + spare-oxygen bonus) - the win, board and unlock gate all use it.
    public static int FinalScore;
    public static string FinalDetail;
    // Race result: true when the finished race beat (or set) the world's fastest time.
    // FinalScore holds the race time in centiseconds for the win/leaderboard screens.
    public static bool RaceNewBest;

    /// <summary>Grade the current race is run at (0 .. 4, index-aligned with the five race
    /// grades; grade N+1 is the next star to earn on the world select). Set when the race
    /// scene spawns its rival field.</summary>
    public static int RaceGradeIndex = 0;

    /// <summary>Star count of a star grade earned for the FIRST TIME on the run that just
    /// closed (0 = none). The results flash it up (StarCeremony) in the grade's own colours,
    /// and the world-select board pulses the same stars via LevelManager's session flags.
    /// Read-then-cleared by the boards; also reset on every fresh run.</summary>
    public static int NewStarGradeCount = 0;

    /// <summary>World index of a planet whose HARVEST the race that just finished UNLOCKED
    /// for the first time (-1 = none). Beating BRONZE in a world's RACE is what opens the
    /// NEXT world's HARVEST; the win recorder flags it the moment that check point first
    /// lands, so the congrats board can headline the new world. Read-then-cleared by the
    /// board; also reset on every fresh run.</summary>
    public static int NewWorldUnlockIndex = -1;

    /// <summary>World index whose RIDGE SURVIVAL was unlocked by the just-finished clear (-1 = none).
    /// The Ridge opens after its Prospector Run is cleared; the congrats board announces it.
    /// Read-then-cleared by the board; also reset on every fresh run.</summary>
    public static int NewSurvivalUnlockIndex = -1;

    /// <summary>Clears the accumulated run state (called when a new game starts from the menu).</summary>
    public static void ResetRun()
    {
        FinalScore = 0;
        FinalDetail = "";
        RaceNewBest = false;
        RaceGradeIndex = 0;
        NewStarGradeCount = 0;
        NewWorldUnlockIndex = -1;
        NewSurvivalUnlockIndex = -1;
        RunTokensEarned = 0;
        RaceTokensEarned = 0;
        FirstClearBonus = 0;
        FirstMedalBonus = 0;
        FirstRunBonus = 0;
        ProspectorFirstClearBonus = 0;
        ProspectorPerfectBonus = 0;
        HarvestAllBonus = 0;
        RaceResult = RivalRacer.RaceResult.Lost;
        RidgeSurvivalLoops = 0;
        RidgeSurvivalMeters = 0f;
        RidgeGradeBits = 0;
        RidgeCarryOxygenPct = -1f;   // fresh deploy: full tank
        RidgeCarryHullPct = -1f;     // fresh deploy: pristine hull
        RidgeRunFailed = false;
        // Per-run ad placement flags (revive used, typed run state) must start clean too.
        TokensDoubled = false;
        AdCoordinator.ResetRun();
    }

    // --------------------------------------------- Ridge Survival (the Ridge race replay).
    // The RIDGE race plays as SURVIVAL: no rivals, no gates, no time board - just distance.
    // A ridge run keeps going past the finish: boarding the lander banks the completed leg,
    // then a fresh deploy at the crest starts the next leg with the hazard ramp stepped up.
    // Both counters are STATIC so they survive the scene reload between legs (ResetRun clears
    // them whenever a brand-new run launches from a menu).
    /// <summary>Completed legs this session (each is one full run of the ridge course).</summary>
    public static int RidgeSurvivalLoops = 0;
    /// <summary>Total distance in metres banked across completed legs (the running score).</summary>
    public static float RidgeSurvivalMeters = 0f;

    // Ridge SURVIVAL token tiers: drive a graded CROSSING LINE and the tier pays tokens once
    // (Bronze 1.5 km, Silver 3 km, Gold 4.5 km, Platinum 8 km). The distances are deliberately
    // not tidy leg multiples - each line lands mid-leg through the storm, and Platinum only
    // shows inside the seventh leg, so it demands a real seven-leg slog.
    /// <summary>Cumulative survival-metre thresholds for each grade, in order Bronze->Platinum.</summary>
    public static readonly float[] RidgeGradeDistances = { 1500f, 3000f, 4500f, 8000f };
    /// <summary>Display names for the four grades (index-aligned with RidgeGradeDistances).</summary>
    public static readonly string[] RidgeGradeNames = { "BRONZE", "SILVER", "GOLD", "PLATINUM" };
    /// <summary>Tokens banked the first time each grade's crossing line is driven past.</summary>
    public static readonly int[] RidgeGradeTokens = { 40, 80, 120, 200 };
    /// <summary>Painted line + label colour per grade.</summary>
    public static readonly Color[] RidgeGradeColors =
    {
        new Color(0.72f, 0.5f, 0.26f),
        new Color(0.384f, 0.506f, 0.624f),
        new Color(1f, 0.83f, 0.18f),
        new Color(0f, 0.808f, 0.82f),
    };
    /// <summary>Bitmask of grade tiers already banked this run (one bit per grade).</summary>
    public static int RidgeGradeBits;

    /// <summary>Index of the HIGHEST grade banked this run (Bronze=0 .. Platinum=3), or -1
    /// when no tier line has been crossed yet. Drives the outcome screen's headline.</summary>
    public static int HighestRidgeGradeIndex()
    {
        for (int i = RidgeGradeTokens.Length - 1; i >= 0; i--)
            if ((RidgeGradeBits & (1 << i)) != 0) return i;
        return -1;
    }

    /// <summary>True once a Ridge Survival run has ENDED IN FAILURE (out of oxygen / fell off the
    /// ridge). The next Ridge load treats itself as a RETRY and starts over from leg one; a load
    /// that follows a CLEARED leg leaves this false, so the boarding loop keeps its banked distance,
    /// oxygen and hull. Set by RidgeRunTracker, consumed in GameBootstrap.BuildRidgeSurvival.</summary>
    public static bool RidgeRunFailed;

    /// <summary>Wipes only the Ridge Survival progress: leg count, banked distance, grade bits and the
    /// carried oxygen / hull. A fresh run (or a retry after a failure) starts from this state.</summary>
    public static void ResetRidgeProgress()
    {
        RidgeSurvivalLoops = 0;
        RidgeSurvivalMeters = 0f;
        RidgeGradeBits = 0;
        RidgeCarryOxygenPct = -1f;
        RidgeCarryHullPct = -1f;
        RidgeRunFailed = false;
    }

    /// <summary>O2 percent to carry into the NEXT leg (set at boarding; -1 = fresh tank).</summary>
    public static float RidgeCarryOxygenPct = -1f;
    /// <summary>Hull percent to carry into the NEXT leg (set at boarding; -1 = pristine hull).</summary>
    public static float RidgeCarryHullPct = -1f;

    /// <summary>True while the current build is the Ridge played as survival (its own mode-flavoured
    /// replacement for the rival race). Purely mode/course based: a Survival launch always runs the
    /// Ridge crest.</summary>
    public static bool IsRidgeSurvival => Mode == GameMode.Survival && CurrentCourse == RaceCourse.Ridge;

    /// <summary>True for the modes that play a harvesting run rather than a race: the planet
    /// survey HARVESTER and its canyon counterpart PROSPECTOR RUN. Both run the live oxygen
    /// clock, scatter ore + O2 canisters, bank tokens and record a survival clear - the only
    /// difference is that Prospector drives the world's carved canyon (in the dark). Guard
    /// harvest-shaped gameplay on this, never on <c>Mode == GameMode.Harvester</c> alone.</summary>
    public static bool IsHarvestStyle => Mode == GameMode.Harvester || Mode == GameMode.Prospector;

    // ---------------------------------------- token rewards from the just-finished run.
    // Tokens are earned at run end (the full run total in Harvester, rival-beats in Race)
    // and shown on the result screen; the Wallet in the garage spends them on upgrades.
    // In Harvester the tokens ARE the leaderboard score - there is no points/token split.
    /// <summary>Tokens banked by the finished Harvester run (nuggets 1:1 + oxygen bonus).</summary>
    public static int RunTokensEarned;
    /// <summary>Tokens banked by beating race rivals (0 when no rival was beaten).</summary>
    public static int RaceTokensEarned;
    /// <summary>One-time first-clear bonus credited to the wallet this run (100 on the Moon,
    /// 0 otherwise). Separate from RunTokensEarned so it never touches the world's board.</summary>
    public static int FirstClearBonus;
    /// <summary>One-time first-run WELCOME bonus credited to the wallet the moment the player's
    /// FIRST Moon harvest attempt CONCLUDES, win or lose (100; see LevelManager.FirstAttemptBonusAmount
    /// and TryClaimFirstAttemptBonus). Not paid on merely starting the level - only when that first
    /// harvest attempt reaches its end, by clearing or by dying, so it rewards a full effort either
    /// way. Moon harvest only (level 0); another world's first harvest must not pay it. Shown as its
    /// own line on the end-of-run board, stacked on top of any clear/bonus that same first run
    /// earned.</summary>
    public static int FirstRunBonus;
    /// <summary>One-time first-medal bonus credited to the wallet on the first-ever canyon-race
    /// medal (100; see LevelManager.FirstMedalBonusAmount and TryClaimFirstMedalBonus). Shown as
    /// its own line on the race finish board, distinct from the rival-beat pot in RaceTokensEarned.</summary>
    public static int FirstMedalBonus;
    /// <summary>One-time 100-token bonus credited when a Prospector Run is cleared for the first
    /// time. Failed attempts never receive it.</summary>
    public static int ProspectorFirstClearBonus;
    /// <summary>PERFECT Prospector Run bonus credited this run (150) when every ore nugget was
    /// collected. Zero on any run that missed even one nugget. Shown as its own board line.</summary>
    public static int ProspectorPerfectBonus;
    /// <summary>One-time all-grades bonus for clearing EVERY harvest grade on a world (100),
    /// set when the last site star is earned. Not yet in the wallet - it banks when the star
    /// ceremony popup's OK is pressed (see StarCeremony) so the reward lands on acknowledgment.</summary>
    public static int HarvestAllBonus;
    /// <summary>Wallet balance at the moment the run scene loaded. Every token banked during
    /// the run (nuggets + oxygen bonus + first-clear + rival-beat + ridge tiers) lands on top
    /// of it, so the win board can count the whole run's GAIN onto the banked display. Snapshots
    /// fresh on every scene load in GameBootstrap.Awake.</summary>
    public static int RunStartTokens;
    /// <summary>Which rival tier the finished race beat (or Lost).</summary>
    public static RivalRacer.RaceResult RaceResult = RivalRacer.RaceResult.Lost;
    /// <summary>True when this run banks DOUBLE tokens (the rewarded token-doubler was granted
    /// on the intro deploy hold). Every Harvester token path - LevelScore(), CreditRunParts(),
    /// the win board and the world board - goes through the doubled value, so the wallet, the
    /// score board and the ranking all agree. Reset with the run in ResetRun.</summary>
    public static bool TokensDoubled;

    // ------------------------------------------------------------------- routing
    /// <summary>Wipes every saved value once when the save version changes. A cheap no-op
    /// otherwise; call at the start of every scene so any load resets a mismatched account.</summary>
    public static void ResetSaveIfVersionChanged()
    {
        if (SaveBridge.GetString(SaveVersionKey, "") == SaveVersion) return;
        SaveBridge.DeleteAll();
        SaveBridge.SetString(SaveVersionKey, SaveVersion);
        SaveBridge.Save();
    }

    /// <summary>Marks the save as the current version (stamps + flushes). A full wipe that goes
    /// around ResetSaveIfVersionChanged (the [TEST] START FRESH button) must call this right
    /// after DeleteAll, otherwise the very next scene boot sees the version key missing and wipes
    /// everything AGAIN - deleting whatever the player earned in that first fresh window.</summary>
    public static void StampSaveVersion()
    {
        SaveBridge.SetString(SaveVersionKey, SaveVersion);
        SaveBridge.Save();
    }

    /// <summary>When true, the next Menu-scene load opens the planet map instead of the menu
    /// (a failed Harvester run returns there).</summary>
    public static bool PlanetMapQueued { get; set; }

    /// <summary>When true, the next Menu-scene load opens the GARAGE instead of the menu (the
    /// token badge on a congrats / commiseration board is a garage shortcut). Mutually exclusive
    /// with <see cref="PlanetMapQueued"/> - whichever screen asked for the hop wins.</summary>
    public static bool GarageQueued { get; set; }

    /// <summary>Leaves the finished run for the GARAGE, the same route the menu screens' token
    /// badge takes. The run is over either way, so the per-run state is reset and the CrazyGames
    /// gameplay session is closed exactly like GoToMenu / GoToPlanetMap.</summary>
    public static void GoToGarage()
    {
        CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.GameplayStop);
        IsLaunchAuthorized = false;
        ResetRun();
        PlanetMapQueued = false;
        GarageQueued = true;
        SceneManager.LoadScene(MenuScene);
    }

    public static void GoToMenu()
    {
        CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.GameplayStop);
        IsLaunchAuthorized = false; // a future Main load must come through the menu again
        ResetRun();
        SceneManager.LoadScene(MenuScene);
    }

    /// <summary>Returns a failed Harvester run to the planet-selector screen.</summary>
    public static void GoToPlanetMap()
    {
        CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.GameplayStop);
        IsLaunchAuthorized = false;
        ResetRun();
        PlanetMapQueued = true;
        SceneManager.LoadScene(MenuScene);
    }

    public static void StartHarvester()
    {
        StartHarvesterAt(0); // fallback: the map screen always picks an explicit planet
    }

    /// <summary>If a Start* is called while the game scene is already live (a win-screen
    /// REMATCH / RETRY / NEXT relaunches the level directly, without routing through the map),
    /// close the current CrazyGames gameplay session first so Start/Stop stay balanced.</summary>
    private static void StopIfInGameplay()
    {
        if (SceneManager.GetActiveScene().name == GameScene)
            CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.GameplayStop);
    }

    /// <summary>Launches Lunar Harvester on a specific (unlocked) planet index.</summary>
    public static void StartHarvesterAt(int index)
    {
        StopIfInGameplay();
        IsLaunchAuthorized = true;
        Mode = GameMode.Harvester;
        // Harvest has no course variant, but CurrentCourse is sticky state that outlives the run
        // that set it (it is only assigned by the race/survival entry points). Leaving the last
        // course in place let a run that followed the RIDGE carry CurrentCourse == Ridge into
        // harvest, where mode-aware code read it as still being on the ridge - which put the
        // ridge's crevice warning towers on a harvest map that has no gaps in it. Clearing it
        // here means every mode starts from a known course instead of inheriting the last one.
        CurrentCourse = RaceCourse.Canyon;
        ResetRun();
        LevelManager.CurrentLevelIndex = Mathf.Clamp(index, 0, LevelManager.PlanetCount - 1);
        SceneManager.LoadScene(GameScene);
    }

    /// <summary>Launches Lunar Race on a specific (unlocked) planet index. Race is the fair
    /// stock arcade: no oxygen clock, no garage upgrades, and no weather hazards - the course's
    /// downhill canyon is the only opponent. Every world is a race track with its own time board.</summary>
    public static void StartRaceAt(int index)
    {
        StartRaceAt(index, RaceCourse.Canyon);
    }

    /// <summary>Launches Lunar Race on a specific planet with the chosen course variant.</summary>
    public static void StartRaceAt(int index, RaceCourse course)
    {
        StopIfInGameplay();
        IsLaunchAuthorized = true;
        Mode = GameMode.Race;
        CurrentCourse = course;
        ResetRun();
        LevelManager.CurrentLevelIndex = Mathf.Clamp(index, 0, LevelManager.PlanetCount - 1);
        SceneManager.LoadScene(GameScene);
    }

    /// <summary>Launches Ridge SURVIVAL on a specific planet: the Ridge crest played for
    /// distance - no rivals, no gates, no time board - with O2 canisters refilling the tank.</summary>
    public static void StartSurvivalAt(int index)
    {
        StartSurvivalAt(index, RaceCourse.Ridge);
    }

    /// <summary>Launches Ridge Survival on a specific planet with the course variant. Survival is
    /// the last step of a planet's chain (its canyon race opens it) and is launchable on every
    /// build - the guard lives in LevelManager.IsModeUnlocked, not here.</summary>
    public static void StartSurvivalAt(int index, RaceCourse course)
    {
        StopIfInGameplay();
        IsLaunchAuthorized = true;
        Mode = GameMode.Survival;
        CurrentCourse = course;
        ResetRun();
        LevelManager.CurrentLevelIndex = Mathf.Clamp(index, 0, LevelManager.PlanetCount - 1);
        SceneManager.LoadScene(GameScene);
    }

    /// <summary>Launches PROSPECTOR RUN on a specific planet: a harvesting descent down the
    /// world's carved canyon - live oxygen clock, ore and O2 canisters, but played in the dark
    /// with a limited field of vision. It occupies the canyon slot of the chain; surviving it
    /// opens this world's RIDGE SURVIVAL.</summary>
    public static void StartProspectorAt(int index)
    {
        StopIfInGameplay();
        IsLaunchAuthorized = true;
        Mode = GameMode.Prospector;
        // The canyon lane is what Prospector runs: MoonTerrainGen carves it when Mode is Race,
        // Prospector or Ridge Survival, and branches on CurrentCourse (anything but Ridge is
        // Canyon). Pin it here so the lane is guaranteed.
        CurrentCourse = RaceCourse.Canyon;
        ResetRun();
        LevelManager.CurrentLevelIndex = Mathf.Clamp(index, 0, LevelManager.PlanetCount - 1);
        SceneManager.LoadScene(GameScene);
    }

    /// <summary>
    /// Editor helper: authorises a direct Main-scene load so "Play Test Level" enters gameplay
    /// straight away (the menu flow normally does this). Optional planet index; -1 keeps the
    /// current level selection.
    /// </summary>
    public static void AuthorizeLaunch(int levelIndex = -1)
    {
        IsLaunchAuthorized = true;
        Mode = GameMode.Harvester;
        ResetRun();
        if (levelIndex >= 0)
        {
            LevelManager.CurrentLevelIndex = Mathf.Clamp(levelIndex, 0, LevelManager.PlanetCount - 1);
        }
    }

    public static void GoToLeaderboard()
    {
        CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.GameplayStop);
        SceneManager.LoadScene(LeaderboardScene);
    }

    /// <summary>Editor-only: queues a direct Harvester launch that survives domain reload when
    /// Enter Play Mode Options is enabled. Stores the planet index in SessionState so
    /// GameBootstrap can restore it after the static fields are wiped by the reload.</summary>
    public static void SetEditorPlaytest(int planetIndex, GameMode mode = GameMode.Harvester)
    {
#if UNITY_EDITOR
        UnityEditor.SessionState.SetInt("lunar.playtest.planet", planetIndex);
        UnityEditor.SessionState.SetInt("lunar.playtest.mode", (int)mode);
#endif
        IsLaunchAuthorized = true;
        Mode = mode;
        ResetRun();
        LevelManager.CurrentLevelIndex = Mathf.Clamp(planetIndex, 0, LevelManager.PlanetCount - 1);
    }

    /// <summary>Editor-only: called at the top of GameBootstrap.Awake to restore a queued editor
    /// playtest whose static intent was wiped by domain reload.</summary>
    public static void RestoreEditorPlaytest()
    {
#if UNITY_EDITOR
        // SessionState has no HasKey: a planet index (never -1) already doubles as the
        // "was a playtest queued?" sentinel, so an absent key reads back as -1.
        int planet = UnityEditor.SessionState.GetInt("lunar.playtest.planet", -1);
        int mode   = UnityEditor.SessionState.GetInt("lunar.playtest.mode", (int)GameMode.Harvester);
        UnityEditor.SessionState.EraseInt("lunar.playtest.planet");
        UnityEditor.SessionState.EraseInt("lunar.playtest.mode");
        if (planet >= 0)
        {
            IsLaunchAuthorized = true;
            Mode = (GameMode)mode;
            ResetRun();
            LevelManager.CurrentLevelIndex = Mathf.Clamp(planet, 0, LevelManager.PlanetCount - 1);
        }
#endif
    }

    /// <summary>Human-readable name for a race course variant.</summary>
    public static string CourseName(RaceCourse course)
    {
        return course == RaceCourse.Ridge ? "RIDGE" : "CANYON";
    }

    /// <summary>Leaderboard key suffix for a race course variant.</summary>
    public static string CourseKey(RaceCourse course)
    {
        return course == RaceCourse.Ridge ? "ridge" : "canyon";
    }

    /// <summary>Trims a raw username to something safe to display (max 16 chars, no control chars).</summary>
    public static string Sanitize(string raw)
    {
        if (raw == null) return "";
        string trimmed = raw.Trim();
        if (trimmed.Length > 16) trimmed = trimmed.Substring(0, 16);
        var sb = new System.Text.StringBuilder(trimmed.Length);
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (c == '\r' || c == '\n' || c == '\t') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
}