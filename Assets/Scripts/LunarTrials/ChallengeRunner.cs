using UnityEngine;

/// <summary>
/// The Lunar Trials challenge controller: starts a challenge (routing into the standard
/// GameFlow scene launch with the challenge stashed for GameBootstrap's build overrides),
/// owns the completion/medal bookkeeping, and answers the landing-pad ore gate.
///
/// Persistence: clears and best medals live in the save store (cloud-backed on CrazyGames)
/// under `lunar.trials.cleared.<id>` / `lunar.trials.medal.<id>`.
/// </summary>
public static class ChallengeRunner
{
    private const string ClearedPrefix = "lunar.trials.cleared.";
    private const string MedalPrefix = "lunar.trials.medal.";

    /// <summary>The challenge the pending Main-scene build should run (null = a normal
    /// campaign run; cleared when the trials select screen opens).</summary>
    public static ChallengeDef Active { get; private set; }

    /// <summary>The challenge whose clear board is due. Latched at RecordClear so the win
    /// screen shows the Trials board even if the active-challenge pointer is disturbed
    /// between the pad arrival and the boarding cinematic finishing. Consumed on show.</summary>
    public static ChallengeDef PendingWinBoard { get; private set; }

    public static ChallengeDef WinBoardDef => Active ?? PendingWinBoard;

    public static void ConsumeWinBoard()
    {
        PendingWinBoard = null;
    }

    public static void ClearActive()
    {
        Active = null;
        PendingWinBoard = null;
        GameBootstrap.TrialsChallenge = null;
    }

    /// <summary>Editor playtest hook: marks the challenge active for this play session (the
    /// GameBootstrap staging uses it so the run manager, gates and win board all bind).</summary>
    public static void ActivateForSession(ChallengeDef def)
    {
        Active = def;
    }

    /// <summary>Full restart of the active challenge: a fresh scene launch through the same
    /// StartChallenge path, so every run counter, pod, nugget and the clock start at zero.
    /// Pairs the platform lifecycle events across the reload (the new build fires a fresh
    /// GameplayStart).</summary>
    public static void RestartActive()
    {
        if (Active == null) return;
        if (AdCoordinator.GameplayLive)
        {
            CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.GameplayStop);
            AdCoordinator.SetGameplayLive(false);
        }
        StartChallenge(Active);
    }

    /// <summary>Opens the next challenge in the ladder (the clear just unlocked it). Falls
    /// back to the main menu at the end of the chain.</summary>
    public static void StartNext()
    {
        ChallengeDef next = Active != null ? ChallengePack.NextAfter(Active.id) : null;
        if (next != null) StartChallenge(next);
        else GameFlow.GoToMenu();
    }

    /// <summary>Launches a challenge: stashes it for GameBootstrap, then routes through the
    /// standard GameFlow scene start for its mode (all builds happen on the Moon, index 0).
    /// No-op outside the Lunar Trials edition.</summary>
    public static void StartChallenge(ChallengeDef def)
    {
        if (def == null || !GameEdition.IsTrials)
        {
            Debug.LogWarning("[Trials] StartChallenge refused: def=" + (def == null ? "null" : def.id)
                + ", IsTrials=" + GameEdition.IsTrials);
            return;
        }
        Debug.Log("[Trials] StartChallenge: " + def.id + " -> launching Main");
        Active = def;
        GameBootstrap.TrialsChallenge = def;
        switch (def.mode)
        {
            case ChallengeDef.Mode.Prospector:
                GameFlow.Mode = GameMode.Prospector;
                GameFlow.StartProspectorAt(0);
                break;
            case ChallengeDef.Mode.Ridge:
                GameFlow.Mode = GameMode.Survival;
                GameFlow.StartSurvivalAt(0, RaceCourse.Ridge);
                break;
            case ChallengeDef.Mode.Race:
                GameFlow.Mode = GameMode.Race;
                GameFlow.StartRaceAt(0, RaceCourse.Canyon);
                break;
            default:
                GameFlow.Mode = GameMode.Harvester;
                GameFlow.StartHarvesterAt(0);
                break;
        }
    }

    /// <summary>The landing-pad ore gate: a challenge only banks its clear once the buggy
    /// arrives with the target ore aboard (challenges without a target always pass).</summary>
    public static bool OreGateOpen(OxygenManager oxygen)
    {
        if (Active == null || Active.oreTarget <= 0) return true;
        if (oxygen == null) return true;
        return oxygen.HarvestablesCollected >= Active.oreTarget;
    }

    public static bool IsCleared(ChallengeDef def)
    {
        return def != null && SaveBridge.GetInt(ClearedPrefix + def.id, 0) != 0;
    }

    public static string MedalOf(ChallengeDef def)
    {
        return def != null ? SaveBridge.GetString(MedalPrefix + def.id, "") : "";
    }

    /// <summary>Banks a clear (and its medal, keeping the best) for the active challenge.</summary>
    public static void RecordClear(float elapsedSeconds)
    {
        if (Active == null) return;
        PendingWinBoard = Active;   // latch the win board through the boarding cinematic
        SaveBridge.SetInt(ClearedPrefix + Active.id, 1);
        string medal = Active.MedalFor(elapsedSeconds);
        if (!string.IsNullOrEmpty(medal) && MedalRank(medal) > MedalRank(MedalOf(Active)))
        {
            SaveBridge.SetString(MedalPrefix + Active.id, medal);
        }
        SaveBridge.Save();
    }

    private static int MedalRank(string medal)
    {
        if (medal == "GOLD") return 3;
        if (medal == "SILVER") return 2;
        if (medal == "BRONZE") return 1;
        return 0;
    }
}

/// <summary>
/// Trials ice profile: a persistent glaze writer that holds the whole arena at a frozen
/// grip level for the duration of an ice-profile challenge. Added to the buggy by
/// GameBootstrap when the active challenge asks for ice.
/// </summary>
public class TrialsIcePatch : MonoBehaviour
{
    [Tooltip("The glaze level held on the buggy (0 dry .. 1 fully iced).")]
    public float glazeLevel = 0.85f;

    private BuggyController buggy;

    private void Start()
    {
        buggy = FindAnyObjectByType<BuggyController>();
    }

    private void Update()
    {
        if (buggy != null) buggy.glazeFromPatch = glazeLevel;
    }
}
