using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The main menu, shown before the 3D world opens: enter a username, then launch Lunar
/// Harvester (the planet-by-planet campaign), Lunar Race (each world's carved canyon) or
/// Ridge SURVIVAL (the crest played for distance), or quit the game. All UI is built from
/// code so the scene is just this component.
/// </summary>
public partial class MenuController : MonoBehaviour
{
    public static MenuController Instance { get; private set; }

    [Tooltip("Maximum username length the input field accepts.")]
    public int usernameMaxLength = 16;

    private InputField usernameField;
    private Text placeholder;
    private Text userPromptLabel;
    private Text usernameHintLabel;
    private Text portalUserLabel;

    private void Awake()
    {
        Instance = this;
        GameFlow.ResetSaveIfVersionChanged();

        UiKit.EnsureEventSystem();
        EnsureCamera();
        GameSettings.Load();
        CrazyGamesBridge.Init();
        // Account integration: when the boot GetUser resolves a signed-in CrazyGames player,
        // RefreshUsernameUI swaps the manual username field for their platform identity.
        CrazyGamesBridge.PortalUsernameChanged += RefreshUsernameUI;
        // Host detection only - a no-op anywhere but kongregate.com, and called here as well as
        // from KongregateSession.Ensure so Enabled/StateLabel are already correct by the time
        // anything below reads them.
        KongregateBridge.Init();
        MenuMusic.Ensure();
        AudioInputGate.Ensure();
        StartCoroutine(WarmWorldOrbs());
        // Assign a guest callsign up-front so a fresh player is never scored under the
        // anonymous fallback; the input field below stays editable to rename it.
        GameFlow.EnsureUsername();

        // Lunar Trials edition: the challenge ladder replaces the regular menu entirely.
        if (GameEdition.IsTrials)
        {
            ChallengeRunner.ClearActive();
            TrialsSelectScreen.Open();
            return;
        }

        BuildUI();

        // A failed Harvester run routes back here with the planet map queued; a finish board's
        // token badge routes back with the garage queued. One request at a time - garage first.
        if (GameFlow.GarageQueued)
        {
            GameFlow.GarageQueued = false;
            GameFlow.PlanetMapQueued = false;
            OpenGarage();
        }
        else if (GameFlow.PlanetMapQueued)
        {
            GameFlow.PlanetMapQueued = false;
            OpenPlanetMap();
        }
        else if (FastLaunchApplies && !fastLaunchedThisSession)
        {
            // CrazyGames Full-Launch onboarding: land new sessions straight in the first
            // available harvest run - no menu trip (Gameplay requirement: land directly in
            // gameplay). Portal + editor only: Kongregate, itch.io and desktop builds keep
            // the classic menu entry point. One auto-launch per app session, and never when
            // a garage/planet-map flow is queued - QUIT TO MENU and the end boards always
            // show the real menu afterwards.
            fastLaunchedThisSession = true;
            StartFirstUnlockedHarvest();
            return;
        }
    }

    /// <summary>True where the CG fast-launch + first-run tutorials apply (TutorialCoach gate).</summary>
    private static bool FastLaunchApplies => TutorialCoach.Enabled;

    private static bool fastLaunchedThisSession;

