using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Central gate for EVERY ad in Lunar Harvest. All placement code calls this coordinator -
/// never the SDK directly - so there is exactly one place that:
///   - decides whether ads are live at all (the master switch a curator flips for the
///     CrazyGames full-release build),
///   - enforces CrazyGames policy: gameplayStop() BEFORE any ad whose request falls in
///     live gameplay, gameplayStart() after a rewarded ad that resumes the run, one
///     interstitial per session, and never an interstitial outside a natural break,
///   - freezes the world (Time.timeScale = 0 + AudioListener.pause) the moment an ad
///     starts and restores it when it ends, so no hazard can drain the run or kill the
///     buggy while the player is watching (the SDK auto-pauses real ads; the localhost
///     test overlay does not, so the coordinator owns this),
///   - funnels the granted rewards back through the game (tokens, O2 refill, checkpoint
///     revive, token doubler) and lets the placement refresh its UI.
///
/// The whole layer is DORMANT by default (Basic Launch): with AdsEnabled false every
/// request refuses instantly (deny callbacks run, grants never fire) and no button/label
/// that is gated on <see cref="Live"/> ever spawns.
///
/// Flipping AdsEnabled to true (the Full Launch release build) activates:
///   - Garage          : rewarded top-up worth one upgrade (see GarageTopUpTokens).
///   - Harvester       : rewarded O2-refill revive on oxygen death + rewarded "2x TOKENS"
///                       doubler on the deploy hold.
///     - Ridge Survival  : rewarded checkpoint revive at the last 200 m marker.
///   - Any mode        : rewarded 50% hull-repair revive on hull destruction (one per run).
///                       Revives are OCCASIONAL: only every 3rd revive-eligible death offers
///                       one (persisted counter - see ReviveDeathInterval).
///   - Lunar Race      : finishing LAST pays a rewarded consolation - one FREE upgrade tier,
///                       alternating +1 ENGINE / +1 BOOST per claim (while either has room).
///   - Any run end     : one midgame interstitial before the win / commiserations board.
/// </summary>
public static class AdCoordinator
{
    /// <summary>MASTER SWITCH. OFF by default so the Basic Launch submission build carries no
    /// ad UI at all (CrazyGames disables monetization in Basic and rejects "rewarded ad
    /// buttons without effect"). Flip to true only for the ad-enabled Full Launch release
    /// build. With it true, every placement still reads <see cref="Live"/> - and Ready is
    /// only ever true on the CrazyGames portal (or a localhost demo), so ordinary web
    /// uploads of the same build never serve ads.</summary>
    public static bool AdsEnabled = false;

    /// <summary>True when ads are armed AND the SDK finished init - the gate every placement
    /// UI checks before drawing its "WATCH AD" affordance.</summary>
    public static bool Live => AdsEnabled && CrazyGamesBridge.Ready;

    /// <summary>Tokens a garage rewarded top-up pays. Deliberately ONE upgrade's worth: the
    /// cheapest workshop rank costs 100, so a top-up at most buys the player the upgrade
    /// they could not afford - never a big spendable windfall for one ad.</summary>
    public static int GarageTopUpTokens = 100;

    /// <summary>Minimum seconds between rewarded ads (CrazyGames rate limit).</summary>
    private const float RewardedCooldown = 45f;

    /// <summary>Persisted count of revive-eligible deaths. The rewarded revive is offered only
    /// on every <see cref="ReviveDeathInterval"/>-th such death, per CrazyGames' rule that a
    /// revive must stay an occasional, special opportunity - not one offered on every loss.</summary>
    private const string ReviveDeathCountKey = "lunar.revive.deathcount";

    /// <summary>How many revive-eligible deaths between rewarded revive offers.</summary>
    public const int ReviveDeathInterval = 3;

    // The SDK mirrors the in-flight ad on CrazyGamesBridge.AdState (0 idle / 1 started /
    // 2 finished / 3 error). The coordinator polls it on the bridge's cadence.
    private const int AdStateStarted = 1;
    private const int AdStateFinished = 2;
    private const int AdStateError = 3;
    private const float AdRequestTimeout = 45f;

    // ------------------------------------------------------------------ run / gameplay state
    /// <summary>True while the current run's GameplayStart is still open (set at the intro
    /// HandOff, cleared the first time an ad claims a GameplayStop). Guards the stop-first
    /// rule without double-sending.</summary>
    public static bool GameplayLive { get; private set; }

