using System;
#if !NO_CRAZY_SDK
using CrazyGames;
#endif
using UnityEngine;

/// <summary>
/// CrazyGames integration backed by the OFFICIAL CrazyGames Unity SDK (CrazySDK 5.x,
/// assets under Assets/CrazySDK + Assets/Plugins/crazySDK.jslib). The SDK loads and
/// initializes itself at runtime from the jslib (no index.html injection needed), which
/// is exactly what the CrazyGames preview/QA tool recognizes as an SDK environment.
///
/// This bridge keeps the full public surface the game already talks to (see below) but
/// each member is now a thin wrapper over CrazySDK:
///   - Init/Poll  : CrazySDK.Init + IsContentReady; readiness is decided by the SDK's
///                  environment ('local' on localhost, 'crazygames'/'preview' on the
///                  portal, 'disabled' on foreign hosts), never by a URL flag.
///   - Data module: CrazySDK.Data (the cloud/guest key-value store = "online progress").
///   - Lifecycle  : CrazySDK.Game.GameplayStart/Stop/HappyTime. LoadingStart/LoadingStop
///                  are NOT supported for Unity (the Unity loader owns loading), so the
///                  LoadingStop legacy event is a no-op.
///   - Ads        : CrazySDK.Ad.RequestAd, mirrored into the polled state AdCoordinator
///                  already consumes (0 idle / 1 started / 2 finished / 3 error + busy).
///                  AdCoordinator owns the gameplay freeze (pauseGameDuringAd is OFF in
///                  CrazyGamesSettings so the SDK never fights its time-scale capture).
///   - Platform mute ("SdkMuted"): CrazySDK.Game.Settings.muteAudio, polled each tick.
///   - Account    : CrazySDK.User.GetUser at boot - a signed-in CrazyGames player's username
///                  is adopted (guests and off-portal hosts keep the local name; see
///                  PortalUsername / PortalUsernameChanged).
///   - Leaderboard: score encrypted with the SDK's own ScoreEncryption (AES-CTR + Unity
///                  marker) and posted via CrazySDK.User.SubmitScore (official API).
///   - TabHidden / IsMobileDevice: LunarExtras.jslib (host introspection, works anywhere).
///
/// The SDK is available on CrazyGames (env 'crazygames'/'preview') and localhost
/// ('local'), and DISABLED on whitelisted domains - so ordinary web uploads of the same
/// build never flip Ready and keep running on PlayerPrefs untouched. Note the official
/// SDK sitelocks non-localhost, non-CrazyGames hosts unless they are whitelisted in
/// Assets/CrazySDK/Resources/CrazyGamesSettings.asset.
/// </summary>
public static class CrazyGamesBridge
{
    private const int StateIdle = 0;
    private const int StateLoading = 1;
    private const int StateReady = 2;
    private const int StateFailed = 3;
    private const int StateNosdk = 4;
    private const int StateBlocked = 5;

    /// <summary>Human-readable mirror state for the on-screen build footer.</summary>
    public static string StateLabel
    {
        get
        {
            if (!enabled) return "OFF";
            switch (state)
            {
                case StateReady: return "READY";
                case StateLoading: return "LOADING";
                case StateNosdk: return "NO SDK";
                case StateBlocked: return "DOMAIN";
                case StateFailed: return "FAILED";
                default: return "IDLE";
            }
        }
    }

    // Local dev convenience only (serve.py / MenuController test hooks): forcing the
    // "CrazyGames" path on a phone or tablet on the same LAN. NOT an environment check -
    // readiness comes from the SDK environment described above.
    public const string UrlFlagTest = "cgtest=1";

    private static int state;
    private static bool enabled;
    private static float lastPollAt;
    private static float initStartAt;
    private const float PollInterval = 0.5f;
    private const float InitTimeout = 25f;
    private static string mirrorError = "";

    /// <summary>True once the game decided to talk to the CrazyGames SDK this run.</summary>
    public static bool Enabled => enabled;

    /// <summary>True when the SDK has finished init and the Data module can be used.</summary>
    public static bool Ready => enabled && state == StateReady;

    /// <summary>True only on a LIVE CrazyGames session: the SDK reported an environment we
    /// can talk to (the CrazyGames portal + regional TLDs, or a localhost demo). Portal-
    /// specific game tuning - beyond the standard CrazyGames ad wrappers - keys off this,
    /// so ordinary web uploads (itch.io etc.) run the full rule set unchanged.</summary>
    public static bool PortalLive => Enabled && Ready;