    private void EnsureCamera()
    {
        Camera cam = Camera.main;
        if (cam != null) return;
        GameObject go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.02f, 0.05f, 1f);
        go.AddComponent<AudioListener>();
        // Layers 8-15 are reserved for the GaragePreview showrooms: the garage's full viewport
        // uses layer 8 and the shop's per-card viewports use 9..12 (one layer each, since every
        // showroom is built at the world origin and would otherwise bleed into its neighbours).
        // Excluding the whole band keeps every showroom inside its RenderTexture viewport, never
        // in the menu background.
        cam.cullingMask &= ~(0xFF << 8);
    }

    private void BuildUI()
    {
        Canvas canvas = UiKit.CreateRootCanvas("MenuCanvas", 100);

        // Lunar artwork backdrop, plus a translucent veil so the UI stays readable on the
        // lighter parts of the image. Fixed screen-filling background shared with the leaderboard.
        UiKit.Background(canvas.transform, "LunarBackground", "LunarBackground");
        RectTransform dim = UiKit.Rect(canvas.transform, "Dim", Vector2.zero, Vector2.zero, Vector2.zero);
        UiKit.StretchToFull(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0.015f, 0.015f, 0.035f, 0.6f);

        UiKit.Label(canvas.transform, "Title", "LUNAR HARVEST", 70,
            new Color(238f / 255f, 221f / 255f, 130f / 255f, 1f), new Vector2(0f, -50f), TextAnchor.MiddleCenter, 1200f, 90f,
            UiKit.UiFontKind.Display);
        UiKit.Label(canvas.transform, "Subtitle", "HARVEST - SURVIVE - RETURN", 26,
            new Color(0.6f, 0.68f, 0.8f, 1f), new Vector2(0f, -122f), TextAnchor.MiddleCenter, 1000f, 40f,
            UiKit.UiFontKind.Display);

        // --- Primary action grid ---
        // A 2x2 block of image buttons moved up and more central now that the old SAVE button
        // freed the middle: Quick Launch (top-left), level select map (top-right), Shop
        // (bottom-left) and Leaderboard (bottom-right). The garage moved onto the token badge
        // (top-left); Shop opens the vehicle bays. All four are anchored to the horizontal
        // centre axis so the block reads as one square instead of hugging the screen edges;
        // each cell keeps an identical footprint whatever the art's native ratio. The art is
        // semi-transparent (so the backdrop bleeds through) and framed by a thin border.
        // Hovering a cell shows its role in the tooltip below.
        Text hoverTip = UiKit.Label(canvas.transform, "HoverTip", "", 30,
            new Color(1f, 0.9f, 0.6f, 1f), new Vector2(0f, -372f), TextAnchor.MiddleCenter, 600f, 40f);
        hoverTip.gameObject.SetActive(false);

        Color gridFrame = new Color(0.38f, 0.45f, 0.52f, 0.55f);

        UiKit.WireHover(UiKit.ImageButton(canvas.transform, "B_QuickLaunch", "QuickLaunch",
            new Vector2(0.5f, 1f), new Vector2(-240f, -420f), new Vector2(440f, 190f),
            () => StartFirstUnlockedHarvest(), 0.82f, 3f, gridFrame), hoverTip, "QUICK LAUNCH");

        UiKit.WireHover(UiKit.ImageButton(canvas.transform, "B_SolarSystem", "SolarSystem",
            new Vector2(0.5f, 1f), new Vector2(240f, -420f), new Vector2(440f, 190f),
            () => OpenPlanetMap(false), 0.82f, 3f, gridFrame), hoverTip, "WORLD SELECT");

        UiKit.WireHover(UiKit.ImageButton(canvas.transform, "B_Shop", "Shop",
            new Vector2(0.5f, 1f), new Vector2(-240f, -630f), new Vector2(440f, 190f),
            () => OpenShop(), 0.82f, 3f, gridFrame), hoverTip, "SHOP");

        UiKit.WireHover(UiKit.ImageButton(canvas.transform, "B_Podium", "Podium",
            new Vector2(0.5f, 1f), new Vector2(240f, -630f), new Vector2(440f, 190f),
            () => GameFlow.GoToLeaderboard(), 0.82f, 3f, gridFrame), hoverTip, "LEADERBOARD");

        // Sound toggle (top-right): menus have music/UI sounds too, and this is the only
        // place to change the setting before a run starts.
        Button soundButton = null;
        Text soundLabel = null;
        soundButton = UiKit.Button(canvas.transform, "B_Sound", GameSettings.Label, 22,
            new Color(0.08f, 0.1f, 0.13f, 0.9f), new Color(0.8f, 0.9f, 1f, 1f),
            new Vector2(-40f, 40f), new Vector2(210f, 48f), () =>
            {
                GameSettings.ToggleMute();
                if (soundLabel != null) soundLabel.text = GameSettings.Label;
            });
        soundLabel = soundButton.GetComponentInChildren<Text>();

        // --- Username entry ---
        // Just one tiny caption above the box (the old "PILOT USERNAME" header and the
        // hint line under it are gone - the field itself already shows what to type).
        // Signed-in CrazyGames players play under their platform username instead (account
        // integration): RefreshUsernameUI hides the field and shows their identity, and
        // stays in this manual form for guests and non-portal hosts.
        userPromptLabel = UiKit.Label(canvas.transform, "UserPrompt", "username", 20,
            new Color(0.6f, 0.68f, 0.8f, 1f), new Vector2(0f, -232f), TextAnchor.MiddleCenter, 300f, 26f);

        RectTransform fieldRt = UiKit.Panel(canvas.transform, "UsernameField",
            UiKit.AnchorFor(new Vector2(0f, -263f)), new Vector2(0f, -263f),
            new Vector2(460f, 58f), new Color(0.08f, 0.09f, 0.11f, 1f));

        Text fieldText = UiKit.Label(fieldRt, "Text", GameFlow.Username, 30,
            Color.white, Vector2.zero, TextAnchor.MiddleLeft, 430f, 50f);
        RectTransform fieldTextRt = fieldText.rectTransform;
        UiKit.StretchToFull(fieldTextRt);
        fieldTextRt.offsetMin = new Vector2(18f, 0f);
        fieldTextRt.offsetMax = new Vector2(-18f, 0f);

        Text placeholder = UiKit.Label(fieldRt, "Placeholder", "type your username", 30,
            new Color(0.45f, 0.45f, 0.5f, 1f), Vector2.zero, TextAnchor.MiddleLeft, 430f, 50f);
        this.placeholder = placeholder;
        RectTransform placeholderRt = placeholder.rectTransform;
        UiKit.StretchToFull(placeholderRt);
        placeholderRt.offsetMin = new Vector2(18f, 0f);
        placeholderRt.offsetMax = new Vector2(-18f, 0f);

        usernameField = fieldRt.gameObject.AddComponent<InputField>();
        usernameField.textComponent = fieldText;
        usernameField.placeholder = placeholder;
        usernameField.characterLimit = usernameMaxLength;
        usernameField.lineType = InputField.LineType.SingleLine;
        usernameField.text = GameFlow.Username;
        usernameField.onEndEdit.AddListener(OnUsernameEntered);
        placeholder.gameObject.SetActive(string.IsNullOrEmpty(usernameField.text));

        // The username saves itself on enter/deselect (no SAVE button to trip over on a phone
        // keyboard - dismissing the field is enough to bank the name for the leaderboards).
        usernameHintLabel = UiKit.Label(canvas.transform, "UsernameHint", "saves automatically", 18,
            new Color(0.42f, 0.48f, 0.55f, 1f), new Vector2(0f, -319f), TextAnchor.MiddleCenter, 340f, 24f);

        // Platform-identity label occupying the same slot as the field; visible only when a
        // signed-in CrazyGames user was adopted (RefreshUsernameUI).
        portalUserLabel = UiKit.Label(canvas.transform, "PortalUser", "", 30,
            new Color(0.85f, 0.92f, 1f, 1f), new Vector2(0f, -263f), TextAnchor.MiddleCenter, 460f, 58f);
        portalUserLabel.gameObject.SetActive(false);
        RefreshUsernameUI();

        // Test hooks (editor / localhost / cgtest=1 only): unlock every world and mode so any
        // backdrop (e.g. Titan's Saturn) can be checked on the web without grinding the check-
        // point chain, and wipe all saved progress for a genuine fresh start. Both invisible on
        // the live CrazyGames game.
        if (AllowCustomUnlocks())
        {
            UiKit.Button(canvas.transform, "B_TestUnlock", "[TEST] UNLOCK ALL", 22,
                new Color(0.22f, 0.2f, 0.06f, 1f), new Color(1f, 1f, 0.7f, 1f),
                new Vector2(-220f, -872f), new Vector2(380f, 46f), () => UnlockAllForTesting());
            UiKit.Button(canvas.transform, "B_TestFresh", "[TEST] START FRESH", 22,
                new Color(0.1f, 0.16f, 0.22f, 1f), new Color(0.7f, 0.85f, 1f, 1f),
                new Vector2(220f, -872f), new Vector2(380f, 46f), () => StartFreshForTesting());
        }

        // --- Quit ---
        // Application.Quit() cannot close a browser tab/window, so the quit button is
        // meaningless on WebGL builds; keep it for the standalone desktop player only.
#if !UNITY_WEBGL
        UiKit.Button(canvas.transform, "B_Quit", "QUIT", 26,
            new Color(0.28f, 0.08f, 0.08f, 1f), new Color(1f, 0.7f, 0.7f, 1f),
            new Vector2(0f, -812f), new Vector2(260f, 56f), () => Quit());
#endif

        // Tiny footer so a stale/cached build is easy to spot. Build tag only - the SDK/ad
        // debug state (SDK / ADS / PORTAL / last ad error) is not player-facing and is out
        // of the submitted build; RefreshBuildTag keeps this in sync.
        buildTagLabel = UiKit.Label(canvas.transform, "BuildTag",
            "BUILD " + GameFlow.BuildTag, 16,
            new Color(0.4f, 0.45f, 0.5f, 1f), new Vector2(0f, 8f), TextAnchor.MiddleCenter, 460f, 24f);

        // Top-left token badge: the garage wallet. Click it for the garage; it pulses while
        // an upgrade is affordable.
        AttachWallet(canvas);
    }

    // ------------------------------------------------------------------ util drawer

    /// <summary>Pre-bakes every world's orb strip plus the shared disc/shade/limb sprites at
    /// boot, one planet per frame, so the FIRST World Select open is as fast as every later
    /// one. Five light frames while the menu is already up cost nothing a player notices;
    /// a half-second bake on the player's first click of WORLD SELECT is what it used to be.</summary>
    private IEnumerator WarmWorldOrbs()
    {
        WorldOrb.PrimeSprites();
        for (int i = 0; i < LevelManager.PlanetCount; i++)
        {
            WorldOrb.WarmUp(i);
            yield return null;
        }
    }

    private static bool AllowCustomUnlocks()
    {
        if (Application.isEditor) return true;
        string url = Application.absoluteURL;
        return url.Contains("127.0.0.1") || url.Contains("localhost") || url.Contains(CrazyGamesBridge.UrlFlagTest);
    }

    private void UnlockAllForTesting()
    {
        for (int w = 0; w < LevelManager.PlanetCount; w++)
        {
            LevelManager.UnlockHarvestGate(w);
            LevelManager.RecordHarvestBest(w, 500);
            LevelManager.RecordRaceBestTier(w, 2);
            LevelManager.RecordRaceBestGrade(w, RivalRacer.GradeCount - 1);
            LevelManager.RecordRidgeBestGrade(w, 3);
            // The reworked chain (LevelManager.SlotRequirementMet) reads TWO survive flags, not
            // the grade stores: a world's PROSPECTOR CLEAR opens its ridge, and a world's RIDGE
            // CLEAR opens the next planet. Without these the derived chain stalls at the Moon.
            LevelManager.RecordProspectorCleared(w);
            LevelManager.RecordRidgeCleared(w);
        }
        UpgradeSystem.AddTokens(5000);
        VehicleSystem.OwnAllForTesting();
        GameObject gone = GameObject.Find("MenuCanvas");
        if (gone != null) Destroy(gone);
        BuildUI();
    }

    /// <summary>Wipes every saved value (PlayerPrefs + the CrazyGames account copy) and all
    /// in-memory run/progress state, then rebuilds the menu - a genuine from-scratch start
    /// for testing the intro, the unlock chain and the first-timer flow in the editor.</summary>
    private void StartFreshForTesting()
    {
        GameFlow.ResetRun();
        GameFlow.ResetRidgeProgress();
        LevelManager.CurrentLevelIndex = 0;
        GameFlow.Mode = GameMode.Harvester;
        GameFlow.CurrentCourse = RaceCourse.Canyon;
        GameFlow.PlanetMapQueued = false;
        GameFlow.GarageQueued = false;
        SaveBridge.DeleteAll();
        // Stamp the save version so the next scene boot's ResetSaveIfVersionChanged can't see a
        // missing key and wipe AGAIN (deleting whatever the player earns in this fresh window).
        GameFlow.StampSaveVersion();
        GameObject gone = GameObject.Find("MenuCanvas");
        if (gone != null) Destroy(gone);
        BuildUI();
    }

    private static Text Txt(RectTransform parent, string name, string text, int size, Color color,
        Vector2 pos, TextAnchor align, float width = 220f, float height = 40f,
        UiKit.UiFontKind fontKind = UiKit.UiFontKind.Body)
    {
        return UiKit.Label(parent, name, text, size, color, pos, align, width, height, fontKind);
    }

    

    // World Select lives in MenuController.WorldSelect.cs (same partial class).

    /// <summary>Quick Start: launches the first world that still needs a harvest, falling back
    /// to the first unlocked (then the very first) world. Skips the menu entirely.</summary>
    private static void StartFirstUnlockedHarvest()
    {
        int target = -1;
        int fallback = -1;
        for (int w = 0; w < LevelManager.PlanetCount; w++)
        {
            if (!LevelManager.IsUnlocked(w)) continue;
            if (fallback < 0) fallback = w;
            if (!LevelManager.IsCleared(w)) { target = w; break; }
        }
        if (target < 0) target = fallback < 0 ? 0 : fallback;
        GameFlow.StartHarvesterAt(target);
    }

    // The garage lives in MenuController.Garage.cs (same partial class).

    private void OnUsernameEntered(string value)
    {
        SaveUsername(value);
        if (placeholder != null) placeholder.gameObject.SetActive(string.IsNullOrEmpty(value));
    }

    private void SaveUsername(string value)
    {
        GameFlow.Username = value;
    }

    /// <summary>Account integration display: a signed-in CrazyGames player's platform username
    /// replaces the manual entry field (guests and non-portal hosts keep the editable field).</summary>
    private void RefreshUsernameUI()
    {
        if (this == null) return;
        bool portal = !string.IsNullOrEmpty(CrazyGamesBridge.PortalUsername);
        if (userPromptLabel != null) userPromptLabel.gameObject.SetActive(!portal);
        if (usernameField != null) usernameField.gameObject.SetActive(!portal);
        if (usernameHintLabel != null) usernameHintLabel.gameObject.SetActive(!portal);
        if (portalUserLabel != null)
        {
            portalUserLabel.gameObject.SetActive(portal);
            if (portal)
                portalUserLabel.text = "SIGNED IN AS " + CrazyGamesBridge.PortalUsername.ToUpper();
        }
    }

    private void OnDestroy()
    {
        CrazyGamesBridge.PortalUsernameChanged -= RefreshUsernameUI;
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}