    /// <summary>Keeps the coordinator's gameplay mirror honest. The run scene calls this
    /// with true at the intro HandOff (beside the real GameplayStart send).</summary>
    public static void SetGameplayLive(bool live)
    {
        GameplayLive = live;
    }

    /// <summary>Clears per-run placement flags. Called from GameFlow.ResetRun.</summary>
    public static void ResetRun()
    {
        reviveUsedThisRun = false;
        consolationSpentThisRun = false;
    }

    /// <summary>True once THIS run's one true upgrade ad has been granted. The race-last
    /// consolation is exactly one opportunity per failed run - after a grant the board hides
    /// the offer until a new run starts (which resets this via ResetRun).</summary>
    public static bool ConsolationSpent => consolationSpentThisRun;

    // ------------------------------------------------------------------ in-flight request state
    private static int pendingKind;            // 0 none, 1 rewarded, 2 midgame
    private static Action pendingGrant;
    private static Action pendingDeny;
    private static bool pendingResume;         // rewarded ad that must re-open gameplay
    private static Action pendingMidgameDone;
    private static float adRequestAt = -1f;    // -1 = nothing in flight (real-time query)
    private static int adStateSeen;            // last state consumed from the mirror
    private static float adPrevTimeScale = 1f; // captured at the first freeze the coordinator makes (depth 0)
    private static float lastRewardedAt = -999f;
    private static bool midgameShownThisSession;
    private static bool consolationSpentThisRun;

    /// <summary>True while a placement is waiting on the SDK (ad finding / showing).</summary>
    public static bool AdPending => adRequestAt > 0f;

    /// <summary>Seconds until the next rewarded ad may be requested (0 when ready). The 45 s
    /// CrazyGames rate limit refusals were silent, so placements surface this so a denied tap
    /// can show "ADS: Ns" instead of doing nothing.</summary>
    public static float RewardedCooldownRemaining =>
        Mathf.Max(0f, RewardedCooldown - (Time.realtimeSinceStartup - lastRewardedAt));

    // Per-run: a revive offer is worth exactly one paid continue per run.
    private static bool reviveUsedThisRun;

    // ------------------------------------------------------------------ game freeze (consent + ad)
    // The O2-refill / checkpoint-revive consent panel and the ad itself both freeze the
    // world. Both use a single depth counter so an ad that slides in right after the panel
    // pops (or vice versa) restores the exact pre-freeze Time.timeScale once, at the end.
    private static int freezeDepth;

    /// <summary>Freezes the world (pauses audio, halts time) - used while the revive consent
    /// panel is up so nothing can drain/kill the parked run while the player decides.</summary>
    public static void PushGamePause()
    {
        if (freezeDepth == 0) adPrevTimeScale = Time.timeScale;
        freezeDepth++;
        AudioListener.pause = true;
        Time.timeScale = 0f;
    }

    /// <summary>Releases one nested freeze; restores time + audio only at depth zero.</summary>
    public static void PopGamePause()
    {
        if (freezeDepth <= 0) return;
        freezeDepth--;
        if (freezeDepth == 0)
        {
            Time.timeScale = adPrevTimeScale;
            AudioListener.pause = false;
        }
    }

    private static void EnsureStoppedForAd()
    {
        if (!GameplayLive) return;
        GameplayLive = false;
        SendEvent(CrazyGamesBridge.SdkEvent.GameplayStop);
    }

    private static void SendEvent(CrazyGamesBridge.SdkEvent ev)
    {
        if (!CrazyGamesBridge.Ready) return;
        CrazyGamesBridge.SendEvent(ev);
    }

    // ------------------------------------------------------------------ the request core
    /// <summary>Player-initiated rewarded ad. refused instantly (deny fires) when ads are
    /// dormant, another request is in flight, or the rewarded cooldown has not elapsed. On
    /// a clean adFinished the grant fires; on ANY error/timeout the deny fires - a reward
    /// is NEVER granted on an errored ad (CrazyGames rules).</summary>
    private static void RequestRewarded(string slot, Action grant, Action deny, bool resumeGameplay)
    {
        if (!Live || adRequestAt > 0f || Time.realtimeSinceStartup - lastRewardedAt < RewardedCooldown)
        {
            deny?.Invoke();
            return;
        }

        pendingKind = 1;
        pendingGrant = grant;
        pendingDeny = deny;
        pendingResume = resumeGameplay;
        pendingMidgameDone = null;
        adRequestAt = Time.realtimeSinceStartup;
        adStateSeen = 0;
        EnsureStoppedForAd();

        Log("requesting rewarded ad slot " + slot);
        CrazyGamesBridge.RequestAd(CrazyGamesBridge.AdType.Rewarded);
    }

