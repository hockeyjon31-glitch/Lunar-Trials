using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Level-opening cinematic. On load the world appears first as a planet backdrop card
/// (artist image from Resources/PlanetBackdrops, or a procedural stand-in) that slowly
/// grows in view; it then fades into the live 3D level as the camera pulls a scripted
///     dolly/orbit along the course. Once the planet is fully zoomed in, a three-column mission
///     briefing card holds on screen - LEFT (green/gold): SURVIVAL objective on a ridge run,
///     this world's RESOURCES in Harvester, or the RIVALS' target times in Race; MIDDLE (green):
///     REWARDS, the token payouts including the ridge crest grade lines; RIGHT (red): the
///     per-level surface hazards. The player reads it at leisure and confirms with ENTER /
    ///     SPACE / CLICK to deploy, and the shot settles onto the buggy. The sequence then hands
    /// off: to the existing 3-2-1-GO race countdown for Lunar Race (built disabled by
    /// GameBootstrap, re-enabled here), or straight to driving control for Harvester.
///
/// GameBootstrap creates one of these on every level load. The shots drive BuggyCamera's
/// cinematic fields (offsetScale for zoom, cinematicLookAt + cinematicOffset for framing),
/// mirroring the proven input-lock + audio-hook pattern of LevelEndSequence.
/// </summary>
public class LevelIntroSequence : MonoBehaviour
{
    [Header("References")]
    public BuggyController buggy;
    public MoonTerrainGen terrain;
    public RaceCountdown countdown; // race handoff; null in Harvester
    public OxygenManager oxygen;

    [Header("Timing (seconds)")]
    [Tooltip("Black veil over the empty level, cleared as the planet card fades in.")]
    public float fadeFromBlack = 0.9f;
    [Tooltip("Seconds until the planet card is fully shown.")]
    public float planetFadeIn = 1.2f;
    [Tooltip("Length of the planet-card phase before the live reveal begins.")]
    public float backdropDuration = 1.9f;
    [Tooltip("Length of the 3D dolly/orbit reveal that pulls onto the buggy.")]
    public float revealDuration = 7.5f;
    [Tooltip("How far into the reveal the planet card starts dissolving (crossfade to the live world).")]
    public float planetCrossStart = 0.35f;
    [Tooltip("How long the planet-card crossfade takes once it starts.")]
    public float planetCrossDuration = 1.7f;
    [Tooltip("How far into the reveal the planet + course title card starts fading in.")]
    public float titleInStart = 2.5f;
    [Tooltip("How fast the DEPLOY prompt blinks while the briefing waits (Hz).")]
    public float promptPulseSpeed = 3.2f;
    [Tooltip("How long the DEPLOY prompt fades in when the briefing hold starts.")]
    public float promptFadeIn = 0.5f;
    [Tooltip("How long the briefing card fades out while the camera settles onto the buggy.")]
    public float briefFadeOut = 0.35f;
    [Tooltip("Settling beat that drops the dolly onto the normal chase framing.")]
    public float settleDuration = 0.8f;
    [Tooltip("How far the camera pulls back at the widest shot frame (offsetScale).")]
    public float cameraStartScale = 8.5f;
    [Tooltip("Where the reveal ends its zoom-in (offsetScale at handoff).")]
    public float cameraEndScale = 1.15f;
    [Tooltip("How far up the course the reveal dolly glides the framing (metres).")]
    public float dollyLookAhead = 190f;

    [Header("Skip & returners")]
    [Tooltip("Returning players (intro seen once) skip straight to the deploy beat.")]
    public bool autoSkipReturningPlayers = true;
    [Tooltip("How long the settle is when the intro is skipped (seconds).")]
    public float skipSettleDuration = 0.35f;

    private const string IntroSeenKey = "lunar.intro.seen";

    private enum Phase { Backdrop, Reveal, Hold, Settle, Done }

    private Phase phase = Phase.Backdrop;
    private float timer;
    private bool handedOff;
    private bool skipTapped;
    private float settleStartScale;

    /// <summary>True while the intro is still playing. Note the object can outlive the
    /// cinematic, so callers must test this rather than merely looking for the component.</summary>
    public bool IsRunning => phase != Phase.Done;

    private BuggyCamera followCam;
    private Image veil;
    private Image backdrop;
    private RawImage videoBackdrop; // Titan plays a looping atmospheric clip instead of the still card
    private bool videoReady;
    private bool videoPending;    // clip is still preparing: hold the card back rather than flash the still
    private float backdropAlpha;  // last alpha asked for, so a late arrival re-applies itself
    private const float VideoFallbackSeconds = 4f; // give up waiting, then show the still card
    private RectTransform backdropRt;
    private Text titlePlanet;
    private Text titleSub;
    private Image[] titleStars;
    private Image briefingPanelImg;
    private Text resourceTitle;
    private Text resourceLines;
    private Text rewardTitle;
    private Text rewardLines;
    private Text dangerTitle;
    private Text dangerLines;
    private Text briefingText;
    private Text deployPrompt;
    private Text deployHint;
    private Color leftHeaderColor = new Color(1f, 0.9f, 0.6f, 1f); // Gold now for all formats, because Rewards are Green
    private Color resourceLinesBase = new Color(0.88f, 1f, 0.9f, 1f); // set on build; gold on Ridge Survival
    private Color rewardLinesBase = new Color(0.62f, 1f, 0.7f, 1f);
    private CanvasGroup resourceLinesCg; // fades the rich-text coloured lines (Text.color is bypassed by <color> tags)
    private CanvasGroup rewardLinesCg; // fades the rich-text-coloured grade lines (Text.color is bypassed by <color> tags)
    private AudioSource introMusic;

    // The rewarded "2x TOKENS THIS RUN" offer on the deploy hold. Dormant until the release
    // build flips AdCoordinator.AdsEnabled - while dormant DoublerAvailable is false and the
    // button never spawns, so today's intro is untouched.
    private Button doublerButton;
    private const string DoublerLabel = "2x TOKENS THIS RUN";
    // When a doubler tap is refused by the rewarded-ad cooldown, the button locks with a
    // live "ADS: Ns" countdown instead of doing nothing (the deny was silent before).
    private float doublerLockUntil = -1f;

    private Vector3 focusBase;   // spawn/framing anchor
    private Vector3 focusDrift;  // where the dolly glides the framing to

