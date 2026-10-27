using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The course-hazard states the director cycles through. Which states exist on a
/// run depends on the planet theme: Lunar/Mars weather + rock events, Venus lava + acid rain,
/// Io lava + a molten avalanche down the Ridge crest, Titan freezing methane snow (plus
/// persistent lakes), and so on. Acid rain is VENUS-ONLY - Io channels its sulphur fire into
/// rock falls and a chasing avalanche instead.</summary>
public enum RaceHazardPhase { Calm, Boulders, Overhang, Meteors, Collapse, Lava, Rain, Avalanche }

/// <summary>
/// Course-hazard cadence for Lunar Race, driven by the planet's hazard theme.
///   Lunar: meteor barrages + canyon boulders / ridge collapses (baseline).
///   Mars : same set ramped up - heavier rock/landslide falls and denser meteor rain.
///   Venus: lava flows surge into the canyon base to avoid + acid rain.
///   Io   : the Venus theme, heavier - 3x lava surges + overhanging rock falls, and the course's
///          own rock falls still hit: boulders off the canyon walls, land slides shearing the
///          Ridge flanks. On the Ridge base an AVALANCHE of molten rock rolls in from behind
///          the buggy and chases it down the crest (acid rain stays Venus-only).
///   Titan: persistent liquid methane lakes to navigate around + freezing methane snow.
/// The director owns the alternation and drives the MeteorStorm manually (never self-schedules).
/// Course events always keep clear of the gate-arch checkpoints. Stands down when a run ends.
/// </summary>
public class RaceHazardDirector : MonoBehaviour
{
    public BuggyController buggy;
    public MoonTerrainGen terrain;
    public HUD hud;
    public OxygenManager oxygen;
    /// <summary>The manual-controlled meteor storm the director alternates with (Lunar/Mars only).</summary>
    public MeteorStorm storm;
    /// <summary>Course-Z of every gate arch, so course hazards never land on a checkpoint.</summary>
    public float[] checkpointZs = new float[0];
    /// <summary>Completed Ridge Survival legs BEFORE this deploy: every loop tightens the
    /// hazard cadence and hurls more per round. The builder sets it from GameFlow.</summary>
    public int rampLoops = 0;

    [Header("Cadence (seconds)")]
    public float firstHazardDelay = 6.5f;
    public float calmMin = 6f;
    public float calmMax = 10f;
    public float boulderCalmMin = 3f;
    public float boulderCalmMax = 6f;
    public float meteorCalmMin = 5f;
    public float meteorCalmMax = 8f;
    public float lavaCalmMin = 4f;
    public float lavaCalmMax = 7f;
    public float rainCalmMin = 5f;
    public float rainCalmMax = 9f;
    [Tooltip("Odds weight for boulder/rock falls vs meteor rounds on the Canyon (Lunar/Mars).")]
    public float boulderWeight = 4f;
    public float meteorWeight = 1f;
    [Tooltip("Odds a meteor round on the Ridge chains into another meteor barrage (Ridge).")]
    public float ridgeMeteorFollowChance = 0.55f;
    [Tooltip("Odds a lava flow is followed by another lava flow instead of the next theme round (Venus/Io).")]
    public float lavaFollowChance = 0.35f;
    [Tooltip("Minimum distance a course hazard stays away from the nearest gate (metres).")]
    public float checkpointClearance = 36f;

    [Header("Boulder / rock falls")]
    public float boulderTelegraphTime = 2.4f;
    public int boulderCount = 7;
    [Tooltip("Extra wall offset beyond the flat lane where boulders drop from (metres).")]
    public float boulderWallOffset = 26f;
    [Tooltip("Lateral push toward the far wall given as the boulder leaves the cliff (m/s).")]
    public float boulderPush = 20f;
    public float boulderRadius = 3.4f;
    [Tooltip("How long after the telegraph the boulders stay tumbling/rolling before cleanup (seconds).")]
    public float boulderLifetime = 19f;

    [Header("Overhanging rock falls (Titan / Io canyon)")]
    public float overhangTelegraphTime = 2.2f;
    public int overhangRockCount = 5;
    public float overhangRockRadius = 3.2f;
    [Tooltip("How long after the telegraph the fallen rocks stay on the lane before cleanup (seconds).")]
    public float overhangLifetime = 16f;

    [Header("Ridge collapse / landslide")]
    public float collapseTelegraphTime = 2.4f;
    [Tooltip("Half-width of the collapsing flank box; the sink shears ONE side of the ridge.")]
    public float collapseHalfWidth = 10f;
    public float collapseHalfLength = 16f;
    public float collapseDepth = 20f;
    [Tooltip("Odds a Ridge collapse is followed by a SECOND land-fall 40-80m further down the course on the OPPOSITE flank. Pairs drive the 'shear, then shear again' cadence; singles keep it varied.")]
    [Range(0f, 1f)]
    public float collapsePairChance = 0.5f;

    [Header("Lava flow (Venus / Io)")]
    public float lavaTelegraphTime = 2.0f;
    public float lavaLifetime = 8f;
    public float lavaBandLength = 14f;
    [Tooltip("Hull damage dealt each lava tick while driving through the flow.")]
    public float lavaDps = 1.15f;
    public float lavaTickInterval = 0.35f;
    [Tooltip("Share of the lane left clear at the far side of a lava flow (fraction of the half-width).")]
    public float lavaGapMin = 0.30f;
    public float lavaGapMax = 0.55f;
    public float lavaTraction = 0.55f;
    public float lavaSpeedCap = 0.6f;
    [Tooltip("Lava flow fill and glow colours (emissive orange, Io burns brighter).")]
    public Color lavaColor = new Color(1f, 0.42f, 0.08f);
    public Color lavaGlow = new Color(1f, 0.25f, 0.02f);

    [Header("Rain (acid / methane snow)")]
    public float rainDuration = 7f;
    [Tooltip("Hull damage per second while the precipitation is falling (no safe roof on the course).")]
    public float rainDamagePerSecond = 2.7f;
    public float rainWetTraction = 0.9f;

    [Header("Lava avalanche (Io Ridge)")]
    [Tooltip("Warning lead-in before the molten surge rolls in (seconds).")]
    public float avalancheTelegraphTime = 2.2f;
    [Tooltip("How long the chasing front keeps rolling after the telegraph before it ends (seconds).")]
    public float avalancheLifetime = 14f;
    [Tooltip("How far BACK the front starts behind the buggy (metres): far enough to read as a wall building up behind you, close enough to catch a dawdler.")]
    public float avalancheStartGap = 70f;
    [Tooltip("Length of the molten front along the course (metres). The whole band sweeps forward as one chase.")]
    public float avalancheBandLength = 26f;
    [Tooltip("Share of the buggy's ACTIVE max speed the front advances with (0..1): the wave paces just under stock top speed, so full throttle barely outruns it and boost always pulls away.")]
    public float avalancheChaseFactor = 0.95f;
    [Tooltip("Minimum roll speed of the front in m/s (an upgraded buggy still can't sit still and watch it come).")]
    public float avalancheMinChaseMs = 8f;
    [Tooltip("Hull damage dealt per tick while the molten wave rolls under the buggy.")]
    public float avalancheDps = 2.5f;
    public float avalancheTickInterval = 0.35f;
    [Tooltip("Grip/top-speed pressure while inside the molten front: slow enough to make non-boost escape impossible, gentle enough that a boost breaks out the front.")]
    public float avalancheTraction = 0.7f;
    public float avalancheSpeedCap = 0.75f;
    [Tooltip("Calm window before the next Io round after the wave passes (seconds).")]
    public float avalancheCalmMin = 4f;
    public float avalancheCalmMax = 7f;

    [Header("Liquid methane lakes (Titan)")]
    public int lakeCount = 3;
    public float lakeBandLength = 40f;
    [Tooltip("Share of the lane left dry at the far side of a lake (fraction of the half-width).")]
    public float lakeGapMin = 0.30f;
    public float lakeGapMax = 0.50f;
    public float lakeTraction = 0.5f;
    public float lakeSpeedCap = 0.55f;
    public float lakeDamagePerSecond = 0.8f;
    public float lakeTickInterval = 0.4f;

    [Header("Ridge ice patches (Titan)")]
    [Tooltip("How many frozen slick patches to scatter along the Ridge crest surface.")]
    public int ridgeIcePatchCount = 26;
    [Tooltip("Per-patch radius (metres).")]
    public float ridgeIcePatchMinRadius = 2.0f;
    public float ridgeIcePatchMaxRadius = 3.6f;
    [Tooltip("Minimum gap between consecutive patch centres down the course (metres).")]
    public float ridgeIceGap = 18f;
    [Tooltip("How hard an ice patch freezes the ground, 0-1. Ice does not cut grip or top speed: " +
             "it commits the buggy to the speed it arrives at, stops the wheels turning, and makes " +
             "steering and the brakes vague until it skates off. No damage.")]
    public float iceGlaze = 1f;

    [Header("Methane mud flats (Titan, all levels)")]
    [Tooltip("Muddy sections that grab the wheels: darker crumbly brown ground the buggy sinks into, moving a lot slower - 'stuck in the mud'. Canyon gets lane-spanning bands; no damage, just grip + top-speed.")]
    public int mudCount = 6;
    [Tooltip("How LONG a canyon mud band runs down the lane (metres) - Titan's are proper wallows.")]
    public float mudBandLength = 34f;
    [Tooltip("Share of the lane left clear at the far side of a mud band (fraction of the half-width).")]
    public float mudGapMin = 0.30f;
    public float mudGapMax = 0.65f;
    [Tooltip("How much mud grips the wheels: lower = more stuck.")]
    public float mudTraction = 0.35f;
    public float mudSpeedCap = 0.4f;
    [Tooltip("Mud scattered as discs along the Ridge crest (there is no lane to span up there).")]
    public int ridgeMudCount = 8;
    public float ridgeMudMinRadius = 5.0f;
    public float ridgeMudMaxRadius = 8.5f;

    [Header("Canyon ice patches (Lunar / Mars)")]
    [Tooltip("Small frozen sections scattered across the Moon/Mars canyon floor that the buggy slides over - shallow ice sheeted over regolith (no damage; the buggy holds its speed but loses the wheels, steering and brakes, like the Titan ridge patches).")]
    public int canyonIceBaseCount = 3;
    [Tooltip("Extra ice sections per race grade above grade 1, so harder grades contend with more ice.")]
    public int canyonIcePerGrade = 2;
    [Tooltip("Per-section radius at grade 1 (metres).")]
    public float canyonIceMinRadius = 8f;
    public float canyonIceMaxRadius = 12f;
    [Tooltip("Extra radius per race grade above grade 1, so harder grades' ice covers more ground (metres).")]
    public float canyonIceRadiusPerGrade = 1.2f;

    private PlanetTheme theme = PlanetTheme.None;

    private RaceHazardPhase phase = RaceHazardPhase.Calm;
    private RaceHazardPhase upcoming = RaceHazardPhase.Boulders;
    private float phaseTimer;
    private bool telegraphed;
    private float hazardStartTime;
    private BuggyHealth hull;
    private RivalRacer[] rivalCache;
    // Consecutive meteor rounds so far (Ridge): caps the chain so a landfall always interrupts.
    private int meteorChainCount;
    // Canyon rockfalls share the event pool with storms; keep both sides from dominating.
    private int boulderChainCount;