    /// <summary>Starts the SDK. Safe to call once at game boot. The SDK loads its script and
    /// initializes from the jslib at runtime; Poll advances the state once IsInitialized.
    ///
    /// On a NO_CRAZY_SDK build (itch.io and any other foreign host) this is a deliberate no-op
    /// and the bridge stays disabled forever, so Ready is never true and every SDK-backed
    /// feature below stays dormant. That is not just tidiness: the official SDK sitelocks
    /// non-CrazyGames domains and FREEZES the game before the first scene loads, so an itch
    /// build that still contained the SDK would sit on a dead screen no matter what this code
    /// did. See LunarBuggyMenu.BuildWebGLItch.</summary>
    public static void Init()
    {
        if (enabled) return;
#if UNITY_WEBGL && !UNITY_EDITOR && !NO_CRAZY_SDK
        // CrazySDK.Init is safe on any host; on foreign non-whitelisted hosts the SDK's
        // sitelock has already stopped the game at load, so only legit hosts get here.
        enabled = true;
        state = StateLoading;
        initStartAt = Time.realtimeSinceStartup;
        CrazySDK.Init(delegate { });
#endif
    }

    /// <summary>Poll the SDK state; call a few times a second. Both the menu
    /// (MenuController.Garage.Update) and the level (GameBootstrap.Update) tick this, so
    /// game-settings re-reads happen at the first interactive screen of the session.</summary>
    public static void Poll()
    {
        if (!enabled) return;
        if (Time.realtimeSinceStartup - lastPollAt < PollInterval) return;
        lastPollAt = Time.realtimeSinceStartup;
        AdCoordinator.Poll();
        if (state == StateReady)
        {
            EnsureReadyHandled();
            RefreshSdkMute();
            return;
        }
        if (state == StateLoading)
        {
#if NO_CRAZY_SDK
            // Unreachable in practice (Init never enables the bridge on this build) but kept
            // honest so the state machine can never sit in LOADING forever.
            state = StateNosdk;
            mirrorError = "no sdk in build";
#else
            if (CrazySDK.IsInitialized)
            {
                ResolveFromSdk();
            }
            else if (Time.realtimeSinceStartup - initStartAt > InitTimeout)
            {
                // Safety net only: CrazySDK.Init has no error callback, so if the jslib
                // script never arrives / init never resolves, fail closed after a wait.
                state = StateNosdk;
                mirrorError = "init timeout";
            }
#endif
        }
        if (state == StateReady)
        {
            EnsureReadyHandled();
            RefreshSdkMute();
        }
    }

#if !NO_CRAZY_SDK
    private static void ResolveFromSdk()
    {
        string env;
        try { env = CrazySDK.Environment; }
        catch { env = ""; }
        if (string.IsNullOrEmpty(env)) env = "unset";
        if (CrazySDK.IsAvailable && env != "disabled")
        {
            state = StateReady;
            mirrorError = "env " + env;
        }
        else
        {
            state = StateNosdk;
            mirrorError = "sdk env " + env;
        }
    }
#endif

    private static bool readyHandled;

    /// <summary>Runs once, the first time the SDK is seen Ready. Re-reads cloud settings in
    /// case the boot read hit PlayerPrefs before the cloud store was live. LoadingStop is
    /// deliberately NOT sent: the Unity SDK leaves loading to the Unity loader. GameplayStart
    /// is also not sent here: LevelIntroSequence.HandOff emits it when control reaches the
    /// player.</summary>
    private static void EnsureReadyHandled()
    {
        if (readyHandled) return;
        readyHandled = true;
        GameSettings.Reload();
        SaveBridge.EnsureMigrated();
        FetchPortalUser();
    }

    // ------------------------------------------------------------- account integration
    // CrazyGames wants the platform identity used directly: a signed-in player plays under
    // their CrazyGames username (no standalone in-game identity needed), guests keep playing
    // as guests. Mirrors the Kongregate/Newgrounds adoption pattern - the portal name only
    // ever replaces a callsign the game itself generated (or one this adoption previously
    // applied), never a name the player typed themselves.
    private static string adoptedFromPortal = "";
    private static bool portalUserFetched;