    /// <summary>Midgame/INTERSTITIAL. Call ONLY from a natural break - the win / defeat
    /// boards, never mid-play. onDone fires once the ad ends (and builds the board); when
    /// dormant or already shown this session it fires immediately, so the board flow never
    /// changes until release.</summary>
    public static void MaybeRunEndMidgame(Action onDone)
    {
        if (!Live || midgameShownThisSession || adRequestAt > 0f)
        {
            onDone();
            return;
        }

        midgameShownThisSession = true; // exactly one interstitial per app session
        pendingKind = 2;
        pendingGrant = null;
        pendingDeny = null;
        pendingResume = false;
        pendingMidgameDone = onDone;
        adRequestAt = Time.realtimeSinceStartup;
        adStateSeen = 0;
        EnsureStoppedForAd();

        Log("requesting midgame ad at run end");
        CrazyGamesBridge.RequestAd(CrazyGamesBridge.AdType.Midgame);
    }

    private static void Log(string message)
    {
        Debug.Log("[CG] " + message);
    }

    // ------------------------------------------------------------------ placement: garage top-up
    /// <summary>True when the garage should swap its "NEED N MORE TOKENS" label for a
    /// rewarded "WATCH AD +N TOKENS" top-up affordance.</summary>
    public static bool TopUpEnabled => Live;

    /// <summary>Attempts the garage rewarded top-up (the UPGRADE tap when short). On grant
    /// the token credit needs a second, manual UPGRADE tap to actually spend - the ad only
    /// tops the balance up to affordability.</summary>
    public static void TryClaimTopUp(Action onGrant)
    {
        RequestRewarded("garage_top_up", onGrant, null, false);
    }

    // ------------------------------------------------------------------ placement: revive offers
    /// <summary>What a rewarded revive restores to a run that just died:
    /// <see cref="ReviveKind.O2Refill"/> tops the tank and continues in place (Harvester),
    /// <see cref="ReviveKind.CheckpointRespawn"/> snaps back to the last banked 200 m marker
    /// (Ridge Survival, anything but a crest fall), <see cref="ReviveKind.HullRepair"/> gives
    /// 50% of the hull back and continues in place (any mode's hull destruction).</summary>
    public enum ReviveKind { O2Refill, CheckpointRespawn, HullRepair }

    /// <summary>Returns true when a rewarded revive offer was shown and owns the end-of-run
    /// flow now. Called from OxygenManager.BeginGameOver BEFORE recording the outcome: while
    /// the offer is up the world is frozen and nothing is banked/lost. The player can accept
    /// (ad plays, then OxygenManager.ReviveContinue runs the grant) or give up (falls back
    /// to the ordinary fail flow through OxygenManager.FinishGameOver). Dormant (or a mode
    /// without a matching slot): returns false, the caller proceeds as today.
    ///
    /// Occasional BY DESIGN (CrazyGames rewarded-ads rule: a revive must be a special
    /// opportunity, never an expectation): every revive-eligible death increments a persisted
    /// counter and only every <see cref="ReviveDeathInterval"/>-th death is offered the
    /// revive. The counter runs whether or not ads are live, so the cadence is stable across
    /// the Basic → Full switch.</summary>
    public static bool TryOfferRevive(OxygenManager oxygen, string reason)
    {
        // Three slots: Harvester O2 deaths refill the tank in place; Ridge Survival deaths
        // (anything short of falling off the crest, whose screen already faded) respawn at
        // the last banked 200 m marker; a hull destruction on Harvester / Race grants 50%
        // of the hull back in place (Ridge hull deaths ride the checkpoint slot above).
        bool inPlaceO2 = GameFlow.Mode == GameMode.Harvester && reason == "OUT OF OXYGEN";
        bool checkpoint = GameFlow.IsRidgeSurvival && reason != "FELL OFF THE RIDGE";
        bool hullRepair = reason == "HULL DESTROYED" && !checkpoint;
        if (!inPlaceO2 && !checkpoint && !hullRepair) return false;

        // Count this eligible death first (once - BeginGameOver is guarded against
        // double-firing), so the every-Nth cadence holds across sessions and the ads flip.
        int deaths = SaveBridge.GetInt(ReviveDeathCountKey, 0) + 1;
        SaveBridge.SetInt(ReviveDeathCountKey, deaths);
        SaveBridge.Save();

        if (!Live || reviveUsedThisRun) return false;
        if (deaths % ReviveDeathInterval != 0) return false;

        ReviveKind kind = inPlaceO2 ? ReviveKind.O2Refill
            : checkpoint ? ReviveKind.CheckpointRespawn
            : ReviveKind.HullRepair;

        reviveUsedThisRun = true;
        PushGamePause();

        string title, body;
        switch (kind)
        {
            case ReviveKind.O2Refill:
                title = "OUT OF OXYGEN";
                body = "WATCH AN AD TO REFILL THE TANK AND CONTINUE?";
                break;
            case ReviveKind.CheckpointRespawn:
                title = "ROVER LOST";
                body = "WATCH AN AD TO RESPAWN AT THE LAST MARKER AND CONTINUE?";
                break;
            default:
                title = "HULL DESTROYED";
                body = "WATCH AN AD TO REPAIR 50% OF THE HULL AND CONTINUE?";
                break;
        }
        ShowConsent(title, body, "CONTINUE",
            () => ConsentContinue(oxygen, kind),
            () => ConsentGiveUp(oxygen));
        return true;
    }