    // Boulders
    private readonly List<Transform> boulderMarks = new List<Transform>();
    private readonly List<GameObject> boulders = new List<GameObject>();
    private Transform collapseMarks;
    private Vector3 pendingCollapseCenter;
    // Land-fall wake: rivals who fell into the pit scramble out slowly while inside it.
    private Vector3 collapseSlowCenter;
    private float collapseSlowUntil = -1f;
    // Land-fall PAIRS: half the time a collapse is followed by a second shear 40-80m further
    // down the crest on the OPPOSITE flank, seeded at telegraph time and armed once the first
    // pit lands, so driving out of the first pit reads into the next.
    private Vector3 pendingPairCenter;
    private bool hasPair;
    private float pairTelegraphAt;
    private bool pairTelegraphed;
    private float pairStrikeAt;
    private bool pairTriggered;
    private Transform pairMarks;
    // Live lava surface: scrolls its texture along the band so the flow visibly pulls.
    private Material lavaFlowMat;
    private float lavaScrollAcc;

    // Lava flow (current active flow)
    private bool lavaActive;
    private LiquidZone lava;
    private float lavaAcc;

    // Io eruption: a fountain of glowing globules sprays up from the surge source and rains
    // over the lane, each blob burning the buggy on touch.
    private Coroutine lavaFountain;
    private readonly List<LavaGlobule> lavaGlobs = new List<LavaGlobule>();

    // Lava avalanche (Io Ridge): a molten front spawns behind the buggy and chases it down the
    // crest - the front (and its damage zone) roll forward each frame at a pace tied to the
    // buggy's ACTIVE max speed, so a praying-foot driver is swallowed and boost breaks free.
    private bool avalancheActive;
    private float avalancheFrontEnd;
    private float avalancheTickAcc;
    private float avalancheRumbleAcc;
    private GameObject avalancheFloorGo;

    // Rain
    private bool rainActive;
    private float rainDamageAcc;
    private Coroutine rainBurst;
    private readonly List<ActiveStreak> rainStreaks = new List<ActiveStreak>();

    // Titan methane lakes (built once, persistent all run)
    private readonly List<LiquidZone> lakes = new List<LiquidZone>();

    private sealed class ActiveStreak
    {
        public GameObject go;
        public float floor;
        /// <summary>Fall speed in m/s: rain streaks lash down fast, methane snow drifts slowly.</summary>
        public float fallSpeed = 18f;
        /// <summary>Lateral sway amplitude for snow (0 = straight-down rain).</summary>
        public float sway = 0f;
        public float phase = 0f;
    }

private sealed class LiquidZone
    {
        public float minX, maxX, zMin, zMax;
        public float traction, speedCap, dps, tick, tickAcc;
    /// <summary>How hard this zone freezes the ground, 0-1. Read by the buggy as its glaze: an
    /// icy patch does not cut grip or top speed, it commits the buggy to the speed it arrived
    /// at. Ignored by every non-icy zone, which use traction/speedCap as before.</summary>
    public float glaze;
        public bool isMud;
        public bool isIce;
        public bool isLava;
        public GameObject visual;
    }

    private void Start()
    {
        theme = CourseHazards.ThemeFor(LevelManager.CurrentLevel != null ? LevelManager.CurrentLevel.planetName : "");
        ConfigureTheme();
    hull = buggy != null ? buggy.GetComponent<BuggyHealth>() : null;
    if (buggy != null)
    {
        buggy.tractionMultiplier = 1f;
        buggy.speedCapMultiplier = 1f;
        buggy.glazeFromPatch = 0f;
    }
        if (theme == PlanetTheme.Titan)
        {
            // Titan's methane lakes + mud flats are REMOVED everywhere: they read badly and forced
            // ugly detours with no clean line. The Ridge keeps its scattered crest ice patches.
            if (GameFlow.CurrentCourse == RaceCourse.Ridge) BuildRidgeIcePatches();
        }
        // Io's standing ponds are LAVA LAKES, on every RACE course: canyon lanes span wide hot bands
        // that bubble and roast, and the ridge crest is littered with lava vents/ponds where the
        // buggy can't reach the low-level lakes. (Prospector Io builds its own volcanoes + lava
        // lakes through GameBootstrap, so the director never doubles them up here.)
        if (theme == PlanetTheme.Io && GameFlow.Mode != GameMode.Prospector)
        {
            if (GameFlow.CurrentCourse == RaceCourse.Ridge) BuildRidgeLavaPatches();
            else BuildIoLavaLakes();
        }
        // The Moon and Mars canyon floors hold small ice sections to slide over. Harder
        // grades scatter MORE and BIGGER patches (BuildCanyonIcePatches), so the faster races
        // contend with more sliding ground. (The Titan ridge keeps its own 26-patch crest
        // scatter above; the NORMAL lakes/hazards on other themes are untouched.)
        if (theme == PlanetTheme.Lunar || theme == PlanetTheme.Mars)
        {
            if (GameFlow.CurrentCourse == RaceCourse.Canyon) BuildCanyonIcePatches();
        }
        else if (theme == PlanetTheme.Titan
            && GameFlow.Mode == GameMode.Prospector
            && GameFlow.CurrentCourse == RaceCourse.Canyon)
        {
            // Titan Prospector keeps an icy challenge now its lakes are gone: the same sliding
            // canyon ice sections the Moon/Mars runs use.
            BuildCanyonIcePatches();
        }
        upcoming = FirstHazard();
        // Opening hazards count toward the canyon's two-in-a-row cap.
        meteorChainCount = upcoming == RaceHazardPhase.Meteors ? 1 : 0;
        boulderChainCount = upcoming == RaceHazardPhase.Boulders ? 1 : 0;
        phaseTimer = firstHazardDelay;
    }

    /// <summary>Applies the theme's tuning on top of the baseline knobs (Mars & Io ramp up).</summary>
    private void ConfigureTheme()
    {
        switch (theme)
        {
            case PlanetTheme.Lunar:
                if (GameFlow.Mode == GameMode.Prospector && GameFlow.CurrentCourse == RaceCourse.Canyon)
                {
                    // Prospector keeps a deliberate mix of meteor barrages and wall rockfalls.
                    boulderWeight = 3f;
                    meteorWeight = 3f;
                    break;
                }
                // The Moon canyon leans on meteor barrage rounds as much as its wall boulder
                // falls: the stock 4:1 boulder:meteor roll buries the storms (barely a fifth
                // of rounds), so rebalance the canyon pool to make the meteor field a real,
                // frequent part of the ride. (The Ridge already alternates its own collapse /
                // meteor rounds and never reads these weights.)
                if (GameFlow.CurrentCourse == RaceCourse.Canyon)
                {
                    boulderWeight = 3f;
                    meteorWeight = 3f;
                }
                break;
            case PlanetTheme.Mars:
                boulderRadius = 4.1f;
                boulderCount = 9;
                boulderPush = 23f;
                boulderLifetime = 20f;
                boulderCalmMin = 2.5f;
                boulderCalmMax = 5f;
                collapseHalfWidth = 12f;
                collapseHalfLength = 18f;
                collapseDepth = 26f;
                firstHazardDelay = 5.5f;
                if (GameFlow.Mode == GameMode.Prospector && GameFlow.CurrentCourse == RaceCourse.Canyon)
                {
                    // Keep Mars storms frequent enough to share the canyon with rockfalls.
                    boulderWeight = 3f;
                    meteorWeight = 2f;
                }
                break;
            case PlanetTheme.Io:
                if (GameFlow.Mode == GameMode.Prospector && GameFlow.CurrentCourse == RaceCourse.Canyon)
                {
                    // Prospector Io: canyon wall rockfalls share the schedule with the meteor
                    // storm (the theme's own lava is a standing hazard, not a director round).
                    ApplyProspectorCanyonBoulders();
                    firstHazardDelay = 5f;
                    break;
                }
                // Triple threat: surges are longer, hotter, wider and chain together far more
                // often, and each surge erupts a dense fountain of globules (see LavaFountain).
                // Acid rain does NOT fall on Io - its sulphur fire becomes the Ridge's chasing
                // lava avalanche instead (see RunAvalanche).
                lavaDps = 2.0f;
                lavaLifetime = 10f;
                lavaBandLength = 18f;
                lavaGapMin = 0.18f;
                lavaGapMax = 0.42f;
                lavaFollowChance = 0.6f;
                lavaCalmMin = 1.8f;
                lavaCalmMax = 3.6f;
                firstHazardDelay = 5f;
                lavaColor = new Color(1f, 0.32f, 0.05f);
                lavaGlow = new Color(1f, 0.16f, 0f);
                break;
            case PlanetTheme.Titan:
                if (GameFlow.Mode == GameMode.Prospector && GameFlow.CurrentCourse == RaceCourse.Canyon)
                {
                    // Prospector Titan: canyon wall rockfalls share the schedule with the meteor
                    // storm (methane snow + geysers stay as standing hazards).
                    ApplyProspectorCanyonBoulders();
                    firstHazardDelay = 6f;
                    break;
                }
                // A heavy, biting methane blizzard: 4x the acid runs' burn (and far harder than
                // the early builds' 0.75) - riding through the snow fast is the only way out.
                rainDamagePerSecond = 3.0f;
                rainDuration = 7f;
                firstHazardDelay = 7f;
                // Titan's "land falls" (landslides into the canyon) sink HALF as deep as the
                // other worlds' collapses - the methane mud gives way but far less violently.
                collapseDepth = 10f;
                // Canyon carries the lake hazard (many icy methane sheets across the lane);
                // the Ridge gets far fewer lakes (they sit below the crest) but scatters many
                // ice patches down the crest surface instead (see BuildRidgeIcePatches).
                lakeCount = GameFlow.CurrentCourse == RaceCourse.Canyon ? 7 : 2;
                break;
        }
        // Ridge Survival ramps after every completed leg: the theme defaults above are the
        // baseline, and the per-leg ramp tightens them for the next deploy.
        if (rampLoops > 0) ApplyRamp(rampLoops);
    }

    /// <summary>Prospector canyon rockfall / storm tuning: wall boulders and meteor rounds share
    /// one schedule, so avoiding rock is a real threat on every world that runs the director.</summary>
    private void ApplyProspectorCanyonBoulders()
    {
        boulderWeight = 3f;
        meteorWeight = 2f;
        boulderCount = 9;
        boulderRadius = 4.1f;
        boulderPush = 23f;
        boulderLifetime = 20f;
        boulderCalmMin = 2.5f;
        boulderCalmMax = 5f;
    }

    /// <summary>Ramps hazard frequency/intensity after each completed Ridge Survival leg:
    /// tighter calm windows between rounds, shorter telegraphs and more boulders per round,
    /// without ever bottoming out to instant death. Called AFTER the theme defaults so both
    /// the world's personality AND the leg count stay on the knobs.</summary>
    private void ApplyRamp(int loops)
    {
        float f = 1f + 0.30f * loops;   // cadence: calm windows, opening delay
        float fe = 1f + 0.18f * loops;  // intensity: telegraphs, per-round counts
        firstHazardDelay = Mathf.Max(3f, firstHazardDelay / f);
        calmMin = Mathf.Max(2f, calmMin / f);
        calmMax = Mathf.Max(3f, calmMax / f);
        boulderCalmMin = Mathf.Max(1.2f, boulderCalmMin / fe);
        boulderCalmMax = Mathf.Max(1.8f, boulderCalmMax / fe);
        meteorCalmMin = Mathf.Max(1.2f, meteorCalmMin / fe);
        meteorCalmMax = Mathf.Max(1.8f, meteorCalmMax / fe);
        lavaCalmMin = Mathf.Max(1f, lavaCalmMin / fe);
        lavaCalmMax = Mathf.Max(1.5f, lavaCalmMax / fe);
        avalancheTelegraphTime = Mathf.Max(1.2f, avalancheTelegraphTime / fe);
        avalancheChaseFactor = Mathf.Min(1.6f, avalancheChaseFactor + 0.05f * loops);
        avalancheMinChaseMs = Mathf.Min(13f, avalancheMinChaseMs + 0.6f * loops);
        avalancheCalmMin = Mathf.Max(1.2f, avalancheCalmMin / fe);
        avalancheCalmMax = Mathf.Max(1.8f, avalancheCalmMax / fe);
        rainCalmMin = Mathf.Max(1.2f, rainCalmMin / fe);
        rainCalmMax = Mathf.Max(1.8f, rainCalmMax / fe);
        collapseTelegraphTime = Mathf.Max(1.2f, collapseTelegraphTime / fe);
        boulderTelegraphTime = Mathf.Max(1.2f, boulderTelegraphTime / fe);
        overhangTelegraphTime = Mathf.Max(1.1f, overhangTelegraphTime / fe);
        boulderCount = Mathf.Min(14, boulderCount + 2 * loops);
        // Land falls grow on later legs: a wider shear, a longer reach down the crest and a
        // deeper pit every cleared leg (capped so late legs stay fair but demanding).
        collapseHalfWidth = Mathf.Min(16f, collapseHalfWidth * (1f + 0.12f * loops));
        collapseHalfLength = Mathf.Min(26f, collapseHalfLength * (1f + 0.12f * loops));
        collapseDepth = Mathf.Min(40f, collapseDepth * (1f + 0.18f * loops));
    }