    /// <summary>The signed-in CrazyGames player's username, "" when nobody is signed in (guest)
    /// or the host is not the portal. Set once the boot GetUser lands; MenuController listens
    /// for the change and swaps its manual username field for a "signed in as" label.</summary>
    public static string PortalUsername { get; private set; } = "";

    /// <summary>Raised when PortalUsername is resolved (or found to be empty).</summary>
    public static event Action PortalUsernameChanged;

    private static void FetchPortalUser()
    {
        if (portalUserFetched) return;
        portalUserFetched = true;
#if !NO_CRAZY_SDK
        try { CrazySDK.User.GetUser(OnPortalUser); } catch { }
#endif
    }

#if !NO_CRAZY_SDK
    private static void OnPortalUser(PortalUser user)
    {
        string name = user != null && user.username != null ? user.username.Trim() : "";
        PortalUsername = name;
        if (!string.IsNullOrEmpty(name) && name != adoptedFromPortal
            && (IsGeneratedCallsign(GameFlow.Username) || GameFlow.Username == adoptedFromPortal))
        {
            adoptedFromPortal = name;
            GameFlow.Username = name;
        }
        try { PortalUsernameChanged?.Invoke(); } catch { }
    }

    /// <summary>True when the stored callsign is one the game generated ("PILOT-1234"), i.e.
    /// the player has not chosen their own. Only then may we substitute the portal name.
    /// Same shape as KongregateBridge.IsGeneratedCallsign, kept local so the Crazy bridge
    /// does not depend on the Kongregate layer.</summary>
    private static bool IsGeneratedCallsign(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        if (!name.StartsWith("PILOT-", StringComparison.Ordinal)) return false;
        int n;
        return int.TryParse(name.Substring("PILOT-".Length), out n);
    }
#endif

    private static bool sdkMuted;

    /// <summary>True while CrazyGames' own audio mute is on. The platform setting wins
    /// over the in-game SOUND toggle (see GameSettings), so the player cannot unmute over
    /// it. Re-read on the same cadence as the state poll because the site header and the
    /// video player can change it at any time.</summary>
    public static bool SdkMuted => sdkMuted;

    private static void RefreshSdkMute()
    {
#if !NO_CRAZY_SDK
        try
        {
            bool muted = CrazySDK.Game.Settings.muteAudio;
            if (muted == sdkMuted) return;
            sdkMuted = muted;
            GameSettings.SetSdkMuted(muted);
        }
        catch { }
#endif
    }

    // --------------------------------------------------------- Data module (PlayerPrefs parity)
    public static bool HasKey(string key)
    {
#if !NO_CRAZY_SDK
        if (!Ready) return false;
        try { return CrazySDK.Data.HasKey(key); }
        catch { return false; }
#else
        return false;
#endif
    }

    public static string GetString(string key)
    {
#if !NO_CRAZY_SDK
        if (!Ready) return "";
        try { return CrazySDK.Data.GetString(key); }
        catch { return ""; }
#else
        return "";
#endif
    }

    public static void SetString(string key, string value)
    {
#if !NO_CRAZY_SDK
        if (!Ready) return;
        try { CrazySDK.Data.SetString(key, value ?? ""); }
        catch { }
#endif
    }

    public static void DeleteKey(string key)
    {
#if !NO_CRAZY_SDK
        if (!Ready) return;
        try { CrazySDK.Data.DeleteKey(key); }
        catch { }
#endif
    }

    public static void DeleteAll()
    {
#if !NO_CRAZY_SDK
        if (!Ready) return;
        try { CrazySDK.Data.DeleteAll(); }
        catch { }
#endif
    }

    // ---------------------------------------------------------------- leaderboard
    /// <summary>Submits a score to the CrazyGames platform leaderboard (fire-and-forget).
    /// The score is encrypted with the OFFICIAL SDK's ScoreEncryption (AES-CTR, 12-byte IV
    /// prefix + Unity marker byte - the exact format the platform validates server-side for
    /// Unity games) and posted through CrazySDK.User.SubmitScore. The board itself is
    /// displayed by CrazyGames (drawer, game-page widget, player profile).</summary>
    public static void SubmitScore(float score, string encryptionKey)
    {
        if (!Ready || string.IsNullOrEmpty(encryptionKey)) return;
#if !NO_CRAZY_SDK
        try
        {
            string encrypted = CrazyGames.ScoreEncryption.EncryptScore(score, encryptionKey);
            CrazySDK.User.SubmitScore(encrypted, score);
        }
        catch { }
#endif
    }