    private void Start()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (terrain == null) terrain = FindAnyObjectByType<MoonTerrainGen>();
        if (oxygen == null) oxygen = OxygenManager.Instance;
        if (countdown == null)
            countdown = FindAnyObjectByType<RaceCountdown>(FindObjectsInactive.Include) as RaceCountdown;
        followCam = FindAnyObjectByType<BuggyCamera>();

        // Lock the run until the intro hands off (defensive; race already locks at build).
        if (buggy != null) buggy.inputEnabled = false;
        if (oxygen != null) oxygen.clockPaused = true;

        // Anchor the framing on the parked buggy; in a race the dolly glides from the start
        // line up the carved course ahead of it.
        if (buggy != null) focusBase = buggy.transform.position;
        focusDrift = ComputeDriftTarget();

        if (followCam != null)
        {
            followCam.cinematicMode = true;
            followCam.cinematicLookAt = focusBase;
            followCam.offsetScale = cameraStartScale; // widest shot hides behind the veil
        }

        BuildCanvas();

        // Intro music plays once through the planet zoom and briefing (no looping), then
        // fades out when the player deploys (Settle) and dies with the sequence on HandOff.
        AudioClip music = Resources.Load<AudioClip>("Intro_Music");
        if (music != null)
        {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.loop = false;
            src.playOnAwake = false;
            src.volume = 0.8f;
            src.clip = music;
            src.Play();
            introMusic = src;
        }

        // The level is ALREADY fully built by GameBootstrap before this Start - the pause while
        // zooming in is NOT loading, it is the first time the close-up world actually draws
        // (WebGL compiles shaders + uploads meshes on first use), which peaks right where the
        // planet card crossfades to the live dolly. Render the camera through the whole reveal
        // range of shots now, while the black veil still hides the screen, so that cost is paid
        // off-screen and the crossfade stays smooth.
        if (followCam != null)
        {
            Camera cam = followCam.GetComponent<Camera>();
            if (cam != null)
            {
                float[] warm = { cameraStartScale, 5f, 3f, cameraEndScale, 1f };
                foreach (float s in warm)
                {
                    followCam.offsetScale = s;
                    cam.Render();
                }
                followCam.offsetScale = cameraStartScale;
            }
        }