    private static void ConsentContinue(OxygenManager oxygen, ReviveKind kind)
    {
        DestroyConsent();
        PopGamePause();
        // resume = true: the ad's grant hands control back to a LIVE run, so the coordinator
        // re-opens gameplay with a GameplayStart (matching the Stop it sent before the ad).
        string slot = kind == ReviveKind.O2Refill ? "o2_refill"
            : kind == ReviveKind.CheckpointRespawn ? "checkpoint_revive"
            : "hull_repair";
        RequestRewarded(slot,
            () => oxygen.ReviveContinue(kind),
            () => oxygen.FinishGameOver(),
            true);
    }

    private static void ConsentGiveUp(OxygenManager oxygen)
    {
        DestroyConsent();
        PopGamePause();
        oxygen.FinishGameOver();
    }

    // ------------------------------------------------------------------ placement: token doubler
    /// <summary>True when the Harvester deploy hold may offer the rewarded "2x TOKENS THIS
    /// RUN" doubler (Harvester only - the run's whole pay is tokens, so doubling is real).</summary>
    public static bool DoublerAvailable => Live && GameFlow.Mode == GameMode.Harvester;

    /// <summary>Attempts the rewarded token-doubler claim (a tap on the intro hold). Grant
    /// flips GameFlow.TokensDoubled, which LevelScore(), CreditRunParts() and the win board
    /// all read - the doubled total is what banks, shows and ranks everywhere. Both fail and
    /// dormant paths route to onDeny so the intro button can re-arm.</summary>
    public static void TryClaimTokenDouble(Action onGrant, Action onDeny)
    {
        if (!Live || GameFlow.TokensDoubled)
        {
            onDeny?.Invoke();
            return;
        }
        RequestRewarded("token_doubler", onGrant, onDeny, false);
    }

    // ------------------------------------------------------------------ placement: race-last upgrade consolation
    /// <summary>Persists the ENGINE / BOOST alternation so last-place races hand the parts out
    /// in turns across sessions too - not just within a single page load.</summary>
    private const string RaceLastConsolationKey = "lunar.racelast.boostnext";

    /// <summary>True when the race-lost board may offer the free-upgrade consolation: ads are
    /// live AND at least one of ENGINE / BOOST is not maxed ("assuming there is an upgrade to
    /// be had"). Dormant, both parts maxed: false, and nothing is drawn.</summary>
    public static bool ConsolationActive => ConsolationId != null;

    /// <summary>Which part the next race-last consolation grants ("engine" / "boost"): the due
    /// pick alternates each claim, and if the due part is maxed the other is offered instead.
    /// Null when BOTH are maxed - no consolation to be had.</summary>
    public static string ConsolationId
    {
        get
        {
            if (!Live) return null;
            bool boostDue = SaveBridge.GetInt(RaceLastConsolationKey, 0) != 0;
            string due = boostDue ? "boost" : "engine";
            string other = boostDue ? "engine" : "boost";
            if (UpgradeSystem.LevelOf(due) < UpgradeSystem.MaxLevel) return due;
            if (UpgradeSystem.LevelOf(other) < UpgradeSystem.MaxLevel) return other;
            return null;
        }
    }

