using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds and drives the on-screen HUD: oxygen bar, boost meter, speed readout,
/// elapsed time, oxygen pod count, plus game-over and "objective met" banners.
/// All the UI elements are created from scratch at runtime so no prefab setup is required.
/// </summary>
public class HUD : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The buggy whose boost/speed is shown.")]
    public BuggyController buggy;
    [Tooltip("The singleton game state.")]
    public OxygenManager oxygen;
    [Tooltip("The buggy's hull integrity, shown as a red bar.")]
    public BuggyHealth health;
    [Header("Level")]
    [Tooltip("1-based index of the current planet in the campaign.")]
    public int levelIndex = 1;
    [Tooltip("Display name of the current planet.")]
    public string planetName = "THE MOON";
    [Tooltip("Display name of the harvestable resource (empty = none on this planet).")]
    public string resourceName = "";
    [Tooltip("True during Lunar Survival mode: hides the campaign score, shows survival info instead.")]
    public bool survivalMode = false;
    [Tooltip("True during Lunar Race mode: no oxygen clock - a big gold race timer and checkpoint-gate counter instead.")]
    public bool raceMode = false;
    [Tooltip("True during Ridge Survival: the race is played as survival (no gates, no timer, distance scoreboard instead).")]
    public bool ridgeSurvivalMode = false;

    [Tooltip("Race-only: a gate was driven past without passing through it, so the HUD warns the player to head back.")]
    public bool missedGate = false;

    [Tooltip("Race-only: the rival buggy, shown as a small compare line while it is still on track.")]
    public bool rivalActive = false;
    [Tooltip("Temporarily disables the survival compass dial (diagnostic).")]
    public bool showCompass = true;
    [Tooltip("Theme colour of this planet's harvestable, used by the resource counter.")]
    public Color harvestColor = new Color(0.35f, 0.95f, 0.8f, 1f);
    [Tooltip("Ore type this planet banks (Harvester only).")]
    public UpgradeSystem.OreType harvestableOre = UpgradeSystem.OreType.Helium3;

    private Image oxygenBar;
    private Text oxygenText;
    private Text o2AlarmText;
    private Text raceCutoffText;
    private Image boostFill;
    private Text boostText;
    private Text speedText;
    private Text timeText;
    private Text levelText;
    private Text podsText;
    private Text resourceText;
    private Text scoreText;
    private Text doubledTag;
    private Text partsText;
    private Text survivalInfoText;
    private int survivalSet = 1;
    private GameObject commObject;
    private Image commFill;
    private Text commText;
    private GameObject compassObject;
    private RectTransform compassRose;
    private RectTransform compassMarker;
    private RectTransform compassMarker3;
    private Text compassText;
    private Text compassText3;
    // Compass navigation warning text (PROSPECTOR RUN wall / off-route proximity warnings).
    // A separate slot from `warningText` so the neon hazard flashes never fight the navigational
    // voice: the compass warnings are the navigational lifeline a dark run is built around.
    private Text navWarningText;
    // Screen-edge objective arrow: when the next pod / landing pad is off-screen, a baked arrow
    // at the screen edge points at it with its distance, so the dial never has to be read to
    // find the way. Aids the compass instead of replacing it.
    private GameObject edgeArrowObject;
    private RectTransform edgeArrowRt;
    private Image edgeArrowImg;
    private Text edgeArrowText;
    private Sprite edgeArrowSprite;
    private Vector3 objectivePos;
    private string objectiveName = "";
    private bool objectiveActive;
    private float objectiveDist;
    private Camera hudCam;
    private RectTransform hudCanvasRt;
    // Proximity-sonar ping marker: flashes toward the nearest ore cluster, brighter and
    // faster the closer the rover gets; hidden entirely while no cluster is in sonar range.
    private RectTransform compassMarkerPing;
    private Image pingPip;
    private Image pingNeedle;
    // LUNAR TRIALS: a second marker on the shared dial (tinted the planet's ore colour),
    // pointing at the nearest uncollected nugget.
    private RectTransform oreMarker;
    private Text oreText;
    private bool oreDialActive;
    [Tooltip("True while the landing-zone marker is the only objective (final pod collected).")]
    public bool landingZoneActive = false;
    private Image integrityFill;
    private Text integrityText;
    private GameObject bannerObject;
    private Text bannerText;
    private Text warningText;
    private GameObject overlay;
    private Image lensBlotch;
    private Texture2D blotchTexture;
    private float lastBlotchAlpha = -1f;
    private Text windArrow;
    private Text windSpeedText;

    private bool hintActive;
    private float hintTimer;
    private bool hintFading;
    private bool showingHint;
    private string hintText = "";

    // Race route timeline (top centre, Lunar Race only): a point-to-point progress bar with
    // a tick per gate and player + rival pins, plus target-gate distance and rival split.
    private GameObject raceTimelineRoot;
    private Image raceTimelineFill;
    private RectTransform racePlayerPin;
    private readonly List<RectTransform> raceRivalPins = new List<RectTransform>();
    private readonly List<RectTransform> raceGateTicks = new List<RectTransform>();
    private Text raceTargetText;
    private Text raceAheadText;
    private Text raceSplitText;
    private RaceGateManager raceManager;
    private readonly List<RivalRacer> raceRivals = new List<RivalRacer>();
    private bool raceTimelineReady;

    // Ridge Survival progress bar (top centre): the same look as the Canyon race timeline, but the
    // ticks are the four grade distances (Bronze .. Platinum) and the pin is total distance survived.
    private const float RidgeBarTrackW = 520f;
    private GameObject ridgeBarRoot;
    private Image ridgeBarFill;
    private RectTransform ridgePlayerPin;
    private readonly List<Image> ridgeGradeTicks = new List<Image>();
    private readonly List<Text> ridgeGradeLabels = new List<Text>();
    private Text ridgeNextText;
    private Text ridgeToGoText;
    private float ridgeMetersAtStart;   // banked distance when this leg's scene loaded

    private Texture2D whiteTexture;
    private Sprite whiteSprite;

    private void Start()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (oxygen == null) oxygen = OxygenManager.Instance;
        if (health == null) health = FindAnyObjectByType<BuggyHealth>();
        hudCam = Camera.main;

        ridgeMetersAtStart = GameFlow.RidgeSurvivalMeters;
        BuildUI();
        if (oxygen != null) oxygen.OnOxygenChanged += RefreshBars;
    }

    private void OnDestroy()
    {
        if (oxygen != null) oxygen.OnOxygenChanged -= RefreshBars;
    }

    private void BuildUI()
    {
        // Root canvas (Screen Space - Overlay).
        GameObject canvasGo = new GameObject("HUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        // 0.5 match (geometric mean) keeps the HUD alive on both portrait phones (CrazyGames
        // mobile app) and ultrawide desktops instead of cropping a dimension (see UiKit).
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        hudCanvasRt = canvasGo.GetComponent<RectTransform>();

        // Semi-transparent full-screen overlay used for game over / win tint.
        overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvasGo.transform, false);
        Image overlayImg = overlay.GetComponent<Image>();
        overlayImg.color = new Color(0f, 0f, 0f, 0f);
        StretchToFull(overlay.GetComponent<RectTransform>());

        // --- Oxygen bar (top left) ---
        GameObject oxygenBg = MakeBarPanel(canvasGo.transform, "OxygenBar", new Vector2(40f, -40f), new Vector2(420f, 34f), false, new Color(0.1f, 0.1f, 0.12f, 0.85f));

        oxygenBar = MakeFill(oxygenBg.transform, "OxygenFill", new Color(0.25f, 0.75f, 1f, 1f));

        oxygenText = MakeLabel(canvasGo.transform, "OxygenText", "Oxygen 100%", 28,
            new Color(1f, 1f, 1f, 1f), new Vector2(40f, -78f), TextAnchor.MiddleLeft);

        // Lunar Race has no oxygen: hide the tank readouts and let the gate counter (below)
        // use the top-left slot. Ridge Survival KEEPS the tank - it is the survival clock.
        if (raceMode && !ridgeSurvivalMode)
        {
            oxygenBg.SetActive(false);
            oxygenText.gameObject.SetActive(false);
        }

        // --- Zero-oxygen alarm (flashing, shown only during the grace period) ---
        o2AlarmText = MakeLabel(canvasGo.transform, "O2AlarmText", "", 24,
            new Color(1f, 0.3f, 0.2f, 1f), new Vector2(40f, -112f), TextAnchor.MiddleLeft);
        o2AlarmText.gameObject.SetActive(false);

        // --- Boost meter (top right) ---
        GameObject boostBg = MakeBarPanel(canvasGo.transform, "BoostBar", new Vector2(-40f, -40f), new Vector2(320f, 28f), true, new Color(0.1f, 0.1f, 0.12f, 0.85f));
        boostFill = MakeFill(boostBg.transform, "BoostFill", new Color(1f, 0.6f, 0f, 1f));

        boostText = MakeLabel(canvasGo.transform, "BoostText", "Boost (SPACE)", 26,
            new Color(1f, 0.85f, 0.5f, 1f), new Vector2(-40f, -74f), TextAnchor.MiddleRight);

        // --- Speed (bottom right) ---
        speedText = MakeLabel(canvasGo.transform, "SpeedText", "0 km/h", 34,
            new Color(1f, 1f, 1f, 1f), new Vector2(-40f, 60f), TextAnchor.MiddleRight);

        // --- Timer (top centre) ---
        // Lunar Race makes the clock the hero: a bigger gold race timer; the survival-style
        // white clock otherwise. Ridge Survival replaces the clock with the DISTANCE total.
        if (ridgeSurvivalMode)
        {
            timeText = MakeLabel(canvasGo.transform, "TimeText", "DISTANCE 0.00 KM", 46,
                new Color(1f, 0.84f, 0.25f, 1f), new Vector2(0f, -40f), TextAnchor.MiddleCenter);
            timeText.fontStyle = FontStyle.Bold;
            RectTransform distRt = timeText.GetComponent<RectTransform>();
            distRt.sizeDelta = new Vector2(640f, 68f);
            timeText.verticalOverflow = VerticalWrapMode.Overflow;
        }
        else if (raceMode)
        {
            timeText = MakeLabel(canvasGo.transform, "TimeText", "00:00.00", 46,
                new Color(1f, 0.84f, 0.25f, 1f), new Vector2(0f, -40f), TextAnchor.MiddleCenter);
            timeText.fontStyle = FontStyle.Bold;
            // MakeLabel's default 420x40 box horizontally overflows but vertically TRUNCATES,
            // which clips a 46pt-bold clock to nothing. Give the race timer a taller box and
            // let the glyphs overflow instead of being cut off.
            RectTransform timerRt = timeText.GetComponent<RectTransform>();
            timerRt.sizeDelta = new Vector2(560f, 68f);
            timeText.verticalOverflow = VerticalWrapMode.Overflow;
        }
        else
        {
            timeText = MakeLabel(canvasGo.transform, "TimeText", "00:00", 34,
                new Color(0.8f, 0.9f, 1f, 1f), new Vector2(0f, -40f), TextAnchor.MiddleCenter);
        }

        // --- Environmental-hazard warning (below the timer) ---
        // A separate flashing banner (not the objective-hint slot) so an incoming meteor
        // storm can scream "GET MOVING" without wiping the "REACH THE LANDING ZONE" hint.
        warningText = MakeLabel(canvasGo.transform, "StormWarningText", "", 30,
            new Color(1f, 0.55f, 0.15f, 1f), new Vector2(0f, -118f), TextAnchor.MiddleCenter,
            UiKit.UiFontKind.Display);
        warningText.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 42f);
        warningText.gameObject.SetActive(false);

        // --- Race post-bronze cutoff countdown (Lunar Race only) ---
        // Once the BRONZE rival crosses the line the race auto-concludes after a short grace
        // (LevelEndSequence.cutoffAfterBronze). A big flashing countdown over the lower-middle
        // screen warns the player the race is about to end so a late drive knows exactly how
        // much time is left to cross the line.
        raceCutoffText = MakeLabel(canvasGo.transform, "RaceCutoffText", "", 44,
            new Color(1f, 0.3f, 0.15f, 1f), new Vector2(0f, -250f), TextAnchor.MiddleCenter);
        raceCutoffText.GetComponent<RectTransform>().sizeDelta = new Vector2(1100f, 64f);
        raceCutoffText.fontStyle = FontStyle.Bold;
        raceCutoffText.verticalOverflow = VerticalWrapMode.Overflow;
        raceCutoffText.gameObject.SetActive(false);

        // --- Pods count / checkpoint gates (below the alarm) ---
        // Ridge Survival counts CANISTERS the same way the gates used to be counted.
        podsText = MakeLabel(canvasGo.transform, "PodsText",
            ridgeSurvivalMode ? "CANISTERS 0 / 0" : (raceMode ? "GATES 0 / 10" : "Oxygen Pods: 0"), 24,
            new Color(0.7f, 0.95f, 0.8f, 1f), new Vector2(40f, -146f), TextAnchor.MiddleLeft);

        // --- Harvested resource count (prominent panel below pods, Harvester Mode only) ---
        // A dark backing panel plus a big, bold, resource-tinted counter so how much of the
        // planet's resource has been bagged (and how much is left worth risking O2 for) is
        // readable at a glance.
        if (!string.IsNullOrEmpty(resourceName) && !survivalMode)
        {
            Color resBright = Color.Lerp(harvestColor, Color.white, 0.25f);
            MakeBarPanel(canvasGo.transform, "ResourcePanel", new Vector2(40f, -182f), new Vector2(360f, 42f), false,
                new Color(0.03f, 0.07f, 0.09f, 0.8f));
            resourceText = MakeLabel(canvasGo.transform, "ResourceText", resourceName + ": 0", 28,
                resBright, new Vector2(40f, -182f), TextAnchor.MiddleLeft);
            resourceText.fontStyle = FontStyle.Bold;
            resourceText.GetComponent<RectTransform>().sizeDelta = new Vector2(340f, 40f);
        }

        // --- Live score (bottom centre, Harvester Mode) / survival info (Survival) ---
        // LUNAR TRIALS: the campaign token-score line does not exist - ore is the only
        // collectable and its tally already lives in the resource panel.
        if (survivalMode)
        {
            survivalInfoText = MakeLabel(canvasGo.transform, "SurvivalInfoText", "SET 1", 28,
                new Color(0.85f, 1f, 0.9f, 1f), new Vector2(0f, 40f), TextAnchor.MiddleCenter);

            // --- Communication range indicator (top centre, below the timer) ---
            commObject = MakeBarPanel(canvasGo.transform, "CommsPanel", new Vector2(0f, -150f), new Vector2(340f, 22f), false, new Color(0.1f, 0.14f, 0.16f, 0.9f));
            RectTransform commRt = commObject.GetComponent<RectTransform>();
            commRt.anchorMin = new Vector2(0.5f, 1f);
            commRt.anchorMax = new Vector2(0.5f, 1f);
            commRt.pivot = new Vector2(0.5f, 1f);
            commRt.anchoredPosition = new Vector2(0f, -150f);
            Image commBg = commObject.GetComponent<Image>();
            commBg.color = new Color(0.1f, 0.14f, 0.16f, 0.9f);
            commFill = MakeFill(commObject.transform, "CommsFill", new Color(0.2f, 1f, 0.4f, 1f));
            commText = MakeLabel(canvasGo.transform, "CommsText", "COMM LINK", 22,
                new Color(0.7f, 0.95f, 1f, 1f), new Vector2(0f, -180f), TextAnchor.MiddleCenter);
            commText.text = "";
            commText.GetComponent<RectTransform>().sizeDelta = new Vector2(700f, 30f);
            commObject.SetActive(false);
            commText.gameObject.SetActive(false);

        }
        else if (!raceMode && ChallengeRunner.Active == null)
        {
            // Blue line: this run's live TOKEN score (nuggets 1:1 + the spare-oxygen bonus
            // as it ticks up), clearly separate from the orange garage token bank below it.
            scoreText = MakeLabel(canvasGo.transform, "ScoreText", "TOKENS  +0", 28,
                new Color(0.45f, 0.8f, 1f, 1f), new Vector2(0f, 40f), TextAnchor.MiddleCenter);
            scoreText.GetComponent<RectTransform>().sizeDelta = new Vector2(800f, 40f);

            // Rewarded "x2" badge: a bold yellow tag beside the live token counter, lit (with
            // a soft pulse) the moment the token doubler is granted, so the active doubling
            // reads at a glance while the tokens gather.
            doubledTag = MakeLabel(canvasGo.transform, "DoubledTag", "x2", 42,
                new Color(1f, 0.85f, 0.25f, 1f), new Vector2(0f, 40f), TextAnchor.MiddleCenter);
            doubledTag.fontStyle = FontStyle.Bold;
            RectTransform drt = doubledTag.GetComponent<RectTransform>();
            drt.sizeDelta = new Vector2(90f, 60f);
            drt.anchoredPosition = scoreText.GetComponent<RectTransform>().anchoredPosition + new Vector2(215f, 0f);
            doubledTag.gameObject.SetActive(false);
        }

        // --- Token wallet (Harvester only): the uniform garage token balance. A run's tokens
        // are banked at the finish (CreditRunParts); this readout mirrors the garage.
        // LUNAR TRIALS: the wallet readout does not exist - ore is the only in-run collectable.
        if (ChallengeRunner.Active == null)
        {
            partsText = MakeLabel(canvasGo.transform, "PartsText",
                "TOKENS  " + UpgradeSystem.GetTokens(), 22,
                new Color(1f, 0.85f, 0.3f, 0.9f), new Vector2(0f, 6f), TextAnchor.MiddleCenter);
        }
        if ((survivalMode || raceMode) && partsText != null) partsText.gameObject.SetActive(false);

        // --- Compass (both modes) ---
        // A heading-up instrument (bottom left) whose single marker points at the next oxygen
        // pod (/canister). Discovery of other valuable pickups is left to the player's own
        // exploration, so only the oxygen target gets a dial needle.
        if (showCompass)
        {
            BuildCompassUI(canvasGo);
        }

        // --- Wind gust direction + speed (top centre, underneath the SIDE GUST! warning) ---
        // Appears only while a gust is active: an arrow showing which way the wind shoves and
        // the gust's strength underneath, so the player can brace before it lands.
        windArrow = MakeLabel(canvasGo.transform, "WindArrow", "»", 42,
            new Color(1f, 1f, 1f, 0.95f), new Vector2(0f, -165f), TextAnchor.MiddleCenter);
        windArrow.fontStyle = FontStyle.Bold;
        windArrow.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 46f);
        windArrow.gameObject.SetActive(false);
        windSpeedText = MakeLabel(canvasGo.transform, "WindSpeed", "WIND 6 m/s", 20,
            new Color(0.75f, 0.9f, 1f, 0.9f), new Vector2(0f, -205f), TextAnchor.MiddleCenter);
        windSpeedText.GetComponent<RectTransform>().sizeDelta = new Vector2(220f, 26f);
        windSpeedText.gameObject.SetActive(false);

        // --- Lens blotch (full screen) ---
        // Random dark droplets that flick onto the chase-cam lens when the buggy sprays
        // debris (lava/mud/lake/ice); tinted by surface colour, faded by the spray emitter.
        blotchTexture = MakeBlotchTexture();
        GameObject blotchGo = new GameObject("LensBlotch", typeof(RectTransform), typeof(Image));
        blotchGo.transform.SetParent(canvasGo.transform, false);
        lensBlotch = blotchGo.GetComponent<Image>();
        lensBlotch.sprite = Sprite.Create(blotchTexture, new Rect(0, 0, blotchTexture.width, blotchTexture.height),
            new Vector2(0.5f, 0.5f));
        lensBlotch.type = Image.Type.Simple;
        lensBlotch.preserveAspect = false;
        lensBlotch.raycastTarget = false;
        lensBlotch.color = new Color(0f, 0f, 0f, 0f);
        StretchToFull(lensBlotch.GetComponent<RectTransform>());
        blotchGo.transform.SetAsLastSibling();

        // --- Race route timeline (top centre, Lunar Race only; Ridge Survival has no gates) ---
        if (raceMode && !ridgeSurvivalMode)
        {
            BuildRaceTimeline(canvasGo);
        }
        // --- Ridge Survival progress bar (same slot as the race timeline) ---
        if (ridgeSurvivalMode)
        {
            BuildRidgeProgressBar(canvasGo);
        }

        // --- Current level / planet (below the timer) ---
        levelText = MakeLabel(canvasGo.transform, "LevelText", "LEVEL 1 · THE MOON", 24,
            new Color(0.85f, 0.95f, 1f, 1f), new Vector2(0f, -84f), TextAnchor.MiddleCenter,
            UiKit.UiFontKind.Display);
        if (survivalMode)
        {
            if (levelText != null) levelText.text = "LUNAR SURVIVAL";
        }
        else if (ridgeSurvivalMode)
        {
            if (levelText != null) levelText.text = "RIDGE SURVIVAL · " + planetName
                + " · LEG " + (GameFlow.RidgeSurvivalLoops + 1);
        }
        else if (raceMode)
        {
            if (levelText != null) levelText.text = "LUNAR RACE · " + GameFlow.CourseName(GameFlow.CurrentCourse) + " · " + planetName;
            // Race GRADE shown as the same gold star ladder the harvest HUD uses, CENTRED just
            // below the title: the grade you're racing right now (grade 1 = one filled star,
            // grade 5 = all five). Same five-slot ladder shape as the map's race rows.
            int grade = Mathf.Clamp(GameFlow.RaceGradeIndex + 1, 1, RivalRacer.GradeCount);
            const float rStarSize = 24f;
            const float rStarGap = 6f;
            int racetotal = RivalRacer.GradeCount;
            float rFirstStarX = -(racetotal - 1) * (rStarSize + rStarGap) * 0.5f;
            UiKit.StarLadder(canvasGo.transform, "LevelGradeStars", grade, racetotal,
                new Vector2(0.5f, 1f),
                new Vector2(rFirstStarX, -132f), rStarSize, rStarGap);
        }
        else if (levelText != null)
        {
            // LUNAR TRIALS: world harvest/prospector grades don't apply to challenges - plain
            // mode + planet title, no star ladder (the resource tally is the progress readout).
            if (ChallengeRunner.Active != null)
            {
                levelText.text = (GameFlow.Mode == GameMode.Prospector ? "PROSPECTOR RUN  ·  " : "") + planetName;
                levelText.rectTransform.anchoredPosition = new Vector2(0f, -84f);
            }
            // Harvest line: planet name plus the grade as a ladder of baked gold stars, both
            // CENTRED as one body like the intro card - the title shifts left by the stars'
            // width so "THE MOON ★★★" reads as a single centred unit. All five slots are shown
            // (filled up to the grade) so it matches the map's ladder; the stars' midline lines
            // up with the name text's vertical centre (-104, the middle of the 40px label box).
            else if (GameFlow.Mode == GameMode.Prospector)
            {
                // Prospector has its own independent difficulty grade. Show its five-star
                // current-grade ladder, aligned with the mode + world title.
                string title = "PROSPECTOR RUN  ·  " + planetName;
                levelText.text = title;
                int grade = LevelManager.GetProspectorGrade(LevelManager.CurrentLevelIndex);
                int total = LevelManager.MaxProspectorGrade;
                float titleW = levelText.preferredWidth;
                if (titleW <= 0f) titleW = title.Length * 0.62f * levelText.fontSize;
                const float starSize = 24f;
                const float starGap = 6f;
                const float titleStarGap = 20f;
                float starsW = total * starSize + (total - 1) * starGap;
                float titleX = -(titleStarGap + starsW) * 0.5f;
                float firstStarX = (titleW + titleStarGap + starSize - starsW) * 0.5f;
                levelText.rectTransform.anchoredPosition = new Vector2(titleX, -84f);
                UiKit.StarLadder(canvasGo.transform, "LevelGradeStars", grade, total,
                    new Vector2(0.5f, 1f), new Vector2(firstStarX, -104f), starSize, starGap);
            }
            else
            {
                levelText.text = planetName;
                int grade = LevelManager.GetHarvestGrade(LevelManager.CurrentLevelIndex);
            int total = LevelManager.MaxHarvestGrade;
            float nameW = levelText.preferredWidth;
            if (nameW <= 0f) nameW = planetName.Length * 0.62f * levelText.fontSize;
            const float starSize = 24f;
            const float starGap = 6f;
            const float titleStarGap = 20f;
            float starsW = total * starSize + (total - 1) * starGap;
            float titleX = -(titleStarGap + starsW) * 0.5f;
            float firstStarX = (nameW + titleStarGap + starSize - starsW) * 0.5f;
            levelText.rectTransform.anchoredPosition = new Vector2(titleX, -84f);
            UiKit.StarLadder(canvasGo.transform, "LevelGradeStars", grade, total,
                new Vector2(0.5f, 1f),
                new Vector2(firstStarX, -104f), starSize, starGap);
            }
        }

        // --- Hull integrity / damage bar (below the resource panel) ---
        GameObject integrityBg = MakeBarPanel(canvasGo.transform, "IntegrityBar", new Vector2(40f, -232f), new Vector2(360f, 22f), false, new Color(0.12f, 0.1f, 0.1f, 0.85f));
        integrityFill = MakeFill(integrityBg.transform, "IntegrityFill", new Color(1f, 0.28f, 0.2f, 1f));

        integrityText = MakeLabel(canvasGo.transform, "IntegrityText", "Integrity 100%", 22,
            new Color(1f, 0.55f, 0.5f, 1f), new Vector2(40f, -265f), TextAnchor.MiddleLeft);

        // --- Banner (centre) for game over / win ---
        // A GameObject can hold only one Graphic, so the background Image and the Text
        // must be separate objects (Text as a child of the Image).
        bannerObject = new GameObject("Banner");
        bannerObject.transform.SetParent(canvasGo.transform, false);
        bannerObject.AddComponent<RectTransform>();
        bannerObject.AddComponent<CanvasRenderer>();
        bannerObject.AddComponent<Image>();

        GameObject bannerTextGo = new GameObject("BannerText");
        bannerTextGo.transform.SetParent(bannerObject.transform, false);
        bannerTextGo.AddComponent<RectTransform>();
        bannerTextGo.AddComponent<CanvasRenderer>();
        bannerText = bannerTextGo.AddComponent<Text>();

        RectTransform bannerRect = bannerObject.GetComponent<RectTransform>();
        bannerRect.anchorMin = new Vector2(0.5f, 0.5f);
        bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
        bannerRect.pivot = new Vector2(0.5f, 0.5f);
        bannerRect.anchoredPosition = Vector2.zero;
        bannerRect.sizeDelta = new Vector2(700f, 160f);
        Image bannerImg = bannerObject.GetComponent<Image>();
        bannerImg.color = new Color(0f, 0f, 0f, 0.65f);

        RectTransform bannerTextRect = bannerTextGo.GetComponent<RectTransform>();
        bannerTextRect.anchorMin = Vector2.zero;
        bannerTextRect.anchorMax = Vector2.one;
        bannerTextRect.offsetMin = Vector2.zero;
        bannerTextRect.offsetMax = Vector2.zero;

        AssignDefaultFont(bannerText);
        bannerText.fontSize = 48;
        bannerText.alignment = TextAnchor.MiddleCenter;
        bannerText.color = Color.white;
        bannerText.text = "";
        bannerObject.SetActive(false);
    }


    /// <summary>
    /// Builds the bottom-left heading-up compass. Its single marker always points at the next
    /// oxygen pod (/canister); the rest of the world is found by eye, keeping oxygen the only
    /// instrument reading the player must follow.
    /// </summary>
    private void BuildCompassUI(GameObject canvasGo)
    {
        compassObject = new GameObject("Compass", typeof(RectTransform));
        compassObject.transform.SetParent(canvasGo.transform, false);
        RectTransform compRt = compassObject.GetComponent<RectTransform>();
        compRt.anchorMin = new Vector2(0f, 0f);
        compRt.anchorMax = new Vector2(0f, 0f);
        compRt.pivot = new Vector2(0f, 0f);
        compRt.anchoredPosition = new Vector2(70f, 95f);
        compRt.sizeDelta = new Vector2(150f, 150f);

        // Dial ring (fixed frame of reference).
        GameObject dialGo = new GameObject("CompassDial", typeof(RectTransform), typeof(Image));
        dialGo.transform.SetParent(compassObject.transform, false);
        StretchToFull(dialGo.GetComponent<RectTransform>());
        Image dial = dialGo.GetComponent<Image>();
        dial.sprite = MakeRingSprite();
        dial.type = Image.Type.Simple;
        dial.color = new Color(0.7f, 0.9f, 1f, 0.65f);

        float half = 75f; // dial radius in px
        float labelR = half - 14f;

        // --- Cardinal rose (rotates with world heading) ---
        GameObject roseGo = new GameObject("CompassRose", typeof(RectTransform));
        roseGo.transform.SetParent(compassObject.transform, false);
        RectTransform roseRt = roseGo.GetComponent<RectTransform>();
        StretchToFull(roseRt);
        compassRose = roseRt;
        MakeRoseLabel(roseRt, "N", new Vector2(0f, labelR), true);
        MakeRoseLabel(roseRt, "E", new Vector2(labelR, 0f), false);
        MakeRoseLabel(roseRt, "S", new Vector2(0f, -labelR), false);
        MakeRoseLabel(roseRt, "W", new Vector2(-labelR, 0f), false);

        EnsureWhiteSprite();

        // --- Pod marker handle (rotates to the next pod's relative bearing) ---
        compassMarker = BuildCompassMarker(compassObject.transform, "PodMarker",
            new Color(1f, 0.9f, 0.2f, 1f), new Color(1f, 0.55f, 0.2f, 1f));

        // --- Fixed "heading" lubber tick at the very top (your reference to line up on) ---
        GameObject lubGo = new GameObject("CompassLubber", typeof(RectTransform), typeof(Image));
        lubGo.transform.SetParent(compassObject.transform, false);
        RectTransform lubRt = lubGo.GetComponent<RectTransform>();
        lubRt.anchorMin = new Vector2(0.5f, 1f);
        lubRt.anchorMax = new Vector2(0.5f, 1f);
        lubRt.pivot = new Vector2(0.5f, 0.5f);
        lubRt.anchoredPosition = new Vector2(0f, 6f);
        lubRt.sizeDelta = new Vector2(6f, 14f);
        Image lubImg = lubGo.GetComponent<Image>();
        lubImg.sprite = whiteSprite;
        lubImg.type = Image.Type.Simple;
        lubImg.color = new Color(1f, 1f, 1f, 1f);

        // Centre cap dot.
        GameObject capGo = new GameObject("CompassCap", typeof(RectTransform), typeof(Image));
        capGo.transform.SetParent(compassObject.transform, false);
        RectTransform capRt = capGo.GetComponent<RectTransform>();
        capRt.anchorMin = new Vector2(0.5f, 0.5f);
        capRt.anchorMax = new Vector2(0.5f, 0.5f);
        capRt.pivot = new Vector2(0.5f, 0.5f);
        capRt.anchoredPosition = Vector2.zero;
        capRt.sizeDelta = new Vector2(10f, 10f);
        Image capImg = capGo.GetComponent<Image>();
        capImg.sprite = MakeCircleSprite();
        capImg.color = new Color(0.9f, 1f, 1f, 0.95f);

        // --- Primary (pod) bearing / distance label ---
        compassText = MakeLabel(canvasGo.transform, "CompassText", "", 20,
            new Color(0.8f, 0.95f, 1f, 1f), new Vector2(205f, 120f), TextAnchor.MiddleLeft);
        compassText.text = "";
        compassText.GetComponent<RectTransform>().sizeDelta = new Vector2(460f, 26f);

        // --- Third (landing zone) marker: orange handle, dormant until the final pod.
        // Shown only while the LevelEndSequence is waiting for the player to reach the pad.
        compassMarker3 = BuildCompassMarker(compassObject.transform, "LandingMarker",
            new Color(1f, 0.55f, 0.12f, 1f), new Color(1f, 0.32f, 0.1f, 1f));
        compassMarker3.gameObject.SetActive(false);

        compassText3 = MakeLabel(canvasGo.transform, "CompassTextLanding", "", 20,
            new Color(1f, 0.62f, 0.2f, 1f), new Vector2(205f, 90f), TextAnchor.MiddleLeft);
        compassText3.text = "";
        compassText3.GetComponent<RectTransform>().sizeDelta = new Vector2(460f, 26f);

        // --- Compass navigation warnings (PROSPECTOR RUN): a flashing amber line under the
        // dial for wall proximity / off-route drift, so the dark descent never needs the eye
        // pulled from the dial to find its way.
        navWarningText = MakeLabel(canvasGo.transform, "CompNavWarn", "", 22,
            new Color(1f, 0.62f, 0.18f, 1f), new Vector2(205f, 60f), TextAnchor.MiddleLeft);
        navWarningText.text = "";
        navWarningText.GetComponent<RectTransform>().sizeDelta = new Vector2(460f, 26f);

        compassMarkerPing = BuildCompassMarker(compassObject.transform, "PingMarker",
            new Color(0.55f, 1f, 0.85f, 1f), new Color(0.4f, 1f, 0.7f, 1f));
        pingPip = FindMarkerImage(compassMarkerPing, "PingMarkerPip");
        pingNeedle = FindMarkerImage(compassMarkerPing, "PingMarkerNeedle");
        compassMarkerPing.gameObject.SetActive(false);

        // --- LUNAR TRIALS: a second marker on the SAME dial - the ORE pointer, tinted the
        // planet's own ore colour, tracking the nearest uncollected nugget. The oxygen marker
        // (blue canister / green final canister) and the sonar blip share the dial; colour
        // separates the objectives.
        oreDialActive = ChallengeRunner.Active != null && ChallengeRunner.Active.oreTarget > 0;
        if (oreDialActive)
        {
            Color oreTint = LevelManager.CurrentLevel != null
                ? LevelManager.CurrentLevel.harvestableColor
                : new Color(1f, 0.8f, 0.2f, 1f);
            oreMarker = BuildCompassMarker(compassObject.transform, "OreMarker", oreTint, oreTint);
            oreText = MakeLabel(canvasGo.transform, "OreText", "", 18, oreTint,
                new Vector2(205f, 150f), TextAnchor.MiddleLeft);
            oreText.text = "";
            oreText.GetComponent<RectTransform>().sizeDelta = new Vector2(460f, 26f);
        }

        compassObject.SetActive(true);

        // --- Safe-area shift: on the CrazyGames mobile app the browser inset (notch /
        // rounded corner) can eat the corner the dial sits in, so nudge it inward to the
        // visible canvas area. Screen.safeArea covers the viewport in WebGL.
        void NudgeForSafeArea(RectTransform rt, Vector2 corner)
        {
            Rect sa = Screen.safeArea;
            float fullW = Mathf.Max(1f, Screen.width);
            float fullH = Mathf.Max(1f, Screen.height);
            if (Mathf.Approximately(sa.width, fullW) && Mathf.Approximately(sa.height, fullH)) return;
            float s = hudCanvasRt != null ? hudCanvasRt.rect.size.x / fullW : 1f;
            Vector2 shift = Vector2.zero;
            if (corner.x <= 0f) shift.x += sa.x * s;
            if (corner.x >= 1f) shift.x -= (fullW - sa.xMax) * s;
            if (corner.y <= 0f) shift.y += sa.y * s;
            if (corner.y >= 1f) shift.y -= (fullH - sa.yMax) * s;
            rt.anchoredPosition += shift;
        }
        NudgeForSafeArea(compassObject.GetComponent<RectTransform>(), Vector2.zero);

        // --- Screen-edge objective arrow (baked sprite, hidden until the target is off-screen) ---
        edgeArrowObject = new GameObject("EdgeObjectiveArrow", typeof(RectTransform), typeof(Image));
        edgeArrowObject.transform.SetParent(canvasGo.transform, false);
        edgeArrowRt = edgeArrowObject.GetComponent<RectTransform>();
        edgeArrowRt.anchorMin = new Vector2(0.5f, 0.5f);
        edgeArrowRt.anchorMax = new Vector2(0.5f, 0.5f);
        edgeArrowRt.pivot = new Vector2(0.5f, 0.5f);
        edgeArrowRt.anchoredPosition = Vector2.zero;
        edgeArrowRt.sizeDelta = new Vector2(46f, 46f);
        edgeArrowImg = edgeArrowObject.GetComponent<Image>();
        edgeArrowImg.sprite = EdgeArrowSprite();
        edgeArrowImg.type = Image.Type.Simple;
        edgeArrowImg.raycastTarget = false;
        edgeArrowImg.color = new Color(1f, 0.9f, 0.2f, 1f);

        edgeArrowText = MakeLabel(edgeArrowObject.transform, "EdgeDist", "", 18,
            Color.white, new Vector2(0f, -30f), TextAnchor.UpperCenter);
        edgeArrowText.rectTransform.sizeDelta = new Vector2(200f, 20f);
        edgeArrowText.rectTransform.anchoredPosition = new Vector2(0f, -31f);
        edgeArrowObject.SetActive(false);
    }

    /// <summary>Builds one compass marker handle with a rim pip and centre needle pointing up.</summary>
    private RectTransform BuildCompassMarker(Transform parent, string name, Color pipColor, Color needleColor)
    {
        GameObject markGo = new GameObject(name + "Handle", typeof(RectTransform));
        markGo.transform.SetParent(parent, false);
        RectTransform markRt = markGo.GetComponent<RectTransform>();
        StretchToFull(markRt);

        // Highlighted rim pip pointing at the target's bearing.
        GameObject pipGo = new GameObject(name + "Pip", typeof(RectTransform), typeof(Image));
        pipGo.transform.SetParent(markRt, false);
        RectTransform pipRt = pipGo.GetComponent<RectTransform>();
        pipRt.anchorMin = new Vector2(0.5f, 1f);
        pipRt.anchorMax = new Vector2(0.5f, 1f);
        pipRt.pivot = new Vector2(0.5f, 0.5f);
        pipRt.anchoredPosition = new Vector2(0f, 2f);
        pipRt.sizeDelta = new Vector2(12f, 14f);
        EnsureWhiteSprite();
        Image pip = pipGo.GetComponent<Image>();
        pip.sprite = whiteSprite;
        pip.type = Image.Type.Simple;
        pip.color = pipColor;

        // Centre needle (long, points up within the marker handle).
        GameObject needleGo = new GameObject(name + "Needle", typeof(RectTransform), typeof(Image));
        needleGo.transform.SetParent(markRt, false);
        RectTransform needleRt = needleGo.GetComponent<RectTransform>();
        needleRt.anchorMin = new Vector2(0.5f, 0.5f);
        needleRt.anchorMax = new Vector2(0.5f, 0.5f);
        needleRt.pivot = new Vector2(0.5f, 0.15f);
        needleRt.anchoredPosition = Vector2.zero;
        needleRt.sizeDelta = new Vector2(4f, 48f);
        Image needleImg = needleGo.GetComponent<Image>();
        needleImg.sprite = whiteSprite;
        needleImg.type = Image.Type.Simple;
        needleImg.color = needleColor;

        return markRt;
    }

    /// <summary>Finds a named child Image within a compass marker handle (pip/needle).</summary>
    private Image FindMarkerImage(RectTransform handle, string childName)
    {
        Transform t = handle.Find(childName);
        if (t == null) return null;
        return t.GetComponent<Image>();
    }

    /// <summary>Re-tints a marker handle's pip + needle. LUNAR TRIALS colour coding: BLUE for
    /// a regular canister, GREEN for the final (green-lit) canister; the ore dial carries the
    /// planet's ore colour.</summary>
    private void ApplyMarkerTint(RectTransform handle, Color? tint)
    {
        if (tint == null || handle == null) return;
        string baseName = handle.name.EndsWith("Handle")
            ? handle.name.Substring(0, handle.name.Length - "Handle".Length)
            : handle.name;
        Image pip = FindMarkerImage(handle, baseName + "Pip");
        Image needle = FindMarkerImage(handle, baseName + "Needle");
        if (pip != null) pip.color = tint.Value;
        if (needle != null) needle.color = tint.Value;
    }

    /// <summary>LUNAR TRIALS: drives the independent ore dial (nearest uncollected nugget).
    /// Hidden while the landing zone owns the objective, the field is empty, or the run is over.
    /// The close-range sonar blip keeps flashing/beeping on top of this as the fine cue.</summary>
    public void SetOreDirection(Vector3 from, Vector3 to, int count)
    {
        if (oreMarker == null || !oreDialActive) return;
        bool runOver = oxygen != null && (oxygen.gameOver || oxygen.reachedEnd);
        if (count <= 0 || landingZoneActive || runOver)
        {
            oreMarker.gameObject.SetActive(false);
            if (oreText != null) oreText.text = "";
            return;
        }
        SetCompassTarget(oreMarker, oreText, from, to, true, "ORE", count, "NO ORE");
    }

    private void Update()
    {
        // Proximity sonar ping: re-query every frame so the flashing compass marker tracks the
        // rover's motion toward the nearest ore cluster and fades out once none is in range.
        // Once the run ends the ping stands down (it is a drive aid, not a menu jingle).
        bool levelOver = oxygen != null && (oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive);
        if (buggy != null && !levelOver)
        {
            bool inRange = Harvestable.NearestCluster(buggy.transform.position, ClusterSonar.SonarRadiusStatic,
                out Vector3 center, out float dist);
            float prox = inRange ? 1f - Mathf.Clamp01(dist / ClusterSonar.SonarRadiusStatic) : 0f;
            UpdateProximityPing(prox, inRange ? center - buggy.transform.position : Vector3.zero);
            // CrazyGames prospector onboarding: the ore tutorial fires the first time the
            // compass ping actually blinks at nearby ore in LIVE gameplay (no-op in every
            // other mode; the timeScale gate keeps a frozen moment from counting).
            if (inRange && Time.timeScale > 0f) TutorialCoach.NotifyOrePing();
        }
        else
        {
            UpdateProximityPing(0f, Vector3.zero);
        }

        // Screen-edge objective arrow (driven by the objective the compass driver last set).
        UpdateEdgeArrow();

        // Continuous readouts.
        if (speedText != null && buggy != null)
        {
            speedText.text = Mathf.RoundToInt(buggy.CurrentSpeedKmh) + " km/h";
        }
        if (timeText != null && oxygen != null)
        {
            if (ridgeSurvivalMode)
            {
                // Distance readout = banked legs + this leg's progress past the start crest.
                timeText.text = "DISTANCE " + OxygenManager.FormatKm(RidgeTotalMeters());
            }
            else if (raceMode)
            {
                timeText.text = OxygenManager.FormatRaceTime(oxygen.ElapsedTime);
            }
            else
            {
                int totalSec = Mathf.FloorToInt(oxygen.ElapsedTime);
                timeText.text = string.Format("{0:00}:{1:00}", totalSec / 60, totalSec % 60);
            }
        }
        if (podsText != null && oxygen != null)
        {
            int total = oxygen.TotalPods > 0 ? oxygen.TotalPods : oxygen.PodsCollected;
            podsText.text = ridgeSurvivalMode
                ? "CANISTERS " + oxygen.PodsCollected + " / " + total
                : (raceMode
                    ? "GATES " + oxygen.PodsCollected + " / " + total
                    : "Oxygen Pods: " + oxygen.PodsCollected + " / " + total);
        }
        if (resourceText != null && oxygen != null)
        {
            resourceText.text = resourceName + ": " + oxygen.HarvestablesCollected + " / " + oxygen.TotalHarvestables;
            // PROSPECTOR RUN banks the clear at HALF the field, so show the live percentage.
            if (GameFlow.Mode == GameMode.Prospector && oxygen.TotalHarvestables > 0)
            {
                int pct = Mathf.FloorToInt(100f * oxygen.HarvestablesCollected / oxygen.TotalHarvestables);
                resourceText.text += "  (" + pct + "%)";
            }
        }
        if (survivalInfoText != null && oxygen != null)
        {
            int sec = Mathf.FloorToInt(oxygen.ElapsedTime);
            survivalInfoText.text = "TIME " + string.Format("{0:00}:{1:00}", sec / 60, sec % 60)
                                    + "  ·  SET " + survivalSet;
        }
        if (scoreText != null && oxygen != null)
        {
            // Harvester score IS tokens: the blue counter grows live with the nuggets banked (their
            // per-planet value) plus the end-of-round spare-oxygen bonus, exactly the number
            // banked to the garage and shown on the win board.
            int run = oxygen.RunHarvestPoints + oxygen.BonusPoints;
            scoreText.text = "TOKENS  +" + run.ToString("N0");
            if (doubledTag != null)
            {
                bool on = GameFlow.TokensDoubled;
                if (doubledTag.gameObject.activeSelf != on) doubledTag.gameObject.SetActive(on);
                if (on)
                {
                    doubledTag.color = new Color(1f, 0.85f, 0.25f,
                        0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f));
                }
            }
        }
        if (partsText != null)
        {
            partsText.text = "TOKENS  " + UpgradeSystem.GetTokens();
        }

        // Zero-oxygen grace period: flash a countdown alarm under the oxygen bar so the
        // player knows to reach a canister before the clock runs out. At or under 10% the
        // same slot shows a critical-oxygen warning (bar pulses red) until the player tops up.
        // Lunar Race has no oxygen tank, so this slot is repurposed as the MISSED-GATE alarm:
        // a flashing red "HEAD BACK" until the skipped gate is passed through and collected.
        // Ridge Survival KEEPS the survival flavours below (grace / critical).
        if (o2AlarmText != null && raceMode && !ridgeSurvivalMode)
        {
            if (missedGate && !oxygen.gameOver && !oxygen.reachedEnd && !oxygen.endSequenceActive)
            {
                o2AlarmText.text = (Mathf.FloorToInt(Time.time * 2.5f) % 2 == 0)
                    ? "MISSED GATE  -  HEAD BACK"
                    : "MISSED GATE  -  HEAD BACK";
                o2AlarmText.color = (Mathf.FloorToInt(Time.time * 3f) % 2 == 0)
                    ? Color.white
                    : new Color(1f, 0.3f, 0.15f, 1f);
                o2AlarmText.gameObject.SetActive(true);
            }
            else
            {
                o2AlarmText.gameObject.SetActive(false);
            }
        }
        else if (o2AlarmText != null && oxygen != null)
        {
            if (oxygen.OxygenGraceActive && !oxygen.gameOver)
            {
                int secs = Mathf.CeilToInt(oxygen.OxygenGraceRemaining);
                o2AlarmText.text = "OXYGEN EMPTY · GET TO A CANISTER " + secs + "s";
                o2AlarmText.color = (Mathf.FloorToInt(Time.time * 3f) % 2 == 0)
                    ? Color.white
                    : new Color(1f, 0.25f, 0.15f, 1f);
                o2AlarmText.gameObject.SetActive(true);
            }
            else if (!oxygen.gameOver && oxygen.CurrentOxygen > 0f
                     && oxygen.CurrentOxygen <= oxygen.maxOxygen * 0.10f)
            {
                o2AlarmText.text = "OXYGEN CRITICAL " + Mathf.RoundToInt(oxygen.CurrentOxygen) + "%";
                o2AlarmText.color = (Mathf.FloorToInt(Time.time * 6f) % 2 == 0)
                    ? Color.white
                    : Color.red;
                o2AlarmText.gameObject.SetActive(true);
            }
            else
            {
                o2AlarmText.gameObject.SetActive(false);
            }
        }

        // Race route timeline live update (top centre, Lunar Race only).
        if (raceMode && !ridgeSurvivalMode)
        {
            RefreshRaceTimeline();
        }
        if (ridgeSurvivalMode)
        {
            RefreshRidgeProgress();
        }

        // Flashing environmental-hazard warning (meteor storms etc.): alternates a hot orange
        // with white so an inbound blast reads as urgent without touching the objective banner.
        if (warningText != null && warningText.gameObject.activeSelf)
        {
            bool bright = Mathf.FloorToInt(Time.time * 4f) % 2 == 0;
            warningText.color = bright
                ? new Color(1f, 0.95f, 0.7f, 1f)
                : new Color(1f, 0.45f, 0.1f, 1f);
        }

        // Oxygen display is read directly from the manager every frame so it always
        // reflects the real value (drain and pod top-ups), independent of events.
        if (oxygen != null)
        {
            if (oxygenBar != null)
            {
                oxygenBar.fillAmount = oxygen.CurrentOxygen / oxygen.maxOxygen;
                // Pulse the bar red while the tank is critical (matching the warning text).
                bool critical = !oxygen.gameOver && oxygen.CurrentOxygen > 0f
                                && oxygen.CurrentOxygen <= oxygen.maxOxygen * 0.10f;
                if (critical)
                {
                    bool flash = Mathf.FloorToInt(Time.time * 6f) % 2 == 0;
                    oxygenBar.color = flash ? new Color(1f, 0.2f, 0.15f, 1f) : Color.white;
                }
                else
                {
                    oxygenBar.color = new Color(0.25f, 0.75f, 1f, 1f);
                }
            }
            if (oxygenText != null)
            {
                oxygenText.text = "Oxygen " + Mathf.RoundToInt(oxygen.CurrentOxygen) + "%";
            }
        }

        // Hull integrity is pulled straight from the buggy's health every frame.
        if (health != null)
        {
            if (integrityFill != null)
            {
                integrityFill.fillAmount = health.CurrentHealth / health.maxHealth;
            }
            if (integrityText != null)
            {
                integrityText.text = "Integrity " + Mathf.RoundToInt(health.CurrentHealth) + "%";
            }
        }

        // Boost bar reflect buggy state.
        if (boostFill != null && buggy != null)
        {
            boostFill.fillAmount = buggy.BoostAmount / buggy.maxBoost;
            boostText.text = buggy.IsBoosting ? "BOOSTING (SPACE)" : "Boost (SPACE)";
        }

        // Banner: centre hint, game-over or win.
        if (oxygen != null)
        {
            Color c = overlay.GetComponent<Image>().color;
            bool gameOver = oxygen.gameOver;
            bool reachedEnd = oxygen.reachedEnd;

            if (hintActive && !gameOver && !reachedEnd)
            {
                if (hintFading)
                {
                    // Timed hint: fade the banner text and the dim tint off together, then
                    // drop the banner entirely once the text is invisible.
                    Color hintCol = bannerText.color;
                    hintCol.a = Mathf.MoveTowards(hintCol.a, 0f, Time.deltaTime * 1.6f);
                    bannerText.color = hintCol;

                    Color oc = c;
                    oc.a = Mathf.MoveTowards(oc.a, 0f, Time.deltaTime * 1.6f);
                    overlay.GetComponent<Image>().color = oc;

                    if (hintCol.a <= 0f)
                    {
                        bannerObject.SetActive(false);
                        showingHint = false;
                        hintActive = false;
                        hintFading = false;
                    }
                }
                else
                {
                    if (hintTimer > 0f)
                    {
                        hintTimer -= Time.deltaTime;
                        if (hintTimer <= 0f) hintFading = true;
                    }

                    if (!bannerObject.activeSelf || !showingHint || bannerText.text != hintText)
                    {
                        bannerObject.SetActive(true);
                        bannerText.text = hintText;
                        bannerText.fontSize = 34;
                        bannerText.color = new Color(0.4f, 0.95f, 1f, 1f);
                        showingHint = true;
                    }
                    c = Color.Lerp(c, new Color(0.02f, 0.05f, 0.1f, 0.4f), Time.deltaTime * 2.5f);
                    overlay.GetComponent<Image>().color = c;
                }
            }
            else
            {
                if (showingHint)
                {
                    bannerObject.SetActive(false);
                    showingHint = false;
                }

                if (gameOver)
                {
                    string reason = string.IsNullOrEmpty(oxygen.gameOverReason) ? "OUT OF OXYGEN" : oxygen.gameOverReason;
                    if (!bannerObject.activeSelf || bannerText.text != reason)
                    {
                        bannerObject.SetActive(true);
                        bannerText.text = reason;
                        bannerText.fontSize = 48;
                        bannerText.color = new Color(1f, 0.3f, 0.3f, 1f);
                    }
                    c = Color.Lerp(c, new Color(0.3f, 0f, 0f, 0.55f), Time.deltaTime * 2f);
                }
                else if (reachedEnd && raceMode && !ridgeSurvivalMode)
                {
                    // Lunar Race banner: verdict against the rival LEAGUE, not "level complete".
                    int playerCenti = oxygen != null ? Mathf.RoundToInt(oxygen.ElapsedTime * 100f) : 0;
                    RivalRacer.RaceResult res = RivalRacer.ResultFor(playerCenti);
                    string verdict = RivalRacer.ResultTitle(res, oxygen == null || !oxygen.RaceTimeoutConcluded);
                    Color verdictColor = RivalRacer.ResultColor(res);
                    if (!bannerObject.activeSelf || bannerText.text != verdict)
                    {
                        bannerObject.SetActive(true);
                        bannerText.text = verdict;
                        bannerText.fontSize = 48;
                        bannerText.color = verdictColor;
                    }
                    bool won = res != RivalRacer.RaceResult.Lost;
                    c = Color.Lerp(c, won ? new Color(0f, 0.2f, 0.1f, 0.5f) : new Color(0.22f, 0.04f, 0.02f, 0.5f), Time.deltaTime * 2f);
                }
                else if (reachedEnd && ridgeSurvivalMode)
                {
                    // Ridge Survival banner: the whole course survived = a leg banked.
                    string legText = "LEG " + GameFlow.RidgeSurvivalLoops + " CLEARED";
                    if (!bannerObject.activeSelf || bannerText.text != legText)
                    {
                        bannerObject.SetActive(true);
                        bannerText.text = legText;
                        bannerText.fontSize = 48;
                        bannerText.color = new Color(0.4f, 1f, 0.6f, 1f);
                    }
                    c = Color.Lerp(c, new Color(0f, 0.2f, 0.1f, 0.5f), Time.deltaTime * 2f);
                }
                else
                {
                    // Plain play again (e.g. the run just revived behind the banner): drop any
                    // residual game-over / win banner and fade its dim tint back to clear -
                    // otherwise the "OUT OF OXYGEN" text and red wash stay up after a revive.
                    // A finished HARVEST leg lands here too: its old "LEVEL X COMPLETE!" banner
                    // is retired, because the congrats board and the star ceremony already
                    // announce the clear (and the grade/stars that earned it).
                    if (bannerObject.activeSelf)
                    {
                        bannerObject.SetActive(false);
                        showingHint = false;
                    }
                    c = Color.Lerp(c, Color.clear, Time.deltaTime * 3f);
                }
                overlay.GetComponent<Image>().color = c;
            }
        }
    }

    private void RefreshBars()
    {
        if (oxygenBar != null && oxygen != null)
        {
            oxygenBar.fillAmount = oxygen.CurrentOxygen / oxygen.maxOxygen;
            oxygenText.text = "Oxygen " + Mathf.RoundToInt(oxygen.CurrentOxygen) + "%";
        }
    }

    /// <summary>
    /// Shows a centre-banner hint, e.g. "REACH THE LANDING ZONE". Pass a positive
    /// <paramref name="durationSec"/> to auto-fade it (and its dim tint) away after that long.
    /// </summary>
    public void ShowHint(string text, float durationSec = 0f)
    {
        hintText = text;
        hintActive = true;
        hintTimer = durationSec;
        hintFading = false;
        if (bannerText != null) bannerText.color = new Color(0.4f, 0.95f, 1f, 1f);
    }

    /// <summary>Shows a flashing hazard warning under the timer (meteor storms etc.).</summary>
    public void ShowWarning(string text)
    {
        if (warningText == null) return;
        warningText.text = text;
        warningText.gameObject.SetActive(true);
    }

    /// <summary>Hides the flashing hazard warning.</summary>
    public void HideWarning()
    {
        if (warningText == null) return;
        warningText.gameObject.SetActive(false);
    }

    /// <summary>Shows a compass navigation warning (PROSPECTOR RUN wall proximity / off-route
    /// drift). Lives under the dial in its own slot so it never fights the hazard flashes.</summary>
    public void ShowNavWarning(string text)
    {
        if (navWarningText == null) return;
        navWarningText.text = text;
        navWarningText.gameObject.SetActive(true);
    }

    /// <summary>Hides the compass navigation warning.</summary>
    public void HideNavWarning()
    {
        if (navWarningText == null) return;
        navWarningText.gameObject.SetActive(false);
    }

    /// <summary>Shows the race post-bronze cutoff countdown: how many seconds the player has
    /// left to cross the line before the race auto-concludes. Flashes faster as it nears zero.
    /// Driven every frame by LevelEndSequence while the cutoff window is open.</summary>
    public void SetRaceCutoffCountdown(float secondsLeft)
    {
        if (raceCutoffText == null) return;
        int secs = Mathf.CeilToInt(Mathf.Max(0f, secondsLeft));
        raceCutoffText.text = "RACE ENDING IN  " + secs + "  -  CROSS THE LINE";
        if (!raceCutoffText.gameObject.activeSelf) raceCutoffText.gameObject.SetActive(true);
        float flash = secondsLeft <= 4f ? 6f : 2.5f;
        bool bright = Mathf.FloorToInt(Time.time * flash) % 2 == 0;
        raceCutoffText.color = bright
            ? Color.white
            : new Color(1f, 0.25f, 0.1f, 1f);
    }

    /// <summary>Hides the race cutoff countdown (the player crossed the line, the cutoff fade
    /// began, or the run ended another way).</summary>
    public void HideRaceCutoffCountdown()
    {
        if (raceCutoffText == null) return;
        raceCutoffText.gameObject.SetActive(false);
    }

    /// <summary>Paints a spray of scattered droplets onto the chase-cam lens for a moment.
    /// <paramref name="color"/> is the surface tint (lava orange / mud brown / lake green-blue /
    /// ice silver), <paramref name="alpha"/> is the fade value driven by the spray emitter.</summary>
    public void SetSurfaceBlotch(Color color, float alpha)
    {
        if (lensBlotch == null) return;
        if (alpha <= 0.01f) { HideSurfaceBlotch(); return; }

        // A fresh hit (alpha rising again) slaps new droplets on in a different spot/orientation.
        if (alpha > lastBlotchAlpha + 0.05f)
        {
            RectTransform rt = lensBlotch.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(Random.Range(-24f, 24f), Random.Range(-14f, 14f));
            rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        }
        lastBlotchAlpha = alpha;

        lensBlotch.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        lensBlotch.gameObject.SetActive(true);
        lensBlotch.transform.SetAsLastSibling(); // droplets sit on top of the whole HUD
    }

    /// <summary>Clears the chase-cam lens blotch.</summary>
    public void HideSurfaceBlotch()
    {
        lastBlotchAlpha = -1f;
        if (lensBlotch == null) return;
        lensBlotch.gameObject.SetActive(false);
    }

    /// <summary>Shows the wind-gust indicator beside the compass: an arrow pointing the way the
    /// gust shoves (<paramref name="dirSign"/> +1 right / -1 left) with its strength underneath.
    /// The callers hide it again as soon as the gust dies down.</summary>
    public void ShowWindGust(int dirSign, float speed)
    {
        if (windArrow == null) return;
        // Latin-1 chevrons (U+00AB/U+00BB) render from the bundled LegacyRuntime font on WebGL;
        // the solid-triangle glyphs (U+25BA/U+25C4) are not in the font and would render as holes.
        windArrow.text = dirSign > 0 ? "»" : "«";
        windArrow.gameObject.SetActive(true);
        if (windSpeedText != null)
        {
            windSpeedText.text = "WIND " + Mathf.RoundToInt(speed) + " m/s";
            windSpeedText.gameObject.SetActive(true);
        }
    }

    /// <summary>Hides the wind-gust indicator (gust over).</summary>
    public void HideWindGust()
    {
        if (windArrow != null) windArrow.gameObject.SetActive(false);
        if (windSpeedText != null) windSpeedText.gameObject.SetActive(false);
    }

    /// <summary>Sets the Survival difficulty-set counter shown in the HUD.</summary>
    public void SetSurvivalSet(int setIndex)
    {
        survivalSet = Mathf.Max(1, setIndex);
    }

    /// <summary>
    /// Drives the communication-range indicator (Lunar Survival). <paramref name="danger"/> is
    /// 0 (dead on the route) .. 1 (at the out-of-range edge). The bar shows remaining link and
    /// turns red as the buggy nears the edge; the banner appears only once <paramref name="warned"/>.
    /// </summary>
    public void SetCommLoss(float danger, bool warned)
    {
        if (commObject == null || commFill == null || commText == null) return;

        float link = Mathf.Clamp01(1f - danger);
        bool showBar = warned || link < 0.5f;   // only surface the meter once the link gets weak
        commObject.SetActive(showBar);
        commText.gameObject.SetActive(warned);
        if (!showBar) return;

        commFill.fillAmount = link;
        commFill.color = warned
            ? new Color(1f, 0.25f, 0.2f, 1f)
            : new Color(0.2f, 1f, 0.4f, 1f);

        if (warned)
        {
            int pct = Mathf.RoundToInt(link * 100f);
            commText.color = (Time.time * 3f) % 2f < 1f ? Color.white : new Color(1f, 0.6f, 0.2f, 1f);
            commText.text = "GOING OUT OF COMMUNICATION RANGE  ·  LINK " + pct + "%";
        }
    }

    /// <summary>
    /// Drives the compass: the cardinal rose rotates to always show true world direction; the
    /// highlighted rim marker and needle point at the NEXT oxygen pod/canister (<paramref name="to"/>)
    /// at its relative bearing, so the player turns until marker, needle and the fixed top heading
    /// tick line up (target dead ahead). <paramref name="count"/> is un-collected targets remaining;
    /// <paramref name="targetName"/> feeds the label ("NEXT CANISTER", "NEXT POD").
    /// </summary>
    public void SetRouteDirection(Vector3 from, Vector3 to, int count, string targetName = "NEXT CANISTER", Color? markerTint = null)
    {
        if (compassMarker == null) return;
        // Once the final pod is collected the landing zone takes over as the only
        // objective: the orange marker replaces the pod marker until boarding.
        if (landingZoneActive) return;
        ApplyMarkerTint(compassMarker, markerTint);

        Vector3 dir = to - from;
        dir.y = 0f;
        float dist = dir.magnitude;
        bool hasTarget = count > 0 && dist > 0.5f;

        RotateRose();
        SetCompassTarget(compassMarker, compassText, from, to, hasTarget,
            targetName, count, "NO " + targetName.Replace("NEXT ", "") + "S AHEAD");
    }

    /// <summary>Rotates the cardinal rose to match the buggy's world heading (up on the dial = heading).</summary>
    private void RotateRose()
    {
        if (compassRose != null)
        {
            float yaw = buggy != null ? buggy.transform.eulerAngles.y : 0f;
            // Facing north (yaw=0) keeps N at the top; facing east (yaw=+90) swings N to the
            // left (north is to your left when heading east) => rotate the rose by +yaw.
            compassRose.localRotation = Quaternion.Euler(0f, 0f, yaw);
        }
    }

    /// <summary>
    /// Points one compass marker at a target's relative bearing and writes its label. The rotation
    /// eases so it reads smoothly instead of whipping on every frame; it snaps on first appear.
    /// </summary>
    private void SetCompassTarget(RectTransform marker, Text label, Vector3 from, Vector3 to, bool hasTarget, string name, int count, string noneText)
    {
        Vector3 dir = to - from;
        dir.y = 0f;
        float dist = dir.magnitude;

        // Track the live objective for the screen-edge arrow: the pod marker's target during
        // normal driving, the landing pad once the final pod is collected (SetLandingZone
        // drives the third marker). Losing the target clears the arrow.
        if (marker == compassMarker || marker == compassMarker3)
        {
            objectiveActive = hasTarget;
            if (hasTarget)
            {
                objectivePos = to;
                objectiveName = name;
                objectiveDist = dist;
            }
        }

        if (marker == null)
        {
            if (label != null) label.text = hasTarget ? name + " " + Mathf.RoundToInt(dist) + "m" : noneText;
            return;
        }

        if (hasTarget)
        {
            Vector3 forwardFlat = Vector3.ProjectOnPlane(
                buggy != null ? buggy.transform.forward : Vector3.forward, Vector3.up).normalized;
            // Bearing of the target relative to the buggy's heading (positive = to the right).
            float angle = Vector3.SignedAngle(forwardFlat, dir.normalized, Vector3.up);

            bool wasHidden = !marker.gameObject.activeSelf;
            float targetZ = -angle;
            float currentZ = marker.localRotation.eulerAngles.z;
            float smoothed = wasHidden
                ? targetZ
                : Mathf.LerpAngle(currentZ, targetZ, Mathf.Clamp01(Time.deltaTime * 6f));
            marker.localRotation = Quaternion.Euler(0f, 0f, smoothed);
            marker.gameObject.SetActive(true);

            if (label != null)
            {
                int d = Mathf.RoundToInt(dist);
                string bearing = Mathf.Abs(angle) > 2f
                    ? "  (" + Mathf.RoundToInt(angle) + "° " + (angle <= 0 ? "L" : "R") + ")"
                    : "  AHEAD";
                label.text = name + " · " + d + "m" + bearing + (count > 0 ? "  " + count + " LEFT" : "");
            }
        }
        else
        {
            marker.gameObject.SetActive(false);
            if (label != null) label.text = noneText;
        }
    }

    /// <summary>
    /// Screen-edge objective arrow: once the next pod / landing pad is out of the viewport, a
    /// rotated arrow sits at the nearest screen edge pointing at it with its distance label, so
    /// the player turns toward the arrow without ever reading the dial. Hidden while the target
    /// is on screen (the world itself is then visible) and once the run is over.
    /// </summary>
    private void UpdateEdgeArrow()
    {
        if (edgeArrowObject == null) return;
        if (!objectiveActive || hudCam == null || Screen.width < 4 || Screen.height < 4
            || (oxygen != null && (oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive)))
        {
            if (edgeArrowObject.activeSelf) edgeArrowObject.SetActive(false);
            return;
        }

        Vector3 sp = hudCam.WorldToScreenPoint(objectivePos);
        bool behind = sp.z < 0f;
        Vector2 sc = new Vector2(sp.x / Screen.width, sp.y / Screen.height);   // 0..1, bottom-left
        bool onScreen = !behind && sc.x > 0.02f && sc.x < 0.98f
                        && sc.y > 0.05f && sc.y < 0.95f;
        if (onScreen)
        {
            if (edgeArrowObject.activeSelf) edgeArrowObject.SetActive(false);
            return;
        }

        Vector2 dir = sc - new Vector2(0.5f, 0.5f);
        if (behind) dir = -dir;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
        dir.Normalize();

        // Clamp the arrow to a keep-box inset from each edge (accounts for the corner HUD and
        // roughly for mobile notches), and rotate it to point at the off-screen target.
        Vector2 half = hudCanvasRt != null ? hudCanvasRt.rect.size * 0.5f : new Vector2(960f, 540f);
        Vector2 keep = new Vector2(Mathf.Max(60f, half.x - 120f), Mathf.Max(70f, half.y - 150f));
        Vector2 pos = new Vector2(
            Mathf.Clamp((sc.x - 0.5f) * 2f * half.x, -keep.x, keep.x),
            Mathf.Clamp((sc.y - 0.5f) * 2f * half.y, -keep.y, keep.y));
        edgeArrowRt.anchoredPosition = pos;
        edgeArrowRt.localRotation = Quaternion.Euler(0f, 0f,
            Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
        edgeArrowImg.color = landingZoneActive
            ? new Color(1f, 0.55f, 0.12f, 1f)
            : new Color(1f, 0.9f, 0.2f, 1f);
        if (edgeArrowText != null)
        {
            edgeArrowText.text = objectiveName + "\n" + Mathf.RoundToInt(objectiveDist) + "m";
            // The label lives on the rotated arrow root, so cancel the arrow's spin to keep
            // the distance text readable at any bearing.
            edgeArrowText.rectTransform.localRotation = Quaternion.identity;
        }
        edgeArrowObject.SetActive(true);
    }

    /// <summary>Bakes the edge arrow as a filled upward triangle (WebGL strips exotic text glyphs,
    /// so the pointer must be a sprite, exactly like the menu's stars and discs).</summary>
    private Sprite EdgeArrowSprite()
    {
        if (edgeArrowSprite != null) return edgeArrowSprite;
        const int S = 48;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[S * S];
        float ax = S * 0.5f, ay = S - 2f;          // apex near the top
        float bx = 2f,    by = 6f;                  // base corners
        float cx = S - 3f, cy = 6f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float px0 = x + 0.5f, py = y + 0.5f;
                float d1 = (px0 - bx) * (ay - by) - (ax - bx) * (py - by);
                float d2 = (px0 - cx) * (by - cy) - (bx - cx) * (py - cy);
                float d3 = (px0 - ax) * (cy - ay) - (cx - ax) * (py - ay);
                bool inside = (d1 >= 0f && d2 >= 0f && d3 >= 0f) || (d1 <= 0f && d2 <= 0f && d3 <= 0f);
                px[y * S + x] = new Color(1f, 1f, 1f, inside ? 1f : 0f);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        edgeArrowSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        edgeArrowSprite.name = "EdgeObjectiveArrow";
        return edgeArrowSprite;
    }

    /// <summary>
    /// Switches the compass to landing-zone mode (final pod collected): the orange marker points
    /// at the pad and the pod marker is hidden until the player boards.
    /// <paramref name="from"/> is the buggy's current position so the bearing tracks live.
    /// </summary>
    public void SetLandingZone(Vector3 from, Vector3 to)
    {
        if (compassMarker == null) return;
        landingZoneActive = true;

        RotateRose();

        // Hide the route markers/labels; the landing zone is now the only objective.
        if (compassMarker != null) compassMarker.gameObject.SetActive(false);
        if (compassMarker3 == null) return;
        if (compassText != null) compassText.text = "";

        Vector3 dir = to - from;
        dir.y = 0f;
        bool hasTarget = dir.magnitude > 0.5f;
        SetCompassTarget(compassMarker3, compassText3, from, to, hasTarget, "LANDING ZONE", 0, "LANDING ZONE");
    }

    /// <summary>Ends landing-zone mode once boarding begins, so the compass is free again.</summary>
    public void ClearLandingZone()
    {
        landingZoneActive = false;
        if (compassMarker3 != null) compassMarker3.gameObject.SetActive(false);
        if (compassText3 != null) compassText3.text = "";
    }

    /// <summary>
    /// Drives the flashing proximity-sonar marker. Feeds the rover's current proximity to the
    /// nearest ore cluster (1 = right on it, 0 = just inside the sonar's outer edge);
    /// <paramref name="direction"/> is the world-space vector to that cluster. The marker is
    /// hidden entirely while no cluster is in range, and its flash is brighter and faster the
    /// closer the rover gets.
    /// </summary>
    public void UpdateProximityPing(float proximity, Vector3 direction)
    {
        if (compassMarkerPing == null) return;

        bool active = proximity > 0f && direction.sqrMagnitude > 0.0001f;
        compassMarkerPing.gameObject.SetActive(active);
        if (active)
        {
            RotateRose();
            Vector3 forwardFlat = Vector3.ProjectOnPlane(
                buggy != null ? buggy.transform.forward : Vector3.forward, Vector3.up).normalized;
            Vector3 dir = direction;
            dir.y = 0f;
            float angle = Vector3.SignedAngle(forwardFlat, dir.normalized, Vector3.up);
            compassMarkerPing.localRotation = Quaternion.Euler(0f, 0f, -angle);

            float baseAlpha = 0.25f + 0.75f * proximity;
            float flash = Mathf.Sin(Time.time * (2.5f + 9f * proximity));
            float a = Mathf.Clamp01(flash) * baseAlpha;
            if (pingPip != null) pingPip.color = new Color(pingPip.color.r, pingPip.color.g, pingPip.color.b, a);
            if (pingNeedle != null) pingNeedle.color = new Color(pingNeedle.color.r, pingNeedle.color.g, pingNeedle.color.b, a);
        }
    }

    /// <summary>Hides the centre-banner hint when the player reaches the objective.</summary>
    public void HideHint()
    {
        hintActive = false;
    }

    // ---------- Race route timeline ----------

    /// <summary>
    /// Builds the top-centre race timeline: a point-to-point progress bar with a tick per
    /// gate, the player's and rival's live position pins, the target gate name + distance
    /// ahead, and a live you-vs-rival split readout.
    /// </summary>
    private void BuildRaceTimeline(GameObject canvasGo)
    {
        GameObject rootGo = new GameObject("RaceTimeline", typeof(RectTransform), typeof(Image));
        rootGo.transform.SetParent(canvasGo.transform, false);
        RectTransform root = rootGo.GetComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 1f);
        root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -170f);
        root.sizeDelta = new Vector2(480f, 78f);
        rootGo.GetComponent<Image>().color = new Color(0.03f, 0.06f, 0.08f, 0.78f);
        raceTimelineRoot = rootGo;

        // Progress track.
        GameObject track = new GameObject("RaceTrack", typeof(RectTransform), typeof(Image));
        track.transform.SetParent(root, false);
        RectTransform ttr = track.GetComponent<RectTransform>();
        ttr.anchorMin = new Vector2(0f, 1f);
        ttr.anchorMax = new Vector2(1f, 1f);
        ttr.pivot = new Vector2(0.5f, 0.5f);
        ttr.anchoredPosition = new Vector2(0f, -20f);
        ttr.sizeDelta = new Vector2(-40f, 14f);
        track.GetComponent<Image>().color = new Color(0.12f, 0.15f, 0.17f, 0.95f);

        raceTimelineFill = MakeFill(track.transform, "RaceFill", new Color(0.2f, 0.75f, 0.95f, 1f));

        // One tick per gate, evenly looped into place by RefreshRaceTimeline (they live on
        // the track, so early on they're all clustered at the start line - exactly right).
        int gateCount = oxygen != null && oxygen.TotalPods > 0 ? oxygen.TotalPods : 10;
        for (int i = 0; i < gateCount; i++)
        {
            GameObject tick = new GameObject("GateTick", typeof(RectTransform), typeof(Image));
            tick.transform.SetParent(track.transform, false);
            RectTransform tickRt = tick.GetComponent<RectTransform>();
            tickRt.anchorMin = tickRt.anchorMax = new Vector2(0.5f, 0.5f);
            tickRt.pivot = new Vector2(0.5f, 0.5f);
            tickRt.sizeDelta = new Vector2(3f, 12f);
            tick.GetComponent<Image>().color = new Color(0.6f, 0.65f, 0.7f, 1f);
            raceGateTicks.Add(tickRt);
        }

        racePlayerPin = MakeTimelinePin(track.transform, "PlayerPin", 16f, new Color(0.35f, 0.8f, 1f, 1f));
        // One pin per rival tier (bronze / silver / gold) - found and coloured on refresh.
        raceRivalPins.Add(MakeTimelinePin(track.transform, "RivalPinBronze", 11f, Color.gray));
        raceRivalPins.Add(MakeTimelinePin(track.transform, "RivalPinSilver", 12f, Color.gray));
        raceRivalPins.Add(MakeTimelinePin(track.transform, "RivalPinGold", 13f, Color.gray));

        raceTargetText = MakeChildLabel(root, "TimelineTarget", "TARGET: GATE 1", 18,
            new Color(1f, 0.75f, 0.3f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            TextAnchor.MiddleLeft, new Vector2(10f, 4f));
        raceAheadText = MakeChildLabel(root, "TimelineAhead", "0M AHEAD", 18,
            new Color(0.6f, 0.85f, 1f, 1f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            TextAnchor.MiddleRight, new Vector2(-10f, 4f));
        raceSplitText = MakeChildLabel(root, "TimelineSplit", "VS GOLD", 18,
            new Color(1f, 0.95f, 0.9f, 0.95f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            TextAnchor.MiddleCenter, new Vector2(0f, 4f));
    }

    private RectTransform MakeTimelinePin(Transform parent, string name, float size, Color color)
    {
        GameObject pin = new GameObject(name, typeof(RectTransform), typeof(Image));
        pin.transform.SetParent(parent, false);
        RectTransform rt = pin.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        Image img = pin.GetComponent<Image>();
        img.sprite = MakeCircleSprite();
        img.color = color;
        return rt;
    }

    private Text MakeChildLabel(RectTransform parent, string name, string initial, int size, Color color,
        Vector2 anchor, Vector2 pivot, TextAnchor align, Vector2 anchoredPos)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(300f, 30f);
        Text text = go.AddComponent<Text>();
        AssignDefaultFont(text);
        text.fontSize = size;
        text.alignment = align;
        text.color = color;
        text.text = initial;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return text;
    }

    /// <summary>Live-updates the race route timeline every frame (race mode only).</summary>
    private void RefreshRaceTimeline()
    {
        if (raceTimelineRoot == null) return;

        if (!raceTimelineReady)
        {
            raceManager = FindAnyObjectByType<RaceGateManager>();
            raceRivals.Clear();
            raceRivals.AddRange(RivalRacer.All());
            // Order pins bronze -> silver -> gold so they stay tied to the league.
            raceRivals.Sort((a, b) => a.tier.CompareTo(b.tier));
            raceTimelineReady = true;
        }
        if (raceManager == null || buggy == null) return;

        float startZ = raceManager.courseStartZ;
        float len = Mathf.Max(1f, raceManager.courseEndZ - startZ);
        float playerFrac = Mathf.Clamp01((buggy.transform.position.z - startZ) / len);

        const float trackW = 440f;
        float halfW = trackW * 0.5f;

        if (raceTimelineFill != null) raceTimelineFill.fillAmount = playerFrac;
        if (racePlayerPin != null) racePlayerPin.anchoredPosition = new Vector2(-halfW + playerFrac * trackW, 0f);

        // One pin per rival, tier-coloured, sliding as they cross the timeline.
        for (int i = 0; i < raceRivalPins.Count; i++)
        {
            RectTransform pin = raceRivalPins[i];
            if (pin == null) continue;
            RivalRacer r = i < raceRivals.Count ? raceRivals[i] : null;
            Image img = pin.GetComponent<Image>();
            if (r != null)
            {
                float f = Mathf.Clamp01((r.CurrentZ - startZ) / len);
                pin.anchoredPosition = new Vector2(-halfW + f * trackW, 0f);
                pin.gameObject.SetActive(true);
                if (img != null) img.color = RivalRacer.TierColor(r.tier);
            }
            else
            {
                pin.gameObject.SetActive(false);
            }
        }

        for (int i = 0; i < raceGateTicks.Count; i++)
        {
            RectTransform tick = raceGateTicks[i];
            if (tick == null) continue;
            if (raceManager.gates != null && i < raceManager.gates.Count && raceManager.gates[i] != null)
            {
                float f = Mathf.Clamp01((raceManager.gates[i].transform.position.z - startZ) / len);
                tick.anchoredPosition = new Vector2(-halfW + f * trackW, 0f);
                tick.gameObject.SetActive(true);
            }
            else
            {
                tick.gameObject.SetActive(false);
            }
        }

        if (raceTargetText != null && raceAheadText != null)
        {
            int idx = raceManager.NextTargetIndex();
            if (idx >= 0 && idx < raceManager.gates.Count && raceManager.gates[idx] != null)
            {
                OxygenPod g = raceManager.gates[idx];
                int dist = Mathf.Max(0, Mathf.RoundToInt(g.transform.position.z - buggy.transform.position.z));
                raceTargetText.text = g.isFinalPod ? "TARGET: FINISH" : "TARGET: GATE " + (idx + 1);
                raceAheadText.text = dist + "M AHEAD";
            }
            else
            {
                raceTargetText.text = "ALL GATES PASSED";
                raceAheadText.text = "GO TO THE FINISH!";
            }
        }

        if (raceSplitText != null)
        {
            // Split vs the FASTEST rival on the field - the top unlocked grade, which decides
            // the maximum verdict for this run.
            RivalRacer top = raceRivals.Count > 0 ? raceRivals[raceRivals.Count - 1] : null;
            if (top != null)
            {
                float lead = buggy.transform.position.z - top.CurrentZ;
                string name = RivalRacer.TierName(top.tier);
                raceSplitText.text = lead >= 0f
                    ? name + " +" + Mathf.RoundToInt(lead) + "M"
                    : name + " -" + Mathf.RoundToInt(-lead) + "M";
            }
            else
            {
                raceSplitText.text = "VS BRONZE";
            }
        }
    }

    // ---------- Ridge Survival progress bar ----------

    /// <summary>Total survival distance: the legs already banked plus this leg's progress past the
    /// start crest. Once THIS leg has been banked (boarding the lander adds it to the banked total)
    /// the buggy is still parked at the finish line, so its position must no longer be added - doing
    /// so counted the finished leg twice and made the readout jump by a whole leg during the launch.</summary>
    private float RidgeTotalMeters()
    {
        float banked = GameFlow.RidgeSurvivalMeters;
        if (banked > ridgeMetersAtStart + 0.5f) return banked;   // this leg is already in the bank
        float progress = Mathf.Max(0f,
            (buggy != null ? buggy.transform.position.z : 0f) - MoonTerrainGen.RaceStartZ);
        return banked + progress;
    }

    /// <summary>Builds the top-centre Ridge progress bar: a track that fills with total distance, a tick
    /// and coloured label per grade (Bronze / Silver / Gold / Platinum), the player's pin, and a
    /// "next grade" line with the distance still to go.</summary>
    private void BuildRidgeProgressBar(GameObject canvasGo)
    {
        GameObject rootGo = new GameObject("RidgeProgress", typeof(RectTransform), typeof(Image));
        rootGo.transform.SetParent(canvasGo.transform, false);
        RectTransform root = rootGo.GetComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 1f);
        root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -170f);
        root.sizeDelta = new Vector2(560f, 92f);
        rootGo.GetComponent<Image>().color = new Color(0.03f, 0.06f, 0.08f, 0.78f);
        ridgeBarRoot = rootGo;

        GameObject track = new GameObject("RidgeTrack", typeof(RectTransform), typeof(Image));
        track.transform.SetParent(root, false);
        RectTransform ttr = track.GetComponent<RectTransform>();
        ttr.anchorMin = new Vector2(0f, 1f);
        ttr.anchorMax = new Vector2(1f, 1f);
        ttr.pivot = new Vector2(0.5f, 0.5f);
        ttr.anchoredPosition = new Vector2(0f, -42f);
        ttr.sizeDelta = new Vector2(-40f, 14f);
        track.GetComponent<Image>().color = new Color(0.12f, 0.15f, 0.17f, 0.95f);

        ridgeBarFill = MakeFill(track.transform, "RidgeFill", new Color(0.3f, 0.85f, 0.5f, 1f));

        int last = GameFlow.RidgeGradeDistances.Length - 1;
        for (int i = 0; i <= last; i++)
        {
            Color gc = GameFlow.RidgeGradeColors[i];

            GameObject tick = new GameObject("GradeTick", typeof(RectTransform), typeof(Image));
            tick.transform.SetParent(track.transform, false);
            RectTransform tickRt = tick.GetComponent<RectTransform>();
            tickRt.anchorMin = tickRt.anchorMax = new Vector2(0.5f, 0.5f);
            tickRt.pivot = new Vector2(0.5f, 0.5f);
            tickRt.sizeDelta = new Vector2(4f, 20f);
            Image tickImg = tick.GetComponent<Image>();
            tickImg.color = gc;
            ridgeGradeTicks.Add(tickImg);

            // The last label (Platinum) sits at the bar's right end, so it hangs inward from its tick.
            bool isLast = i == last;
            Text label = MakeChildLabel(ttr, "GradeLabel" + i, GameFlow.RidgeGradeNames[i], 15, gc,
                new Vector2(0.5f, 0.5f), new Vector2(isLast ? 1f : 0.5f, 0.5f),
                isLast ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter, new Vector2(0f, 20f));
            ridgeGradeLabels.Add(label);
        }

        ridgePlayerPin = MakeTimelinePin(track.transform, "PlayerPin", 16f, new Color(1f, 0.95f, 0.6f, 1f));

        ridgeNextText = MakeChildLabel(root, "RidgeNext", "", 18, new Color(1f, 0.75f, 0.3f, 1f),
            new Vector2(0f, 0f), new Vector2(0f, 0f), TextAnchor.MiddleLeft, new Vector2(10f, 4f));
        ridgeToGoText = MakeChildLabel(root, "RidgeToGo", "", 18, new Color(0.6f, 0.95f, 0.75f, 1f),
            new Vector2(1f, 0f), new Vector2(1f, 0f), TextAnchor.MiddleRight, new Vector2(-10f, 4f));
    }

    /// <summary>Live-updates the Ridge progress bar every frame.</summary>
    private void RefreshRidgeProgress()
    {
        if (ridgeBarRoot == null) return;

        float total = RidgeTotalMeters();
        float[] d = GameFlow.RidgeGradeDistances;
        int last = d.Length - 1;
        float span = Mathf.Max(1f, d[last]);
        float halfW = RidgeBarTrackW * 0.5f;

        float f = Mathf.Clamp01(total / span);
        if (ridgeBarFill != null) ridgeBarFill.fillAmount = f;
        if (ridgePlayerPin != null) ridgePlayerPin.anchoredPosition = new Vector2(-halfW + f * RidgeBarTrackW, 0f);

        // Grade ticks sit at their true distance along the bar; reached grades light up.
        int next = -1;
        for (int i = 0; i <= last; i++)
        {
            float x = -halfW + Mathf.Clamp01(d[i] / span) * RidgeBarTrackW;
            bool reached = total >= d[i];
            if (next < 0 && !reached) next = i;

            Color gc = GameFlow.RidgeGradeColors[i];
            gc.a = reached ? 1f : 0.6f;
            if (i < ridgeGradeTicks.Count && ridgeGradeTicks[i] != null)
            {
                ridgeGradeTicks[i].rectTransform.anchoredPosition = new Vector2(x, 0f);
                ridgeGradeTicks[i].color = gc;
            }
            if (i < ridgeGradeLabels.Count && ridgeGradeLabels[i] != null)
            {
                ridgeGradeLabels[i].rectTransform.anchoredPosition = new Vector2(x + (i == last ? 10f : 0f), 20f);
                ridgeGradeLabels[i].color = gc;
            }
        }

        if (ridgeNextText == null || ridgeToGoText == null) return;
        if (next >= 0)
        {
            ridgeNextText.text = "NEXT: " + GameFlow.RidgeGradeNames[next] + "  ·  +" + GameFlow.RidgeGradeTokens[next] + " TOKENS";
            ridgeNextText.color = GameFlow.RidgeGradeColors[next];
            ridgeToGoText.text = OxygenManager.FormatKm(d[next] - total) + " TO GO";
        }
        else
        {
            ridgeNextText.text = "ALL GRADES BANKED";
            ridgeNextText.color = GameFlow.RidgeGradeColors[last];
            ridgeToGoText.text = "+" + OxygenManager.FormatKm(total - d[last]);
        }
    }

    // ---------- UI helpers ----------

    private GameObject MakeBarPanel(Transform parent, string name, Vector2 anchoredPos, Vector2 size, bool rightAnchored = false, Color? color = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rightAnchored)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
        }
        else
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
        }
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        if (color.HasValue) img.color = color.Value;
        return go;
    }

    private Image MakeFill(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        StretchToFull(rt);
        rt.offsetMin = new Vector2(4f, 4f);
        rt.offsetMax = new Vector2(-4f, -4f);
        Image img = go.GetComponent<Image>();
        img.color = color;
        // A Filled-type Image without a sprite can render its full rect and ignore
        // fillAmount on this Unity version, so every fill bar gets a real 1-colour white
        // sprite (tinted via Image.color). That guarantees fillAmount affects the render.
        EnsureWhiteSprite();

        img.sprite = whiteSprite;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = 0;
        img.fillAmount = 1f;
        return img;
    }

    /// <summary>Ensures the shared 1x1 white sprite (used for all tintable Image fills) exists.</summary>
    private void EnsureWhiteSprite()
    {
        if (whiteSprite != null) return;
        whiteTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[16];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        whiteTexture.SetPixels32(px);
        whiteTexture.Apply();
        whiteTexture.wrapMode = TextureWrapMode.Clamp;
        whiteSprite = Sprite.Create(whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
    }

    /// <summary>Builds a filled white circle sprite (for the compass centre dot).</summary>
    private Sprite circleSprite;
    private Sprite MakeCircleSprite()
    {
        if (circleSprite != null) return circleSprite;
        int size = 64;
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                bool inside = (dx * dx + dy * dy) <= c * c;
                px[y * size + x] = inside ? Color.white : new Color32(0, 0, 0, 0);
            }
        }
        t.SetPixels32(px);
        t.Apply();
        t.wrapMode = TextureWrapMode.Clamp;
        circleSprite = Sprite.Create(t, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return circleSprite;
    }

    /// <summary>Builds a circular RING sprite (for the compass dial) with a transparent centre.</summary>
    private Sprite ringSprite;
    private Sprite MakeRingSprite()
    {
        if (ringSprite != null) return ringSprite;
        int size = 128;
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        float outer = c * c;
        float inner = (c * 0.52f) * (c * 0.52f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d2 = dx * dx + dy * dy;
                bool inRing = d2 <= outer && d2 >= inner;
                px[y * size + x] = inRing ? Color.white : new Color32(0, 0, 0, 0);
            }
        }
        t.SetPixels32(px);
        t.Apply();
        t.wrapMode = TextureWrapMode.Clamp;
        ringSprite = Sprite.Create(t, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return ringSprite;
    }

    /// <summary>Builds the chase-cam lens speckle: white droplets with soft edges on a
    /// transparent ground, tinted by the spray emitter's colour via the image tint.</summary>
    private Texture2D MakeBlotchTexture()
    {
        const int S = 128;
        Color[] px = new Color[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float n = 0f;
                float amp = 1f;
                float freq = 3.5f;
                float sum = 0f;
                for (int o = 0; o < 5; o++)
                {
                    n += amp * Mathf.PerlinNoise(x / (float)S * freq + o * 17.31f, y / (float)S * freq + o * 9.7f);
                    sum += amp;
                    amp *= 0.5f;
                    freq *= 1.9f;
                }
                n /= sum;
                float a = Mathf.SmoothStep(0.64f, 0.82f, n);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        }
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.name = "LensBlotch";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>Creates a compass cardinal label (N/E/S/W) at a local offset within the rose.</summary>
    private void MakeRoseLabel(Transform parent, string text, Vector2 localPos, bool highlight)
    {
        GameObject go = new GameObject("Rose_" + text, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = localPos;
        rt.sizeDelta = new Vector2(40f, 24f);
        Text t = go.GetComponent<Text>();
        AssignDefaultFont(t);
        t.fontSize = highlight ? 24 : 18;
        t.fontStyle = highlight ? FontStyle.Bold : FontStyle.Normal;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.color = highlight ? new Color(1f, 1f, 1f, 1f) : new Color(0.75f, 0.9f, 1f, 0.85f);
        t.text = text;
    }

    private Text MakeLabel(Transform parent, string name, string initial, int fontSize, Color color, Vector2 anchoredPos, TextAnchor alignment,
        UiKit.UiFontKind fontKind = UiKit.UiFontKind.Body)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();

        // Anchor the label to the corner implied by its coordinate signs: negative X is
        // right-aligned, zero X is centred, negative Y means top-anchored, positive Y means
        // bottom-anchored. Without this the labels would all sit relative to the canvas's
        // bottom-left corner and land off-screen. The pivot must match the anchor corner so
        // the label's box extends inward (on-screen) instead of hanging off an edge.
        float anchorX = anchoredPos.x < 0f ? 1f : (anchoredPos.x > 0f ? 0f : 0.5f);
        float anchorY = anchoredPos.y < 0f ? 1f : 0f;
        Vector2 anchor = new Vector2(anchorX, anchorY);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(420f, 40f);

        Text text = go.GetComponent<Text>();
        text.font = UiKit.ResolveFont(fontKind);
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        // Vertical OVERFLOW, never Truncate: tall line metrics on the bundled fonts cull
        // otherwise-fine labels to nothing inside the default 40px box (the speed readout
        // was the casualty - see the race-timer fix above for the same bug with Arial).
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = initial;
        return text;
    }

    private void StretchToFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // Unity's built-in font lookup can return null (or throw internally) depending on the
    // Editor version. A uGUI Text with a null font renders blank (invisible) even though
    // the .text string is set, so resolution lives in ONE place (UiKit), which walks the
    // bundled Rajdhani/Russo One faces down through Unity built-ins to an OS font.
    private static void AssignDefaultFont(Text text)
    {
        if (text == null) return;
        text.font = ResolveDefaultFont();
    }

    /// <summary>Resolves a usable font for uGUI Text/InputField elements (shared by the HUD and screen UI).
    /// Delegates to the shared UiKit type system (Body face).</summary>
    public static Font ResolveDefaultFont()
    {
        return UiKit.ResolveDefaultFont();
    }
}