        // Returning players skip the planet zoom and briefing entirely and hop straight to the
        // short deploy settle, so replaying a level is one brief beat instead of a ten-second
        // cinematic. The Shader-warm render above already ran, so the first frames are smooth.
        // On the release build (ads live) the Harvester intro EXCEPTIONS this: the deploy hold
        // carries the rewarded "2x TOKENS" offer, so returners get the hold (and the option)
        // one more brief beat instead of the raw skip. Any other mode skips as before.
        if (autoSkipReturningPlayers && SaveBridge.GetInt(IntroSeenKey, 0) > 0
            && !AdCoordinator.DoublerAvailable)
        {
            SkipToDeploy();
        }
    }

    private void BuildCanvas()
    {
        Canvas canvas = UiKit.CreateRootCanvas("LevelIntroCanvas", 300);
        canvas.transform.SetParent(transform, false);

        // Space behind the planet card (shows through the card's transparent corners).
        GameObject spaceGo = new GameObject("Space", typeof(RectTransform), typeof(Image));
        spaceGo.transform.SetParent(canvas.transform, false);
        Image space = spaceGo.GetComponent<Image>();
        UiKit.StretchToFull(space.rectTransform);
        space.color = new Color(0.005f, 0.005f, 0.02f, 1f);

        // Titan's backdrop card is a looping video clip (StreamingAssets/Titan_Clipped.mp4)
        // streamed through a RenderTexture, so the same URL source plays on desktop and WebGL
        // (VideoClip assets are unsupported on WebGL - the URL option is required there). It
        // lives on a RawImage child of the still card, so the "world growing in view" scale
        // beats still apply; the still card stays underneath as the fallback if the clip can't
        // be prepared.

        // Planet backdrop card; artist art is loaded from Resources/PlanetBackdrops/<key>.png,
        // otherwise a procedural stand-in keeps the "world growing in view" beats intact.
        GameObject bgGo = new GameObject("PlanetCard", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(canvas.transform, false);
        backdropRt = bgGo.GetComponent<RectTransform>();
        UiKit.StretchToFull(backdropRt);
        backdrop = bgGo.GetComponent<Image>();
        Texture2D art = LoadPlanetArt();
        backdrop.sprite = Sprite.Create(art, new Rect(0f, 0f, art.width, art.height), new Vector2(0.5f, 0.5f));
        backdrop.preserveAspect = true;
        backdrop.color = new Color(1f, 1f, 1f, 0f);
        TryStartTitanVideo(bgGo.transform);

        // Planet + course title card; with the dangers card it forms the mission briefing, held on
        // screen at the end of the reveal until the player confirms deployment.
        titlePlanet = MakeTitle(canvas.transform, "TitlePlanet", 80, new Vector2(0f, -90f), Color.white);
        titleSub = MakeTitle(canvas.transform, "TitleCourse", 32, new Vector2(0f, -20f),
            new Color(0.85f, 0.94f, 1f, 0.9f));

        string sub = GameFlow.IsRidgeSurvival
            ? "RIDGE - SURVIVAL"
            : (GameFlow.Mode == GameMode.Race
                ? GameFlow.CourseName(GameFlow.CurrentCourse) + " - LUNAR RACE"
                : GameFlow.Mode == GameMode.Prospector
                    ? "PROSPECTOR RUN - CANYON DESCENT"
                    : "LUNAR HARVESTER - EXPLORATION");
        string planet = LevelManager.CurrentLevel.planetName;
        titlePlanet.text = planet;
        titleSub.text = sub;

        // Harvest rating as a star row beside the world's name - "THE MOON ★★★" reading how far the
        // world's field has been worked up: each star is one harvest GRADE (the level you're ON,
        // so a fresh level-1 field shows its first star and a cleared grade-2 field shows two).
        // The title text and the star row sit CENTRED as one body - the title shifts left by the
        // stars' width so the pair never reads lopsided.
        if (GameFlow.Mode == GameMode.Harvester || GameFlow.Mode == GameMode.Prospector)
        {
            bool prospector = GameFlow.Mode == GameMode.Prospector;
            int grade = prospector
                ? LevelManager.GetProspectorGrade(LevelManager.CurrentLevelIndex)
                : LevelManager.GetHarvestGrade(LevelManager.CurrentLevelIndex);
            float nameW = titlePlanet.preferredWidth;
            if (nameW <= 0f) nameW = titlePlanet.text.Length * 0.62f * titlePlanet.fontSize;
            const float starSize = 40f;
            const float starGap = 10f;
            const float titleStarGap = 34f;
            float starsW = grade * starSize + (grade - 1) * starGap;

            // Centre [title + gap + stars] as one body on the card's centre line.
            float titleX = -(titleStarGap + starsW) * 0.5f;
            float firstStarX = (nameW + titleStarGap + starSize - starsW) * 0.5f;
            titlePlanet.rectTransform.anchoredPosition = new Vector2(titleX, -90f);

            // Baked gold star sprites (never text: WebGL strips the \u2605 glyph), brighter
            // than the old font-only stars.
            titleStars = UiKit.StarLadder(canvas.transform, "TitleStars", grade, grade,
                new Vector2(0.5f, 0.5f), new Vector2(firstStarX, -90f), starSize, starGap);
            foreach (Image im in titleStars) im.color = new Color(1f, 0.88f, 0.32f, 0f);
        }

        // Mission briefing panel: two columns under the title card. LEFT = the level's riches -
        // RESOURCES (green) in a harvest run, SURVIVAL (gold) on a ridge run, or the RIVALS
        // (gold) with their target times in a race. RIGHT = HAZARDS (red) - the surface hazards,
        // so the drive is a worth/reward decision before deployment. Below both, a per-level
        // narrative paragraph (plain block, no heading; filled per level in MissionBriefing.cs).
        // Narrative-only: this is the pre-deploy screen, it never clutters the menus.
        RectTransform briefingPanelRt = UiKit.Panel(canvas.transform, "BriefingPanel",
            new Vector2(0.5f, 0.5f), new Vector2(0f, -290f), new Vector2(1900f, 500f),
            new Color(0f, 0f, 0f, 0f));
        briefingPanelImg = briefingPanelRt.GetComponent<Image>();

        bool race = GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival;
        Color resourceHeader = new Color(1f, 0.9f, 0.6f, 1f);
        Color rivalHeader = new Color(1f, 0.84f, 0.35f, 1f);
        Color hazardHeader = new Color(1f, 0.48f, 0.42f, 1f);

// LEFT column. Harvest mode = what this world provides; Race mode = the rivals to beat;
        // Ridge Survival = the survival objective (survival is its own board).
        leftHeaderColor = race ? rivalHeader : resourceHeader;
        resourceTitle = MakeTitle(canvas.transform, "LeftHeader", 32,
            new Vector2(-470f, -150f), leftHeaderColor);
        resourceTitle.text = race ? "RIVALS" : "RESOURCES";
        if (race && !GameFlow.IsRidgeSurvival)
        {
            resourceTitle.text = "RIVALS  \u00B7  GRADE " + (Mathf.Clamp(GameFlow.RaceGradeIndex, 0, RivalRacer.GradeCount - 1) + 1)
                + " OF " + RivalRacer.GradeCount;
        }
        resourceLines = MakeTitle(canvas.transform, "LeftLines", 26,
            new Vector2(-470f, -245f), new Color(0.88f, 1f, 0.9f, 1f));
        string harvestable = LevelManager.CurrentLevel.harvestableName;
        if (string.IsNullOrEmpty(harvestable)) harvestable = "Mineral deposits";
        string[] leftList;
        if (GameFlow.IsRidgeSurvival)
        {
            resourceTitle.text = "SURVIVAL";
            // Gold styling. Opacity is owned entirely by the resourceLines CanvasGroup below
            // (applied after the glyph vertices, so tagged and plain runs fade alike), so the
            // colour here never has to smuggle an alpha value - no repeat of the "3 lines glow
            // through the planet rise" leak.
            resourceLinesBase = new Color(1f, 0.9f, 0.6f, 1f);
            // The survival board: how to survive the crest. The grade lines and their token
            // payouts live next door in the middle REWARDS column (green).
            leftList = new[]
            {
                "Survive the ridge crest",
                "Marked target distances",
                "O2 canisters refill the tank",
            };
        }
        else if (race)
        {
            // The competition board, timed for the run's CURRENT grade: each harder grade makes
            // every rival 5 seconds quicker. Each tier line is rendered in its own medal colour
            // (RivalRacer.TierColor) via rich-text tags, matching the payout pots on the
            // REWARDS board. Clearing the grade (beating the GOLD rival) unlocks the next faster
            // field on this world.
            int raceGrade = Mathf.Clamp(GameFlow.RaceGradeIndex, 0, RivalRacer.GradeCount - 1);
            string timeGold = OxygenManager.FormatRaceTime(Mathf.RoundToInt(RivalRacer.DefaultSeconds(RivalRacer.Tier.Gold, raceGrade) * 100f));
            string timeSilver = OxygenManager.FormatRaceTime(Mathf.RoundToInt(RivalRacer.DefaultSeconds(RivalRacer.Tier.Silver, raceGrade) * 100f));
            string timeBronze = OxygenManager.FormatRaceTime(Mathf.RoundToInt(RivalRacer.DefaultSeconds(RivalRacer.Tier.Bronze, raceGrade) * 100f));
            leftList = new[]
            {
                Rich("GOLD  \u00B7  " + timeGold, RivalRacer.TierColor(RivalRacer.Tier.Gold)),
                Rich("SILVER  \u00B7  " + timeSilver, RivalRacer.TierColor(RivalRacer.Tier.Silver)),
                Rich("BRONZE  \u00B7  " + timeBronze, RivalRacer.TierColor(RivalRacer.Tier.Bronze)),
                "Beat the GOLD rival to clear this grade",
            };
        }
        else
        {
            // The world's own riches; its token rewards are on the middle REWARDS board next door.
            // The harvest site rating is shown as gold stars beside the planet title (TitleStars);
            // a graded field carries extra deposits and tighter storms, kept out of these columns.
            leftList = new[]
            {
                "Oxygen canisters",
                harvestable + " deposits",
            };
        }
        resourceLines.text = "• " + string.Join("\n• ", leftList);
        RectTransform resourceLinesRt = resourceLines.rectTransform;
        resourceLinesRt.sizeDelta = new Vector2(560f, 220f);
        resourceLines.verticalOverflow = VerticalWrapMode.Overflow;
        resourceLines.horizontalOverflow = HorizontalWrapMode.Wrap;

        // Race RIVALS lines can carry <color> rich-text tags (medal colours). Those bake their
        // alpha into the glyph vertices, bypassing Text.color, so drive this text's opacity via
        // a CanvasGroup (applied after vertices) exactly like the REWARDS lines below. Colour is
        // fixed at full alpha; the group (starting 0) owns the fade.
        resourceLines.color = new Color(resourceLinesBase.r, resourceLinesBase.g, resourceLinesBase.b, 1f);
        resourceLinesCg = resourceLines.gameObject.AddComponent<CanvasGroup>();
        resourceLinesCg.alpha = 0f;

        // MIDDLE column: REWARDS (green) - what the run pays out. Ridge Survival lists the crest
        // grade lines with their token awards (mirrors GameFlow's constants so the intro can
        // never drift from the painted crossing lines); Race lists the rival tier token pots
        // and the BRONZE check point that opens the NEXT world; Harvester lists the nugget
        // token values, the spare-oxygen bonus and the SURVIVE check point that opens its RACE.
        rewardTitle = MakeTitle(canvas.transform, "RewardHeader", 32,
            new Vector2(0f, -150f), new Color(0.45f, 1f, 0.55f, 1f));
        rewardTitle.text = "REWARDS";
        rewardLines = MakeTitle(canvas.transform, "RewardLines", 26,
            new Vector2(0f, -245f), new Color(0.62f, 1f, 0.7f, 1f));
        string[] rewardList;
        if (GameFlow.IsRidgeSurvival)
        {
            // The four grade lines, plus a note that this is a BONUS level - the mode is never
            // part of the unlock chain (it cannot open or gate anything) and Titan has nothing
            // after it regardless.
            System.Collections.Generic.List<string> survivalRewards = new System.Collections.Generic.List<string>
            {
                RidgeTierLine(0),
                RidgeTierLine(1),
                RidgeTierLine(2),
                RidgeTierLine(3),
            };
            rewardList = survivalRewards.ToArray();
        }
        else if (race)
        {
            // The three payout pots, each in its own medal colour to match the RIVALS times -
            // totals from UpgradeSystem so the board can never drift from the actual awards.
            // Beating BRONZE is the chain's check point that opens the NEXT planet's HARVEST.
            rewardList = new[]
            {
                Rich(UpgradeSystem.RaceGoldTokens + " \u00B7 Tokens", RivalRacer.TierColor(RivalRacer.Tier.Gold)),
                Rich(UpgradeSystem.RaceSilverTokens + " \u00B7 Tokens", RivalRacer.TierColor(RivalRacer.Tier.Silver)),
                Rich(UpgradeSystem.RaceBronzeTokens + " \u00B7 Tokens", RivalRacer.TierColor(RivalRacer.Tier.Bronze)),
                "Beat BRONZE to unlock the NEXT WORLD",
            };
        }
        else
        {
            // The harvest board plus the chain's check point: surviving the HARVEST opens
            // this world's RACE, and the intro says so - with the best on record beneath.
            System.Collections.Generic.List<string> harvestRewards = new System.Collections.Generic.List<string>
            {
                "+" + LevelManager.CurrentLevel.harvestableScore + " Tokens per " + LevelManager.CurrentLevel.harvestableName + " Deposit",
                "Bonus Tokens for Spare Oxygen",
            };
            int world = LevelManager.CurrentLevelIndex;
            int firstClearBonus = LevelManager.FirstClearBonus(world);
            if (firstClearBonus > 0 && !LevelManager.IsHarvestCleared(world))
                harvestRewards.Add("First clear  +" + firstClearBonus + " Tokens bonus");
            // PROSPECTOR RUN walks the same harvest board: the check point it advertises is
            // surviving the dark descent to unlock the NEXT world (its harvest is already open).
            harvestRewards.Add(GameFlow.Mode == GameMode.Prospector
                ? "Survive the descent to unlock the NEXT WORLD"
                : (LevelManager.IsModeUnlocked(world, GameMode.Race)
                    ? "RACE course unlocked"
                    : "Survive to unlock the RACE course"));
            int bestScore = LevelManager.GetHarvestBest(world);
            if (bestScore > 0)
                harvestRewards.Add("Top score:  " + bestScore.ToString("N0"));
            rewardList = harvestRewards.ToArray();
        }
        rewardLines.text = "• " + string.Join("\n• ", rewardList);
        RectTransform rewardLinesRt = rewardLines.rectTransform;
        rewardLinesRt.sizeDelta = new Vector2(560f, 220f);
        rewardLines.verticalOverflow = VerticalWrapMode.Overflow;
        rewardLines.horizontalOverflow = HorizontalWrapMode.Wrap;

        // uGUI <color> rich-text tags bake their alpha straight into the glyph vertices, which
        // BYPASSES Text.color entirely - so fading that field leaves the grade colours stuck at
        // full opacity (the "turns up early, never fades" regression). A CanvasGroup alpha is
        // applied AFTER the vertices, so it fades the tagged lines and the plain ones alike.
        // MakeTitle left this at alpha 0; the group (also 0) becomes the single fade control.
        rewardLines.color = new Color(rewardLinesBase.r, rewardLinesBase.g, rewardLinesBase.b, 1f);
        rewardLinesCg = rewardLines.gameObject.AddComponent<CanvasGroup>();
        rewardLinesCg.alpha = 0f;

        dangerTitle = MakeTitle(canvas.transform, "HazardHeader", 32,
            new Vector2(470f, -150f), hazardHeader);
        dangerTitle.text = "HAZARDS";
        dangerLines = MakeTitle(canvas.transform, "HazardLines", 26,
            new Vector2(470f, -245f), new Color(1f, 0.9f, 0.9f, 1f));
        // Per-level, per-course list (matches what the builders actually spawn: harvest keeps
        // only the declared weather; Race Canyon/Ridge swap boulder vs land falls, etc.).
        string[] dangerList = CourseHazards.DangersFor(GameFlow.Mode, GameFlow.CurrentCourse, planet);
        dangerLines.text = "• " + string.Join("\n• ", dangerList);
        RectTransform dangerLinesRt = dangerLines.rectTransform;
        dangerLinesRt.sizeDelta = new Vector2(320f, 220f);
        dangerLines.verticalOverflow = VerticalWrapMode.Overflow;
        dangerLines.horizontalOverflow = HorizontalWrapMode.Wrap;

        // Written mission briefing: a reserved block under the columns, headed for the moment
        // and filled per level in MissionBriefing.cs (different text for all 15 levels).
        briefingText = MakeTitle(canvas.transform, "MissionBriefingText", 24,
            new Vector2(0f, -425f), new Color(0.85f, 0.9f, 1f, 1f));
        briefingText.text = MissionBriefing.For(GameFlow.Mode, GameFlow.CurrentCourse, planet, harvestable);
        RectTransform briefingTextRt = briefingText.rectTransform;
        briefingTextRt.sizeDelta = new Vector2(1900f, 190f);
        briefingText.verticalOverflow = VerticalWrapMode.Overflow;
        briefingText.horizontalOverflow = HorizontalWrapMode.Wrap;

        // Deploy prompt: the briefing holds on this screen until the player confirms, so the
        // mission brief can be read at leisure before being dropped onto the world.
        deployPrompt = MakeTitle(canvas.transform, "DeployPrompt", 28,
            new Vector2(0f, -525f), new Color(1f, 0.9f, 0.6f, 0f));
        deployPrompt.text = "PRESS ENTER TO DEPLOY BUGGY  ·  or SPACE / CLICK / SKIP";

        // Controls reminder under the prompt: this is the player's first real control moment,
        // so the deck spells out exactly what will drive the buggy.
        deployHint = MakeTitle(canvas.transform, "DeployHint", 20,
            new Vector2(0f, -573f), new Color(0.82f, 0.88f, 1f, 0f), UiKit.UiFontKind.Body);
        deployHint.text = "WASD / ARROWS  DRIVE   ·   SPACE  BOOST   ·   SHIFT  DRIFT   ·   P  PAUSE   ·   touch on mobile";

        // Full-screen black veil for the fade-from-black (and it guarantees the raw level mesh
        // is never visible while the card is still rising).
        GameObject veilGo = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        veilGo.transform.SetParent(canvas.transform, false);
        veil = veilGo.GetComponent<Image>();
        UiKit.StretchToFull(veil.rectTransform);
        veil.color = Color.black;

        // SKIP button: any player can jump straight to deployment from any beat of the
        // cinematic. Built LAST so it sits above the veil and is always clickable.
        RectTransform skipRt = UiKit.Button(canvas.transform, "B_SkipIntro", "SKIP", 24,
            new Color(0.05f, 0.10f, 0.09f, 0.85f), new Color(0.85f, 1f, 0.9f, 0.95f),
            new Vector2(-36f, -36f), new Vector2(128f, 48f), SkipToDeploy).GetComponent<RectTransform>();
        skipRt.SetAsLastSibling();

        // TOKEN DOUBLER (rewarded ad, dormant): the deploy hold's only in-break affordance -
        // a Harvester-only offer to watch one ad BEFORE the run and bank double tokens this
        // run (GameFlow.TokensDoubled doubles LevelScore()/CreditRunParts() downstream, so
        // the wallet, board and ranking all agree). Built after SKIP so it layers on top, and
        // only when AdsEnabled is live (DoublerAvailable) so today's build never shows it.
        if (AdCoordinator.DoublerAvailable)
        {
            doublerButton = UiKit.Button(canvas.transform, "B_TokenDouble", DoublerLabel, 20,
                new Color(0.10f, 0.16f, 0.11f, 0.85f), new Color(0.8f, 1f, 0.85f, 0.95f),
                new Vector2(36f, -36f), new Vector2(260f, 48f), ClaimTokenDouble);
            doublerButton.GetComponent<RectTransform>().SetAsLastSibling();
        }
    }

private static Text MakeTitle(Transform parent, string name, int fontSize, Vector2 anchoredPos, Color color,
        UiKit.UiFontKind fontKind = UiKit.UiFontKind.Display)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(1600f, fontSize * 1.6f);
        Text t = go.GetComponent<Text>();
        t.font = UiKit.ResolveFont(fontKind);
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.color = new Color(color.r, color.g, color.b, 0f);
        return t;
    }

    /// <summary>Wraps a briefing line in a uGUI rich-text colour tag. NOTE: such tags bake their
    /// alpha straight into the glyph vertices and BYPASS Text.color, so any text using them
    /// must be faded through a CanvasGroup (see resourceLinesCg / rewardLinesCg), never via
    /// Text.color.</summary>
    private static string Rich(string content, Color color)
    {
        return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + content + "</color>";
    }

    /// <summary>One ridge grade line for the REWARDS column: "BRONZE 1.5 km · 40", wrapped in
    /// that grade's own painted-line colour (GameFlow.RidgeGradeColors) - so the briefing
    /// colours match the stripes on the ridge itself.</summary>
    private static string RidgeTierLine(int index)
    {
        float km = GameFlow.RidgeGradeDistances[index] / 1000f;
        string dist = km == Mathf.RoundToInt(km) ? km.ToString("0") : km.ToString("0.0");
        return Rich(GameFlow.RidgeGradeNames[index] + " " + dist + " km \u00B7 " + GameFlow.RidgeGradeTokens[index],
            GameFlow.RidgeGradeColors[index]);
    }

    private void Update()
    {
        if (phase == Phase.Done) return;
        timer += Time.deltaTime;
        TickDoublerCooldown();

        switch (phase)
        {
            case Phase.Backdrop:
                UpdateBackdrop();
                if (timer >= backdropDuration) { phase = Phase.Reveal; timer = 0f; }
                break;

            case Phase.Reveal:
                UpdateReveal();
                if (timer >= revealDuration) { phase = Phase.Hold; timer = 0f; }
                break;

            case Phase.Hold:
                UpdateHold();
                break;

            case Phase.Settle:
            {
                float dur = skipTapped ? skipSettleDuration : settleDuration;
                UpdateSettle(dur);
                if (timer >= dur) HandOff();
                break;
            }
        }
    }

    private void UpdateBackdrop()
    {
        // Veil lifts and the planet card fades in, slowly growing (the world getting bigger).
        float veilA = 1f - Mathf.Clamp01(timer / fadeFromBlack);
        veil.color = new Color(0f, 0f, 0f, veilA);

        float pT = Mathf.Clamp01((timer - 0.25f) / planetFadeIn);
        SetBackdropAlpha(Ease(pT));
        float grow = Mathf.Lerp(1f, 1.3f, Mathf.Clamp01(timer / backdropDuration));
        backdropRt.localScale = Vector3.one * grow;

        ApplyShot(Mathf.Lerp(cameraStartScale, 2.8f, Ease(Mathf.Clamp01(timer / backdropDuration))),
            focusBase, 0f);
    }

    private void UpdateReveal()
    {
        float rt = Mathf.Clamp01(timer / revealDuration);

        // Crossfade the planet card away into the live world. It keeps growing from the backdrop's
        // end scale (1.3, so no zoom-out pop on the phase switch) up to 1.5, where the square art
        // fills the full screen with planet colour and no letterbox bars - the "world in view"
        // beat ends fully zoomed before the transition. Growth and fade share the same ease as
        // the camera dolly below, so the card never lurches ahead of the zoom behind it.
        // planetCrossStart is a fraction of the reveal, converted to seconds for the timer.
        float crossAt = planetCrossStart * revealDuration;
        float cross = Mathf.Clamp01((timer - crossAt) / planetCrossDuration);
        float ce = Ease(cross);
        float cardA = 1f - ce;
        SetBackdropAlpha(cardA);
        backdropRt.localScale = Vector3.one * Mathf.Lerp(1.3f, 1.5f, ce);

        // Title + briefing card all share one fade, triggered by TitleAlpha at titleInStart -
        // every mode fades in together. (Nothing special-cased per mode; the long-standing
        // "3 SURVIVAL lines early" issue was a build-time alpha leak in BuildCanvas, fixed
        // there, not a pacing difference.)
        float titleAlpha = TitleAlpha(rt);
        SetTitleAlpha(titleAlpha);

        // Dolly: pull in and glide the framing up the course with a gentle lateral sway.
        ApplyShot(Mathf.Lerp(2.8f, cameraEndScale, Ease(rt)), Vector3.Lerp(focusBase, focusDrift, Ease(rt)),
            Mathf.Sin(Time.time * 0.9f) * 9f);
    }

    private void UpdateHold()
    {
        // Freeze the reveal's final shot behind the mission briefing and wait for the player to
        // confirm deployment (Enter / keypad Enter / Space / any mouse click). They read the
        // brief first, at their own pace, then the camera settles onto the buggy and the run
        // hands off.
        ApplyShot(cameraEndScale, focusDrift, Mathf.Sin(Time.time * 0.9f) * 9f);
        SetTitleAlpha(1f);

        float fade = Mathf.Clamp01(timer / promptFadeIn);
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * promptPulseSpeed);
        SetPromptAlpha(fade * (0.35f + 0.65f * blink));

        bool deploy = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                      || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0);
        if (deploy)
        {
            BeginSettle(false);
        }
    }

    private void UpdateSettle(float dur)
    {
        // Drop the last shot onto the buggy: close the zoom and hop off the cinematic branch
        // so the chase camera glides onto its normal follow framing.
        float st = Mathf.Clamp01(timer / dur);
        float targetScale = Mathf.Lerp(settleStartScale, 1f, Ease(st));
        if (followCam != null) followCam.offsetScale = targetScale;
        if (followCam != null) followCam.cinematicLookAt = buggy != null ? buggy.transform.position : focusBase;
        if (st >= 0.75f && followCam != null) followCam.cinematicMode = false;

        // Leave the briefing: the card and the deploy prompt fade out while the shot lands.
        float outA = Mathf.Clamp01((dur - timer) / briefFadeOut);
        SetTitleAlpha(outA);
        SetPromptAlpha(outA);

        // Fade the intro music out as the run begins.
        if (introMusic != null) introMusic.volume = Mathf.MoveTowards(introMusic.volume, 0f, Time.deltaTime * 2.5f);
    }

    /// <summary>Starts the settle beat, capturing the current zoom so the close-in eases from
    /// wherever the skip was pressed (a straight jump would pop from the wide backdop shot).</summary>
    private void BeginSettle(bool skip)
    {
        skipTapped = skip;
        settleStartScale = followCam != null ? followCam.offsetScale : cameraEndScale;
        phase = Phase.Settle;
        timer = 0f;
    }

    /// <summary>Rewarded "2x TOKENS THIS RUN" offer on the Harvester deploy hold: watch one ad
    /// BEFORE the run, then every token banked this run doubles (see GameFlow.TokensDoubled).
    /// Dormant until AdCoordinator.AdsEnabled flips; grant/deny keep the button honest so a
    /// player can re-tap after a failed/skipped ad.</summary>
    private void ClaimTokenDouble()
    {
        if (doublerButton == null || handedOff || GameFlow.TokensDoubled || AdCoordinator.AdPending) return;
        doublerButton.interactable = false;
        Text lbl = doublerButton.GetComponentInChildren<Text>();
        if (lbl != null) lbl.text = "UNLOCKING...";
        AdCoordinator.TryClaimTokenDouble(
            () =>
            {
                GameFlow.TokensDoubled = true;
                if (lbl != null) lbl.text = "TOKENS x2 THIS RUN";
            },
            () =>
            {
                if (lbl != null) lbl.text = DoublerLabel;
                doublerButton.interactable = true;
                // The referral refused the ad (cooldown): lock the button with a countdown so
                // the refusal is visible. Any other deny (ads not live) just re-arms instantly.
                if (AdCoordinator.RewardedCooldownRemaining > 0f)
                {
                    doublerButton.interactable = false;
                    doublerLockUntil = Time.realtimeSinceStartup + AdCoordinator.RewardedCooldownRemaining;
                }
            });
    }

    /// <summary>Live tick for the doubler's cooldown lock: shows "ADS: Ns" while the rewarded
    /// rate limit holds, then re-arms the button with its normal label.</summary>
    private void TickDoublerCooldown()
    {
        if (doublerButton == null || GameFlow.TokensDoubled || doublerLockUntil < 0f || handedOff) return;
        float left = doublerLockUntil - Time.realtimeSinceStartup;
        Text lbl = doublerButton.GetComponentInChildren<Text>();
        if (left <= 0f)
        {
            doublerLockUntil = -1f;
            doublerButton.interactable = true;
            if (lbl != null) lbl.text = DoublerLabel;
        }
        else if (lbl != null)
        {
            lbl.text = "ADS: " + Mathf.CeilToInt(left) + "s";
        }
    }

    /// <summary>Skips the cinematic at any point and heads straight for HandOff: lifts the veil,
    /// drops the briefing and planet card, kills the Titan video and fades the music. Returning
    /// players are routed here automatically from Start(); the SKIP button calls it from any beat.</summary>
    public void SkipToDeploy()
    {
        if (handedOff || phase == Phase.Done) return;
        BeginSettle(true);
        if (veil != null) veil.color = new Color(0f, 0f, 0f, 0f);
        SetBackdropAlpha(0f);
        SetTitleAlpha(0f);
        SetPromptAlpha(0f);
        if (videoBackdrop != null)
        {
            VideoPlayer vp = videoBackdrop.GetComponent<VideoPlayer>();
            if (vp != null) vp.Stop();
            videoBackdrop.gameObject.SetActive(false);
            videoBackdrop = null;
            videoReady = false;
            videoPending = false; // also stands down the fallback coroutine
        }
        if (introMusic != null) introMusic.volume = 0f;
    }

    private void ApplyShot(float scale, Vector3 look, float swayX)
    {
        if (followCam == null) return;
        followCam.offsetScale = scale;
        followCam.cinematicLookAt = look + Vector3.right * swayX;
        // A slow azimuth sway adds the orbit feel to the pull-in (RotateY of a fixed offset).
        followCam.cinematicOffset = Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.35f) * 10f, 0f)
                                    * new Vector3(0f, 30f, -58f);
    }

    private float TitleAlpha(float rt)
    {
        // Fade the briefing card IN only (no timed auto-out): the card then stays up on the
        // hold screen until the player confirms deployment, and fades out during the settle.
        // Every mode shares this same beat INCLUDING Ridge Survival - one single ramp for the
        // title and all three briefing columns. The "3 SURVIVAL lines appear early" issue is
        // NOT pacing: it was a build-time alpha leak (BuildCanvas gold recolor set alpha back
        // to 1 on top of MakeTitle's 0), now fixed at the source.
        float relIn = rt * revealDuration;
        return Mathf.Clamp01((relIn - titleInStart) / 0.55f);
    }

    private void SetPromptAlpha(float a)
    {
        a = Mathf.Clamp01(a);
        if (deployPrompt != null)
            deployPrompt.color = new Color(1f, 0.9f, 0.6f, a);
        if (deployHint != null)
            deployHint.color = new Color(0.82f, 0.88f, 1f, a * 0.85f);
    }

    private void SetTitleAlpha(float a)
    {
        a = Mathf.Clamp01(a);
        if (titlePlanet != null)
            titlePlanet.color = new Color(1f, 1f, 1f, a);
        if (titleSub != null)
            titleSub.color = new Color(0.85f, 0.94f, 1f, a * 0.9f);
        if (titleStars != null)
        {
            foreach (Image star in titleStars)
            {
                if (star != null) star.color = new Color(1f, 0.88f, 0.32f, a);
            }
        }
        if (resourceTitle != null)
            resourceTitle.color = new Color(leftHeaderColor.r, leftHeaderColor.g, leftHeaderColor.b, a * 0.95f);
        if (resourceLinesCg != null)
            resourceLinesCg.alpha = a;
        if (rewardTitle != null)
            rewardTitle.color = new Color(0.45f, 1f, 0.55f, a * 0.95f);
        if (rewardLinesCg != null)
            rewardLinesCg.alpha = a;
        if (dangerTitle != null)
            dangerTitle.color = new Color(1f, 0.48f, 0.42f, a * 0.95f);
        if (dangerLines != null)
            dangerLines.color = new Color(1f, 0.9f, 0.9f, a);
        if (briefingText != null)
            briefingText.color = new Color(0.85f, 0.9f, 1f, a);
        if (briefingPanelImg != null)
        {
            Color c = briefingPanelImg.color;
            briefingPanelImg.color = new Color(c.r, c.g, c.b, a * 0.55f);
        }
    }

    private Vector3 ComputeDriftTarget()
    {
        Vector3 basePos = buggy != null ? buggy.transform.position : focusBase;
        if ((GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival) && terrain != null && terrain.raceEnabled)
        {
            float z0 = MoonTerrainGen.RaceStartZ + 8f;
            float z1 = z0 + dollyLookAhead;
            float x = terrain.RaceCenterX(z1);
            float y = terrain.SampleHeight(new Vector3(x, 0f, z1)) + 6f;
            return new Vector3(x, y, z1);
        }
        Vector3 f = buggy != null ? buggy.transform.forward : Vector3.forward;
        f.y = 0f;
        f.Normalize();
        return basePos + f * 140f + Vector3.up * 6f;
    }

    private void HandOff()
    {
        if (handedOff) return;
        handedOff = true;
        phase = Phase.Done;

        // Mark the intro as seen so next run auto-skips for this player.
        SaveIntroSeen();

        if (GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival)
        {
            // Give the baton to the 3-2-1-GO ceremony: it was built disabled and re-enabling it
            // here (skipFadeIn) drops straight into the numbers with no second fade-from-black.
            // It unlocks the buggy, starts the race clock and releases the rivals on GO.
            if (countdown != null) countdown.enabled = true;
        }
        else
        {
            // Harvester: hand the player direct driving control.
            if (buggy != null) buggy.inputEnabled = true;
            if (oxygen != null) oxygen.clockPaused = false;
        }

        // Stop and release the intro video so its RenderTexture isn't leaked on the scene switch.
        if (videoBackdrop != null)
        {
            VideoPlayer vp = videoBackdrop.GetComponent<VideoPlayer>();
            if (vp != null) vp.Stop();
            if (vp != null && vp.targetTexture != null) vp.targetTexture.Release();
            videoBackdrop.texture = null;
            videoPending = false;
        }

        // Real gameplay begins NOW: the player has control. This is the (single) CrazyGames
        // GameplayStart - the boot-time one on the menu was removed so the "initial download
        // size" probe measures up to the first playable moment. PauseSystem keeps pairing the
        // Stop/Start events from here on, and leaving the level sends GameplayStop. The ad
        // coordinator mirrors this live flag so it can send the mandatory GameplayStop the
        // first time an ad shows during a run (see AdCoordinator.SetGameplayLive).
        CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.GameplayStart);
        AdCoordinator.SetGameplayLive(true);
        Debug.Log("[CG] GameplayStart at level handoff (mode " + GameFlow.Mode + ")");

        // CrazyGames first-run onboarding: post this mode's tutorial tips now that control
        // (or the race countdown) is live. No-op off the portal/editor.
        TutorialCoach.OnHandOff();

        Destroy(gameObject);
    }

    /// <summary>Marks the intro as seen (per player, cloud-backed) so returning players get the
    /// short auto-skip path on the next run.</summary>
    private void SaveIntroSeen()
    {
        SaveBridge.SetInt(IntroSeenKey, 1);
        SaveBridge.Save();
    }

    /// <summary>Fades whichever backdrop is live: the Titan video card when prepared, else the
    /// still/procedural card below it. While the clip is still preparing NEITHER is shown - the
    /// still art is the wrong planet picture, and popping it in and straight back out to the clip
    /// reads as a glitch. The card only appears once the clip's first real frame is up, and the
    /// still returns solely if the clip fails or takes too long to arrive.</summary>
    private void SetBackdropAlpha(float a)
    {
        a = Mathf.Clamp01(a);
        backdropAlpha = a;

        if (videoPending && videoBackdrop != null)
        {
            backdrop.color = new Color(1f, 1f, 1f, 0f);
            videoBackdrop.color = new Color(1f, 1f, 1f, 0f);
            return;
        }

        if (videoReady && videoBackdrop != null)
        {
            videoBackdrop.color = new Color(1f, 1f, 1f, a);
            backdrop.color = new Color(1f, 1f, 1f, 0f);
        }
        else
        {
            backdrop.color = new Color(1f, 1f, 1f, a);
            if (videoBackdrop != null) videoBackdrop.color = new Color(1f, 1f, 1f, 0f);
        }
    }

    // ------------------------------------------------------------- planet art
    // Artist backdrops live in Assets/Resources/PlanetBackdrops and are packed into the
    // WebGL build (StreamingAssets cannot be read from the module browser on some platforms).
    // Filename = planet key (moon, mars, venus, io, titan). Without art a procedural card
    // keeps the "world getting bigger in view" beats intact until the dolly reveal.
    private Texture2D LoadPlanetArt()
    {
        string key = PlanetKey(LevelManager.CurrentLevel.planetName);
        Texture2D art = Resources.Load<Texture2D>("PlanetBackdrops/" + key);
        if (art != null) return art;
        return MakePlanetCard(PlanetColor(key));
    }

    /// <summary>Titan's intro card is a looping atmospheric clip (StreamingAssets/
    /// Titan_Clipped.mp4), driven by a VideoPlayer URL source so it plays identically on
    /// desktop and WebGL (VideoClip assets are unsupported on WebGL - the URL option is
    /// required there). The RawImage is parented to the still card so the whole backdrop
    /// scales together through the "world growing in view" beat; the still card underneath
    /// is the fallback if the clip cannot be prepared.</summary>
    private void TryStartTitanVideo(Transform parent)
    {
        if (PlanetKey(LevelManager.CurrentLevel.planetName) != "titan") return;

        GameObject vGo = new GameObject("PlanetVideoCard", typeof(RectTransform), typeof(RawImage));
        vGo.transform.SetParent(parent, false);
        RectTransform vRt = vGo.GetComponent<RectTransform>();
        UiKit.StretchToFull(vRt);
        videoBackdrop = vGo.GetComponent<RawImage>();
        videoBackdrop.raycastTarget = false;
        videoBackdrop.color = new Color(1f, 1f, 1f, 0f);
        // Hold the card back until the clip resolves one way or the other, so the still art
        // underneath is never shown just to be pulled again.
        videoPending = true;

        RenderTexture rt = new RenderTexture(1280, 720, 0);
        VideoPlayer vp = vGo.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.source = VideoSource.Url;
        vp.url = new System.Uri(System.IO.Path.Combine(Application.streamingAssetsPath, "Titan_Clipped.mp4")).AbsoluteUri;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.targetTexture = rt;
        vp.audioOutputMode = VideoAudioOutputMode.None;
        vp.isLooping = true;
        // Without this, prepareCompleted can land before the first frame is decoded and the card
        // would flash an empty texture. Waiting for frame one makes the cut land on real footage.
        vp.waitForFirstFrame = true;
        vp.prepareCompleted += _ =>
        {
            videoBackdrop.texture = rt;
            videoReady = true;
            videoPending = false;
            vp.Play();
            // The clip may land after the intro's last fade beat, so re-apply the current alpha
            // rather than waiting to be asked again.
            SetBackdropAlpha(backdropAlpha);
        };
        vp.errorReceived += (_, message) =>
        {
            Debug.LogWarning("Titan intro video unavailable (" + message + "); using the still card.");
            videoReady = false;
            videoPending = false;
            SetBackdropAlpha(backdropAlpha);
        };
        vp.Prepare();

        // If the clip never prepares and never errors, stop waiting so the intro is never left
        // with no backdrop at all.
        StartCoroutine(ReleaseVideoFallback());
    }

    private IEnumerator ReleaseVideoFallback()
    {
        yield return new WaitForSeconds(VideoFallbackSeconds);
        if (!videoPending) yield break;
        Debug.LogWarning("Titan intro video did not become ready in " + VideoFallbackSeconds
            + "s; using the still card.");
        videoPending = false;
        videoReady = false;
        SetBackdropAlpha(backdropAlpha);
    }

    private static string PlanetKey(string planetName)
    {
        switch (planetName)
        {
            case "THE MOON": return "moon";
            case "MARS": return "mars";
            case "VENUS": return "venus";
            case "IO": return "io";
            case "TITAN": return "titan";
            default: return planetName.ToLowerInvariant().Replace(" ", "");
        }
    }

    private static Color PlanetColor(string key)
    {
        switch (key)
        {
            case "mars": return new Color(0.78f, 0.27f, 0.12f);
            case "venus": return new Color(0.92f, 0.72f, 0.4f);
            case "io": return new Color(0.96f, 0.78f, 0.3f);
            case "titan": return new Color(0.85f, 0.66f, 0.5f);
            default: return new Color(0.7f, 0.7f, 0.74f); // moon: dusty grey
        }
    }

    // Procedural stand-in: a soft-shaded planet disc over a faint starfield. Good enough to
    // stage the intro until the artist artlands in Resources/PlanetBackdrops.
    private static Texture2D MakePlanetCard(Color color)
    {
        const int S = 256;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[S * S];
        System.Random rng = new System.Random(12345);
        for (int i = 0; i < 70; i++)
        {
            int sx = rng.Next(S);
            int sy = rng.Next(S);
            px[sy * S + sx] = new Color(1f, 1f, 1f, rng.NextDouble() < 0.4 ? 0.55f : 0.25f);
        }
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float R = S * 0.31f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                int idx = y * S + x;
                if (px[idx].a > 0f) continue;
                float d = Vector2.Distance(new Vector2(x, y), c);
                if (d >= R) continue;
                float k = d / R;
                float shade = 1f - k * k * 0.55f;                       // limb darkening
                float m = 0.10f * Mathf.Sin(x * 0.11f + y * 0.16f)
                        * Mathf.Sin(y * 0.13f - x * 0.07f);              // surface mottle
                Color col = color * Mathf.Clamp01(shade + m);
                col.a = 1f;
                px[idx] = col;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static float Ease(float t)
    {
        return t * t * (3f - 2f * t);
    }
}