    /// <summary>The consolation button's label: which part the next watched ad hands over
    /// ("WATCH AD · +1 ENGINE", alternating to "…+1 BOOST CELLS" after a grant).</summary>
    public static string ConsolationButtonText
    {
        get
        {
            string id = ConsolationId;
            return string.IsNullOrEmpty(id)
                ? "ENGINE & BOOST CELLS MAXED"
                : "WATCH AD  ·  +1 " + UpgradeSystem.DisplayName(id);
        }
    }

    /// <summary>Attempts the race-last consolation (a tap on the commiserations board). On a
    /// clean ad the DUE part gains one free tier, the ENGINE/BOOST alternation flips so the
    /// NEXT consolation offers the other part, then <paramref name="onGrant"/> fires so the
    /// board can refresh its label. Every refusal path - dormant, rewarded cooldown, ad error
    /// or both parts maxed - routes to <paramref name="onDeny"/>; the button simply stays ready.</summary>
    public static void TryClaimConsolation(Action onGrant, Action onDeny)
    {
        string id = ConsolationId;
        if (string.IsNullOrEmpty(id))
        {
            onDeny?.Invoke();
            return;
        }
        RequestRewarded("race_lost_upgrade",
            () =>
            {
                if (UpgradeSystem.GrantFreeLevel(id))
                {
                    consolationSpentThisRun = true; // one true upgrade ad per failed run
                    SaveBridge.SetInt(RaceLastConsolationKey,
                        SaveBridge.GetInt(RaceLastConsolationKey, 0) == 0 ? 1 : 0);
                    SaveBridge.Save();
                    onGrant?.Invoke();
                }
                else
                {
                    onDeny?.Invoke();
                }
            },
            onDeny, false);
    }

    // ------------------------------------------------------------------ the ad state machine
    /// <summary>Consumed on the bridge's poll cadence (CrazyGamesBridge.Poll and the menu's
    /// Update both tick it): watches the SDK's mirrored ad state and drives the freezes and
    /// the grant/deny/done callbacks.</summary>
    public static void Poll()
    {
        if (adRequestAt < 0f)
        {
            // Nothing in flight: tidy a stale mirror if the page left one.
            if (CrazyGamesBridge.AdState != 0)
            {
                if (!CrazyGamesBridge.AdBusy)
                {
                    CrazyGamesBridge.ClearAdState();
                    adStateSeen = 0;
                }
            }
            return;
        }

        // Hang safety: the SDK never reported start/end/error - fail the placement so the
        // game can never sit frozen behind a missing callback.
        if (Time.realtimeSinceStartup - adRequestAt > AdRequestTimeout)
        {
            Log("ad request timed out (" + AdRequestTimeout + "s) - failing the placement");
            CompleteAd(AdStateError);
            return;
        }

        int st = CrazyGamesBridge.AdState;
        if (st == adStateSeen) return;
        if (st != 0 || CrazyGamesBridge.AdBusy) adStateSeen = st;

        if (st == AdStateStarted)
        {
            // Freeze the moment the ad covers the screen (real ads also auto-pause, but the
            // SDK's localhost simulation does not - freeze here either way; never drain).
            // This goes through the SAME depth counter as the consent panel, so an ad that
            // starts while the panel is still up nests correctly and the pre-freeze
            // Time.timeScale is restored exactly once, at the final pop.
            PushGamePause();
        }
        else if (st == AdStateFinished || st == AdStateError)
        {
            CompleteAd(st);
        }
    }