    private RaceHazardPhase FirstHazard()
    {
        // PROSPECTOR RUN: the opening hazard comes from the canyon boulder/meteor pool on every
        // world (Ridge legs never run the director for Prospector).
        if (GameFlow.Mode == GameMode.Prospector && GameFlow.CurrentCourse == RaceCourse.Canyon)
        {
            return RollCanyonWeatherWithStreakCaps();
        }
        switch (theme)
        {
            case PlanetTheme.Venus:
                return RollVenusHazards();
            case PlanetTheme.Io:
                // Io opens straight into a lava surge - no gentle warm-up on the most volcanic
                // world - and thereafter mixes in the course rock falls (see RollIoHazards).
                return RaceHazardPhase.Lava;
            case PlanetTheme.Titan:
                // Titan canyon can open with a shallow landslide, overhead rock falls, or snow.
                return GameFlow.CurrentCourse == RaceCourse.Canyon
                    ? RollTitanCanyonHazards()
                    : RaceHazardPhase.Rain;
            case PlanetTheme.Mars:
            case PlanetTheme.Lunar:
            default:
                if (GameFlow.CurrentCourse == RaceCourse.Ridge)
                {
                    bool firstMeteors = Random.value < 0.5f;
                    meteorChainCount = firstMeteors ? 1 : 0;
                    return firstMeteors ? RaceHazardPhase.Meteors : RaceHazardPhase.Collapse;
                }
                return RollCanyonWeather();
        }
    }

    /// <summary>Titan's canyon pool: shallow mud landslides, overhead rocks and methane snow
    /// share the run; the Ridge stays on snow (its crest ice patches carry the variety there).</summary>
    private RaceHazardPhase RollTitanCanyonHazards()
    {
        const float landW = 3f, rockW = 2f, snowW = 2f;
        float r = Random.value * (landW + rockW + snowW);
        if (r < landW) return RaceHazardPhase.Collapse;
        if (r < landW + rockW) return RaceHazardPhase.Overhang;
        return RaceHazardPhase.Rain;
    }

    private RaceHazardPhase RollCanyonWeather()
    {
        float total = boulderWeight + meteorWeight;
        return Random.value * total < boulderWeight ? RaceHazardPhase.Boulders : RaceHazardPhase.Meteors;
    }

    private RaceHazardPhase RollLiquid()
    {
        float total = boulderWeight + meteorWeight; // reuse the weights: lava = "boulder", rain = "meteor"
        return Random.value * total < boulderWeight ? RaceHazardPhase.Lava : RaceHazardPhase.Rain;
    }

    /// <summary>Venus's hazard pool: lava and acid rain on every course, and boulder falls
    /// rolling off the canyon walls too (the Ridge keeps the pure weather mix).</summary>
    private RaceHazardPhase RollVenusHazards()
    {
        if (GameFlow.CurrentCourse != RaceCourse.Canyon) return RollLiquid();
        const float lavaW = 4f, rockW = 2f, rainW = 1f;
        float r = Random.value * (lavaW + rockW + rainW);
        if (r < lavaW) return RaceHazardPhase.Lava;
        if (r < lavaW + rockW) return RaceHazardPhase.Boulders;
        return RaceHazardPhase.Rain;
    }

    /// <summary>Io's full hazard pool: heavy lava surges lead, but the course's own rock falls
    /// hit too - overhanging rocks raining straight down onto the canyon lane, land slides
    /// shearing the Ridge flanks. Acid rain never falls on Io: the CANYON trades its old rain
    /// round for rock falls, and on the RIDGE the weather slot is a molten avalanche rolling
    /// in from behind the buggy (see RunAvalanche).</summary>
    private RaceHazardPhase RollIoHazards()
    {
        bool ridge = GameFlow.CurrentCourse == RaceCourse.Ridge;
        const float lavaW = 5f;
        if (ridge)
        {
            // Ridge base: lava surges lead, land slides shear the flanks, and a molten
            // avalanche chases the buggy down the crest instead of the old acid rain. The
            // avalanche is a real share of the ride - roughly a third of full-pool rolls.
            const float collapseW = 2f, avaunchW = 3f;
            float r = Random.value * (lavaW + collapseW + avaunchW);
            if (r < lavaW) return RaceHazardPhase.Lava;
            if (r < lavaW + collapseW) return RaceHazardPhase.Collapse;
            return RaceHazardPhase.Avalanche;
        }
        const float rockW = 2f;
        return Random.value * (lavaW + rockW) < lavaW ? RaceHazardPhase.Lava : RaceHazardPhase.Overhang;
    }

    private void Update()
    {
        if (buggy == null || terrain == null) return;
        if (!buggy.inputEnabled) return;
        if (oxygen == null) oxygen = OxygenManager.Instance;

        if (oxygen != null && (oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive))
        {
            Freeze();
            return;
        }

        // Persistent surface pressure: lava flow, lakes and rain all affect the buggy live.
        ApplySurfaceZones();
        AnimateLava();
        if (rainActive)
        {
            ApplyRainDamage();
            MoveRainStreaks();
        }

        if (phase == RaceHazardPhase.Calm)
        {
            phaseTimer -= Time.deltaTime;
            if (phaseTimer <= 0f) BeginHazard(upcoming);
        }
        else
        {
            RunHazard();
        }
    }

    private void BeginHazard(RaceHazardPhase which)
    {
        phase = which;
        telegraphed = false;
        hazardStartTime = Time.time;
        phaseTimer = 0f;

        switch (which)
        {
            case RaceHazardPhase.Boulders: BuildBoulderTelegraph(); break;
            case RaceHazardPhase.Overhang: BuildOverhangTelegraph(); break;
            case RaceHazardPhase.Collapse: BuildCollapseTelegraph(); break;
            case RaceHazardPhase.Lava: BuildLavaTelegraph(); break;
            case RaceHazardPhase.Rain: BeginRain(); break;
            case RaceHazardPhase.Avalanche: BeginAvalancheTelegraph(); break;
            case RaceHazardPhase.Meteors: if (storm != null) storm.BeginStorm(); break;
        }
    }

    private void RunHazard()
    {
        switch (phase)
        {
            case RaceHazardPhase.Boulders: RunBoulders(); break;
            case RaceHazardPhase.Overhang: RunOverhang(); break;
            case RaceHazardPhase.Meteors: RunMeteors(); break;
            case RaceHazardPhase.Collapse: RunCollapse(); break;
            case RaceHazardPhase.Lava: RunLava(); break;
            case RaceHazardPhase.Rain: RunRain(); break;
            case RaceHazardPhase.Avalanche: RunAvalanche(); break;
        }
    }

    // ------------------------------------------------------------------ meteors
    private void RunMeteors()
    {
        if (storm == null || !storm.BarrageActive) EndHazard();
    }

    // ------------------------------------------------------------------ boulders (Canyon)
    private void BuildBoulderTelegraph()
    {
        if (hud != null) hud.ShowWarning(BoulderLabel());
        RoverSounds.PlayStormAlert();
        int marksCount = 5;
        for (int i = 0; i < marksCount; i++)
        {
            Transform m = BuildMark(GetBoulderDropPoint(i), true);
            if (m != null) boulderMarks.Add(m);
        }
    }

    private Vector3 GetBoulderDropPoint(int index)
    {
        Vector3 b = buggy.transform.position;
        float z = b.z + 20f + index * 9f;
        int side = (index % 4 < 2) ? 1 : -1; // alternate walls
        float cx = terrain.RaceCenterX(z);
        float x = cx + side * (RaceHalfWidth() + boulderWallOffset);
        z = Mathf.Clamp(z, MoonTerrainGen.RaceStartZ + 20f, terrain.RaceFinishZ - 30f);
        return new Vector3(x, 0f, z);
    }

    private void RunBoulders()
    {
        if (!telegraphed && Time.time - hazardStartTime >= boulderTelegraphTime)
        {
            telegraphed = true;
            ClearMarks();
            for (int i = 0; i < boulderCount; i++)
            {
                SpawnTumblingBoulder(GetBoulderDropPoint(i + 1));
            }
            // The rocks break off the wall: play the fall where they land in the lane.
            Vector3 fallCue = GetBoulderDropPoint(1);
            fallCue.y = terrain.SampleHeight(fallCue);
            RoverSounds.PlayRockfall(fallCue, 1f);
        }

        if (telegraphed && Time.time - hazardStartTime >= boulderTelegraphTime + boulderLifetime)
        {
            CleanupBoulders(true);
            EndHazard();
        }
    }

    private void SpawnTumblingBoulder(Vector3 drop)
    {
        drop.y = terrain.SampleHeight(drop);
        if (drop.y <= 0f) return;

        LevelDefinition def = LevelManager.CurrentLevel;
        string planetKey = def != null ? def.planetName : "default";
        Color regolith = def != null ? def.regolithColor : new Color(0.60f, 0.60f, 0.63f);

        GameObject go = new GameObject("HazardBoulder_" + boulders.Count);
        go.transform.position = new Vector3(drop.x, drop.y + boulderRadius * 0.5f, drop.z);
        go.transform.rotation = Random.rotation;

        Material mat = Yfcm.World(planetKey, Yfcm.WorldTint(planetKey, regolith, 0.85f), new Vector2(2f, 2f), 0.08f, true);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            mat.mainTexture = RockArt.TextureFor(regolith, planetKey);
            mat.color = new Color(0.85f, 0.85f, 0.82f, 1f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Glossiness", 0.08f);
        }
        RockArt.BuildRockShape(go.transform, boulderRadius, mat, boulders.Count);
        Boulder marker = go.AddComponent<Boulder>();
        marker.raceHazard = true;   // dodging this rock can pay out an adrenaline surge
        marker.radius = boulderRadius;
        Boulder.MakeSlick(go); // wheels rebound off the boulder instead of sticking

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.6f;
        rb.AddTorque(Random.onUnitSphere * 70f, ForceMode.VelocityChange);

        // Explosive launch: hard lateral push, upward pop and a down-course kick so the rock
        // arcs out, lands deep in the lane and keeps rolling across.
        float cx = terrain.RaceCenterX(drop.z);
        float dir = drop.x > cx ? 1f : -1f;
        Vector3 push = new Vector3(-dir * boulderPush, Random.Range(1.5f, 6f), Random.Range(3f, 8f));
        rb.linearVelocity = push;

        boulders.Add(go);
    }

