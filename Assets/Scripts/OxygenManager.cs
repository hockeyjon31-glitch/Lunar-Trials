using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
    /// Global game state: tracks the player's oxygen, which gradually declines over time.
    /// Reaching an oxygen pod (checkpoint) tops the oxygen back up.
    /// If oxygen reaches zero the game is over: the run's result is recorded and the game
    /// closes the 3D world - Survival to its own Leaderboard screen, Harvester back to the
    /// planet map (see GameFlow).
    ///
    /// In Harvester Mode every nugget banks its planet's TOKENS DIRECTLY (Moon 2, Mars 3,
    /// Venus 4, Io 5, Titan 6), and spare oxygen adds 2 tokens per 10% saved. The run's
    /// total tokens are at once the garage reward AND the score on that world's leaderboard
    /// - there is no points/credit layer separate from tokens. Race mode banks tokens for
    /// beating rival tiers, and opens the next course by spending tokens.
    ///
    /// This component should be placed once in the scene on a "GameManager" GameObject.
    /// </summary>
public class OxygenManager : MonoBehaviour
{
    public static OxygenManager Instance { get; private set; }

    [Header("Oxygen")]
    [Tooltip("Starting oxygen amount (0..100).")]
    public float maxOxygen = 100f;
    [Tooltip("Oxygen lost per second of gameplay.")]
    public float drainPerSecond = 2.2f;
    [Tooltip("Oxygen restored when an oxygen pod / canister is collected.")]
    public float restorePerPod = 30f;
    [Tooltip("Oxygen restored while resting at a checkpoint (out of the buggy gameplay, unused here).")]
    public float checkpointRegenPerSecond = 6f;
    [Tooltip("TOKENS earned per unit of ending oxygen after the level is cleared (the buggy's "
            + "remaining oxygen drains away during boarding): 2 tokens per 10% of the tank.")]
    public float bonusPerOxygenUnit = 0.2f;
    [Tooltip("Delay after the game-over banner before the next screen loads (short: the claxon cuts and the result shows straight away).")]
    public float gameOverToBoardDelay = 0.8f;
    [Tooltip("How fast the buggy's remaining oxygen drains to zero during the boarding "
            + "sequence, converting into bonus TOKENS along the way.")]
    public float bonusDrainPerSecond = 40f;
    [Tooltip("Grace period (seconds) to reach a canister after oxygen hits zero. Collecting one in time lets the run continue; otherwise it is game over.")]
    public float o2GraceSeconds = 5f;

    [Header("State")]
    public bool gameOver = false;
    public bool reachedEnd = false;
    [Tooltip("Why the game ended; shown on the HUD banner.")]
    public string gameOverReason = "";
    [Tooltip("True while the end-of-level lander sequence plays (oxygen clock is paused).")]
    public bool endSequenceActive = false;
    [Tooltip("True while the race-start countdown runs: the race clock is frozen and ElapsedTime "
            + "does not advance until 'GO' hands the player control (Lunar Race only).")]
    public bool clockPaused = false;
    [Tooltip("Race-only: the run ended by the post-bronze TIME LIMIT (see LevelEndSequence) rather "
            + "than by crossing the line. The race is concluded normally, but the DNF time is NOT "
            + "written to the world's fastest-time board.")]
    public bool RaceTimeoutConcluded { get; private set; }

    /// <summary>Flags the run as concluded by the race time limit, then completes it through
    /// the normal race-result path (boarding skipped - see LevelEndSequence). The recorded time
    /// is kept out of the fastest-time board because the player never crossed the line.</summary>
    public void ConcludeRaceTimeout()
    {
        RaceTimeoutConcluded = true;
        CompleteLevel();
    }

    public float CurrentOxygen { get; private set; }

    /// <summary>Restores a carried-in O2 level (Ridge Survival loops the scene: the next
    /// deploy keeps the tank percentage the previous leg finished with).</summary>
    public void SetOxygenFraction(float fraction)
    {
        CurrentOxygen = maxOxygen * Mathf.Clamp01(fraction);
        OnOxygenChanged?.Invoke();
    }
    public float ElapsedTime { get; private set; }
    public int PodsCollected { get; private set; }
    public int TotalPods { get; private set; }

    /// <summary>True while the buggy is on the zero-oxygen grace clock (must reach a canister).</summary>
    public bool OxygenGraceActive { get; private set; }
    /// <summary>Seconds left on the zero-oxygen grace clock before the run ends.</summary>
    public float OxygenGraceRemaining { get; private set; }

    // --- Harvester Mode run tracking (reset per level; the tally IS the token score) ---
    public int HarvestablesCollected { get; private set; }
    public int TotalHarvestables { get; private set; }
    /// <summary>Tokens banked by the nuggets so far (each nugget is worth its planet's
    /// token value: Moon 2, Mars 3, Venus 4, Io 5, Titan 6). The popup and the live HUD
    /// counter always agree exactly.</summary>
    public int RunHarvestPoints { get; private set; }
    /// <summary>Token gain of the last nugget collected, so the collect popup and the live
    /// counter always agree exactly.</summary>
    public int LastOreGain { get; private set; }

    /// <summary>End-of-round spare-oxygen BONUS tokens (2 per 10% of the tank). Nothing is
    /// earned during play; once the final pod is collected and the boarding sequence starts,
    /// the buggy's remaining oxygen drains away and becomes these tokens on the HUD.
    /// Finishing with more oxygen left always converts to more bonus.</summary>
    public int BonusPoints { get; private set; }
    /// <summary>Fractional bonus banked so far; whole tokens are published to <see cref="BonusPoints"/>.</summary>
    private float bonusAccum;