    private static void CompleteAd(int finalState)
    {
        bool granted = finalState == AdStateFinished;
        int kind = pendingKind;
        Action grant = pendingGrant;
        Action deny = pendingDeny;
        Action done = pendingMidgameDone;
        bool resume = pendingResume;

        pendingKind = 0;
        pendingGrant = null;
        pendingDeny = null;
        pendingMidgameDone = null;
        pendingResume = false;
        adRequestAt = -1f;
        adStateSeen = 0;

        CrazyGamesBridge.ClearAdState();

        // Release the ad's freeze no matter how it ended. Balanced with the single push in
        // Poll; sharing the consent panel's depth counter means this pop can never restore
        // time while the panel is still paused behind it, nor clobber its captured value.
        PopGamePause();

        // Rewarded rate-limit gate: a midgame interstitial is a different bucket and must NOT
        // stamp it - a run-end interstitial immediately followed by the (same-board) consolation
        // offer is the natural flow, so only granted REWARDED ads set the cooldown.
        if (granted && kind == 1) lastRewardedAt = Time.realtimeSinceStartup;

        if (kind == 1) // rewarded
        {
            if (granted)
            {
                Log("rewarded ad finished - granting");
                grant?.Invoke();
                if (resume)
                {
                    // Control is back with a live run: re-open gameplay with the Start that
                    // balances the Stop sent before the ad.
                    SendEvent(CrazyGamesBridge.SdkEvent.GameplayStart);
                    GameplayLive = true;
                }
            }
            else
            {
                // The SDK refused the request (adblock, no fill, or the platform simply has
                // ads disabled). Deny quietly: a failed ad is never rewarded, and the
                // submitted build shows no developer-facing error messaging.
                Log("rewarded ad errored - denying");
                deny?.Invoke();
            }
        }
        else if (kind == 2) // midgame: finish or abort, the board always follows
        {
            Log("midgame ad " + (granted ? "finished" : "errored") + " - building the board");
            done?.Invoke();
        }
    }

    // ------------------------------------------------------------------ the revive consent panel
    // A minimal in-scene modal: dim veil, a title, a body line and two buttons. The world
    // behind is frozen (PushGamePause above) so the parked run cannot end while this is up.
    // The modal is DESTROYED the moment either button is tapped - otherwise it stays on screen
    // through the ad and after the revive, dimming the run forever.
    private static Canvas consentCanvas;

    private static void DestroyConsent()
    {
        if (consentCanvas != null)
        {
            UnityEngine.Object.Destroy(consentCanvas.gameObject);
            consentCanvas = null;
        }
    }

    private static void ShowConsent(string title, string body, string acceptLabel,
        Action onAccept, Action onGiveUp)
    {
        consentCanvas = UiKit.CreateRootCanvas("AdConsentCanvas", 500);
        UiKit.EnsureEventSystem();

        RectTransform dim = UiKit.Panel(consentCanvas.transform, "ConsentDim", Vector2.zero, Vector2.zero,
            new Vector2(1f, 1f), new Color(0.01f, 0.02f, 0.06f, 0.62f));
        UiKit.StretchToFull(dim);

        RectTransform box = UiKit.Panel(consentCanvas.transform, "ConsentBox",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(600f, 400f),
            new Color(0.05f, 0.10f, 0.08f, 0.97f));

        UiKit.Label(consentCanvas.transform, "AdConsentTitle", title, 44,
            new Color(1f, 0.88f, 0.5f, 1f), new Vector2(0f, 100f), TextAnchor.MiddleCenter, 560f, 60f,
            UiKit.UiFontKind.Display);
        UiKit.Label(consentCanvas.transform, "AdConsentBody", body, 26,
            new Color(0.85f, 0.92f, 0.9f, 1f), new Vector2(0f, 28f), TextAnchor.MiddleCenter, 560f, 60f);

        // CrazyGames rewarded-ads rule: the "continue without watching" choice must be the
        // SAME size, font and colour as the watch-ad choice - neither may be visually
        // promoted over the other. Only the label wording differs.
        Color buttonBg = new Color(0.14f, 0.45f, 0.28f, 1f);
        Color buttonText = new Color(0.85f, 1f, 0.9f, 1f);
        Vector2 buttonSize = new Vector2(320f, 58f);

        Button accept = UiKit.Button(consentCanvas.transform, "B_AdConsentAccept", acceptLabel, 30,
            buttonBg, buttonText, new Vector2(0f, -60f), buttonSize, onAccept);
        Button giveUp = UiKit.Button(consentCanvas.transform, "B_AdConsentGiveUp", "GIVE UP", 30,
            buttonBg, buttonText, new Vector2(0f, -136f), buttonSize, onGiveUp);
        accept.GetComponent<RectTransform>().SetAsLastSibling();
        giveUp.GetComponent<RectTransform>().SetAsLastSibling();
    }
}