    // ------------------------------------------------------------------ overhanging rock falls (Titan / Io canyon)
    // Rocks dropped from the canyon rim/overhang STRAIGHT DOWN onto the lane (unlike the wall
    // boulders that launch across). Telegraphed with marks ON the lane where they will hit.
    private void BuildOverhangTelegraph()
    {
        if (hud != null) hud.ShowWarning(OverhangLabel());
        RoverSounds.PlayStormAlert();
        for (int i = 0; i < overhangRockCount; i++)
        {
            Transform m = BuildMark(GetOverhangDropPoint(i), true);
            if (m != null) boulderMarks.Add(m);
        }
    }

    private Vector3 GetOverhangDropPoint(int index)
    {
        Vector3 b = buggy.transform.position;
        float z = b.z + 18f + index * 8f;
        z = Mathf.Clamp(z, MoonTerrainGen.RaceStartZ + 20f, terrain.RaceFinishZ - 30f);
        float cx = terrain.RaceCenterX(z);
        // Rocks fall across the lane rather than at the walls; small alternating offsets.
        int side = (index % 3 == 2) ? 1 : (index % 3 == 1 ? -1 : 0);
        float x = cx + side * RaceHalfWidth() * 0.45f;
        return new Vector3(x, 0f, z);
    }

    private void RunOverhang()
    {
        if (!telegraphed && Time.time - hazardStartTime >= overhangTelegraphTime)
        {
            telegraphed = true;
            ClearMarks();
            for (int i = 0; i < overhangRockCount; i++)
            {
                SpawnOverhangRock(GetOverhangDropPoint(i + 1));
            }
            // Rocks shed from the overhang: play the fall where they land in the lane.
            Vector3 fallCue = GetOverhangDropPoint(1);
            fallCue.y = terrain.SampleHeight(fallCue);
            RoverSounds.PlayRockfall(fallCue, 1f);
        }

        if (telegraphed && Time.time - hazardStartTime >= overhangTelegraphTime + overhangLifetime)
        {
            CleanupBoulders(true);
            EndHazard();
        }
    }

    private void SpawnOverhangRock(Vector3 drop)
    {
        float groundY = terrain.SampleHeight(drop);
        if (groundY <= 0f) return;

        LevelDefinition def = LevelManager.CurrentLevel;
        string planetKey = def != null ? def.planetName : "default";
        Color regolith = def != null ? def.regolithColor : new Color(0.60f, 0.60f, 0.63f);

        // Drop height scales with the canyon's own wall height so the fall is always "overhead".
        float wall = def != null ? def.raceWallHeight : 40f;
        float fallHeight = Mathf.Clamp(wall, 30f, 100f);
        float size = overhangRockRadius * Random.Range(0.85f, 1.2f);

        GameObject go = new GameObject("OverhangRock_" + boulders.Count);
        go.transform.position = new Vector3(drop.x, groundY + fallHeight, drop.z);
        go.transform.rotation = Random.rotation;

        Material mat = Yfcm.World(planetKey, Yfcm.WorldTint(planetKey, regolith, 0.85f), new Vector2(2f, 2f), 0.08f, true);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            mat.mainTexture = RockArt.TextureFor(regolith, planetKey);
            mat.color = new Color(0.85f, 0.85f, 0.82f, 1f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Glossiness", 0.08f);
        }
        RockArt.BuildRockShape(go.transform, size, mat, boulders.Count);
        Boulder marker = go.AddComponent<Boulder>();
        marker.raceHazard = true;   // dodging this rock can pay out an adrenaline surge
        marker.radius = size;
        Boulder.MakeSlick(go);

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.linearDamping = 0.4f;
        rb.angularDamping = 0.5f;
        // Drop straight down hard; the cushion of the canyon floor stops them fast.
        rb.linearVelocity = new Vector3(Random.Range(-1f, 1f), -6f, Random.Range(-1f, 1f));
        rb.AddTorque(Random.onUnitSphere * 40f, ForceMode.VelocityChange);

        boulders.Add(go);
    }

    private string OverhangLabel() => "OVERHANG ROCK FALL INBOUND";

    // ------------------------------------------------------------------ collapse / landslide (Ridge)
    private void BuildCollapseTelegraph()
    {
        if (hud != null) hud.ShowWarning(CollapseLabel());
        RoverSounds.PlayStormAlert();
        pendingCollapseCenter = GetCollapseCenter();
        collapseMarks = BuildMark(pendingCollapseCenter, false);

        // Half the time the shear comes in a PAIR: a second land-fall follows 40-80m further
        // down the crest on the OPPOSITE flank, so clearing the first pit reads into the next.
        // Pull back to a single if the finish line crowds the second (no room to sink it).
        hasPair = Random.value < collapsePairChance;
        if (hasPair)
        {
            float sideSign = Mathf.Sign(pendingCollapseCenter.x - terrain.RaceCenterX(pendingCollapseCenter.z));
            float pairZ = pendingCollapseCenter.z + Random.Range(40f, 80f);
            pairZ = AvoidAnchors(pairZ, checkpointZs, checkpointClearance);
            pairZ = Mathf.Clamp(pairZ, MoonTerrainGen.RaceStartZ + 30f, terrain.RaceFinishZ - 40f);
            if (pairZ < pendingCollapseCenter.z + 32f)
            {
                hasPair = false; // shoehorned into the first pit - keep it a single shear
            }
            else
            {
                pendingPairCenter = new Vector3(
                    terrain.RaceCenterX(pairZ) - sideSign * collapseHalfWidth, 0f, pairZ);
                pairTelegraphAt = -1f; // armed once the first pit lands (RunCollapse)
                pairStrikeAt = 0f;
                pairTelegraphed = false;
                pairTriggered = false;
            }
        }
    }

    private Vector3 GetCollapseCenter()
    {
        Vector3 b = buggy.transform.position;
        // The landfall sinks over ~1.3s, so it seeds ahead of the buggy so the pit reads as
        // it's approached. How far ahead scales gently with the buggy's upgraded Engine/Boost
        // top speed - at only 1.3x the telegraph reach, upgraded buggies still get a fair dodge
        // without the pit running away past their headlight range (min 32m so the pit always
        // reads as something the driver approaches, never spawns under the bumper).
        float z = b.z + Mathf.Max(32f, buggy.ReachDistance(collapseTelegraphTime, 1.3f));
        z = AvoidAnchors(z, checkpointZs, checkpointClearance);
        z = Mathf.Clamp(z, MoonTerrainGen.RaceStartZ + 30f, terrain.RaceFinishZ - 40f);
        float cx = terrain.RaceCenterX(z);
        // Shear ONE flank: the sink box runs from the centre line out over a whole side, so
        // that flank and the lane on its half collapse while the far half stays drivable.
        // Bias the collapse to the INSIDE of the crest's corner - the shortest line the buggy
        // would take - so the driver must swing wide around the outside line. The ridge weave
        // is cx = A*sin(2πz/λ): the curvature toward +x is -sin(2πz/λ), so the inside flank is
        // that sign. Mostly (but not always) the collapse eats that flank, keeping it a hazard
        // you read ahead rather than a fully scripted route. Near a weave inflection the sine
        // is ~0 and the crest is ~straight, so pick a side at random instead of centring the
        // pit on the whole lane.
        float wave = Mathf.Sin(z / terrain.raceWeaveWavelength * Mathf.PI * 2f);
        float inside = Mathf.Abs(wave) < 0.08f
            ? (Random.value < 0.5f ? 1f : -1f)
            : -Mathf.Sign(wave);
        float side = Random.value < 0.7f ? inside : -inside;
        return new Vector3(cx + side * collapseHalfWidth, 0f, z);
    }

    private void RunCollapse()
    {
        // FIRST shear: telegraph, then sink the flank.
        if (!telegraphed && Time.time - hazardStartTime >= collapseTelegraphTime)
        {
            telegraphed = true;
            if (collapseMarks != null) Destroy(collapseMarks.gameObject);
            TriggerCollapse();
            // A pair? Arm the second telegraph ~2s after the first lands so it follows on as
            // the driver climbs out of the first pit, not as a simultaneous double shear.
            if (hasPair && pairTelegraphAt < 0f)
                pairTelegraphAt = Time.time + Random.Range(1.6f, 2.6f);
        }

        // SECOND shear of a pair: telegraph, then sink the opposite flank.
        if (hasPair && !pairTelegraphed && pairTelegraphAt > 0f && Time.time >= pairTelegraphAt)
        {
            pairTelegraphed = true;
            if (hud != null) hud.ShowWarning(CollapseLabel());
            pairMarks = BuildMark(pendingPairCenter, false);
            pairStrikeAt = Time.time + collapseTelegraphTime;
        }
        if (pairTelegraphed && !pairTriggered && Time.time >= pairStrikeAt)
        {
            pairTriggered = true;
            if (pairMarks != null) Destroy(pairMarks.gameObject);
            TriggerCollapseAt(pendingPairCenter);
        }

        // Either land-fall, once it has fully sunk, clears the round forward. A pair holds the
        // round open until its own second shear has also finished sinking.
        bool firstOwnDone = telegraphed && Time.time - hazardStartTime >= collapseTelegraphTime + 6f;
        if (hasPair || pairTriggered)
        {
            if (pairTriggered && Time.time >= pairStrikeAt + 6f) EndHazard();
        }
        else if (firstOwnDone)
        {
            EndHazard();
        }
    }

    private void TriggerCollapse()
    {
        TriggerCollapseAt(pendingCollapseCenter);
    }

    private void TriggerCollapseAt(Vector3 c)
    {
        if (c == Vector3.zero) c = GetCollapseCenter();
        terrain.StartProgressiveCollapse(new Vector2(c.x, c.z), collapseHalfWidth, collapseHalfLength, collapseDepth);
        // Land-fall wake: rivals that fell into the pit stay bogged while they climb out.
        collapseSlowCenter = c;
        collapseSlowUntil = Time.time + 7f;
        if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(1f);
        // A land-fall is shearing rock, not a meteor blast: use the rock-fall sample here too.
        RoverSounds.PlayRockfall(c, 1f);
    }

    // ------------------------------------------------------------------ lava flow (Venus / Io)
    private void BuildLavaTelegraph()
    {
        if (hud != null) hud.ShowWarning(LavaLabel());
        RoverSounds.PlayStormAlert();
        Vector3 b = buggy.transform.position;
        float z = b.z + 32f;
        z = AvoidAnchors(z, checkpointZs, lavaBandLength * 0.5f + 10f);
        z = Mathf.Clamp(z, MoonTerrainGen.RaceStartZ + 30f, terrain.RaceFinishZ - 40f);
        lava = BuildZone(z, lavaBandLength, lavaGapMin, lavaGapMax,
            lavaTraction, lavaSpeedCap, lavaDps, lavaTickInterval);
        Transform mark = BuildMark(new Vector3((lava.minX + lava.maxX) * 0.5f, 0f, z), false);
        if (mark != null) boulderMarks.Add(mark); // reuse the free list for the flow mark
    }

    private void RunLava()
    {
        if (!telegraphed && Time.time - hazardStartTime >= lavaTelegraphTime)
        {
            telegraphed = true;
            ClearMarks();
            IgniteLava(lava);
            RoverSounds.PlayMeteorImpact(new Vector3((lava.minX + lava.maxX) * 0.5f, 0f, (lava.zMin + lava.zMax) * 0.5f), 1f);
            if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.7f);
        }