    // -------------------------------------------------------------- lifecycle events
    public enum SdkEvent
    {
        LoadingStop = 1,
        GameplayStart = 2,
        GameplayStop = 3,
        HappyTime = 4,
    }

    public static void SendEvent(SdkEvent ev)
    {
#if !NO_CRAZY_SDK
        if (!Ready) return;
        try
        {
            switch (ev)
            {
                case SdkEvent.GameplayStart: CrazySDK.Game.GameplayStart(); break;
                case SdkEvent.GameplayStop: CrazySDK.Game.GameplayStop(); break;
                case SdkEvent.HappyTime: CrazySDK.Game.HappyTime(); break;
                // case SdkEvent.LoadingStop: not supported for Unity (Unity loader owns loading)
            }
        }
        catch { }
#endif
    }

    // ---------------------------------------------------------------- ads
    // CrazySDK.Ad.RequestAd is callback-driven; this bridge mirrors its result on the
    // polled state AdCoordinator already reads. All placement policy lives in AdCoordinator.
    public enum AdType { Rewarded = 1, Midgame = 2 }

    private static int adMirror;   // 0 idle, 1 started, 2 finished, 3 error
    private static bool adBusy;

    /// <summary>The last adError the SDK returned ("" until one happens). Survives the state
    /// mirror so the UI can quote the platform's exact refusal after an ad failure.</summary>
    public static string LastAdError { get; private set; } = "";

    /// <summary>Current SDK ad lifecycle state: 0 idle, 1 started, 2 finished, 3 error.</summary>
    public static int AdState => adMirror;

    /// <summary>True while an ad request is sitting with the SDK (finding / showing an ad).</summary>
    public static bool AdBusy => adBusy;

    /// <summary>Asks the SDK to show an ad of the given type (rewarded or midgame). No-op
    /// unless the SDK is ready; the callbacks mirror the outcome on AdState/AdBusy.</summary>
    public static void RequestAd(AdType type)
    {
#if !NO_CRAZY_SDK
        if (!Ready) return;
        CrazyAdType t = type == AdType.Rewarded ? CrazyAdType.Rewarded : CrazyAdType.Midgame;
        adMirror = 0;
        adBusy = true;
        LastAdError = "";
        try
        {
            CrazySDK.Ad.RequestAd(t,
                () => adMirror = 1,
                err =>
                {
                    adBusy = false;
                    adMirror = 3;
                    mirrorError = err != null ? "ad " + err.message : "ad error";
                    LastAdError = err != null
                        ? (string.IsNullOrEmpty(err.code) ? "" : err.code + ": ") + err.message
                        : "unknown error";
                },
                () => { adBusy = false; adMirror = 2; });
        }
        catch (Exception ex)
        {
            adBusy = false;
            adMirror = 3;
            LastAdError = ex.Message;
        }
#endif
    }

    /// <summary>Clears the mirrored ad state after the coordinator consumed the outcome.</summary>
    public static void ClearAdState()
    {
        adMirror = 0;
        adBusy = false;
    }

    /// <summary>The SDK-recorded reason the SDK did not come up ready or an ad failed, e.g.
    /// "init timeout" or "sdkenv disabled". Empty when ready. Shown in the build footer.</summary>
    public static string MirrorError
    {
        get
        {
            if (enabled && state == StateReady) return "";
            return mirrorError;
        }
    }

    // ------------------------------------------------------ page/host introspection
    /// <summary>True while the browser tab/page is hidden. Unity WebGL does not reliably
    /// raise OnApplicationFocus on a tab switch, so the pause system polls this to stop the
    /// run when the player cannot see it (CrazyGames requires that).</summary>
    public static bool TabHidden
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return LunarTabHidden() != 0; } catch { return false; }
#else
            return false;
#endif
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private static int mobileCache = -1;
#endif

    /// <summary>True on a phone/tablet browser (user-agent check). Native platforms are
    /// already covered by Application.isMobilePlatform at the call site.</summary>
    public static bool IsMobileDevice
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (mobileCache < 0) { try { mobileCache = (LunarIsMobile() != 0) ? 1 : 0; } catch { mobileCache = 0; } }
            return mobileCache != 0;
#else
            return false;
#endif
        }
    }

    // ------------------------------------------------------------- native entry points
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int LunarTabHidden();

    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int LunarIsMobile();
#endif
}