    /// <summary>Raised when any oxygen pod/canister is collected (SurvivalDirector counts sets).</summary>
    public event Action OnRunPodCollected;
    /// <summary>Raised when the final pod is collected; the level-end cinematic listens for it.</summary>
    public event Action OnFinalPodCollected;
    public event Action OnGameOver;
    public event Action OnOxygenChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        CurrentOxygen = maxOxygen;
        gameOver = false;
        reachedEnd = false;
        gameOverReason = "";
        ElapsedTime = 0f;
        PodsCollected = 0;
        TotalPods = 0;
        HarvestablesCollected = 0;
        TotalHarvestables = 0;
        RunHarvestPoints = 0;
        LastOreGain = 0;
        BonusPoints = 0;
        bonusAccum = 0f;
        endSequenceActive = false;
    }

    private void Update()
    {
        // The run is over once the end-sequence starts (or on game over / win).
        if (gameOver || reachedEnd) return;

        // The race clock stays frozen through the 3-2-1-GO ceremony (race countdown only).
        if (clockPaused) return;

        if (endSequenceActive)
        {
            // Lunar Race has no BONUS conversion: boarding the lander FREEZES the clock at
            // the finish time and the run is done. Ridge Survival does the same - a cleared
            // leg banks distance, never points.
            if (GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival) return;

            // LUNAR TRIALS: no oxygen-bonus conversion either - the Trials payout is the
            // medal formula on the congrats board (Gold 150 / Silver 135 / Bronze 120 /
            // none 100, +25 no-damage), so the old harvester +N ticker stays silent here.
            if (ChallengeRunner.Active != null)
            {
                ElapsedTime += Time.deltaTime;   // clock keeps rolling for the medal timer
                return;
            }

            // BONUS conversion: the level is cleared, so the buggy's remaining oxygen drains
            // away fast and converts into TOKENS (2 per 10% of the tank). Tokens tick up as
            // the tank empties, then freeze for the win screen.
            if (CurrentOxygen > 0f)
            {
                float converted = Mathf.Min(bonusDrainPerSecond * Time.deltaTime, CurrentOxygen);
                CurrentOxygen -= converted;
                bonusAccum += converted * bonusPerOxygenUnit;
                BonusPoints = Mathf.FloorToInt(bonusAccum);
                OnOxygenChanged?.Invoke();
            }
            return;
        }

        // Oxygen drains over time.
        CurrentOxygen -= drainPerSecond * Time.deltaTime;
        ElapsedTime += Time.deltaTime;

        CurrentOxygen = Mathf.Max(0f, CurrentOxygen);
        OnOxygenChanged?.Invoke();

        if (CurrentOxygen <= 0f)
        {
            // Zero oxygen does not kill instantly: start a short grace clock during which the
            // buggy can still coast to a canister. Collecting one refills oxygen and cancels it.
            if (!OxygenGraceActive)
            {
                OxygenGraceActive = true;
                OxygenGraceRemaining = o2GraceSeconds;
            }
            else
            {
                OxygenGraceRemaining -= Time.deltaTime;
                if (OxygenGraceRemaining <= 0f)
                {
                    OxygenGraceRemaining = 0f;
                    GameOver();
                }
            }
        }
        else if (OxygenGraceActive)
        {
            // Oxygen was refilled above zero - the grace period is over.
            OxygenGraceActive = false;
            OxygenGraceRemaining = 0f;
        }

        // Ridge Survival banks tokens the moment a graded crossing line is driven past.
        if (GameFlow.IsRidgeSurvival) CheckRidgeGrades();
    }

    /// <summary>Trails the run's cumulative survival distance and banks the TOKEN TIER for
    /// every grade line the buggy has driven past (Bronze 1.5 km, Silver 3 km, Gold 4.5 km,
    /// Platinum 8 km). Each grade pays exactly once per run: a bitmask remembers what is
    /// already banked, so redeploying a fresh leg never hands the same tier out twice.</summary>
    private void CheckRidgeGrades()
    {
        BuggyController bug = FindAnyObjectByType<BuggyController>();
        if (bug == null) return;
        float progress = Mathf.Max(0f, bug.transform.position.z - MoonTerrainGen.RaceStartZ);
        float meters = GameFlow.RidgeSurvivalMeters + progress;

        for (int i = 0; i < GameFlow.RidgeGradeTokens.Length; i++)
        {
            int bit = 1 << i;
            if ((GameFlow.RidgeGradeBits & bit) != 0) continue;
            if (meters < GameFlow.RidgeGradeDistances[i]) continue;
            GameFlow.RidgeGradeBits |= bit;

            int tokens = GameFlow.RidgeGradeTokens[i];
            UpgradeSystem.AddTokens(tokens);
            ScorePopup.Spawn(bug.transform.position + Vector3.up * 2.5f,
                "+" + tokens + " TOKENS  " + GameFlow.RidgeGradeNames[i] + " TIER",
                GameFlow.RidgeGradeColors[i]);
            Debug.Log("Lunar Buggy: ridge survival banked " + GameFlow.RidgeGradeNames[i]
                + " tier at " + FormatKm(meters) + " - " + tokens + " tokens.");
        }
    }

    /// <summary>Called by an oxygen pod / canister when the buggy reaches it.</summary>
    public void CollectPod(bool isFinalPod)
    {
        PodsCollected++;
        CurrentOxygen = Mathf.Min(maxOxygen, CurrentOxygen + restorePerPod);
        // A collected canister cancels the zero-oxygen grace clock (oxygen is above zero again).
        OxygenGraceActive = false;
        OxygenGraceRemaining = 0f;
        OnOxygenChanged?.Invoke();
        OnRunPodCollected?.Invoke();

        // CrazyGames first-run onboarding: after the first canister, point at the next one and
        // explain the green-lit final canister + pickup rocket. No-op off portal/editor.
        TutorialCoach.NotifyPodCollected(PodsCollected, isFinalPod);

        if (isFinalPod && OnFinalPodCollected != null)
        {
            // Hand the baton to the level-end cinematic (lander + boarding).
            OnFinalPodCollected?.Invoke();
        }
    }

    /// <summary>Pauses the normal oxygen clock and starts the end-of-level bonus conversion:
    /// the buggy's remaining oxygen drains fast and becomes bonus TOKENS on the HUD while
    /// the boarding cinematic plays. Nothing is converted during play.</summary>
    public void BeginEndSequence()
    {
        endSequenceActive = true;
    }

    /// <summary>Tells the game the level is genuinely cleared (lander boarding finished).</summary>
    public void CompleteLevel()
    {
        if (reachedEnd) return;
        ReachedEnd();
    }

    private void GameOver()
    {
        BeginGameOver("OUT OF OXYGEN");
    }

    /// <summary>Called by a harvestable nugget (Helium-3 etc.) when the buggy touches it.
    /// The nugget's value banks straight into the run's token tally (1:1).</summary>
    public void CollectHarvestable(int points)
    {
        if (gameOver || reachedEnd) return;
        HarvestablesCollected++;
        RunHarvestPoints += points;
        LastOreGain = points; // the nugget's token value

        // CrazyGames first-run onboarding: what ore is worth and where it banks. No-op off portal/editor.
        TutorialCoach.NotifyOreCollected(points);
    }

    /// <summary>Registers how many harvestables the current level spawned.</summary>
    public void RegisterTotalHarvestables(int count)
    {
        TotalHarvestables = count;
    }

    /// <summary>Ends the run because the buggy was destroyed (hull integrity reached zero).</summary>
    public void BuggyDestroyed()
    {
        BeginGameOver("HULL DESTROYED");
    }

    /// <summary>Ends the run because the buggy lost communication with the relay. (Lunar Survival.)</summary>
    public void OutOfRange()
    {
        BeginGameOver("OUT OF COMMUNICATION RANGE");
    }

    /// <summary>Ends the run because the buggy dropped off the ridge (Ridge Survival): the
    /// screen has already faded to black; this just locks it in with the distance reached.</summary>
    public void FallOffRidge()
    {
        BeginGameOver("FELL OFF THE RIDGE");
    }

    /// <summary>
    /// Dropped into one of the Ridge's hop gaps. Deliberately NOT folded into
    /// <see cref="FallOffRidge"/>: that reason is excluded from the revive offer (its screen has
    /// already faded), and this death is the one case where a rewind is the right answer - the
    /// gap walls are unclimbable and the run-up behind a gap is far too short to retry it, so
    /// the stuck-reset would just drop the player back in with the same impossible jump. Any
    /// OTHER Ridge reason falls into AdCoordinator's checkpoint slot, which offers the rewarded
    /// ad to respawn at the last 200 m marker - which is behind the gap with the whole straight
    /// to build speed on. Without ads the run just ends here, same as any other ridge death.
    /// </summary>
    public void FellIntoGap()
    {
        BeginGameOver("FELL INTO A GAP");
    }

    private void BeginGameOver(string reason)
    {
        if (gameOver) return;
        gameOver = true;
        gameOverReason = reason;

        // Death means the run's harvest is lost: nothing is banked, no ore credits. Only a
        // cleared planet (ReachedEnd) pays out - keeping the run stake meaningful.

        // The run is over: the buggy must not keep driving under the game-over banner.
        BuggyController buggy = FindAnyObjectByType<BuggyController>();
        if (buggy != null) buggy.inputEnabled = false;

        // REVIVE OFFERS (rewarded ads, dormant while AdCoordinator.AdsEnabled is false - the
        // Basic Launch default): a Harvester O2 death can refill the tank and continue in
        // place; a Ridge Survival death (other than falling off the crest, whose screen has
        // already faded) can respawn at the last 200 m marker; a hull destruction on
        // Harvester/Race grants 50% of the hull back in place. Offers are OCCASIONAL - only
        // every 3rd revive-eligible death gets one (persisted counter in AdCoordinator).
        // While the offer panel is up the world is frozen and the failure is
        // NOT recorded - accepting the ad grants the continue (ReviveContinue), declining
        // falls through to the normal fail flow. With ads off the offer never appears.
        if (AdCoordinator.TryOfferRevive(this, reason)) return;

        // Lunar Trials: fail fast WITH a press to retry - the world freezes, the reason shows
        // on a fail panel, and RETRY reloads the challenge completely fresh.
        if (TrialsRunManager.TrialsActive && TrialsRunManager.Current.TryFailPanel(reason)) return;

        FinishGameOver();
    }

    /// <summary>Completes a FAILED run: records the result, raises the game-over signal and
    /// schedules the commiserations board. Split out of BeginGameOver so a pending rewarded
    /// revive offer can back out before anything is recorded.</summary>
    public void FinishGameOver()
    {
        RecordResultToLeaderboard();
        AwardFirstRunBonus();
        OnGameOver?.Invoke();

        // Give the banner a moment, then close the 3D world and show the outcome.
        Invoke(nameof(ShowDefeatScreen), gameOverToBoardDelay);
    }

    /// <summary>Pays the one-time first-Moon-harvest welcome bonus the moment the player's very
    /// first Moon harvest attempt CONCLUDES - win or lose (100 to the wallet). Harvest on the Moon
    /// (level 0) only: the prospector and the ridge never trigger it, and a first visit to
    /// Mars/Venus/Io/Titan must not either. Called from <see cref="ReachedEnd"/> on a clear and from
    /// <see cref="FinishGameOver"/> on a death, so either end of that first attempt announces it.
    /// Never paid on merely starting the level. The claim flag is persisted, so later runs pay
    /// nothing and the hundred stacks on top of whatever that first run earned.</summary>
    private void AwardFirstRunBonus()
    {
        if (GameFlow.Mode != GameMode.Harvester) return;
        if (LevelManager.CurrentLevelIndex != 0) return;
        if (!LevelManager.TryClaimFirstAttemptBonus()) return;
        GameFlow.FirstRunBonus = LevelManager.FirstAttemptBonusAmount;
        UpgradeSystem.AddTokens(GameFlow.FirstRunBonus);
        Debug.Log("Lunar Buggy: first harvest run bonus paid "
            + GameFlow.FirstRunBonus + " tokens to the wallet.");
    }

    /// <summary>Revives the run after a rewarded ad completes. O2Refill tops the tank and hands
    /// back the wheel where the run stopped (Harvester); CheckpointRespawn (Ridge Survival)
    /// snaps the buggy onto the last banked 200 m marker with a full tank and a full hull;
    /// HullRepair keeps the run in place with 50% of the hull back (any mode's hull death).
    /// The run resumes exactly where the ad left it.</summary>
    public void ReviveContinue(AdCoordinator.ReviveKind kind)
    {
        gameOver = false;
        gameOverReason = "";
        reachedEnd = false;
        endSequenceActive = false;
        clockPaused = false;

        BuggyController buggy = FindAnyObjectByType<BuggyController>();

        if (kind == AdCoordinator.ReviveKind.O2Refill)
        {
            // The run simply continues from where the tank ran dry: full tank, no grace clock.
            SetOxygenFraction(1f);
            OxygenGraceActive = false;
            OxygenGraceRemaining = 0f;
        }
        else if (kind == AdCoordinator.ReviveKind.HullRepair)
        {
            // The run continues where the hull gave out: half the max hull back, in place.
            BuggyHealth bh = FindAnyObjectByType<BuggyHealth>();
            if (bh != null) bh.SetHealthFraction(0.5f);
        }
        else
        {
            // Ridge Survival: snap back to the closest 200 m marker behind the death point
            // (the painted crest strips are laid every 200 m in BuildRidgeTargetMarkers).
            MoonTerrainGen t = FindAnyObjectByType<MoonTerrainGen>();
            if (buggy != null && t != null)
            {
                float progress = Mathf.Max(0f, buggy.transform.position.z - MoonTerrainGen.RaceStartZ);
                float dz = Mathf.Floor(progress / 200f) * 200f;
                float z = MoonTerrainGen.RaceStartZ + dz;
                float x = t.RaceCenterX(z);
                float y = t.SampleHeight(new Vector3(x, 0f, z)) + 1.5f;

                Rigidbody rb = buggy.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                buggy.transform.position = new Vector3(x, y, z);
                buggy.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            }
            SetOxygenFraction(1f);
            OxygenGraceActive = false;
            OxygenGraceRemaining = 0f;
            BuggyHealth bh = FindAnyObjectByType<BuggyHealth>();
            if (bh != null) bh.SetHealthFraction(1f);
        }

        if (buggy != null) buggy.inputEnabled = true;

        string pop = kind == AdCoordinator.ReviveKind.HullRepair ? "HULL REPAIRED - CONTINUING"
            : kind == AdCoordinator.ReviveKind.O2Refill ? "TANK REFILLED - CONTINUING"
            : "RESPAWNED - CONTINUING";
        ScorePopup.Spawn(
            buggy != null ? buggy.transform.position : new Vector3(0f, 2f, 0f),
            pop,
            UpgradeSystem.TokenColor);
    }

    /// <summary>Closes the 3D world after a run ends: a failed run goes to the commiserations
    /// screen so the player can retry or quit. INTERSTITIAL SLOT (midgame, dormant): the run
    /// is over and the board is the natural break - AdCoordinator shows one interstitial here
    /// per session (with the mandatory gameplayStop) and only then builds the board; with ads
    /// off it builds the board immediately, exactly as today.</summary>
    private void ShowDefeatScreen()
    {
        AdCoordinator.MaybeRunEndMidgame(BuildCommiserationsOverlay);
    }

    /// <summary>Shows the end-of-run screen. A failed run with NO tier to show gets the
    /// commiserations board ("RACE LOST" on race/survival). If a ridge survival tier WAS
    /// banked first, the screen flips to a congrats board instead - the headline names the
    /// highest tier earned, and there is no sad sting for a banked tier.</summary>
    private void BuildCommiserationsOverlay()
    {
        int tier = GameFlow.HighestRidgeGradeIndex();
        bool tierWon = tier >= 0;
        // The end-of-run board always rings in the game-over music, win or lose.
        RoverSounds.PlayGameOverMusic();

        Canvas canvas = UiKit.CreateRootCanvas("CommiserationsCanvas", 200);
        UiKit.EnsureEventSystem();
        // Lunar artwork backdrop under a translucent veil, same stack as the menu screens, so
        // the end-of-run board feels like part of the game's world (muted by the dim overlay).
        UiKit.Background(canvas.transform, "LunarBackground", "LunarBackground");
        RectTransform dim = UiKit.Panel(canvas.transform, "ComDim", Vector2.zero, Vector2.zero, new Vector2(1f, 1f), new Color(0.01f, 0.02f, 0.06f, 0.62f));
        UiKit.StretchToFull(dim);

        Color dimWhite = new Color(1f, 1f, 1f, 0.7f);

        bool isRace = GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival;

        if (GameFlow.IsRidgeSurvival && tierWon)
        {
            string tierName = GameFlow.RidgeGradeNames[tier];
            UiKit.Label(canvas.transform, "ComTitle", tierName + " TIER", 60,
                GameFlow.RidgeGradeColors[tier], new Vector2(0f, -150f), TextAnchor.MiddleCenter, 1400f, 90f,
                UiKit.UiFontKind.Display);
            UiKit.Label(canvas.transform, "ComReason",
                "CONGRATS - " + GameFlow.RidgeGradeTokens[tier] + " TOKENS BANKED",
                32, Color.white, new Vector2(0f, -245f), TextAnchor.MiddleCenter, 1400f, 50f);
            UiKit.Label(canvas.transform, "ComTotal",
                "YOU SURVIVED " + FormatKm(GameFlow.FinalScore) + " OF " + GameFlow.FinalDetail.ToUpperInvariant() + " RIDGE",
                30, Color.white, new Vector2(0f, -310f), TextAnchor.MiddleCenter, 1400f, 55f);
            string next = tier + 1 < GameFlow.RidgeGradeNames.Length
                ? "next is " + GameFlow.RidgeGradeNames[tier + 1].ToLowerInvariant()
                    + " at " + FormatKm(GameFlow.RidgeGradeDistances[tier + 1]) + "."
                : "you've topped every tier.";
            UiKit.Label(canvas.transform, "ComNote",
                "The crest took the rover, but the " + tierName.ToLowerInvariant() + " tier is banked - " + next,
                22, dimWhite, new Vector2(0f, -365f), TextAnchor.MiddleCenter, 1400f, 40f);

            // FIRST-time tier banked: flash the earned ridge stars over the board, each in its
            // own grade colour, then fade back to the congrats screen.
            int newStars = GameFlow.NewStarGradeCount;
            if (newStars > 0)
            {
                GameFlow.NewStarGradeCount = 0; // consumed
                StarCeremony.Show(newStars, GameFlow.RidgeGradeNames.Length, GameFlow.RidgeGradeColors,
                    tierName + " TIER REACHED",
                    tier + 1 < GameFlow.RidgeGradeNames.Length
                        ? "NEXT: " + GameFlow.RidgeGradeNames[tier + 1] + " AT " + FormatKm(GameFlow.RidgeGradeDistances[tier + 1])
                        : "EVERY TIER TOPPED ON THIS RIDGE");
            }
        }
        else if (isRace)
        {
            UiKit.Label(canvas.transform, "ComTitle", "RACE LOST", 64,
                new Color(1f, 0.35f, 0.25f, 1f), new Vector2(0f, -150f), TextAnchor.MiddleCenter, 1400f, 90f,
                UiKit.UiFontKind.Display);

            string reason = string.IsNullOrWhiteSpace(gameOverReason) ? "THE RUN ENDED" : gameOverReason;
            UiKit.Label(canvas.transform, "ComReason", reason, 32, dimWhite, new Vector2(0f, -245f), TextAnchor.MiddleCenter, 1400f, 50f);

            if (GameFlow.IsRidgeSurvival)
            {
                UiKit.Label(canvas.transform, "ComTotal",
                    "YOU SURVIVED " + FormatKm(GameFlow.FinalScore) + " OF " + GameFlow.FinalDetail.ToUpperInvariant() + " RIDGE",
                    30, Color.white, new Vector2(0f, -310f), TextAnchor.MiddleCenter, 1400f, 55f);

                UiKit.Label(canvas.transform, "ComNote",
                    "The rover was lost - that distance stands. Deploy again and push the crest further.",
                    22, dimWhite, new Vector2(0f, -365f), TextAnchor.MiddleCenter, 1400f, 40f);
            }
            else
            {
                UiKit.Label(canvas.transform, "ComTotal",
                    "YOU RACED " + FormatRaceTime(GameFlow.FinalScore) + " ON " + GameFlow.FinalDetail.ToUpperInvariant(),
                    30, Color.white, new Vector2(0f, -310f), TextAnchor.MiddleCenter, 1400f, 55f);

                UiKit.Label(canvas.transform, "ComNote",
                    "The rover was lost before the finish - no time recorded. Race again and hunt the bronze line.",
                    22, dimWhite, new Vector2(0f, -365f), TextAnchor.MiddleCenter, 1400f, 40f);
            }
        }
        else
        {
            int total = GameFlow.FinalScore;
            UiKit.Label(canvas.transform, "ComSorry", "Sorry", 40, Color.white,
                new Vector2(0f, -310f), TextAnchor.MiddleCenter, 1400f, 55f);
            UiKit.Label(canvas.transform, "ComTotal",
                "Your " + total + " tokens on " + GameFlow.FinalDetail + " were lost with the rover.",
                30, Color.white, new Vector2(0f, -370f), TextAnchor.MiddleCenter, 1400f, 55f);
        }

        // The one-time first-harvest welcome bonus is announced on whichever board that first
        // attempt reached - it is banked to the wallet even though the run itself failed. The
        // harvest/prospector "sorry" board keeps its top free, so the note reads above "Sorry";
        // the race/ridge boards put it in the free band under the note.
        if (GameFlow.FirstRunBonus > 0)
        {
            float bonusY = isRace ? -405f : -250f;
            UiKit.Label(canvas.transform, "ComFirstRun",
                "FIRST RUN BONUS  +" + GameFlow.FirstRunBonus.ToString("N0") + " TOKENS TO WALLET",
                26, UpgradeSystem.TokenColor, new Vector2(0f, bonusY), TextAnchor.MiddleCenter, 1400f, 36f);
        }

        // A lost race still gets its one free-upgrade consolation: crashing the rover (or dying
        // on the ridge with no tier banked) offers the same +1 ENGINE/BOOST ad as finishing
        // last, exactly once per failed run.
        if (isRace && !tierWon) BuildConsolationOffer(canvas);

        // --- Navigation row: NEXT / RETRY / RETURN as image art. The three are always present;
        // NEXT fades out when its destination is still locked (a lost race hasn't won the next
        // world's harvest, a lost harvest hasn't opened this world's PROSPECTOR RUN, a ridge run
        // only moves on once the next world's harvest is open). RETRY always re-deploys the same run.
        string nextTip;
        System.Action nextAction;
        bool nextEnabled;
        int current = LevelManager.CurrentLevelIndex;
        if (GameFlow.IsRidgeSurvival)
        {
            // A ridge run never points NEXT back at its own ridge - it is already open by
            // definition here - so the ridge board always steps on to the next world's harvest.
            int nextIdx = current + 1;
            nextTip = "NEXT WORLD";
            nextAction = () => GameFlow.StartHarvesterAt(nextIdx);
            nextEnabled = nextIdx < LevelManager.PlanetCount
                && LevelManager.IsModeUnlocked(nextIdx, GameMode.Harvester);
        }
        else if (isRace)
        {
            // Same resolver as the race WIN board: this world's RIDGE once its 5 harvest stars
            // are banked, otherwise the NEXT world's harvest - never a race.
            ResolveNextSlot(current, out nextAction, out nextEnabled, out nextTip);
        }
        else
        {
            if (GameFlow.Mode == GameMode.Prospector)
            {
                nextTip = "SURVIVAL RIDGE";
                nextAction = () => GameFlow.StartSurvivalAt(current);
                nextEnabled = LevelManager.IsModeUnlocked(current, GameMode.Survival);
            }
            else
            {
                // A cleared harvest's next slot is this world's Prospector Run.
                bool prospectorOpen = LevelManager.IsModeUnlocked(current, GameMode.Prospector);
                if (prospectorOpen)
                {
                    nextTip = "PROSPECTOR RUN";
                    nextAction = () => GameFlow.StartProspectorAt(current);
                    nextEnabled = true;
                }
                else
                {
                    int nextIdx = current + 1;
                    nextTip = "NEXT WORLD";
                    nextAction = () => GameFlow.StartHarvesterAt(nextIdx);
                    nextEnabled = nextIdx < LevelManager.PlanetCount
                        && LevelManager.IsModeUnlocked(nextIdx, GameMode.Harvester);
                }
            }
        }
        BuildNavRow(canvas.transform, "ComNav", nextTip, nextAction, nextEnabled, "RETRY", RestartLevelFresh);

        AddBankBadge(canvas);
    }

    /// <summary>Resolves what a finish board's NEXT button offers, in the order the player cares
    /// about. The chain proper runs HARVEST(world) -> RACE(world) -> RIDGE(world) ->
    /// HARVEST(world+1). So after a race NEXT prefers this world's own RIDGE when it is open (the
    /// freshly-unlocked content), and otherwise the NEXT world along the chain, whose HARVEST is
    /// the slot this race's bronze does NOT yet open (the ridge does). Never a
    /// race - a race is always this world's own second slot, reached from its harvest board, so
    /// aiming NEXT at one used to look like a dead end.
    /// The world-map button (RETURN) stays on every board for anything NEXT does not offer.</summary>
    /// <param name="current">The world the finished run was played on.</param>
    /// <param name="action">Launches the destination. Always set, so the button exists.</param>
    /// <param name="enabled">False when the destination is still locked (button renders faded).</param>
    /// <param name="tip">Hover caption naming the destination.</param>
    private static void ResolveNextSlot(int current, out System.Action action, out bool enabled, out string tip)
    {
        if (LevelManager.IsModeUnlocked(current, GameMode.Survival))
        {
            action = () => GameFlow.StartSurvivalAt(current);
            enabled = true;
            tip = "SURVIVAL RIDGE";
            return;
        }
        int nextIdx = current + 1;
        action = () => GameFlow.StartHarvesterAt(nextIdx);
        enabled = nextIdx < LevelManager.PlanetCount
            && LevelManager.IsModeUnlocked(nextIdx, GameMode.Harvester);
        tip = "NEXT WORLD";
    }

    /// <summary>Installs the standard finish-screen navigation row: NEXT (right), RETRY (centre),
    /// RETURN (left), laid low enough to clear the headline text above. A shared hover tip pops
    /// in above the row, and a caption under each button spells it out (mobile-friendly).
    /// NEXT is ALWAYS present; when its destination is still locked it renders FADED and cannot
    /// be clicked, so the path onward stays visible even when out of reach this run.</summary>
    private static void BuildNavRow(Transform canvas, string tag,
        string nextTip, System.Action nextAction, bool nextEnabled,
        string retryTip, System.Action retryAction)
    {
        const float ButtonY = -560f;
        const float TipY = -452f;

        Text hoverTip = UiKit.Label(canvas, tag + "HoverTip", "", 30,
            new Color(1f, 0.9f, 0.6f, 1f), new Vector2(0f, TipY), TextAnchor.MiddleCenter, 600f, 40f);
        hoverTip.gameObject.SetActive(false);

        Button next = UiKit.ImageButton(canvas, tag + "_Next", "Next_Button",
            new Vector2(0.5f, 1f), new Vector2(165f, ButtonY), new Vector2(150f, 140f), nextAction);
        UiKit.WireHover(next, hoverTip, nextTip);
        if (!nextEnabled)
        {
            next.interactable = false;
            ColorBlock cb = next.colors;
            cb.disabledColor = new Color(0.5f, 0.5f, 0.55f, 0.2f);
            next.colors = cb;
        }

        Button again = UiKit.ImageButton(canvas, tag + "_Again", "Retry_Button",
            new Vector2(0.5f, 1f), new Vector2(0f, ButtonY), new Vector2(140f, 140f), retryAction);
        UiKit.WireHover(again, hoverTip, retryTip);

        Button map = UiKit.ImageButton(canvas, tag + "_Map", "End",
            new Vector2(0.5f, 1f), new Vector2(-165f, ButtonY), new Vector2(150f, 140f), () => GameFlow.GoToPlanetMap());
        UiKit.WireHover(map, hoverTip, "RETURN");
    }

    /// <summary>Adds the top-left BANKED-TOKENS badge and plays the coin transfer when this run
    /// banked anything into the wallet. The badge starts at the pre-run balance and counts up to
    /// the current total while gold coins arc from a harvested pile into the number. Returns the
    /// badge so a caller can tick further credits onto it (the all-grades bonus banks on OK).
    /// It doubles as the GARAGE SHORTCUT, the same affordance the menu screens' badge has: the
    /// number follows the saved wallet, the coins pulse while an upgrade is affordable, and a
    /// click routes out of the run to the garage (GameFlow.GoToGarage). The transfer composes
    /// with the pulse rather than fighting it - the coin-landing nudge keeps priority over the
    /// pulse's scale swell, and the live-wallet poll stands aside while the count is running.</summary>
    private static WalletBadge AddBankBadge(Canvas canvas)
    {
        int start = Mathf.Min(GameFlow.RunStartTokens, UpgradeSystem.GetTokens());
        int gain = UpgradeSystem.GetTokens() - start;
        WalletBadge badge = WalletBadge.AttachTopLeft(canvas, start);
        if (gain > 0) badge.PlayTransfer(gain, new Vector2(0.5f, 0.16f));
        badge.liveWallet = true;
        badge.SetClickHandler(() => GameFlow.GoToGarage());
        return badge;
    }

    /// <summary>The run's TOKENS, which ARE its score: the nuggets banked 1:1 (each nugget
    /// is worth its planet's token value) plus the spare-oxygen bonus at the end of a
    /// cleared level (2 tokens per 10% of the tank). This single number feeds the win
    /// screen and the world's leaderboard - no separate points layer. (World/mode
    /// progression is the LevelManager check-point chain, not this score.)
    /// A granted token doubler (GameFlow.TokensDoubled) doubles the total right here, so the
    /// wallet credit, the world board and the best-score recording all agree on one figure.</summary>
    public int LevelScore()
    {
        int baseScore = RunHarvestPoints + BonusPoints;
        return GameFlow.TokensDoubled ? baseScore * 2 : baseScore;
    }

    /// <summary>Hands a FAILED run's outcome to GameFlow for the commiserations board. Only a
    /// safely completed run (ReachedEnd) banks anything: a crash never records a leaderboard
    /// entry, a best score or a survival distance - with the one exception of Ridge Survival,
    /// where death IS the end of the run and the distance reached is its score.</summary>
    private void RecordResultToLeaderboard()
    {
        if (GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival)
        {
            // Ridge Survival banks its DISTANCE: no gate/token/time scoring, so a death (fall
            // off the ridge, hull, or O2 out) records where the rover died. Distance is never
            // written to the race TIME boards (they sort times); it lands on the ridge's own
            // best-distance-survived board, and the result screens show it.
            if (GameFlow.IsRidgeSurvival)
            {
                BuggyController bug = FindAnyObjectByType<BuggyController>();
                float progress = bug != null
                    ? Mathf.Max(0f, bug.transform.position.z - MoonTerrainGen.RaceStartZ)
                    : 0f;
                int meters = Mathf.RoundToInt(GameFlow.RidgeSurvivalMeters + progress);
                GameFlow.FinalScore = meters;
                GameFlow.FinalDetail = LevelManager.CurrentLevel != null ? LevelManager.CurrentLevel.planetName : "";
                // Persist the highest grade reached on this world (the ridge is the last step of a
                // planet's chain - its BRONZE check point is what opens the next world).
                // A grade banked for the FIRST time flags the end-of-run star ceremony.
                int runTier = GameFlow.HighestRidgeGradeIndex();
                int prevRidge = LevelManager.GetRidgeBestGrade(LevelManager.CurrentLevelIndex);
                LevelManager.RecordRidgeBestGrade(LevelManager.CurrentLevelIndex, runTier);
                if (runTier > prevRidge)
                {
                    GameFlow.NewStarGradeCount = runTier + 1;
                }
                if (meters > 0)
                {
                    int prevBest = Leaderboard.BestSurvivalWorld(LevelManager.CurrentLevelIndex);
                    GameFlow.RaceNewBest = meters > prevBest;
                    Leaderboard.AddSurvivalWorld(LevelManager.CurrentLevelIndex, GameFlow.DisplayName,
                        meters, GameFlow.FinalDetail, UpgradeSystem.BuildLevel);
                    Debug.Log("Lunar Buggy: ridge survival lost on " + GameFlow.FinalDetail
                        + " at " + FormatKm(meters) + " - distance recorded on the survival board.");
                }
                return;
            }

            // A FAILED race records nothing: only a completed run gets a board spot. The
            // time so far is still handed to GameFlow so the commiserations screen shows it.
            int centi = Mathf.Max(0, Mathf.RoundToInt(ElapsedTime * 100f));
            GameFlow.FinalScore = centi;
            GameFlow.FinalDetail = LevelManager.CurrentLevel != null ? LevelManager.CurrentLevel.planetName : "";
            GameFlow.RaceNewBest = false;
            if (centi > 0)
            {
                Debug.Log("Lunar Buggy: failed " + GameFlow.FinalDetail + " race at " + FormatRaceTime(centi) + " - no time recorded.");
            }
            return;
        }

        // A failed harvest/prospector run records NOTHING: only a safe return (ReachedEnd)
        // banks tokens, a leaderboard entry and a best score. The run's tokens are still handed
        // to GameFlow so the commiserations board can show what was lost with the rover.
        int score = LevelScore();
        GameFlow.FinalScore = score;
        GameFlow.FinalDetail = LevelManager.CurrentLevel != null ? LevelManager.CurrentLevel.planetName : "";
        if (score > 0)
        {
            Debug.Log("Lunar Buggy: failed " + GameFlow.FinalDetail + " run lost its "
                + score + " tokens - nothing recorded.");
        }
    }

    private void ReachedEnd()
    {
        if (reachedEnd) return;
        reachedEnd = true;
        // The player's very first harvest pays its one-time welcome the moment the attempt ends;
        // AwardFirstRunBonus self-gates to the harvest, so the prospector/ridge leg clears ignore it.
        AwardFirstRunBonus();
        if (GameFlow.Mode == GameMode.Harvester) RecordHarvestWin();
        else if (GameFlow.Mode == GameMode.Prospector) RecordProspectorWin();
        else if (GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival)
        {
            if (GameFlow.IsRidgeSurvival) RecordRidgeLegWin();
            else RecordRaceWin();
        }
        // A cleared planet banks the run's tokens (nuggets 1:1 + the spare-oxygen bonus).
        CreditRunParts();
        OnGameOver?.Invoke();

        if (GameFlow.IsRidgeSurvival)
        {
            // Ridge Survival loops STRAIGHT back onto the crest: no leg-complete menu, no
            // intro card. Bank the carry-over (O2 + hull exactly as they were at the line -
            // the boarding freezes them), flash the "LEG CLEARED" banner a beat, reload.
            // Clearing a leg pays out ONE CANISTER'S WORTH of oxygen on top of whatever the
            // tank ended on, so a cleared leg always hands the next crest something to spend.
            // The canister chain up here is deliberately half the length of a full race's, and
            // this is what makes the next leg's first canister comfortably reachable on a stock
            // buggy. It is a top-up, not a refill - a leg finished near-full keeps its own
            // level rather than being tipped over the tank, exactly as collecting a canister
            // near-full behaves.
            float carriedOxygen = Mathf.Min(maxOxygen, CurrentOxygen + restorePerPod);
            GameFlow.RidgeCarryOxygenPct = maxOxygen > 0f ? carriedOxygen / maxOxygen * 100f : 100f;
            BuggyHealth bh = FindAnyObjectByType<BuggyHealth>();
            GameFlow.RidgeCarryHullPct = bh != null ? bh.CurrentHealth / Mathf.Max(1f, bh.maxHealth) * 100f : 100f;
            Invoke(nameof(RestartLevel), 2.0f);
        }
        else
        {
            Invoke(nameof(ShowWin), 2f);
        }
    }

    /// <summary>
    /// A cleared run's tokens (nuggets 1:1 + spare-oxygen bonus) land on that world's own
    /// board, just like a failed run. Surviving the HARVEST is the check point that opens
    /// that world's RACE (the chain's next slot - see LevelManager), so the clear is
    /// recorded here; the best score is tracked separately for the board and the intro.
    /// </summary>
    private void RecordHarvestWin()
    {
        if (GameFlow.Mode != GameMode.Harvester) return;

        // The very first time a world's harvest is survived it pays a one-time bonus (100 tokens
        // on the Moon) so a new player can afford the first garage upgrade immediately. It
        // credits the WALLET only - the world's board keeps the run's own score.
        int world = LevelManager.CurrentLevelIndex;
        int firstClearBonus = LevelManager.IsHarvestCleared(world) ? 0 : LevelManager.FirstClearBonus(world);
        int score = LevelScore();
        LevelManager.RecordHarvestBest(world, score);
        int prevSites = LevelManager.GetHarvestSitesPassed(world);
        LevelManager.RecordHarvestCleared(world);
        // Surviving raises the world's harvest GRADE (2..5) and lights one more site star on
        // the map ladder. The FIRST time a star is earned it flashes up on this congrats board
        // (StarCeremony) and pulses on the HARVESTER tab until that tab has been seen.
        int newSites = LevelManager.GetHarvestSitesPassed(world);
        if (newSites > prevSites)
        {
            GameFlow.NewStarGradeCount = newSites;
        }
        // Passing the LAST grade lights the final site star and banks a one-time 100-token
        // bonus right here - the same wallet pattern as the first-clear bonus (it lands with
        // the run's own tokens and shows on the board as "ALL GRADES CLEARED BONUS"). The same
        // first-time all-grades clear is what opens the planet's bonus RIDGE SURVIVAL (a
        // standalone mode outside the chain, see LevelManager.IsModeUnlocked), so the clear
        // flags it for the congrats board to headline.
        if (newSites >= LevelManager.MaxHarvestGrade && prevSites < LevelManager.MaxHarvestGrade)
        {
            GameFlow.HarvestAllBonus = LevelManager.HarvestAllClearBonusAmount;
            UpgradeSystem.AddTokens(GameFlow.HarvestAllBonus);
            GameFlow.NewSurvivalUnlockIndex = world;
            Debug.Log("Lunar Buggy: all " + LevelManager.MaxHarvestGrade
                + " harvest grades cleared on "
                + (LevelManager.CurrentLevel != null ? LevelManager.CurrentLevel.planetName : "?")
                + " - " + GameFlow.HarvestAllBonus + "-token bonus banked,"
                + " Ridge Survival unlocked.");
        }
        GameFlow.FirstClearBonus = firstClearBonus;
        if (firstClearBonus > 0)
        {
            UpgradeSystem.AddTokens(firstClearBonus);
            Debug.Log("Lunar Buggy: first clear of "
                + (LevelManager.CurrentLevel != null ? LevelManager.CurrentLevel.planetName : "?")
                + " paid a " + firstClearBonus + "-token first-clear bonus.");
        }
        GameFlow.FinalScore = score;
        GameFlow.FinalDetail = LevelManager.CurrentLevel != null ? LevelManager.CurrentLevel.planetName : "";
        if (score > 0)
        {
            Leaderboard.AddWorld(LevelManager.CurrentLevelIndex, GameFlow.DisplayName, score, GameFlow.FinalDetail, UpgradeSystem.BuildLevel);
            Debug.Log("Lunar Buggy: cleared " + GameFlow.FinalDetail + " earned " + score + " tokens on that world's board.");
        }
    }

    /// <summary>
    /// A completed PROSPECTOR RUN: the buggy reached the end of the world's dark canyon lane.
    /// The run scores exactly like a harvest (nuggets 1:1 + spare-oxygen bonus) and banks those
    /// tokens through <see cref="CreditRunParts"/>. Each successful run advances its own
    /// Prospector grade and star ladder independently from Harvest. Surviving it also opens this
    /// world's RIDGE SURVIVAL; the next planet's harvest stays locked until Ridge is completed.
    /// </summary>
    private void RecordProspectorWin()
    {
        if (GameFlow.Mode != GameMode.Prospector) return;

        int world = LevelManager.CurrentLevelIndex;
        int previousStars = LevelManager.GetProspectorSitesPassed(world);
        bool ridgeWasLocked = !LevelManager.IsModeUnlocked(world, GameMode.Survival);
        LevelManager.RecordProspectorCleared(world);
        int newStars = LevelManager.GetProspectorSitesPassed(world);
        if (newStars > previousStars) GameFlow.NewStarGradeCount = newStars;
        GameFlow.NewWorldUnlockIndex = -1;
        GameFlow.NewSurvivalUnlockIndex = ridgeWasLocked ? world : -1;
        int score = LevelScore();
        LevelManager.RecordProspectorBest(world, score);
        GameFlow.FinalScore = score;
        GameFlow.FinalDetail = LevelManager.CurrentLevel != null ? LevelManager.CurrentLevel.planetName : "";
        if (LevelManager.TryClaimProspectorFirstClearBonus())
        {
            GameFlow.ProspectorFirstClearBonus = LevelManager.ProspectorFirstClearBonusAmount;
            UpgradeSystem.AddTokens(GameFlow.ProspectorFirstClearBonus);
        }
        // PERFECT PROSPECTOR: hauling back EVERY nugget in the field pays a one-off perfect bonus.
        if (TotalHarvestables > 0 && HarvestablesCollected >= TotalHarvestables)
        {
            GameFlow.ProspectorPerfectBonus = LevelManager.ProspectorPerfectBonusAmount;
            UpgradeSystem.AddTokens(GameFlow.ProspectorPerfectBonus);
            NewgroundsBridge.AwardProspectorPerfect();   // secret medal: every nugget hauled back
        }
    }

    /// <summary>
    /// A completed Lunar Race: the clock stopped at boarding, so the elapsed time (centiseconds)
    /// goes on that world's own time board (smallest = fastest). Beating a rival tier pays
    /// TOKENS straight into the garage wallet (each tier banks its own TOTAL pot: Bronze 60,
    /// Silver 100, Gold 200; see RivalRacer.TokensEarnedFor). Finishing the race but beating
    /// nobody (last over the line inside the post-bronze window) pays the small RACE-LAST
    /// consolation; never crossing the line (the cutoff concluded, or a crash AFTER bronze
    /// finished) pays nothing. Beating BRONZE also marks the
    /// check point that opens the NEXT planet's HARVEST (the chain's next slot) - no token cost.
    /// Beating the WHOLE field (the GOLD rival) clears the run's RACE GRADE and unlocks the
    /// next, faster grade on this world. The finish time is flagged as NEW BEST when it beats
    /// (or sets) the previous fastest.
    /// </summary>
    private void RecordRaceWin()
    {
        if (GameFlow.Mode != GameMode.Race) return;
        int centi = Mathf.Max(0, Mathf.RoundToInt(ElapsedTime * 100f));
        string courseKey = GameFlow.CourseKey(GameFlow.CurrentCourse);
        int prevBest = Leaderboard.BestRaceWorld(LevelManager.CurrentLevelIndex, courseKey);
        GameFlow.FinalScore = centi;
        GameFlow.FinalDetail = LevelManager.CurrentLevel != null ? LevelManager.CurrentLevel.planetName : "";
        GameFlow.RaceNewBest = !RaceTimeoutConcluded && (prevBest == 0 || centi < prevBest);
        GameFlow.RaceResult = RivalRacer.ResultFor(centi);
        GameFlow.RaceTokensEarned = RivalRacer.TokensEarnedFor(centi, !RaceTimeoutConcluded);
        // The ONE-TIME FIRST MEDAL bonus: the first canyon-race medal ever won (Bronze up, any
        // world) pays a flat 100 tokens straight to the wallet - the race's mirror of the harvest
        // first-clear hook, so a new player can afford the first garage upgrade after a clean
        // canyon run. Scoped to the CANYON course (the only race the mode offers) and gated on a
        // persisted flag so it can never pay twice. Banked before the board builds, so the
        // wallet badge ticks it up with the run's own rival-beat tokens.
        if (GameFlow.CurrentCourse == RaceCourse.Canyon
            && GameFlow.RaceResult != RivalRacer.RaceResult.Lost
            && LevelManager.TryClaimFirstMedalBonus())
        {
            GameFlow.FirstMedalBonus = LevelManager.FirstMedalBonusAmount;
            UpgradeSystem.AddTokens(GameFlow.FirstMedalBonus);
            Debug.Log("Lunar Buggy: first canyon medal paid a " + GameFlow.FirstMedalBonus
                + "-token first-medal bonus to the wallet.");
        }
        // Persist the best tier beaten on this world: beating BRONZE is what opens the NEXT
        // planet's HARVEST (the derived unlock chain reads this store on every load). Flag a
        // FIRST-TIME world unlock (the next planet's harvest going from locked to open) so the
        // congrats board can headline it - only the run that opens it announces it.
        int nextWorld = LevelManager.CurrentLevelIndex + 1;
        bool nextWasLocked = nextWorld < LevelManager.PlanetCount
            && !LevelManager.IsModeUnlocked(nextWorld, GameMode.Harvester);
        LevelManager.RecordRaceBestTier(LevelManager.CurrentLevelIndex, RivalRacer.TierIndexOf(GameFlow.RaceResult));
        GameFlow.NewWorldUnlockIndex = nextWasLocked && nextWorld < LevelManager.PlanetCount
            && LevelManager.IsModeUnlocked(nextWorld, GameMode.Harvester)
            ? nextWorld : -1;
        // Beating the whole field clears this run's grade and unlocks the next, faster one.
        if (RivalRacer.GradeClearedFor(centi))
        {
            // A grade cleared for the FIRST time (the previous best beaten) flashes its stars
            // up on the finish screen and pulses them on the world map's next visit.
            int prevGrade = LevelManager.GetRaceBestGrade(LevelManager.CurrentLevelIndex);
            LevelManager.RecordRaceBestGrade(LevelManager.CurrentLevelIndex, GameFlow.RaceGradeIndex);
            if (GameFlow.RaceGradeIndex > prevGrade)
            {
                GameFlow.NewStarGradeCount = GameFlow.RaceGradeIndex + 1;
            }
        }
        if (GameFlow.RaceTokensEarned > 0)
        {
            UpgradeSystem.AddTokens(GameFlow.RaceTokensEarned);
        }
        // A time-limit-concluded race never crossed the line: the elapsed time is shown on the
        // result board but is not a genuine finish, so it must not displace the world's best.
        if (centi > 0 && !RaceTimeoutConcluded)
        {
            Leaderboard.AddRaceWorld(LevelManager.CurrentLevelIndex, GameFlow.DisplayName, centi, GameFlow.FinalDetail, courseKey, 0);
            Debug.Log("Lunar Buggy: Lunar Race cleared " + GameFlow.FinalDetail
                + " (" + GameFlow.CourseName(GameFlow.CurrentCourse) + ")"
                + " in " + FormatRaceTime(centi) + (GameFlow.RaceNewBest ? " (NEW BEST)" : "")
                + " earned " + GameFlow.RaceTokensEarned + " tokens.");
        }
    }

    /// <summary>
    /// A completed Ridge Survival LEG: the whole course aced in one go, the leg's distance is
    /// banked into the session total and the loop counter steps so the NEXT deploy's hazard
    /// director ramps harder. No time, no rivals, no tokens - accumulated distance is the
    /// only score, shown on the leg-complete screen before the crest redeploys.
    /// </summary>
    private void RecordRidgeLegWin()
    {
        if (!GameFlow.IsRidgeSurvival) return;
        MoonTerrainGen t = FindAnyObjectByType<MoonTerrainGen>();
        float legM = t != null ? (t.RaceFinishZ - MoonTerrainGen.RaceStartZ) : 0f;
        GameFlow.RidgeSurvivalMeters += legM;
        GameFlow.RidgeSurvivalLoops++;
        LevelManager.RecordRidgeCleared(LevelManager.CurrentLevelIndex);
        GameFlow.FinalScore = Mathf.RoundToInt(GameFlow.RidgeSurvivalMeters);
        GameFlow.FinalDetail = LevelManager.CurrentLevel != null ? LevelManager.CurrentLevel.planetName : "";
        GameFlow.RaceNewBest = false;
        // Persist the highest grade reached on this world (the ridge is the last step of a
        // planet's chain - its BRONZE check point opens the next world). A grade banked
        // for the FIRST time is flagged so the ceremony plays: the leg-complete loop reloads
        // straight back onto the crest, so it defers to the first end-of-run board after this
        // (which flags this leg's own highest when it closes).
        int runTier = GameFlow.HighestRidgeGradeIndex();
        int prevRidge = LevelManager.GetRidgeBestGrade(LevelManager.CurrentLevelIndex);
        LevelManager.RecordRidgeBestGrade(LevelManager.CurrentLevelIndex, runTier);
        if (runTier > prevRidge)
        {
            GameFlow.NewStarGradeCount = runTier + 1;
        }
        Debug.Log("Lunar Buggy: ridge survival leg " + GameFlow.RidgeSurvivalLoops
            + " cleared - total " + FormatKm(GameFlow.RidgeSurvivalMeters));
    }

    /// <summary>
    /// Banks the run's tokens into the garage wallet (1:1 - what the HUD counted is exactly
    /// what is banked). The cleared run's nugget tokens plus the spare-oxygen bonus are the
    /// score AND the pay, so the world's leaderboard and the wallet track the exact same
    /// number the player spends on upgrades. Survival is the separate stock arcade and
    /// never pays. Flashes a "+N TOKENS" popup in gold above the buggy.
    /// </summary>
    private void CreditRunParts()
    {
        // Both harvest-shaped modes (Harvester + Prospector Run) bank their tokens 1:1.
        if (!GameFlow.IsHarvestStyle) return;

        // Must equal LevelScore() so the wallet credit, the board score and the win screen
        // agree exactly (a granted doubler doubles all three through LevelScore()).
        GameFlow.RunTokensEarned = LevelScore();

        if (GameFlow.RunTokensEarned > 0)
        {
            UpgradeSystem.AddTokens(GameFlow.RunTokensEarned);
            BuggyController buggy = FindAnyObjectByType<BuggyController>();
            Vector3 at = buggy != null ? buggy.transform.position : new Vector3(0f, 2f, 0f);
            ScorePopup.Spawn(at, "+" + GameFlow.RunTokensEarned + " TOKENS", UpgradeSystem.TokenColor);
        }
    }

    /// <summary>Reloads the current scene, restarting the run on the same planet. Plain reload -
    /// the per-run state is deliberately KEPT (Ridge Survival's leg-continue reload banks O2/hull
    /// carry into statics before this; resetting here would wipe the leg chain).</summary>
    public void RestartLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>Fresh-run RETRY: clears the per-run state (token doubler, revive allowance,
    /// scores) then reloads - so a retried run never inherits a paid doubler without watching
    /// another ad. Used by the finish-screen RETRY buttons, never the Ridge leg-continue.</summary>
    public void RestartLevelFresh()
    {
        GameFlow.ResetRun();
        RestartLevel();
    }

    private void ShowWin()
    {
        // The final pod means this planet is cleared: show the player their tokens and let
        // them choose. Restarting a failed run (RestartLevel) keeps the current planet instead.
        // INTERSTITIAL SLOT (midgame, dormant): same natural break as the defeat board - the
        // run ended, the board is the next thing shown. AdCoordinator inserts one interstitial
        // (gameplayStop first) when live; dormant it calls BuildWinOverlay() right away.
        AdCoordinator.MaybeRunEndMidgame(BuildWinOverlay);
    }

    /// <summary>Shows the clear screen: the run's token score, split into the nuggets banked
    /// and the spare-oxygen bonus (both blue), and a choice between the RACE the clear just
    /// opened, a re-run of the harvest, and the world selector. A Lunar Race gets its own
    /// finish-time screen instead.</summary>
    private void BuildWinOverlay()
    {
        // LUNAR TRIALS: the challenge clear board replaces the campaign congrats screen
        // entirely - no grades, no prospector chain, campaign token split. Own trials payout.
        ChallengeDef trialsDef = ChallengeRunner.WinBoardDef;
        if (trialsDef != null)
        {
            ChallengeRunner.ConsumeWinBoard();
            TrialsWinBoard.Show(trialsDef);
            return;
        }

        if (GameFlow.Mode == GameMode.Race)
        {
            BuildRaceWinOverlay();
            return;
        }

        // Both harvest-shaped modes (Harvester + Prospector Run) share the congrats board.
        if (!GameFlow.IsHarvestStyle)
        {
            GameFlow.GoToPlanetMap();
            return;
        }

        RoverSounds.PlayCelebration();

        int newStars = GameFlow.NewStarGradeCount;
        if (newStars > 0)
        {
            GameFlow.NewStarGradeCount = 0;
            int gradeTotal = GameFlow.Mode == GameMode.Prospector
                ? LevelManager.MaxProspectorGrade : LevelManager.MaxHarvestGrade;
            Color[] starGold = new Color[gradeTotal];
            for (int i = 0; i < starGold.Length; i++) starGold[i] = new Color(1f, 0.88f, 0.32f, 1f);
            string modeLabel = GameFlow.Mode == GameMode.Prospector ? "PROSPECTOR" : "HARVEST";
            StarCeremony.Show(newStars, gradeTotal, starGold,
                modeLabel + " GRADE " + newStars + " PASSED!",
                newStars >= gradeTotal ? "" : "GRADE " + (newStars + 1) + " UNLOCKED.");
        }

        Canvas canvas = UiKit.CreateRootCanvas("WinCanvas", 200);
        UiKit.EnsureEventSystem();
        UiKit.Background(canvas.transform, "LunarBackground", "LunarBackground");
        RectTransform dim = UiKit.Panel(canvas.transform, "WinDim", Vector2.zero, Vector2.zero,
            new Vector2(1f, 1f), new Color(0.01f, 0.02f, 0.06f, 0.62f));
        UiKit.StretchToFull(dim);

        Color gold = new Color(1f, 0.84f, 0.25f, 1f);
        Color blue = new Color(0.45f, 0.8f, 1f, 1f);
        UiKit.Label(canvas.transform, "WinTitle", "CONGRATULATIONS", 56, gold,
            new Vector2(0f, -150f), TextAnchor.MiddleCenter, 1400f, 90f, UiKit.UiFontKind.Display);

        int survivalUnlock = GameFlow.NewSurvivalUnlockIndex;
        if (survivalUnlock >= 0 && survivalUnlock < LevelManager.PlanetCount)
        {
            GameFlow.NewSurvivalUnlockIndex = -1;
            string name = LevelManager.Planets[survivalUnlock].planetName;
            UiKit.Label(canvas.transform, "WinSurvivalUnlocked", "RIDGE SURVIVAL MODE UNLOCKED!",
                44, gold, new Vector2(0f, -60f), TextAnchor.MiddleCenter, 1400f, 56f, UiKit.UiFontKind.Display);
            UiKit.Label(canvas.transform, "WinSurvivalUnlockedSub", "TACKLE " + name + " RIDGE",
                24, new Color(1f, 0.95f, 0.78f, 1f), new Vector2(0f, -94f),
                TextAnchor.MiddleCenter, 1400f, 40f);
        }

        int credits = RunHarvestPoints;
        int bonus = BonusPoints;
        int total = GameFlow.FinalScore;
        // A granted token doubler doubles the FINAL total (LevelScore()); double the split
        // too so the board's arithmetic always adds up on screen.
        if (GameFlow.TokensDoubled)
        {
            credits *= 2;
            bonus *= 2;
        }
        UiKit.Label(canvas.transform, "WinTotal",
            "YOU EARNED " + total.ToString("N0") + " TOKENS ON " + GameFlow.FinalDetail.ToUpperInvariant(),
            34, Color.white, new Vector2(0f, -245f), TextAnchor.MiddleCenter, 1400f, 60f);

        UiKit.Label(canvas.transform, "WinBreakdown",
            "HARVEST " + credits.ToString("N0") + "  +  OXYGEN BONUS " + bonus.ToString("N0") + "  =  " + total.ToString("N0"),
            28, blue, new Vector2(0f, -315f), TextAnchor.MiddleCenter, 1400f, 50f);

        // Bonus lines stack downwards from the breakdown so they can never collide: the token
        // doubler notice, the first-clear wallet bonus and the all-grades bonus can all appear
        // in the same clear, and a fixed Y put the 26-28 pt labels on top of each other.
        float bonusY = -370f;
        const float bonusPitch = 52f;
        if (GameFlow.FirstRunBonus > 0)
        {
            UiKit.Label(canvas.transform, "WinFirstRun",
                "FIRST RUN BONUS  +" + GameFlow.FirstRunBonus.ToString("N0") + " TOKENS TO WALLET",
                28, gold, new Vector2(0f, bonusY), TextAnchor.MiddleCenter, 1400f, 44f);
            bonusY -= bonusPitch;
        }

        if (GameFlow.TokensDoubled)
        {
            UiKit.Label(canvas.transform, "WinDouble",
                "TOKENS x2  ·  DOUBLER ACTIVE",
                26, gold, new Vector2(0f, bonusY), TextAnchor.MiddleCenter, 1400f, 44f);
            bonusY -= bonusPitch;
        }

        if (GameFlow.ProspectorFirstClearBonus > 0)
        {
            UiKit.Label(canvas.transform, "WinProspectorFirstClear",
                "PROSPECTOR FIRST CLEAR BONUS  +" + GameFlow.ProspectorFirstClearBonus.ToString("N0") + " TOKENS TO WALLET",
                28, gold, new Vector2(0f, bonusY), TextAnchor.MiddleCenter, 1400f, 44f);
            bonusY -= bonusPitch;
        }

        if (GameFlow.ProspectorPerfectBonus > 0)
        {
            UiKit.Label(canvas.transform, "WinProspectorPerfect",
                "PERFECT RUN - EVERY NUGGET  +" + GameFlow.ProspectorPerfectBonus.ToString("N0") + " TOKENS TO WALLET",
                28, gold, new Vector2(0f, bonusY), TextAnchor.MiddleCenter, 1400f, 44f);
            bonusY -= bonusPitch;
        }

        if (GameFlow.FirstClearBonus > 0)
        {
            UiKit.Label(canvas.transform, "WinFirstClear",
                "FIRST CLEAR BONUS  +" + GameFlow.FirstClearBonus.ToString("N0") + " TOKENS TO WALLET",
                28, gold, new Vector2(0f, bonusY), TextAnchor.MiddleCenter, 1400f, 44f);
            bonusY -= bonusPitch;
        }

        if (GameFlow.HarvestAllBonus > 0)
        {
            UiKit.Label(canvas.transform, "WinHarvestAll",
                "ALL GRADES CLEARED BONUS  +" + GameFlow.HarvestAllBonus.ToString("N0") + " TOKENS TO WALLET",
                28, gold, new Vector2(0f, bonusY), TextAnchor.MiddleCenter, 1400f, 44f);
            bonusY -= bonusPitch;
        }

        // Navigation row: NEXT (this world's PROSPECTOR RUN, opened by the harvest clear) / RE-RUN /
        // world map.
        // The middle button re-deploys the run, and a CLEARED harvest has already bumped this
        // world's stored grade (LevelManager.RecordHarvestCleared), so the same re-deploy drops
        // the player into the NEXT, harder grade. Say so while one is left: a fresh clear with
        // grades still to run reads NEXT GRADE, and only a run that survived nothing (the
        // commiseration board) or a world that has banked all MaxHarvestGrade sites reads RETRY.
        int sitesPassed = LevelManager.GetHarvestSitesPassed(LevelManager.CurrentLevelIndex);
        bool gradeToAdvance = sitesPassed > 0 && sitesPassed < LevelManager.MaxHarvestGrade;
        string againTip = GameFlow.Mode == GameMode.Prospector ? "RETRY" : (gradeToAdvance ? "NEXT GRADE" : "RETRY");
        string nextTip;
        System.Action nextAction;
        bool nextEnabled;
        if (GameFlow.Mode == GameMode.Prospector)
        {
            // A cleared Prospector opens only this world's Ridge. The next planet's harvest
            // stays locked until a full Ridge leg has been completed.
            int currentWorld = LevelManager.CurrentLevelIndex;
            nextTip = "SURVIVAL RIDGE";
            nextAction = () => GameFlow.StartSurvivalAt(currentWorld);
            nextEnabled = LevelManager.IsModeUnlocked(currentWorld, GameMode.Survival);
        }
        else
        {
            // Surviving the HARVEST opened this world's PROSPECTOR RUN: the NEXT button walks
            // into that descent (this used to hand off to the old canyon race, which is dormant
            // - the survival chain is what the fresh map actually shows).
            nextTip = "PROSPECTOR RUN";
            nextAction = () => GameFlow.StartProspectorAt(LevelManager.CurrentLevelIndex);
            nextEnabled = LevelManager.IsModeUnlocked(LevelManager.CurrentLevelIndex, GameMode.Prospector);
        }
        BuildNavRow(canvas.transform, "HarvestNav", nextTip, nextAction, nextEnabled, againTip, RestartLevelFresh);

        AddBankBadge(canvas);
    }

    /// <summary>Shows the Lunar Race finish screen. Verdict is scored against the rival LEAGUE:
    /// the best tier beaten (Gold / Silver / Bronze) or RACE LOST. Beating a tier pays its
    /// tokens and - at BRONZE - opens the NEXT planet's HARVEST (the chain's next slot), so
    /// the screen can walk straight into it. RACE AGAIN is always available, win or lose.</summary>
    private void BuildRaceWinOverlay()
    {
        // The course is beaten (crossed the line): ring out the outcome before the panel builds.
        RoverSounds.PlayCelebration();

        Canvas canvas = UiKit.CreateRootCanvas("WinCanvas", 200);
        UiKit.EnsureEventSystem();
        UiKit.Background(canvas.transform, "LunarBackground", "LunarBackground");
        RectTransform dim = UiKit.Panel(canvas.transform, "WinDim", Vector2.zero, Vector2.zero, new Vector2(1f, 1f), new Color(0.01f, 0.02f, 0.06f, 0.62f));
        UiKit.StretchToFull(dim);

        RivalRacer.RaceResult result = GameFlow.RaceResult;
        Color verdictColor = RivalRacer.ResultColor(result);
        Color dimWhite = new Color(1f, 1f, 1f, 0.6f);

        UiKit.Label(canvas.transform, "WinTitle", RivalRacer.ResultTitle(result, !RaceTimeoutConcluded),
            58, verdictColor, new Vector2(0f, -150f), TextAnchor.MiddleCenter, 1400f, 90f);

        // A race that just opened the NEXT world's HARVEST for the first time headlines it
        // here, above the verdict - the single most characterful beat of the whole chain,
        // so it gets its own big gold banner (consumed; only the unlocking run shows it).
        int worldUnlock = GameFlow.NewWorldUnlockIndex;
        if (worldUnlock >= 0 && worldUnlock < LevelManager.PlanetCount)
        {
            GameFlow.NewWorldUnlockIndex = -1; // consumed - announce once
            UiKit.Label(canvas.transform, "WinWorldUnlocked",
                LevelManager.Planets[worldUnlock].planetName + " UNLOCKED!",
                44, new Color(1f, 0.88f, 0.32f, 1f), new Vector2(0f, -60f),
                TextAnchor.MiddleCenter, 1400f, 56f);
        }

        string time = FormatRaceTime(GameFlow.FinalScore);
        string courseLabel = GameFlow.CourseName(GameFlow.CurrentCourse);
        string headline = "RACE TIME  " + time + "   ·   " + GameFlow.FinalDetail.ToUpperInvariant()
            + "   ·   " + courseLabel
            + (GameFlow.RaceNewBest ? "   ·   NEW BEST" : "");
        UiKit.Label(canvas.transform, "WinTotal", headline, 32, Color.white, new Vector2(0f, -238f), TextAnchor.MiddleCenter, 1400f, 56f);

        // The rival field's REAL finish times (DNF = they never crossed before the player's
        // win, so beating them was already banked). Obstacles can slow them, so we never
        // pretend they hit their booklet pace.
        string rivalLine = "";
        RivalRacer[] rivals = RivalRacer.All();
        for (int i = 0; i < rivals.Length; i++)
        {
            if (i > 0) rivalLine += "  ·  ";
            string timeStr = rivals[i].CrossedFinish
                ? FormatRaceTime(rivals[i].FinishCenti)
                : "DNF";
            rivalLine += RivalRacer.TierName(rivals[i].tier) + "  " + timeStr;
        }
        if (string.IsNullOrEmpty(rivalLine))
        {
            rivalLine = "BRONZE 01:48.00  ·  SILVER 01:41.50  ·  GOLD 01:35.00";
        }
        UiKit.Label(canvas.transform, "WinRival", "RIVALS   " + rivalLine,
            24, new Color(0.75f, 0.78f, 0.84f, 1f), new Vector2(0f, -280f), TextAnchor.MiddleCenter, 1400f, 44f);

        string tokensText;
        if (GameFlow.RaceTokensEarned > 0)
        {
            // The board's yellow award line. Winning beats a rival (RIVAL-BEAT REWARD); finishing
            // last inside the post-bronze window banks the flat consolation pot, shown bare.
            tokensText = "+" + GameFlow.RaceTokensEarned + " TOKENS"
                + (result != RivalRacer.RaceResult.Lost ? "  (RIVAL-BEAT REWARD)" : "");
        }
        else
        {
            // No tokens: the run never crossed the line (the post-bronze cutoff concluded, or
            // the rover was lost before the finish) - crossing first is the only way to bank.
            tokensText = "NO TOKENS  -  CROSS THE LINE BEFORE THE CUTOFF TO EARN ANY";
        }
        UiKit.Label(canvas.transform, "WinTokens", tokensText,
            26, GameFlow.RaceTokensEarned > 0 ? UpgradeSystem.TokenColor : dimWhite,
            new Vector2(0f, -322f), TextAnchor.MiddleCenter, 1400f, 44f);

        bool firstMedal = GameFlow.FirstMedalBonus > 0;
        if (firstMedal)
        {
            UiKit.Label(canvas.transform, "WinFirstMedal",
                "FIRST MEDAL BONUS!  +" + GameFlow.FirstMedalBonus.ToString("N0") + " TOKENS TO WALLET",
                26, UpgradeSystem.TokenColor, new Vector2(0f, -358f), TextAnchor.MiddleCenter, 1400f, 34f);
        }
        // Everything below the bonus lines drops to leave room for the first-medal note.
        int downShift = firstMedal ? 34 : 0;

        bool won = result != RivalRacer.RaceResult.Lost;
        bool gradeCleared = RivalRacer.GradeClearedFor(GameFlow.FinalScore);
        int grade = Mathf.Clamp(GameFlow.RaceGradeIndex, 0, RivalRacer.GradeCount - 1);
        string gradeNote = gradeCleared
            ? (grade >= RivalRacer.GradeCount - 1
                ? "ALL " + RivalRacer.GradeCount + " RACE GRADES CLEARED ON THIS WORLD"
                : "GRADE " + (grade + 1) + " CLEARED  -  GRADE " + (grade + 2) + " UNLOCKED (FASTER RIVALS)")
            : (won
                ? "GRADE " + (grade + 1) + " NOT CLEARED  -  BEAT THE GOLD RIVAL TO ADVANCE"
                : "GRADE " + (grade + 1) + "  -  BEAT A RIVAL TO THE LINE FIRST");
        UiKit.Label(canvas.transform, "WinNote", gradeNote,
            22, gradeCleared ? UpgradeSystem.TokenColor : dimWhite, new Vector2(0f, -362f - downShift), TextAnchor.MiddleCenter, 1400f, 40f);

        int best = Leaderboard.BestRaceWorld(LevelManager.CurrentLevelIndex, GameFlow.CourseKey(GameFlow.CurrentCourse));
        UiKit.Label(canvas.transform, "WinBest",
            courseLabel + " FASTEST  " + FormatRaceTime(best),
            22, UpgradeSystem.TokenColor, new Vector2(0f, -400f - downShift), TextAnchor.MiddleCenter, 1400f, 40f);

        // RACE-LAST consolation: a lost race is allowed one silver lining - a rewarded ad grants
        // a FREE upgrade tier (ENGINE / BOOST alternating per claim, only while the due part
        // still has room; see AdCoordinator.TryClaimConsolation). Dormant, both parts maxed,
        // or the run's one consolation already spent, the row never draws.
        if (!won) BuildConsolationOffer(canvas);

        // Navigation row: NEXT / RETRY (or "TRY NEXT GRADE") / world map. NEXT follows the shared
        // resolver: this world's RIDGE once its 5 harvest stars are banked, otherwise the NEXT
        // world's HARVEST - never a race.
        bool lastGrade = grade >= RivalRacer.GradeCount - 1;
        string againTip = (gradeCleared && !lastGrade) ? "TRY NEXT GRADE" : "RETRY";
        System.Action nextAction;
        bool nextEnabled;
        string nextTip;
        ResolveNextSlot(LevelManager.CurrentLevelIndex, out nextAction, out nextEnabled, out nextTip);
        BuildNavRow(canvas.transform, "RaceNav",
            nextTip, nextAction, nextEnabled,
            againTip, RestartLevelFresh);

        // FIRST-time grade clear: flash the earned stars up over the board (StarCeremony) in
        // their gold, one on top of the other, then fade so the finish screen reads through.
        int newStars = GameFlow.NewStarGradeCount;
        if (newStars > 0)
        {
            GameFlow.NewStarGradeCount = 0; // consumed - only one ceremony per board
            Color[] gold = new Color[RivalRacer.GradeCount];
            for (int i = 0; i < gold.Length; i++) gold[i] = new Color(1f, 0.88f, 0.32f, 1f);
            StarCeremony.Show(newStars, RivalRacer.GradeCount, gold,
                lastGrade ? "ALL " + RivalRacer.GradeCount + " GRADES CLEARED" : "NEW STAR GRADE",
                "GRADE " + (grade + 1) + " OF " + RivalRacer.GradeCount + "  ·  " + GameFlow.FinalDetail.ToUpperInvariant() + " RACE");
        }

        AddBankBadge(canvas);
    }

    /// <summary>Reverts the race-last consolation button from its "GRANTED" flash back to the
    /// plain next-offer label after <paramref name="delay"/> seconds (Realtime - the board
    /// flow keeps time running).</summary>
    private System.Collections.IEnumerator ConsolationFlashRoutine(System.Action refresh, float delay = 2.4f)
    {
        yield return new WaitForSecondsRealtime(delay);
        refresh();
    }

    /// <summary>Holds the race-last consolation button on a live rewarded-cooldown countdown
    /// before re-arming, so an offer tap while the 45 s gate still runs shows "READY IN Ns"
    /// instead of silently deadening.</summary>
    private System.Collections.IEnumerator ConsolationWaitRoutine(Button cons, Text consText, System.Action refresh, float wait)
    {
        cons.interactable = false;
        while (wait > 0f)
        {
            if (consText != null)
            {
                consText.text = "WATCH AD · READY IN " + Mathf.CeilToInt(wait) + "s";
            }
            yield return new WaitForSecondsRealtime(0.25f);
            wait = AdCoordinator.RewardedCooldownRemaining;
        }
        refresh();
    }

    /// <summary>Builds the race-last consolation offer button, shared by the finish-in-last
    /// board and every RACE LOST (crash / ridge-without-tier) board: a rewarded ad grants one
    /// free ENGINE / BOOST tier, exactly one true upgrade ad per failed run. Sits well below
    /// the navigation row so it never overlaps RETRY / RETURN. No-ops when ads are dormant,
    /// both parts are maxed, or the run's one consolation was already spent.</summary>
    private void BuildConsolationOffer(Canvas canvas)
    {
        if (!AdCoordinator.ConsolationActive || AdCoordinator.ConsolationSpent) return;

        Button cons = UiKit.Button(canvas.transform, "B_RaceConsolation", AdCoordinator.ConsolationButtonText, 20,
            new Color(0.16f, 0.28f, 0.48f, 1f), new Color(1f, 0.9f, 0.55f, 1f),
            new Vector2(0f, -770f), new Vector2(500f, 50f), null);
        Text consText = cons.GetComponentInChildren<Text>();

        System.Action refreshConsolation = () =>
        {
            string id = AdCoordinator.ConsolationId;
            cons.interactable = !string.IsNullOrEmpty(id);
            if (consText != null) consText.text = AdCoordinator.ConsolationButtonText;
        };
        cons.onClick.AddListener(() =>
        {
            string id = AdCoordinator.ConsolationId;
            if (string.IsNullOrEmpty(id)) return;
            float wait = AdCoordinator.RewardedCooldownRemaining;
            if (wait > 0f)
            {
                // A granted rewarded ad is still on cooldown (e.g. a garage top-up right before
                // the run ended): say so with a live countdown instead of deadening.
                StartCoroutine(ConsolationWaitRoutine(cons, consText, refreshConsolation, wait));
                return;
            }
            int from = UpgradeSystem.LevelOf(id);
            AdCoordinator.TryClaimConsolation(() =>
            {
                if (consText != null)
                {
                    consText.text = "GRANTED  ·  +1 " + UpgradeSystem.DisplayName(id)
                        + " (LV " + (from + 1) + ")";
                }
                // One true upgrade ad per failed run: hold the grant beat a moment, then the
                // offer row is gone - the next LOST run re-offers the alternating part.
                StartCoroutine(ConsolationFlashRoutine(() => cons.gameObject.SetActive(false)));
            }, null);
        });
        refreshConsolation();
    }

    public void RegisterTotalPods(int count)
    {
        TotalPods = count;
    }

    /// <summary>mm:ss formatting shared by the leaderboard screens.</summary>
    public static string FormatTime(int seconds)
    {
        seconds = Mathf.Max(0, seconds);
        return string.Format("{0:00}:{1:00}", seconds / 60, seconds % 60);
    }

    /// <summary>km formatting for Ridge Survival distances (metres in, "0.00 KM" out).</summary>
    public static string FormatKm(float metres)
    {
        return (Mathf.Max(0f, metres) / 1000f).ToString("0.00") + " KM";
    }

    /// <summary>mm:ss.cc formatting for Lunar Race times (input is centiseconds, as stored
    /// on the race boards, so the display always matches the recorded value exactly).</summary>
    public static string FormatRaceTime(int centiseconds)
    {
        centiseconds = Mathf.Max(0, centiseconds);
        int cs = centiseconds % 100;
        int s = (centiseconds / 100) % 60;
        int m = centiseconds / 6000;
        return string.Format("{0:00}:{1:00}.{2:00}", m, s, cs);
    }

    /// <summary>mm:ss.cc formatting for the live HUD race clock (input in seconds).</summary>
    public static string FormatRaceTime(float seconds)
    {
        return FormatRaceTime(Mathf.RoundToInt(Mathf.Max(0f, seconds) * 100f));
    }
}