        if (lavaActive && Time.time - hazardStartTime >= lavaTelegraphTime + lavaLifetime)
        {
            ExtinguishLava();
            EndHazard();
        }
    }

    private void IgniteLava(LiquidZone zone)
    {
        lavaActive = true;
        BuildLiquidVisual(zone, true);

        // Io surges erupt: a central vent throws globules into the air over the whole surge.
        if (theme == PlanetTheme.Io)
        {
            StopFountain();
            Vector3 src = new Vector3((zone.minX + zone.maxX) * 0.5f, 0f, (zone.zMin + zone.zMax) * 0.5f);
            float groundY = terrain != null ? terrain.SampleHeight(src) : 0f;
            lavaFountain = StartCoroutine(LavaFountain(new Vector3(src.x, groundY, src.z)));
        }
    }

    private void ExtinguishLava()
    {
        lavaActive = false;
        StopFountain();
        if (lava != null && lava.visual != null) Destroy(lava.visual);
        lava = null;
    }

    private void StopFountain()
    {
        if (lavaFountain != null)
        {
            StopCoroutine(lavaFountain);
            lavaFountain = null;
        }
        for (int i = lavaGlobs.Count - 1; i >= 0; i--)
        {
            if (lavaGlobs[i] != null) Destroy(lavaGlobs[i].gameObject);
        }
        lavaGlobs.Clear();
    }

    private IEnumerator LavaFountain(Vector3 source)
    {
        // Continuous spray for the surge lifetime: blobs pitch up off the vent source, arc
        // across the lane, and splash on the ground (each can catch the buggy en route). Io
        // erupts ~3x denser - more blobs per pulse, hotter/faster launches and a wider spread.
        bool io = theme == PlanetTheme.Io;
        int perPulse = io ? 8 : 3;
        float pulseIn = io ? 0.18f : 0.28f;
        float zSpread = io ? 9f : 2.5f;
        float xSpread = io ? 3f : 2.5f;
        while (lavaActive)
        {
            for (int k = 0; k < perPulse; k++)
            {
                Vector3 p = source + new Vector3(Random.Range(-xSpread, xSpread),
                            Random.Range(0.2f, 1.2f), Random.Range(-zSpread, zSpread));
                SpawnLavaGlobule(p);
            }
            yield return new WaitForSeconds(pulseIn);
        }
    }

    private void SpawnLavaGlobule(Vector3 origin)
    {
        if (lavaGlobs.Count > 80) return; // hard ceiling so globule piles never snowball
        bool io = theme == PlanetTheme.Io;
        LavaGlobule glob = LavaGlobule.Spawn(origin,
            io ? 0.7f : 0.5f, io ? 1.5f : 1.1f,
            io ? 8f : 7f, io ? 14f : 10f,
            lavaColor, lavaGlow, Random.Range(2f, 4f) * (io ? 1.3f : 1f), 1f,
            io ? LavaArt.MoltenFx() : null);
        lavaGlobs.Add(glob);
    }

    // ------------------------------------------------------------------ lava avalanche (Io Ridge)
    // Io keeps no acid storms: instead a molten front rears up BEHIND the buggy on the crest
    // and surges forward, sweeping the whole ridge width. It paces just under the buggy's own
    // top speed, so full throttle barely holds it off and boost pulls clear - but a lift-off in
    // a canister grab, an eclipse-frosted section or a collapse detour lets it roll in and
    // roast you until the wave's lifetime burns out. The leading edge pins just ahead of a
    // caught buggy (you're swallowed at the front, not stranded behind it).
    private void BeginAvalancheTelegraph()
    {
        if (hud != null) hud.ShowWarning(AvalancheLabel());
        RoverSounds.PlayStormAlert();
        if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.5f);
    }

    private void RunAvalanche()
    {
        if (!telegraphed && Time.time - hazardStartTime >= avalancheTelegraphTime)
        {
            telegraphed = true;
            SpawnAvalanche();
        }

        if (!avalancheActive) return;
        AdvanceAvalanche();

        if (Time.time - hazardStartTime >= avalancheTelegraphTime + avalancheLifetime)
        {
            ExtinguishAvalanche();
            EndHazard();
        }
    }

    private void SpawnAvalanche()
    {
        if (buggy == null || terrain == null) return;
        float z = buggy.transform.position.z - avalancheStartGap;
        z = Mathf.Clamp(z, MoonTerrainGen.RaceStartZ + 5f, terrain.RaceFinishZ - 10f);
        avalancheFrontEnd = z;
        avalancheTickAcc = 0f;
        avalancheActive = true;

        float width = (RaceHalfWidth() + 6f) * 2f;
        avalancheFloorGo = BuildAvalancheBand(width, avalancheBandLength);
        PositionAvalancheVisuals();
    }

    /// <summary>Builds the lava avalanche as a sliding band of molten rock chunks instead of a
    /// flat orange slab: a brown basalt crust carrying the planet's mottled rock texture, ringed
    /// with thin glowing red/orange ember edges (leading face, trailing face and both flanks) and
    /// split by a couple of molten cracks running along the run. All flare, no colliders.</summary>
    private GameObject BuildAvalancheBand(float width, float length)
    {
        GameObject band = new GameObject("LavaAvalanche");
        float half = width * 0.5f;

        // Crust: a low slab painted with the shared regolith-mottled rock texture so it reads
        // as tumbling basalt, not a sticker. Sits just proud of the terrain (see Position...).
        GameObject crust = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crust.name = "BasaltCrust";
        crust.transform.SetParent(band.transform, false);
        crust.transform.localPosition = new Vector3(0f, 0f, 0f);
        crust.transform.localScale = new Vector3(width, 0.9f, length);
        Material crustMat = LavaArt.Melt(new Color(0.85f, 0.72f, 0.6f), new Vector2(4f, 3f), 0.1f,
            new Color(1.5f, 1f, 0.5f));
        if (crustMat == null)
        {
            crustMat = new Material(Shader.Find("Standard"));
            crustMat.mainTexture = RockArt.TextureFor(new Color(0.32f, 0.18f, 0.10f), "IoAvalanche");
            crustMat.mainTextureScale = new Vector2(4f, 3f);
            crustMat.color = new Color(0.75f, 0.62f, 0.5f, 1f);
            crustMat.SetFloat("_Metallic", 0f);
            crustMat.SetFloat("_Glossiness", 0.1f);
        }
        crust.GetComponent<MeshRenderer>().sharedMaterial = crustMat;
        DestroyImmediate(crust.GetComponent<Collider>());

        // Two molten cracks slicing the crust along the run.
        BuildAvalancheCrack(band.transform, length, half * 0.35f, 0.55f, 0.32f);
        BuildAvalancheCrack(band.transform, length, -half * 0.25f, 0.62f, 0.26f);

        // Glowing ember edges: thin bright strips on the leading face, trailing face and both
        // flanks so the brown rock band glows hot around its rim (no tall yellow wall block).
        BuildAvalancheEdge(band.transform, width, 0.7f, 0.7f, new Vector3(0f, 0.3f, half), true);   // leading
        BuildAvalancheEdge(band.transform, width, 0.7f, 0.7f, new Vector3(0f, 0.3f, -half), true);  // trailing
        BuildAvalancheEdge(band.transform, 1f, length, 0.7f, new Vector3(half, 0.3f, 0f), false);   // right flank
        BuildAvalancheEdge(band.transform, 1f, length, 0.7f, new Vector3(-half, 0.3f, 0f), false);  // left flank
        return band;
    }

    /// <summary>A thin glowing red/orange frame strip along one rim of the avalanche band - the
    /// hot edge of the molten front (brightest on the leading face). No colliders.
    /// <paramref name="alongWidth"/> puts the long axis on X, otherwise on Z.</summary>
    private void BuildAvalancheEdge(Transform parent, float wide, float long_, float thick, Vector3 pos, bool alongWidth)
    {
        GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        edge.name = "EmberEdge";
        edge.transform.SetParent(parent, false);
        edge.transform.localPosition = pos;
        Vector3 s = alongWidth ? new Vector3(wide, thick, 0.7f) : new Vector3(0.7f, thick, long_);
        edge.transform.localScale = s;
        Material m = new Material(Shader.Find("Standard"));
        m.color = new Color(1f, 0.34f, 0.04f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(1f, 0.42f, 0.05f) * 1.6f);
        edge.GetComponent<MeshRenderer>().sharedMaterial = m;
        DestroyImmediate(edge.GetComponent<Collider>());
    }

    /// <summary>A winding seam of bright molten rock running along the crust (a couple of
    /// glowing boxes meandering down the band's length). Flare only.</summary>
    private void BuildAvalancheCrack(Transform parent, float length, float x, float zFrac, float thickness)
    {
        int segs = 3;
        float step = length / segs;
        float start = -length * 0.5f + (zFrac - 0.5f) * length;
        for (int i = 0; i < segs; i++)
        {
            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = "MoltenCrack";
            seg.transform.SetParent(parent, false);
            seg.transform.localPosition = new Vector3(x, 0.12f, start + step * i);
            seg.transform.localScale = new Vector3(thickness, 0.4f, step * 0.8f);
            Material m = new Material(Shader.Find("Standard"));
            m.color = new Color(1f, 0.5f, 0.09f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.1f) * 1.5f);
            seg.GetComponent<MeshRenderer>().sharedMaterial = m;
            DestroyImmediate(seg.GetComponent<Collider>());
        }
    }

    private void AdvanceAvalanche()
    {
        if (buggy == null || terrain == null) return;

        // Pace tracks the buggy's ACTIVE (upgraded) top speed, so stall-time is swallowed but
        // a boost always breaks the wave's grip. Later ridge legs tighten it (see ApplyRamp).
        float maxMs = buggy.maxSpeedKmh / 3.6f;
        float chase = Mathf.Max(avalancheMinChaseMs, maxMs * avalancheChaseFactor);
        avalancheFrontEnd += chase * Time.deltaTime;
        float overseer = buggy.transform.position.z + 6f;  // the leading edge pins just AHEAD of a caught buggy
        avalancheFrontEnd = Mathf.Min(avalancheFrontEnd, overseer);
        avalancheFrontEnd = Mathf.Clamp(avalancheFrontEnd, MoonTerrainGen.RaceStartZ + 5f, terrain.RaceFinishZ);
        PositionAvalancheVisuals();

        // A low molten rumble trails the wave as it rolls in (throttled - it's a 3D one-shot).
        avalancheRumbleAcc += Time.deltaTime;
        if (avalancheRumbleAcc >= 0.6f && Time.time - hazardStartTime >= avalancheTelegraphTime - 0.2f)
        {
            avalancheRumbleAcc = 0f;
            Vector3 src = new Vector3(terrain.RaceCenterX(avalancheFrontEnd), 0f, avalancheFrontEnd);
            float near = Mathf.Clamp01(1f - Mathf.Abs(avalancheFrontEnd - buggy.transform.position.z) / 80f);
            RoverSounds.PlayEruptRumble(src, 0.3f + 0.7f * near);
        }
    }

    private void PositionAvalancheVisuals()
    {
        if (avalancheFloorGo == null || terrain == null) return;
        float floorZ = avalancheFrontEnd - avalancheBandLength * 0.5f;
        float floorX = terrain.RaceCenterX(floorZ);
        float floorY = terrain.SampleHeight(new Vector3(floorX, 0f, floorZ));
        avalancheFloorGo.transform.position = new Vector3(floorX, floorY + 0.45f, floorZ);
    }

    private void ExtinguishAvalanche()
    {
        avalancheActive = false;
        if (avalancheFloorGo != null) { Destroy(avalancheFloorGo); avalancheFloorGo = null; }
    }

    // ------------------------------------------------------------------ rain (acid / methane SNOW)
    private void BeginRain()
    {
        if (hud != null) hud.ShowWarning(RainLabel());
        rainActive = true;
        rainDamageAcc = 0f;
        RoverSounds.PlayStormAlert();
        rainBurst = StartCoroutine(RainSchedule());
    }

    private void RunRain()
    {
        if (Time.time - hazardStartTime >= rainDuration)
        {
            rainActive = false;
            if (rainBurst != null) { StopCoroutine(rainBurst); rainBurst = null; }
            DestroyRainStreaks();
            EndHazard();
        }
    }

    private void AnimateLava()
    {
        if (lavaFlowMat == null || !lavaActive) return;
        lavaScrollAcc += Time.deltaTime * 0.35f;
        lavaFlowMat.SetTextureOffset("_EmissionMap", new Vector2(lavaScrollAcc, lavaScrollAcc * 0.45f));
    }

    private void ApplyRainDamage()
    {
        if (hull == null) return;
        rainDamageAcc += rainDamagePerSecond * Time.deltaTime;
        while (rainDamageAcc >= 1f)
        {
            hull.TakeDamage(1f);
            rainDamageAcc -= 1f;
        }
    }

    private IEnumerator RainSchedule()
    {
        // Bursts of falling streaks while the rain is up.
        while (rainActive)
        {
            SpawnRainBurst();
            yield return new WaitForSeconds(Random.Range(1.1f, 1.7f));
        }
    }

    private void SpawnRainBurst()
    {
        Color col = RainStreakColor();
        bool snowy = theme == PlanetTheme.Titan;
        // Titan methane snow is a much denser blizzard (260 flakes per dump) than the acid
        // runs' 3x-from-baseline 36-streak storm, and it hangs LOW - brushing the rover as it
        // falls (the cold bites on contact), rather than dissolving high above the course.
        int count = snowy ? 260 : 36;
        for (int k = 0; k < count; k++)
        {
            Vector3 p = buggy.transform.position + new Vector3(Random.Range(-20f, 20f), 0f, Random.Range(-14f, 26f));
            float groundY = terrain.SampleHeight(p);
            if (groundY <= -100f) continue;

            GameObject s;
            if (snowy)
            {
                // Methane snow: small puffy flakes that cling to the cold - not lancing rain.
                s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "SnowFlake";
                s.transform.localScale = Vector3.one * Random.Range(0.16f, 0.28f);
            }
            else
            {
                s = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                s.name = "RainStreak";
                s.transform.localScale = new Vector3(0.09f, Random.Range(1.6f, 3.2f), 0.09f);
            }
            s.transform.position = new Vector3(p.x, groundY + (snowy ? Random.Range(6f, 15f) : Random.Range(12f, 26f)), p.z);

            Material m = new Material(Shader.Find("Standard"));
            m.color = col;
            m.SetFloat("_Metallic", snowy ? 0f : 0.2f);
            m.SetFloat("_Glossiness", snowy ? 0.85f : 0.4f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", col * (snowy ? 0.35f : 0.7f));
            s.GetComponent<MeshRenderer>().sharedMaterial = m;
            DestroyImmediate(s.GetComponent<Collider>());

            rainStreaks.Add(new ActiveStreak
            {
                go = s,
                floor = groundY,
                fallSpeed = snowy ? Random.Range(2.5f, 4.5f) : 18f,
                sway = snowy ? Random.Range(1.2f, 2.2f) : 0f,
                phase = Random.Range(0f, Mathf.PI * 2f),
            });
        }
    }

    private void MoveRainStreaks()
    {
        for (int i = rainStreaks.Count - 1; i >= 0; i--)
        {
            ActiveStreak st = rainStreaks[i];
            if (st.go == null) { rainStreaks.RemoveAt(i); continue; }
            Vector3 pos = st.go.transform.position;
            pos.y -= st.fallSpeed * Time.deltaTime;
            if (st.sway > 0f)
            {
                st.phase += Time.deltaTime * 3.2f;
                pos.x += Mathf.Sin(st.phase) * st.sway * Time.deltaTime;
            }
            st.go.transform.position = pos;
            if (pos.y <= st.floor + 0.3f)
            {
                Destroy(st.go);
                rainStreaks.RemoveAt(i);
            }
        }
    }

    private void DestroyRainStreaks()
    {
        for (int i = 0; i < rainStreaks.Count; i++)
        {
            if (rainStreaks[i].go != null) Destroy(rainStreaks[i].go);
        }
        rainStreaks.Clear();
    }

    // ------------------------------------------------------------------ methane lakes (Titan)
    private void BuildLakes()
    {
        float startZ = MoonTerrainGen.RaceStartZ;
        float finishZ = terrain.RaceFinishZ;
        for (int i = 0; i < lakeCount * 3 && lakes.Count < lakeCount; i++)
        {
            float z = Random.Range(startZ + 70f, finishZ - 70f);
            z = AvoidAnchors(z, checkpointZs, lakeBandLength * 0.5f + 12f);
            z = Mathf.Clamp(z, startZ + 60f, finishZ - 60f);
            if (z < startZ + 60f || z > finishZ - 60f) continue;
            if (LakesNear(z, 70f)) continue;

            LiquidZone zone = BuildZone(z, lakeBandLength, lakeGapMin, lakeGapMax,
                lakeTraction, lakeSpeedCap, lakeDamagePerSecond, lakeTickInterval);
            lakes.Add(zone);
            BuildLiquidVisual(zone, false);
        }
    }

    private bool LakesNear(float z, float clear)
    {
        for (int i = 0; i < lakes.Count; i++)
        {
            if (Mathf.Abs(lakes[i].zMin + 0.5f * (lakes[i].zMax - lakes[i].zMin) - z) < clear) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ Io lava lakes
    // Io has no methane: its standing ponds are LAVA, hotter and bigger than Titan's lakes.
    // Canyon lanes span wider molten bands that bubble constantly and tick hull damage; the
    // ridge crest scatters round lava ponds (the plain's lakes are far below and unreachable).
    private void BuildIoLavaLakes()
    {
        float startZ = MoonTerrainGen.RaceStartZ;
        float finishZ = terrain.RaceFinishZ;
        int wanted = lakeCount + 2; // Io is thicker with lava than Titan is with lakes
        for (int i = 0; i < wanted * 3 && lakes.Count < wanted; i++)
        {
            float z = Random.Range(startZ + 70f, finishZ - 70f);
            z = AvoidAnchors(z, checkpointZs, lakeBandLength * 0.8f + 12f);
            z = Mathf.Clamp(z, startZ + 60f, finishZ - 60f);
            if (z < startZ + 60f || z > finishZ - 60f) continue;
            if (LakesNear(z, 70f)) continue;

            LiquidZone zone = BuildZone(z, lakeBandLength * 1.6f, lakeGapMin * 0.8f, lakeGapMax * 0.9f,
                0.45f, 0.5f, 3f, 0.4f);
            zone.isLava = true;
            lakes.Add(zone);
            BuildLiquidVisual(zone, true);
            AttachLavaBubbler(zone);
        }
    }

    private void BuildRidgeLavaPatches()
    {
        float startZ = MoonTerrainGen.RaceStartZ;
        float finishZ = terrain.RaceFinishZ;
        int placed = 0;
        int wanted = 10;
        for (int i = 0; i < wanted * 6 && placed < wanted; i++)
        {
            float z = Random.Range(startZ + 55f, finishZ - 55f);
            z = AvoidAnchors(z, checkpointZs, 28f);
            z = Mathf.Clamp(z, startZ + 50f, finishZ - 50f);
            if (z < startZ + 50f || z > finishZ - 50f) continue;
            if (LakesNear(z, 28f)) continue;

            float cx = terrain.RaceCenterX(z);
            float r = Random.Range(3.6f, 5.6f);
            float half = terrain.raceRidgeCrestHalfWidth;
            float clamp = Mathf.Max(0f, half - r * 0.75f);
            cx = cx + Random.Range(-clamp, clamp);

            LiquidZone zone = new LiquidZone
            {
                minX = cx - r, maxX = cx + r,
                zMin = z - r, zMax = z + r,
                traction = 0.55f, speedCap = 0.5f,
                dps = 3f, tick = 0.4f, tickAcc = 0f,
                isLava = true,
            };
            lakes.Add(zone);
            BuildLiquidVisual(zone, true);
            AttachLavaBubbler(zone);
            placed++;
        }
    }

    /// <summary>Sits a <see cref="LavaBubbler"/> on a lava zone's visual so the pool visibly
    /// billows bubbles instead of sitting dead-still.</summary>
    private void AttachLavaBubbler(LiquidZone zone)
    {
        if (zone.visual == null) return;
        LavaBubbler b = zone.visual.AddComponent<LavaBubbler>();
        b.halfExtents = new Vector2((zone.maxX - zone.minX) * 0.45f, (zone.zMax - zone.zMin) * 0.45f);
        b.bubbleColor = theme == PlanetTheme.Io ? new Color(1f, 0.45f, 0.1f) : lavaColor;
        b.spawnInterval = 0.5f;
    }

    // ------------------------------------------------------------------ ridge ice patches (Titan)
    // The Ridge course runs 70m above the plain, so the terrain-level methane lakes never reach
    // the buggy. Instead the crest itself is scattered with frozen slick patches - a fresh one
    // every ~40m of course - that cut grip and top speed exactly like a lake but deal no damage.
    private void BuildRidgeIcePatches()
    {
        float startZ = MoonTerrainGen.RaceStartZ;
        float finishZ = terrain.RaceFinishZ;
        int placed = 0;
        for (int i = 0; i < ridgeIcePatchCount * 6 && placed < ridgeIcePatchCount; i++)
        {
            float z = Random.Range(startZ + 55f, finishZ - 55f);
            z = AvoidAnchors(z, checkpointZs, Mathf.Max(14f, ridgeIceGap));
            z = Mathf.Clamp(z, startZ + 50f, finishZ - 50f);
            if (z < startZ + 50f || z > finishZ - 50f) continue;
            if (LakesNear(z, ridgeIceGap)) continue;

            float cx = terrain.RaceCenterX(z);
            float r = Random.Range(ridgeIcePatchMinRadius, ridgeIcePatchMaxRadius);
            float half = terrain.raceRidgeCrestHalfWidth;
            // Keep the whole disc on the flat crest so the buggy actually drives across it.
            float clamp = Mathf.Max(0f, half - r * 0.75f);
            cx = cx + Random.Range(-clamp, clamp);

            LiquidZone zone = new LiquidZone
            {
                minX = cx - r, maxX = cx + r,
                zMin = z - r, zMax = z + r,
                glaze = iceGlaze, traction = 1f, speedCap = 1f,
                dps = 0f, tick = 0.4f, tickAcc = 0f,
                isIce = true,
            };
            lakes.Add(zone);
            BuildLiquidVisual(zone, false);
            placed++;
        }
    }

    // ------------------------------------------------------------------ canyon ice patches (Lunar / Mars)
    // Shallow frozen regolith washes sat across the Moon/Mars canyon lane - the buggy slides
    // over them exactly like the Titan ridge patches (frozen: the speed is held, the wheels stop). They
    // grow with the race grade (GameFlow.RaceGradeIndex): harder grades scatter MORE and BIGGER
    // sections so the faster races slide over more of the course.
    private void BuildCanyonIcePatches()
    {
        float startZ = MoonTerrainGen.RaceStartZ;
        float finishZ = terrain.RaceFinishZ;
        int grade = Mathf.Clamp(GameFlow.RaceGradeIndex, 0, RivalRacer.GradeCount - 1);
        int count = canyonIceBaseCount + canyonIcePerGrade * grade;
        float rExt = canyonIceRadiusPerGrade * grade;

        int placed = 0;
        for (int i = 0; i < count * 6 && placed < count; i++)
        {
            float z = Random.Range(startZ + 60f, finishZ - 60f);
            z = AvoidAnchors(z, checkpointZs, Mathf.Max(20f, canyonIceMinRadius + rExt + 8f));
            z = Mathf.Clamp(z, startZ + 55f, finishZ - 55f);
            if (z < startZ + 55f || z > finishZ - 55f) continue;
            if (LakesNear(z, 45f)) continue;

            float cx = terrain.RaceCenterX(z);
            float r = Random.Range(canyonIceMinRadius, canyonIceMaxRadius) + rExt;
            // Small lateral nudge so sections don't sit dead-centre every time; the disc stays
            // on the lane so the buggy genuinely drives across it.
            float half = terrain.raceCourseHalfWidth;
            float clamp = Mathf.Max(0f, half - r * 0.4f);
            cx = cx + Random.Range(-clamp, clamp);

            LiquidZone zone = new LiquidZone
            {
                minX = cx - r, maxX = cx + r,
                zMin = z - r * 0.8f, zMax = z + r * 0.8f,
                glaze = iceGlaze, traction = 1f, speedCap = 1f,
                dps = 0f, tick = 0.4f, tickAcc = 0f,
                isIce = true,
            };
            lakes.Add(zone);
            BuildLiquidVisual(zone, false);
            placed++;
        }
    }

    // ------------------------------------------------------------------ methane mud flats (Titan)
// Titan's ground is a wet methane/silt slurry. Where that slurry shows through the frost it
// is darker, crumbly brown mud that grabs the wheels: no damage (it is not caustic), just a
// deep "stuck in the mud" wallow that kills pace. Canyon levels band it across the lane;
// the Ridge scatters flat discs along the crest where there is no lane to span.
private void BuildMudLanes()
{
    float startZ = MoonTerrainGen.RaceStartZ;
    float finishZ = terrain.RaceFinishZ;
    for (int i = 0; i < mudCount * 4 && i < 12; i++)
    {
        if (lakes.Count >= lakeCount + mudCount) break;
        float z = Random.Range(startZ + 70f, finishZ - 70f);
        z = AvoidAnchors(z, checkpointZs, mudBandLength * 0.5f + 12f);
        z = Mathf.Clamp(z, startZ + 60f, finishZ - 60f);
        if (z < startZ + 60f || z > finishZ - 60f) continue;
        if (LakesNear(z, 45f)) continue;

        LiquidZone zone = BuildZone(z, mudBandLength, mudGapMin, mudGapMax, mudTraction, mudSpeedCap, 0f, 0.4f);
        zone.isMud = true;
        lakes.Add(zone);
        BuildMudVisual(zone);
    }
}

private void BuildRidgeMudPatches()
{
    float startZ = MoonTerrainGen.RaceStartZ;
    float finishZ = terrain.RaceFinishZ;
    int placed = 0;
    for (int i = 0; i < ridgeMudCount * 6 && placed < ridgeMudCount; i++)
    {
        float z = Random.Range(startZ + 55f, finishZ - 55f);
        z = AvoidAnchors(z, checkpointZs, 28f);
        z = Mathf.Clamp(z, startZ + 50f, finishZ - 50f);
        if (z < startZ + 50f || z > finishZ - 50f) continue;
        if (LakesNear(z, 28f)) continue;

        float cx = terrain.RaceCenterX(z);
        float r = Random.Range(ridgeMudMinRadius, ridgeMudMaxRadius);
        float half = terrain.raceRidgeCrestHalfWidth;
        float clamp = Mathf.Max(0f, half - r * 0.75f);
        cx = cx + Random.Range(-clamp, clamp);

        lakes.Add(new LiquidZone
        {
            minX = cx - r, maxX = cx + r,
            zMin = z - r, zMax = z + r,
            traction = mudTraction, speedCap = mudSpeedCap,
            dps = 0f, tick = 0.4f, tickAcc = 0f,
            isMud = true,
        });
        BuildMudVisual(lakes[lakes.Count - 1]);
        placed++;
    }
}

private void BuildMudVisual(LiquidZone zone)
{
    if (terrain == null) return;
    float cx = (zone.minX + zone.maxX) * 0.5f;
    float cz = (zone.zMin + zone.zMax) * 0.5f;
    float width = zone.maxX - zone.minX;
    float length = zone.zMax - zone.zMin;
    bool roundPatch = Mathf.Abs(width - length) < 0.5f;

    Material mat;
    GameObject go = TerrainDecal.Build(terrain, new Vector2(cx, cz),
        width * 0.5f, length * 0.5f, 0.05f,
        new Color(0.30f, 0.19f, 0.10f), Color.black,
        TerrainDecal.Style.Mud, roundPatch, out mat);
    if (go == null) return;
    go.name = "TitanMud";
}

// ------------------------------------------------------------------ liquid zones (shared shape helpers)
    private LiquidZone BuildZone(float z, float band, float gapMin, float gapMax,
        float traction, float speedCap, float dps, float tick)
    {
        float cx = terrain.RaceCenterX(z);
        float half = RaceHalfWidth();
        bool fromRight = Random.value < 0.5f;
        float gap = Random.Range(gapMin, gapMax);
        float minX, maxX;
        if (fromRight) { maxX = cx + half; minX = cx + half * (1f - gap); }
        else { minX = cx - half; maxX = cx - half * (1f - gap); }

        return new LiquidZone
        {
            minX = minX, maxX = maxX,
            zMin = z - band * 0.5f, zMax = z + band * 0.5f,
            traction = traction, speedCap = speedCap,
            dps = dps, tick = tick, tickAcc = 0f,
        };
    }

    private void BuildLiquidVisual(LiquidZone zone, bool isLava)
    {
        if (terrain == null) return;
        float cx = (zone.minX + zone.maxX) * 0.5f;
        float cz = (zone.zMin + zone.zMax) * 0.5f;
        float width = zone.maxX - zone.minX;
        float length = zone.zMax - zone.zMin;
        bool roundPatch = Mathf.Abs(width - length) < 0.5f;

        Material mat;
        GameObject go = TerrainDecal.Build(terrain, new Vector2(cx, cz),
            width * 0.5f, length * 0.5f, 0.06f,
            isLava ? lavaColor : new Color(0.46f, 0.68f, 0.82f),
            isLava ? lavaGlow : new Color(0.05f, 0.12f, 0.18f) * 1.5f,
            isLava ? TerrainDecal.Style.Lava : TerrainDecal.Style.Ice,
            roundPatch, out mat, isLava && theme == PlanetTheme.Io, !isLava);
        if (go == null) return;

        go.name = isLava ? "LavaFlow" : "MethaneLake";
        if (isLava) lavaFlowMat = mat;
        zone.visual = go;
    }

    private bool InZone(LiquidZone zone, Vector3 p)
    {
        return p.x >= zone.minX && p.x <= zone.maxX && p.z >= zone.zMin && p.z <= zone.zMax;
    }

    // Surface probes used by the SurfaceSpray emitter so the buggy flings the right debris.
    public bool IsInLava(Vector3 p) => (lavaActive && lava != null && InZone(lava, p)) || InAvalanche(p);
    public bool InAvalanche(Vector3 p)
    {
        if (!avalancheActive || terrain == null) return false;
        float half = RaceHalfWidth();
        float cx = terrain.RaceCenterX(avalancheFrontEnd);
        return p.x >= cx - half && p.x <= cx + half
            && p.z >= avalancheFrontEnd - avalancheBandLength && p.z <= avalancheFrontEnd;
    }
    public bool IsInLavaLake(Vector3 p)
    {
        for (int i = 0; i < lakes.Count; i++)
            if (lakes[i].isLava && InZone(lakes[i], p)) return true;
        return false;
    }
    public bool IsInMud(Vector3 p)
    {
        for (int i = 0; i < lakes.Count; i++)
            if (lakes[i].isMud && InZone(lakes[i], p)) return true;
        return false;
    }
    public bool IsInLake(Vector3 p)
    {
        for (int i = 0; i < lakes.Count; i++)
            if (!lakes[i].isMud && !lakes[i].isIce && InZone(lakes[i], p)) return true;
        return false;
    }
    public bool IsInIce(Vector3 p)
    {
        for (int i = 0; i < lakes.Count; i++)
            if (lakes[i].isIce && InZone(lakes[i], p)) return true;
        return false;
    }

    // Applies traction/speed/damage pressure from the live lava flow + the methane lakes.
    private void ApplySurfaceZones()
    {
        if (buggy == null) return;
        Vector3 p = buggy.transform.position;

        float trac = rainActive ? rainWetTraction : 1f;
        float spd = 1f;
        float glaze = 0f;

        if (lavaActive && lava != null && InZone(lava, p))
        {
            trac = Mathf.Min(trac, lava.traction);
            spd = Mathf.Min(spd, lava.speedCap);
            TickZoneDamage(lava);
        }

        // The chasing molten wave pours pressure the same way, but its zone slides forward
        // with the front each frame rather than sitting as a fixed band.
        if (avalancheActive && InAvalanche(p))
        {
            trac = Mathf.Min(trac, avalancheTraction);
            spd = Mathf.Min(spd, avalancheSpeedCap);
            avalancheTickAcc += Time.deltaTime;
            while (avalancheTickAcc >= avalancheTickInterval)
            {
                avalancheTickAcc -= avalancheTickInterval;
                if (hull != null)
                {
                    hull.TakeDamage(avalancheDps * avalancheTickInterval);
                    if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.35f);
                }
            }
        }

        for (int i = 0; i < lakes.Count; i++)
        {
            if (InZone(lakes[i], p))
            {
                // Methane mud only grabs the WHEELS: while the buggy is airborne it can hop
                // clean OVER a mud band and keep full speed (mid-air the wheels are out of the
                // slurry). Lava and liquid-methane pools still roast the hull, so their damage
                // ticks regardless of altitude.
                if (lakes[i].isMud && buggy.IsAirborne) continue;

                // Ice releases with the hop: a grounded buggy carries the full glaze, but each
                // airborne second eases it back toward clear (AirborneRelease 0->1 over
                // airborneReleaseSeconds), so a well-timed jump softens the patch and a full
                // leap over it costs almost nothing.
                if (lakes[i].isIce)
                {
                    // Frozen, not slowed: ice drives the buggy's glaze instead of its traction
                    // and speed shelf. No top-speed cut, so hitting a patch at pace keeps that
                    // pace - the cost is that the wheels stop turning and steering and the
                    // brakes go vague (all handled in BuggyController).
                    // IceResist scales the freeze, so a vehicle with the suspension for it (the
                    // sprint) keeps part of its wheels, steering and brakes and is only partly
                    // held rather than stopped outright.
                    float g = lakes[i].glaze * (1f - buggy.AirborneRelease)
                              * (1f - VehicleSystem.IceResist);
                    glaze = Mathf.Max(glaze, g);
                }
                else if (lakes[i].isMud)
                {
                    // Mud: mudResist lifts the flat's own grip and speed caps toward clean ground
                    // (lerp toward 1), so a vehicle that skates the slurry (the sprint) is far
                    // less bogged than one that wallows in it.
                    float r = VehicleSystem.MudResist;
                    trac = Mathf.Min(trac, Mathf.Lerp(lakes[i].traction, 1f, r));
                    spd = Mathf.Min(spd, Mathf.Lerp(lakes[i].speedCap, 1f, r));
                }
                else
                {
                    trac = Mathf.Min(trac, lakes[i].traction);
                    spd = Mathf.Min(spd, lakes[i].speedCap);
                }
                TickZoneDamage(lakes[i]);
            }
        }

        buggy.tractionMultiplier = trac;
        buggy.speedCapMultiplier = spd;
        // Absolute, not stacked on the previous frame - leaving a patch must clear the freeze.
        buggy.glazeFromPatch = glaze;

        // Mud flats, ice patches, and collapse-pit wakes reach the rivals too: any rival inside a
        // mud flat or skating an ice patch, or scrambling out of a fresh land-fall it fell into, is
        // slowed alongside the player. Mud matches the player's crawl exactly. Ice is only a MILD
        // dip for a rival, because on the player's side it does not slow anything at all - it
        // freezes the rover, so hopping or easing onto a patch is the way to keep your pace and
        // that caution is what outpaces the field (rivals are cached once - they never change
        // mid-race).
        if (rivalCache == null) rivalCache = RivalRacer.All();
        for (int r = 0; r < rivalCache.Length; r++)
        {
            Vector3 rp = rivalCache[r].transform.position;
            bool inMud = false;
            for (int i = 0; i < lakes.Count; i++)
            {
                if (lakes[i].isMud && InZone(lakes[i], rp)) { inMud = true; break; }
            }
            rivalCache[r].SetMudSlow(inMud);

            bool inIce = false;
            for (int i = 0; i < lakes.Count; i++)
            {
                if (lakes[i].isIce && InZone(lakes[i], rp)) { inIce = true; break; }
            }
            rivalCache[r].SetIceSlow(inIce);

            bool inCollapseWake = Time.time < collapseSlowUntil
                && Mathf.Abs(rp.x - collapseSlowCenter.x) <= collapseHalfWidth + 2f
                && Mathf.Abs(rp.z - collapseSlowCenter.z) <= collapseHalfLength + 2f;
            rivalCache[r].SetCollapseSlow(inCollapseWake);
        }
    }

    private void TickZoneDamage(LiquidZone zone)
    {
        if (hull == null || zone.dps <= 0f) return;
        zone.tickAcc += Time.deltaTime;
        while (zone.tickAcc >= zone.tick)
        {
            hull.TakeDamage(zone.dps * zone.tick);
            zone.tickAcc -= zone.tick;
            if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.35f);
        }
    }

    // ------------------------------------------------------------------ shared bits
    private float RaceHalfWidth() =>
        GameFlow.CurrentCourse == RaceCourse.Ridge ? terrain.raceRidgeCrestHalfWidth : terrain.raceCourseHalfWidth;

    private string BoulderLabel() => theme == PlanetTheme.Mars ? "ROCK FALL INBOUND" : "BOULDER FALL INBOUND";
    private string CollapseLabel() => theme == PlanetTheme.Mars ? "LANDSLIDE AHEAD" : "RIDGE COLLAPSE AHEAD";
    private string LavaLabel() => theme == PlanetTheme.Io ? "LAVA SURGE AHEAD" : "LAVA FLOW AHEAD";
    private string AvalancheLabel() => "Lava Avalanche!";
    private string RainLabel()
    {
        // Titan's precipitation is methane SNOW: the freezing cold is what wrecks the hull,
        // not chemistry - so the warning names the blizzard, not the rain. Acid rain is
        // Venus-only (Io channels its sulphur fire into rock falls + a ridge avalanche).
        if (theme == PlanetTheme.Titan) return "FREEZING METHANE SNOW";
        return "ACID RAIN";
    }

    private Color RainStreakColor()
    {
        if (theme == PlanetTheme.Titan) return new Color(0.92f, 0.98f, 1f);
        return theme == PlanetTheme.Io ? new Color(0.85f, 0.95f, 0.45f) : new Color(0.55f, 0.95f, 0.5f);
    }

    private Transform BuildMark(Vector3 world, bool many)
    {
        float y = 0f;
        if (terrain != null) y = terrain.SampleHeight(world);
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "HazardMark";
        go.transform.position = new Vector3(world.x, y + 0.15f, world.z);
        go.transform.localScale = new Vector3(6f, 0.08f, 6f);
        Material mat = HazardMarkPulse.MakeBlinkMaterial(
            new Color(1f, 0.68f, 0.25f, 0.32f),
            new Color(1f, 0.45f, 0.05f));
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        DestroyImmediate(go.GetComponent<Collider>());
        // The pre-warning disc flashes softly like a spotlight (see HazardMarkPulse).
        go.AddComponent<HazardMarkPulse>();
        return go.transform;
    }

    private void ClearMarks()
    {
        for (int i = 0; i < boulderMarks.Count; i++)
        {
            if (boulderMarks[i] != null) Destroy(boulderMarks[i].gameObject);
        }
        boulderMarks.Clear();
        if (collapseMarks != null)
        {
            Destroy(collapseMarks.gameObject);
            collapseMarks = null;
        }
    }

    private void CleanupBoulders(bool force)
    {
        for (int i = boulders.Count - 1; i >= 0; i--)
        {
            if (boulders[i] == null) { boulders.RemoveAt(i); continue; }
            if (force || boulders[i].transform.position.magnitude > 1000f)
            {
                Destroy(boulders[i]);
                boulders.RemoveAt(i);
            }
        }
    }

    private void EndHazard()
    {
        // PROSPECTOR RUN: the canyon hands back to its boulder/meteor pool, capped so a storm
        // chain never dominates the descent. Ridge legs never run the director for Prospector.
        if (GameFlow.Mode == GameMode.Prospector && GameFlow.CurrentCourse == RaceCourse.Canyon)
        {
            upcoming = RollCanyonWeatherWithStreakCaps();
            return;
        }
        switch (theme)
        {
            case PlanetTheme.Venus:
                if (phase == RaceHazardPhase.Lava)
                    upcoming = Random.value < lavaFollowChance ? RaceHazardPhase.Lava : RollVenusHazards();
                else
                    upcoming = RollVenusHazards();
                break;
            case PlanetTheme.Io:
                // Surges chain far more often; when one finally clears, the full Io pool
                // (lava / rock-falls / ridge avalanche) decides the next threat.
                if (phase == RaceHazardPhase.Lava)
                    upcoming = Random.value < lavaFollowChance ? RaceHazardPhase.Lava : RollIoHazards();
                else
                    upcoming = RollIoHazards();
                break;
            case PlanetTheme.Titan:
                // The canyon keeps shearing: landslide flanks, overhead rock falls and snow
                // alternate; the Ridge stays on snow (its crest ice patches carry the variety).
                upcoming = GameFlow.CurrentCourse == RaceCourse.Canyon
                    ? RollTitanCanyonHazards()
                    : RaceHazardPhase.Rain;
                break;
            case PlanetTheme.Mars:
            case PlanetTheme.Lunar:
            default:
                // Both the Moon and Mars courses cap storm chains at TWO so the cannon rounds
                // never dominate a whole run and a collapse/boulder round always breaks them up.
                int maxMeteorChain = (theme == PlanetTheme.Lunar || theme == PlanetTheme.Mars) ? 2 : 3;
                if (GameFlow.CurrentCourse == RaceCourse.Ridge)
                {
                    // Alternate meteors / landfall: a landfall always hands back to meteors, and
                    // meteors chain but never more than a cap in a row (meteorChainCount).
                    if (phase == RaceHazardPhase.Meteors)
                    {
                        if (meteorChainCount < maxMeteorChain && Random.value < ridgeMeteorFollowChance)
                        {
                            upcoming = RaceHazardPhase.Meteors;
                            meteorChainCount++;
                        }
                        else
                        {
                            upcoming = RaceHazardPhase.Collapse;
                            meteorChainCount = 0;
                        }
                    }
                    else
                    {
                        upcoming = RaceHazardPhase.Meteors;
                        meteorChainCount = 1;
                    }
                }
                else
                {
                    // Canyon shares the same 2-round cap on the weather pool: a meteor barrage
                    // can repeat at most once after the one that opened before the boulder/rock
                    // falls take over, so storm rounds stay variety, not the whole ride.
                    bool prospectorCanyon = GameFlow.Mode == GameMode.Prospector;
                    if (prospectorCanyon)
                    {
                        upcoming = RollCanyonWeatherWithStreakCaps();
                    }
                    else
                    {
                        RaceHazardPhase rolled = RollCanyonWeather();
                        if (phase == RaceHazardPhase.Meteors)
                        {
                            if (rolled == RaceHazardPhase.Meteors && meteorChainCount < maxMeteorChain)
                            {
                                upcoming = RaceHazardPhase.Meteors;
                                meteorChainCount++;
                            }
                            else
                            {
                                upcoming = RaceHazardPhase.Boulders;
                                meteorChainCount = 0;
                            }
                        }
                        else
                        {
                            upcoming = rolled;
                            meteorChainCount = rolled == RaceHazardPhase.Meteors ? 1 : 0;
                        }
                    }
                }
                break;
        }
        phase = RaceHazardPhase.Calm;
        phaseTimer = CalmFor(upcoming);
        if (hud != null) hud.HideWarning();
    }

    /// <summary>Weighted Lunar/Mars Prospector Canyon mix with a hard two-event streak cap for
    /// both hazards. When neither cap is reached the configured weights preserve variety.</summary>
    private RaceHazardPhase RollCanyonWeatherWithStreakCaps()
    {
        if (meteorChainCount >= 2) return SetCanyonStreak(RaceHazardPhase.Boulders);
        if (boulderChainCount >= 2) return SetCanyonStreak(RaceHazardPhase.Meteors);
        return SetCanyonStreak(RollCanyonWeather());
    }

    private RaceHazardPhase SetCanyonStreak(RaceHazardPhase next)
    {
        if (next == RaceHazardPhase.Meteors)
        {
            meteorChainCount++;
            boulderChainCount = 0;
        }
        else
        {
            boulderChainCount++;
            meteorChainCount = 0;
        }
        return next;
    }

    private float CalmFor(RaceHazardPhase next)
    {
        switch (next)
        {
            case RaceHazardPhase.Boulders: return Random.Range(boulderCalmMin, boulderCalmMax);
            case RaceHazardPhase.Meteors: return Random.Range(meteorCalmMin, meteorCalmMax);
            case RaceHazardPhase.Lava: return Random.Range(lavaCalmMin, lavaCalmMax);
            case RaceHazardPhase.Rain: return Random.Range(rainCalmMin, rainCalmMax);
            case RaceHazardPhase.Avalanche: return Random.Range(avalancheCalmMin, avalancheCalmMax);
            default: return Random.Range(calmMin, calmMax);
        }
    }

    // Nudges a candidate course-Z away from any anchor (gate) closer than `clearance`.
    private float AvoidAnchors(float z, IReadOnlyList<float> anchors, float clearance)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            bool tooClose = false;
            for (int i = 0; i < anchors.Count; i++)
            {
                float a = anchors[i];
                if (Mathf.Abs(z - a) < clearance)
                {
                    z = z > a ? a + clearance : a - clearance;
                    tooClose = true;
                }
            }
            if (!tooClose) break;
        }
        return z;
    }

    private void Freeze()
    {
        ClearMarks();
        CleanupBoulders(true);
        ExtinguishLava();
        ExtinguishAvalanche();
        rainActive = false;
        if (rainBurst != null) { StopCoroutine(rainBurst); rainBurst = null; }
    DestroyRainStreaks();
    if (buggy != null)
    {
        buggy.tractionMultiplier = 1f;
        buggy.speedCapMultiplier = 1f;
        buggy.glazeFromPatch = 0f;
    }
        phase = RaceHazardPhase.Calm;
        phaseTimer = float.MaxValue;
        if (hud != null) hud.HideWarning();
    }

    private void OnDestroy()
    {
        if (rainBurst != null) StopCoroutine(rainBurst);
        ExtinguishAvalanche();
    }
}