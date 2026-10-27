using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// One-stop runtime level builder. Add this to an empty "Game" GameObject in an otherwise
/// empty scene and press Play: it constructs the entire moon environment, the buggy,
/// the oxygen pods, lighting, skybox, camera and HUD entirely from code.
///
/// This removes the need to hand-assemble prefabs in the Editor.
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    private const int ProspectorPodsRemovedPerGrade = 1;
    private const int ProspectorBouldersAddedPerGrade = 10;
    // PROSPECTOR RUN ore field: a SMALL, fully reachable lane scatter (the run clears on
    // collecting at least HALF of it - see LevelEndSequence). Index = grade-1 (grades 1..5):
    // 15, 18, 20, 23, 25 nuggets, so a harder descent tracks a slightly larger field.
    private static readonly int[] ProspectorOrePerGrade = { 15, 18, 20, 23, 25 };
    // A Prospector ore site must sit on ground this flat or flatter (rise/run) so the lane
    // field is always drivable and every nugget reachable - the fairness guarantee.
    private const float ProspectorMaxOreSlope = 0.35f;

    [Header("Buggy spawn")]
    [Tooltip("Where the buggy starts. Harvest/Survival keep the (0, 0) origin - MoonTerrainGen "
            + "flattens a launch pad there - and races override this via PlaceRaceStart().")]
    public Vector3 startPosition = new Vector3(0f, 4f, 0f);

    [Header("Oxygen pods")]
    [Tooltip("Number of oxygen pod checkpoints along the route.")]
    public int podCount = 10;
    [Tooltip("Average spacing (metres) between consecutive oxygen pods.")]
    public float podSpacing = 100f;

    [Header("Boulders")]
    [Tooltip("Number of large rock obstacles scattered along the route.")]
    public int boulderCount = 12;
    [Tooltip("Average spacing (metres) between boulders along the route.")]
    public float boulderSpacing = 110f;

    [Header("Harvestables (Harvester Mode)")]
    [Tooltip("Display name of the resource to harvest (empty = none on this planet).")]
    public string harvestableName = "";
    [Tooltip("Number of harvestable nuggets to scatter off the route.")]
    public int harvestableCount = 0;
    [Tooltip("Tokens each harvested nugget is worth.")]
    public int harvestableScore = 10;
    [Tooltip("Theme colour of the harvested nuggets on this planet.")]
    public Color harvestableColor = new Color(0.35f, 0.95f, 0.8f);
    [Tooltip("Minimum lateral distance (metres) a nugget sits off the oxygen route.")]
    public float harvestableOffRouteMin = 15f;
    [Tooltip("Maximum lateral distance (metres) a nugget sits off the oxygen route.")]
    public float harvestableOffRouteMax = 55f;

    [Header("Survey features (Io / Titan)")]
    [Tooltip("Io: number of mini volcanoes (crest magma pools) placed off the canister path.")]
    public int ioVolcanoCount = 6;
    [Tooltip("Titan: number of liquid methane lakes placed off the canister path.")]
    public int titanLakeCount = 8;
    public int titanMudCount = 9;
    [Tooltip("Nuggets ringed around each Io volcano.")]
    public int ioVolcanoNuggets = 14;
    [Tooltip("Io: number of BIG bubbling lava lakes placed off the canister path.")]
    public int ioLavaLakeCount = 5;
    [Tooltip("Nuggets ringed around the rim of each Io lava lake.")]
    public int ioLavaLakeNuggets = 12;
    [Tooltip("Nuggets ringed around the edge of each Titan methane lake.")]
    public int titanLakeNuggets = 16;

    private HarvestFeatureController featureController;
    private readonly System.Collections.Generic.List<Vector3> featureCenters =
        new System.Collections.Generic.List<Vector3>();
    private int featureObjectIndex;

    private BuggyController buggy;
    private MoonTerrainGen terrain;
    // Built but held DOWN by the level intro until it hands the camera over on "GO".
    private RaceCountdown raceCountdown;
    private HUD hud;
    private readonly System.Collections.Generic.List<RivalRacer> rivals = new System.Collections.Generic.List<RivalRacer>();
    private float gripMultiplier = 1f;
    private readonly System.Collections.Generic.List<Vector3> podPositions = new System.Collections.Generic.List<Vector3>();
    private readonly System.Collections.Generic.List<float> raceCheckpointZs = new System.Collections.Generic.List<float>();
    private readonly System.Collections.Generic.List<OxygenPod> spawnedPods = new System.Collections.Generic.List<OxygenPod>();
    private readonly System.Collections.Generic.List<Harvestable> spawnedHarvestables = new System.Collections.Generic.List<Harvestable>();

    private void Awake()
    {
        CrazyGamesBridge.Init();
        AudioInputGate.Ensure();
        GameSettings.Load();
        GameFlow.ResetSaveIfVersionChanged();
        // Snapshot the banked wallet for this scene load: the win board counts the run's whole
        // token gain (nuggets + bonus + first-clear + rival-beats + ridge tiers) onto it.
        GameFlow.RunStartTokens = UpgradeSystem.GetTokens();
        // Editor playtest menu items store their intent in SessionState so the launch can
        // survive domain reload (Enter Play Mode Options); restore it before the gate check.
        GameFlow.RestoreEditorPlaytest();
        RestoreTrialsChallenge();

        // Cold-start guard: the Main scene must only be built when it was reached through the
        // menu (which authorises the launch). If it loads on its own - e.g. pressing Play while
        // Main.unity is the active scene in the editor - route straight to the main menu instead
        // of dropping the player into a run.
        if (!GameFlow.IsLaunchAuthorized)
        {
            GameFlow.GoToMenu();
            return;
        }

        // LUNAR TRIALS: a challenge's fixed seed goes in BEFORE anything generates, so every
        // attempt lays out craters, rocks, canisters and ore identically - retrying is skill,
        // and medal timings are fair. The regular game keeps its random layouts.
        if (TrialsChallenge != null) Random.InitState(TrialsChallenge.seed);

        // Point the whole physics world at this planet's gravity BEFORE anything is built, so
        // every WheelCollider, every Rigidbody prop and the hop rockets all agree from the first
        // frame. Physics.gravity is global rather than per-body, so this is the one place it can
        // be set for a run - and it is why the hop derives its launch speed from it instead of
        // carrying its own figure.
        ApplyPlanetGravity();

        // Lunar Race replaces the old Survival deactivation slot: a Race load builds the
        // downhill canyon course on the current planet. (The survival builders and
        // SurvivalDirector are kept dormant for future reuse.) Ridge Survival routes through
        // the same race build, which branches off into the crest (canister) survival.
        if (GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival)
        {
            BuildRace();
            return;
        }

        ApplyLevel(LevelManager.CurrentLevel);
        ApplyTrialsOverride();
        BuildGameManager();
        BuildEnvironment();
        // PROSPECTOR RUN descends the world's carved canyon: start the buggy flat on the
        // course mouth so it drops straight into the lane, instead of the survey origin.
        if (GameFlow.Mode == GameMode.Prospector) PlaceRaceStart();
        BuildBuggy();
        // The dark canyon run carries its own lamp: a short fog-fighting headlight cone.
        if (GameFlow.Mode == GameMode.Prospector) BuildProspectorHeadlight();
        // Trials ice profiles: a persistent glaze writer turns the whole arena into ice.
        if (TrialsChallenge != null
            && (TrialsChallenge.profile == ChallengeDef.Profile.Ice
                || TrialsChallenge.profile == ChallengeDef.Profile.IceAndMeteors))
        {
            buggy.gameObject.AddComponent<TrialsIcePatch>();
        }
        // Ramp challenges: gravity eases light while airborne, so the ramp jumps get a
        // genuinely floaty arc (restored the moment the wheels are back on the ground).
        if (TrialsChallenge != null && TrialsChallenge.rampAtS >= 0f)
        {
            buggy.gameObject.AddComponent<TrialsFloatyAir>();
        }
        BuildBoulders();
        // The trials course dressing (tyre edge markers, one combined mesh) lands right
        // after the boulders - it is scenery that marks where the run wants to go. A
        // challenge with rampAtS also gets its jump ramp ON the line (air-control feature).
        if (TrialsCourse.Active != null)
        {
            CourseBuilder.Build(terrain, transform);
            if (TrialsChallenge != null && TrialsChallenge.rampAtS >= 0f)
                CourseBuilder.BuildRamp(terrain, TrialsCourse.Active, TrialsChallenge.rampAtS, transform);
        }
        BuildDustPatches();
        BuildPods();
        BuildHarvestables();
        BuildLevelEnd();
        BuildCamera();
        BuildHUD();
        BuildHazards();
        BuildSkyAndLighting();
        BuildLevelIntro();
    }

    /// <summary>The Lunar Trials challenge the pending Main-scene build should run (null =
    /// a normal campaign run). Set by ChallengeRunner.StartChallenge just before the scene
    /// load; consumed by ApplyTrialsOverride and the hazard/terrain hooks below.</summary>
    public static ChallengeDef TrialsChallenge;

    /// <summary>LUNAR TRIALS: restores the challenge staged by the "Play Test Trials" editor
    /// menu (SessionState survives the play-mode domain reload; consumed once). The ladder
    /// path sets TrialsChallenge directly via ChallengeRunner and passes through untouched.</summary>
    private void RestoreTrialsChallenge()
    {
#if UNITY_EDITOR
        if (TrialsChallenge != null)
        {
            ChallengeRunner.ActivateForSession(TrialsChallenge);
            return;
        }
        string id = UnityEditor.SessionState.GetString("trials.challenge.id", "");
        if (string.IsNullOrEmpty(id)) return;
        ChallengeDef def = ChallengePack.All.Find(c => c.id == id);
        if (def != null)
        {
            TrialsChallenge = def;
            ChallengeRunner.ActivateForSession(def);
        }
        UnityEditor.SessionState.EraseString("trials.challenge.id");   // consume once
#endif
    }

    /// <summary>Lunar Trials: re-parameterise the standard Moon build from the challenge
    /// definition - fewer canisters, a specific ore field size, a boulder count. Called right
    /// after ApplyLevel so it wins over the planet defaults.</summary>
    private void ApplyTrialsOverride()
    {
        if (TrialsChallenge == null) return;
        // A challenge route always keeps at least TWO canisters: the pod chain defines the
        // route the ore field scatters along (one pod = no route = no ore), so canisterCount
        // is interpreted as EXTRA canisters beyond the mandatory start + finish pair.
        podCount = Mathf.Max(2, TrialsChallenge.canisterCount + 1);
        harvestableCount = Mathf.Max(1, TrialsChallenge.fieldSize);
        boulderCount = TrialsChallenge.boulderCount;
    }

    /// <summary>
    /// Installs the current planet's gravity as the gravity for the entire run.
    ///
    /// Physics.gravity is one global value, not a per-body setting, so this has to happen once
    /// and before anything is built: the terrain colliders, the buggy's WheelColliders, every
    /// Rigidbody prop and the hop rockets all read it, and they agree from the first frame.
    ///
    /// It is deliberately the planet's TUNED figure (LevelDefinition.planetGravity) rather than
    /// its real one. Those values sit above the true planetary gravities precisely so the canyon
    /// and ridge descents keep roughly the acceleration and top speeds they have always had -
    /// slope acceleration is g * sin(theta), so real lunar or Titan gravity would make every
    /// descent feel glacial. The light worlds therefore still feel lighter, they just do not
    /// lose the drive the courses are built around.
    ///
    /// The one exception is the CANYON RACE: wheel-to-wheel racing down a bumpy descent needs the
    /// tyres to stay planted, and a low-gravity world left even a stock rover skipping off every
    /// bump. A race load therefore runs at LevelManager.RaceGravity - the planet's figure pulled
    /// 80% of the way to Earth - so the buggy stays glued for controlled turns. Harvester drives
    /// and Ridge Survival keep the planet's own gravity (the ridge's long floaty hops are what
    /// keep its canisters reachable).
    ///
    /// The trade is tyre load: less gravity means less grip, which is what
    /// LevelDefinition.gripMultiplier trims (a lighter planet leaves less load on the tyres, so
    /// the Moon takes a small extra bite and Io a shade less; the rest sit near 1 on the shared
    /// high stock grip). Anything wanting higher jumps raises the planet's jumpApex, not its gravity.
    /// </summary>
    private void ApplyPlanetGravity()
    {
        LevelDefinition def = LevelManager.CurrentLevel;
        bool canyonRace = GameFlow.Mode == GameMode.Race;
        float g = def != null
            ? (canyonRace ? LevelManager.RaceGravity(LevelManager.CurrentLevelIndex) : def.planetGravity)
            : 9.81f;
        Physics.gravity = new Vector3(0f, -Mathf.Max(0.1f, g), 0f);
    }

    /// <summary>Copies the current planet's tuning onto this bootstrap before the level is built.</summary>
    private void ApplyLevel(LevelDefinition def)
    {
        if (def == null) return;
        podCount = def.podCount;
        podSpacing = def.podSpacing;
        boulderCount = def.boulderCount;
        if (GameFlow.Mode == GameMode.Prospector)
        {
            int grade = LevelManager.GetProspectorGrade(LevelManager.CurrentLevelIndex);
            int basePodCount = Mathf.Max(2, def.podCount);
            podCount = Mathf.Max(2, basePodCount - ProspectorPodsRemovedPerGrade * (grade - 1));
            // Keep the oxygen route spread across roughly the same canyon length as pods are
            // removed: harder grades mean fewer, farther-apart canisters, not an early-ending route.
            podSpacing = def.podSpacing * (basePodCount - 1f) / (podCount - 1f);
        }
        gripMultiplier = def.gripMultiplier;
        harvestableName = def.harvestableName;
        harvestableCount = def.harvestableCount;
        if (GameFlow.Mode == GameMode.Prospector)
        {
            // Prospector uses a small, tight ore field rather than the wide survey scatter:
            // grade 1 starts at 15 nuggets and grade 5 tops out at 25. The field is placed on
            // the canyon floor only (see BuildHarvestables), so every nugget is reachable and
            // the >=50% clear gate stays fair.
            int grade = LevelManager.GetProspectorGrade(LevelManager.CurrentLevelIndex);
            harvestableCount = ProspectorOrePerGrade[
                Mathf.Clamp(grade - 1, 0, ProspectorOrePerGrade.Length - 1)];
        }
        else if (GameFlow.Mode == GameMode.Harvester)
        {
            // Harvest grades shape the field: grade 1 starts slightly leaner than stock, then
            // each cleared run adds ~+30 deposits, so the bumpier, storm-pressed re-runs pay
            // the most (see LevelManager.HarvestGradeOre).
            harvestableCount += LevelManager.HarvestGradeOre(LevelManager.CurrentLevelIndex);
        }
        harvestableScore = def.harvestableScore;
        harvestableColor = def.harvestableColor;
    }

    private void BuildGameManager()
    {
        // Ensure a single OxygenManager exists so oxygen state is tracked.
        if (OxygenManager.Instance == null)
        {
            GameObject managerGo = new GameObject("GameManager");
            managerGo.AddComponent<OxygenManager>();
        }
        // Oxygen runs out faster on the harder planets; a Garage O2 Cell upgrade drains slower.
        // Ridge Survival overrides this after BuildGameManager (drain * DrainFactor with the
        // carry loop), so the tank pressure respects the garage there too. The equipped vehicle
        // trims the drain further (Mars Rover 0.5x - buggy is identity).
        bool upgraded = GameFlow.IsHarvestStyle;
        // Lunar Race has no oxygen clock: the downhill course (not the tank) is the pressure.
        // HARVEST grades add an air tax on top of the world's base drain (see
        // LevelManager.HarvestGradeDrainMul), so the richer, rougher, stormier re-run also costs
        // tank to work. Scoped to the harvest-shaped modes (Harvester + Prospector Run): a race
        // runs no clock, and Ridge Survival banks its own carried O2 between legs and must not
        // inherit a harvest tax.
        float gradeDrain = GameFlow.Mode == GameMode.Prospector
            ? LevelManager.ProspectorGradeDrainMul(LevelManager.CurrentLevelIndex)
            : GameFlow.Mode == GameMode.Harvester
                ? LevelManager.HarvestGradeDrainMul(LevelManager.CurrentLevelIndex)
                : 1f;
        OxygenManager.Instance.drainPerSecond = GameFlow.Mode == GameMode.Race
            ? 0f
            : LevelManager.CurrentLevel.drainPerSecond * gradeDrain * (upgraded ? UpgradeSystem.DrainFactor : 1f) * VehicleSystem.DrainMul;

        // The race clock must not run during the 3-2-1-GO countdown: freeze it here (before
        // any Update can tick) and let the countdown release it on "GO".
        if (GameFlow.Mode == GameMode.Race)
        {
            OxygenManager.Instance.clockPaused = true;
        }
    }

    // ---------------------------------------------------------------- race
    // Lunar Race: one fair, stock buggy on each planet's carved downhill canyon. The gates
    // are literal ARCHES across the lane (driven through, not collected for oxygen), the
    // timer ends when the buggy crosses the finish line, and a computer rival drives the
    // same line at a pace a clean player can beat. No oxygen clock and no upgrades; the
    // pressure is speed and precision against the walls, gates, rival and the meteor storm.
    private void BuildRace()
    {
        if (GameFlow.IsRidgeSurvival)
        {
            BuildRidgeSurvival();
            return;
        }
        // The frontier grade: one past the best grade cleared on this world (0 .. 4), so a
        // fresh world races grade one. Every rival is paced for it, AND the canyon course is
        // carved for it (harder grades narrow and twist the circuit - see MoonTerrainGen), so
        // it must be pinned before the terrain builds.
        GameFlow.RaceGradeIndex = Mathf.Clamp(
            LevelManager.GetRaceBestGrade(LevelManager.CurrentLevelIndex) + 1,
            0, RivalRacer.GradeCount - 1);
        ApplyLevel(LevelManager.CurrentLevel);
        BuildGameManager();
        BuildEnvironment();
        PlaceRaceStart();      // launch the buggy flat on the course mouth (never an upslope)
        BuildBuggy();
        if (buggy != null) buggy.inputEnabled = false; // locked until the countdown's "GO!"
        if (buggy != null) buggy.raceStuckReset = true; // rescue from collapse holes mid-run
        BuildDustPatches();
        BuildRaceGates();
        BuildLevelEnd();
        BuildCamera();
        BuildHUD();
        BuildRivalRacer();
        BuildHazards();        // the storm now harries race runs too (tuned lighter)
        BuildCountdown();      // 3-2-1-GO ceremony: held until the intro hands over, then fade-in + zoom + unlock on GO
        BuildSkyAndLighting();
        BuildLevelIntro();     // planet card + dolly reveal, then hands off to the countdown
    }

    // ------------------------------------------------------- ridge survival
    // The Ridge race played SURVIVAL-style: no gates, no rivals, no timeboard - just distance.
    // O2 canisters top up the survival tank, a fall off the ridge fades and ends the run, and
    // crossing the finish line without falling = a cleared leg. The hazard director steps its
    // ramp with every cleared leg, so the crest stays hairy the longer you rack up distance.
    private void BuildRidgeSurvival()
    {
        // A RETRY after a failed run (out of oxygen / fell off the ridge) starts over from leg one:
        // zero distance, full tank, pristine hull. A load after a CLEARED leg leaves the flag false,
        // so the boarding loop keeps its banked distance and carried O2 / hull.
        if (GameFlow.RidgeRunFailed)
        {
            GameFlow.ResetRidgeProgress();
        }
        gameObject.AddComponent<RidgeRunTracker>();   // flags this run if it ends in failure

        ApplyLevel(LevelManager.CurrentLevel);

        // Ridge Survival is sparser on the crest: half the canister chain a full race would
        // lay (podCount from the level tuning, e.g. 10 -> 5), so each depot is a real find.
        podCount = Mathf.Max(1, podCount / 2);

        BuildGameManager();

        // Ridge Survival carries the OXYGEN clock (Harvester-style canisters) - not the
        // race timer - so the pressure is keeping the tank alive along the ridge crest.
        // Drain speed comes from the planet (like Harvester), slowed by the garage O2
        // upgrade and any vehicle oxygen advantage.
        OxygenManager.Instance.drainPerSecond = LevelManager.CurrentLevel.drainPerSecond * UpgradeSystem.DrainFactor * VehicleSystem.DrainMul;
        OxygenManager.Instance.clockPaused = false;

        // Later legs KEEP the O2 the previous leg ended on (boarding froze the drain, so the
        // fresh tank here starts where the line-crossing left it). A fresh deploy stays full.
        OxygenManager o2 = OxygenManager.Instance;
        if (GameFlow.RidgeCarryOxygenPct >= 0f)
        {
            o2.SetOxygenFraction(GameFlow.RidgeCarryOxygenPct / 100f);
        }

        BuildEnvironment();
        PlaceRaceStart();
        BuildBuggy();
        if (buggy != null) buggy.inputEnabled = false;
        if (buggy != null) buggy.raceStuckReset = true;

        // Io's ridge vista: permanent volcanic peaks on the surrounding plains, purely scenic
        // (off the crest, never driveable) - the climb to the ridge reads like riding into a
        // live caldera field instead of a bare plain.
        BuildRidgeScenicVolcanoes();

        // Hull damage also persists through the boarding loop (fresh deploy = pristine).
        if (GameFlow.RidgeCarryHullPct >= 0f)
        {
            BuggyHealth bh = buggy != null ? buggy.GetComponent<BuggyHealth>() : null;
            if (bh != null) bh.SetHealthFraction(GameFlow.RidgeCarryHullPct / 100f);
        }

        BuildRidgeCanisters();      // O2 canisters instead of gate arches
        BuildDustPatches();         // uses the canister route segments
        BuildRidgeTargetMarkers();  // target distances painted across the crest
        BuildLevelEnd();
        BuildCamera();
        BuildHUD();
        BuildHazards();
        BuildRidgeFallDetector();
        BuildCountdown();           // 3-2-1-GO ceremony: reuses Race's countdown (empty rival list is fine)
        BuildSkyAndLighting();
        if (GameFlow.RidgeSurvivalLoops > 0)
        {
            // Past leg 1 the boarding loop drops straight back onto the crest: skip the
            // planet-card intro AND any leg menu - the 3-2-1-GO countdown (which the intro
            // would otherwise hand off to) opens the next leg on its own.
            if (raceCountdown != null) raceCountdown.enabled = true;
        }
        else
        {
            BuildLevelIntro();          // leg 1: planet card + dolly reveal + survival briefing
        }
    }

    /// <summary>
    /// Populates the O2 canister route down the ridge crest (as the gates used to do) and,
    /// critically, fills raceCheckpointZs BEFORE BuildHazards so the hazard director can keep
    /// collapse/boulder windows clear of the canisters. Canisters look like non-final pods
    /// (core/pillar/beacon) - the regular oxygen-pickup collection, re-skinned as crest depots.
    /// </summary>
    private void BuildRidgeCanisters()
    {
        podPositions.Clear();
        spawnedPods.Clear();

        float startZ = MoonTerrainGen.RaceStartZ;
        float finishZ = terrain.RaceFinishZ;
        float spacing = (finishZ - startZ) / (podCount + 1);

        for (int i = 0; i < podCount; i++)
        {
            float z = startZ + (i + 1) * spacing;
            float x = terrain.RaceCenterX(z);
            float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + 1.5f;
            CreatePod(new Vector3(x, y, z), false, i);
            podPositions.Add(new Vector3(x, 0f, z));
        }

        raceCheckpointZs.Clear();
        for (int i = 0; i < podPositions.Count; i++) raceCheckpointZs.Add(podPositions[i].z);
    }

    /// <summary>
    /// Paints the ridge crest's survival read-outs: thin GREEN target-distance strips (every
    /// 200 m, "X.XX KM" labels) so the player can glance how far the next survivable post is,
    /// plus the four GRADED CROSSING LINES (Bronze 1.5 km / Silver 3 km / Gold 4.5 km /
    /// Platinum 8 km) - each a thicker coloured strip that PAYS TOKENS the first time it is
    /// driven past. Green stays reserved for the cadence read so it reads "safe waypoint",
    /// never "hazard". All positions use the running session total (CUMULATIVE distance).
    /// </summary>
    private void BuildRidgeTargetMarkers()
    {
        if (terrain == null) return;
        float startZ = MoonTerrainGen.RaceStartZ;
        float finishZ = terrain.RaceFinishZ;
        float courseLen = finishZ - startZ;

        Material markMat = new Material(Shader.Find("Unlit/Color"));
        markMat.color = new Color(0.35f, 0.95f, 0.45f, 1f);

        Transform root = new GameObject("RidgeTargetMarkers").transform;
        root.SetParent(transform, false);

        float half = Mathf.Max(1f, terrain.raceRidgeCrestHalfWidth - 0.6f);
        for (float dz = 200f; dz < courseLen - 60f; dz += 200f)
        {
            float z = startZ + dz;
            float x = terrain.RaceCenterX(z);
            float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + 0.045f;

            GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "RidgeTargetMark";
            strip.transform.SetParent(root, false);
            strip.transform.position = new Vector3(x, y, z);
            strip.transform.localScale = new Vector3(half * 2f, 0.09f, 1.5f);
            MeshRenderer mr = strip.GetComponent<MeshRenderer>();
            mr.sharedMaterial = markMat;
            DestroyImmediate(strip.GetComponent<Collider>());

            GameObject labelGo = new GameObject("RidgeTargetLabel");
            labelGo.transform.position = new Vector3(x, y + 2.6f, z);
            TextMesh tm = labelGo.AddComponent<TextMesh>();
            tm.font = HUD.ResolveDefaultFont();
            tm.text = OxygenManager.FormatKm(GameFlow.RidgeSurvivalMeters + dz);
            tm.fontSize = 40;
            tm.characterSize = 0.06f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.6f, 1f, 0.7f, 1f);
            MeshRenderer lmr = labelGo.GetComponent<MeshRenderer>();
            if (lmr != null && tm.font != null) lmr.sharedMaterial = tm.font.material;
        }

        // The four graded TOKEN crossing lines. A tier only paints once its cumulative
        // distance lands somewhere in the CURRENT leg - so it appears the very leg it will
        // be crossed, at the moment the player first drives over it, and already-banked
        // (behind) tiers stay off the fresh crest.
        for (int g = 0; g < GameFlow.RidgeGradeTokens.Length; g++)
        {
            float rel = GameFlow.RidgeGradeDistances[g] - GameFlow.RidgeSurvivalMeters;
            if (rel < 40f || rel > courseLen) continue;
            float z = startZ + rel;
            float x = terrain.RaceCenterX(z);
            float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + 0.06f;
            Color col = GameFlow.RidgeGradeColors[g];

            GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "RidgeGradeLine_" + GameFlow.RidgeGradeNames[g];
            strip.transform.SetParent(root, false);
            strip.transform.position = new Vector3(x, y, z);
            strip.transform.localScale = new Vector3(half * 2f, 0.14f, 1.9f);
            Material gradeMat = new Material(Shader.Find("Unlit/Color"));
            gradeMat.color = col;
            strip.GetComponent<MeshRenderer>().sharedMaterial = gradeMat;
            DestroyImmediate(strip.GetComponent<Collider>());

            GameObject labelGo = new GameObject("RidgeGradeLabel_" + GameFlow.RidgeGradeNames[g]);
            labelGo.transform.position = new Vector3(x, y + 2.9f, z);
            TextMesh tm = labelGo.AddComponent<TextMesh>();
            tm.font = HUD.ResolveDefaultFont();
            tm.text = GameFlow.RidgeGradeNames[g] + "  " + OxygenManager.FormatKm(GameFlow.RidgeGradeDistances[g]);
            tm.fontSize = 46;
            tm.characterSize = 0.06f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = col;
            MeshRenderer lmr = labelGo.GetComponent<MeshRenderer>();
            if (lmr != null && tm.font != null) lmr.sharedMaterial = tm.font.material;

            GameObject payGo = new GameObject("RidgeGradePay_" + GameFlow.RidgeGradeNames[g]);
            payGo.transform.position = new Vector3(x, y + 2.3f, z);
            TextMesh ptm = payGo.AddComponent<TextMesh>();
            ptm.font = HUD.ResolveDefaultFont();
            ptm.text = "+" + GameFlow.RidgeGradeTokens[g] + " TOKENS";
            ptm.fontSize = 30;
            ptm.characterSize = 0.05f;
            ptm.anchor = TextAnchor.MiddleCenter;
            ptm.alignment = TextAlignment.Center;
            ptm.color = Color.Lerp(col, Color.white, 0.35f);
            MeshRenderer plmr = payGo.GetComponent<MeshRenderer>();
            if (plmr != null && ptm.font != null) plmr.sharedMaterial = ptm.font.material;
        }
    }

    /// <summary>Watches for the buggy dropping off the ridge flank: fades the screen to black and
    /// calls FallOffRidge once the fade is done. Placed here (after BuildHazards) so the fall
    /// detector hooks straight into the fully wired O2 system.</summary>
    private void BuildRidgeFallDetector()
    {
        GameObject go = new GameObject("RidgeFallDetector");
        RidgeFallDetector d = go.AddComponent<RidgeFallDetector>();
        d.buggy = buggy;
        d.terrain = terrain;
    }

    /// <summary>Puts the race buggy on the course's centre line at the very start of the course,
    /// facing down-course (+Z). The surface there is the anchor the whole course descends from,
    /// so the launch pad is always flat (canyon floor or ridge crest).</summary>
    private void PlaceRaceStart()
    {
        float sx = terrain.RaceCenterX(MoonTerrainGen.RaceStartZ);
        float sy = terrain.SampleHeight(new Vector3(sx, 0f, MoonTerrainGen.RaceStartZ)) + 1.5f;
        startPosition = new Vector3(sx, sy, MoonTerrainGen.RaceStartZ);
    }
    /// <summary>Builds the race gate ARCHES along the canyon's centre line, evenly spaced so
    /// the chain reads like the Harvester oxygen route. Each gate must be DRIVEN THROUGH -
    /// the arch owns a wide box trigger, and the tracker below warns the player whenever a
    /// gate is driven past without being passed through.</summary>
    private void BuildRaceGates()
    {
        podPositions.Clear();
        spawnedPods.Clear();

        float startZ = MoonTerrainGen.RaceStartZ;
        float finishZ = terrain.RaceFinishZ;
        float spacing = (finishZ - startZ) / (podCount + 1);

        for (int i = 0; i < podCount; i++)
        {
            bool isFinal = (i == podCount - 1);
            float z = startZ + (i + 1) * spacing;
            float x = terrain.RaceCenterX(z);
            float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + 1.5f;

            GameObject podGo = CreatePod(new Vector3(x, y, z), isFinal, i);
            // The arch spans the drive lane: pylons sit on the course floor/crest, inside the
            // dropoffs on the Ridge (narrow crest) and on the flat lane for the Canyon.
            float archHalf = GameFlow.CurrentCourse == RaceCourse.Ridge
                ? terrain.raceRidgeCrestHalfWidth + 1.5f
                : 12.5f;
            RaceGateManager.BuildArch(podGo.GetComponent<OxygenPod>(), archHalf, 7.0f,
                isFinal ? "FINISH" : "GATE " + (i + 1));
            podPositions.Add(new Vector3(x, 0f, z));
        }

        // Remember every gate's course Z so the hazard director can keep collapsing sections
        // and boulder drops clear of checkpoints (a gate you cannot drive through = unfair).
        raceCheckpointZs.Clear();
        for (int i = 0; i < podPositions.Count; i++) raceCheckpointZs.Add(podPositions[i].z);

        GameObject mgrGo = new GameObject("RaceGateManager");
        RaceGateManager mgr = mgrGo.AddComponent<RaceGateManager>();
        mgr.buggy = buggy;
        mgr.hud = hud;
        mgr.oxygen = OxygenManager.Instance;
        mgr.gates = spawnedPods;
        mgr.courseStartZ = startZ;
        mgr.courseEndZ = finishZ;
    }

    /// <summary>Spawns the trio of computer-controlled rival buggies that race the player
    /// down the canyon - the same Bronze / Silver / Gold field every race, timed for the run's
    /// current grade (grade 0 is the default pace; each harder grade quickens every rival - 5s
    /// on the Moon, 1s on Mars which opens at the Moon's pace, while Venus/Io/Titan keep their
    /// friendlier campaign pacing and only ease 1s between grades - see RivalRacer.DefaultSeconds).
    /// The grade is
    /// the world's next un-cleared one (frontier), so beating the whole field clears it and the
    /// next, faster field is unlocked for the next attempt.
    ///
    /// The countdown ceremony holds them all at the grid until "GO". The field launches
    /// from a STAGGERED GRID: the player keeps pole (the line itself) and the rivals line up
    /// in rows AHEAD of it - two abreast on the wide Canyon floor, single file on the narrow
    /// Ridge crest - spaced so nothing overlaps at GO. The rows are part of the race: a rival
    /// that starts ahead of you has to be chased down, the classic last-place arcade start.</summary>
    private void BuildRivalRacer()
    {
        rivals.Clear();
        bool ridge = GameFlow.CurrentCourse == RaceCourse.Ridge;
        RivalRacer.Tier[] tiers = { RivalRacer.Tier.Bronze, RivalRacer.Tier.Silver, RivalRacer.Tier.Gold };

        // The frontier grade was pinned at the top of BuildRace (before the terrain was carved,
        // so the course geometry already matches it). Every rival is paced for that grade.
        int grade = Mathf.Clamp(GameFlow.RaceGradeIndex, 0, RivalRacer.GradeCount - 1);

        // Grid slots (player holds Z=0 rear-centre, pole). Canyon: a neat 2-1 stagger - a row
        // of two abreast at +/-6m with the front car centred a row up - so the field reads as a
        // proper starting grid and the player can pick a lane through it. Ridge (crest only ~7m
        // wide): a single-file stagger at +/-3m so no one is pushed off the crest before the
        // grid melts.
        float[] rows = ridge ? new float[] { 8f, 14f, 20f } : new float[] { 8f, 8f, 15f };
        float[] cols = ridge ? new float[] { -3f, 3f, -3f } : new float[] { 6f, -6f, 0f };

        for (int i = 0; i < tiers.Length; i++)
        {
            GameObject rivalGo = new GameObject("RivalRacer_" + RivalRacer.TierName(tiers[i]));
            RivalRacer rival = rivalGo.AddComponent<RivalRacer>();
            rival.tier = tiers[i];
            rival.rivalTargetSeconds = RivalRacer.DefaultSeconds(tiers[i], grade);
            // Tighter lane control as the tiers climb: gold stays on line, bronze meanders.
            rival.laneSwing = 3f + (float)i;         // bronze 3, silver 4, gold 5
            rival.speedWobble = 0.05f - 0.01f * i;   // bronze 0.05, silver 0.04, gold 0.03
            rival.startRow = rows[i];
            rival.startColumn = cols[i];
            rival.terrain = terrain;
            rival.player = buggy;
            // Build and stand the field on the grid NOW and hold it there, so the rivals are
            // visible through the level intro and the 3-2-1 ceremony instead of popping in at GO.
            rival.Initialize();
            rival.SetHeld(true);
            rivals.Add(rival);
        }
    }

    /// <summary>Builds the 3-2-1-GO start ceremony (black fade-in, countdown numbers, camera
    /// zoom from a wide reveal to the chase shot). It unlocks the buggy, starts the race clock
    /// and releases every rival on "GO". The component is created DISABLED: the level intro
    /// reveals the course first, then re-enables it (skipFadeIn) so the ceremony drops straight
    /// into 3-2-1-GO without a second fade-from-black.</summary>
    private void BuildCountdown()
    {
        GameObject cdGo = new GameObject("RaceCountdown");
        RaceCountdown cd = cdGo.AddComponent<RaceCountdown>();
        cd.buggy = buggy;
        cd.rivals = rivals.ToArray();
        cd.oxygen = OxygenManager.Instance;
        cd.skipFadeIn = true;   // the intro already showed the planet card + live dolly
        cd.enabled = false;     // held until the intro hands off
        raceCountdown = cd;
    }

    /// <summary>Builds the level-opening cinematic: planet backdrop card (user art or a
    /// procedural fallback), scripted dolly/orbit reveal of the course via BuggyCamera's
    /// cinematic shot, planet + course title card, then handoff - the race countdown for
    /// Lunar Race, direct driving control for Harvester.
    ///
    /// LUNAR TRIALS: no cinematic at all. Challenges land straight in control with the
    /// objective as a HUD hint, the run manager starts here (the buggy exists now), and the
    /// GameplayStart event fires immediately - a trials challenge IS gameplay.</summary>
    private void BuildLevelIntro()
    {
        if (TrialsChallenge != null)
        {
            buggy.inputEnabled = true;
            if (OxygenManager.Instance != null) OxygenManager.Instance.clockPaused = false;
            CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.GameplayStart);
            AdCoordinator.SetGameplayLive(true);
            TrialsRunManager.StartChallenge(TrialsChallenge, buggy);
            if (hud != null)
            {
                // Timed hint: the persistent hint dims the whole centre screen - wrong for a
                // challenge that starts in motion. Auto-fade after a few seconds.
                hud.ShowHint(TrialsChallenge.title + " - " + TrialsChallenge.brief, 6f);
            }
            Debug.Log("[Trials] challenge start: " + TrialsChallenge.id
                + " (ore " + TrialsChallenge.oreTarget + "/" + TrialsChallenge.fieldSize
                + ", boulders " + TrialsChallenge.boulderCount + ", canisters "
                + TrialsChallenge.canisterCount + ")");
            return;
        }

        GameObject introGo = new GameObject("LevelIntroSequence");
        LevelIntroSequence intro = introGo.AddComponent<LevelIntroSequence>();
        intro.buggy = buggy;
        intro.terrain = terrain;
        intro.oxygen = OxygenManager.Instance;
        intro.countdown = raceCountdown;
    }

    // ---------------------------------------------------------------- survival
    // Lunar Survival: one continuous effort on a large static map. The route of oxygen
    // canisters loops forever; a SurvivalDirector ramps difficulty after every set of 8.
    // A red Earth hangs on the horizon and darkens as the fog closes in with each set.
    private SurvivalEarth survivalEarth;

    private void BuildSurvival()
    {
        // Start from the Moon's handling tuning; SurvivalDirector ramps from there.
        ApplyLevel(LevelManager.CurrentLevel);
        BuildGameManager();
        BuildEnvironment(true);
        BuildBuggy();
        // Survival keeps driving all over the map, so widen the reset leash well past the edge.
        buggy.maxTravelFromSpawn = 3400f;
        BuildSkyAndLighting();
        BuildSurvivalEarth();
        BuildCamera();
        BuildHUD();

        GameObject dirGo = new GameObject("SurvivalDirector");
        SurvivalDirector dir = dirGo.AddComponent<SurvivalDirector>();
        dir.terrain = terrain;
        dir.buggy = buggy;
        dir.oxygen = OxygenManager.Instance;
        dir.hud = hud;
        dir.survivalEarth = survivalEarth;
        dir.spawnPod = (pos, index) => CreatePod(pos, false, index);
        dir.spawnHazard = (pos, index) => CreateBoulder(pos, Random.Range(3.5f, 6.5f), 1000 + index);
    }

    /// <summary>Puts the red Earth on the Survival horizon (unlit, fog-tinted).</summary>
    private void BuildSurvivalEarth()
    {
        GameObject earthGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        earthGo.name = "SurvivalEarth";
        survivalEarth = earthGo.AddComponent<SurvivalEarth>();
        // High on the far horizon, beyond the playable arena, facing the starting heading.
        survivalEarth.Build(new Vector3(0f, 360f, 2450f), 520f);
    }

    // ---------------------------------------------------------------- environment
    private void BuildEnvironment(bool survival = false)
    {
        // The trials course is designed BEFORE the terrain builds: MoonTerrainGen keeps the
        // drive corridor clean of craters (the same trick the race lane uses), so the loop is
        // always drivable. Deterministic - Random is already seeded with the challenge seed.
        TrialsCourse.Design(TrialsChallenge, new Vector2(startPosition.x, startPosition.z));
        // A challenge with a ramp also grades a downhill run-in into the corridor before it
        // (must be set before the terrain builds - mesh and SampleHeight both apply it).
        if (TrialsCourse.Active != null && TrialsChallenge != null && TrialsChallenge.rampAtS >= 0f)
            TrialsCourse.Active.SetDive(TrialsChallenge.rampAtS);

        GameObject terrainGo = new GameObject("MoonTerrain");
        terrain = terrainGo.AddComponent<MoonTerrainGen>();
        if (survival) terrain.extent = 1600f; // a bigger arena for the looping route
        if (TrialsChallenge != null)
        {
            // Trials: small focused arenas. The terrain's default 260 rocks / 220 craters are
            // tuned for the 1440 m survey map - unscaled they quadruple in density on a 560 m
            // arena and bury the spawn line. A lighter scatter keeps the floor readable.
            terrain.extent = 560f;
            terrain.rockCount = 90;
            terrain.craterCount = 90;
        }
        terrain.Build(LevelManager.CurrentLevel);
        terrainGo.transform.position = Vector3.zero;

        // A distant ring of rolling hills beyond the playfield rim so the horizon reads as a
        // range instead of a bare flat line. Cosmetic and fog-independent (unlit vertex-colour
        // shader); present in every mode, esp. on worlds where the sky is otherwise empty.
        HorizonHills.Build(terrain.extent, LevelManager.CurrentLevel);
    }

    // ------------------------------------------------------------------- buggy
    private void BuildBuggy()
    {
        GameObject buggyGo = new GameObject("Buggy");

        // Rigidbody
        Rigidbody rb = buggyGo.AddComponent<Rigidbody>();
        rb.mass = 330f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 2.5f;

        // Controller
        buggy = buggyGo.AddComponent<BuggyController>();
        RivalRacer.PlayerBuggy = buggy;

        // Apollo LRV drive audio: geared DC motor hum rising with speed + harvest chime.
        buggyGo.AddComponent<RoverSounds>();

        // Hull integrity: impacts with boulders damage the buggy; three heavy hits destroy it.
        BuggyHealth buggyHealth = buggyGo.AddComponent<BuggyHealth>();

        // Venus: the crushing atmosphere chips the hull 1% every 2s (0.5%/s) while the run is
        // live, in both Harvester and Race (paused during the intro/ pre-race countdown via input
        // lock). originalMaxHealth is captured NOW - pre-upgrade - so the chip rate is measured
        // against the stock hull and Hull Garage improvements stretch the survival window.
        if (CourseHazards.HasCrushingPressure(LevelManager.CurrentLevel.planetName))
        {
            VenusPressure pressure = buggyGo.AddComponent<VenusPressure>();
            pressure.health = buggyHealth;
            pressure.buggy = buggy;
            pressure.originalMaxHealth = buggyHealth.maxHealth;
        }

        // Garage upgrades bought with TOKENS permanently tune this build's vehicle - the same
        // tuning in BOTH Harvester AND Race (against computer rivals that's fair play; a future
        // player-vs-player mode fixes one stock set instead). Survival is the separate stock
        // arcade: no upgrades, so everyone is on equal footing.
        // The component Awakes already ran (assets set to defaults), so scale both stat and state.
        bool upgraded = GameFlow.Mode == GameMode.Harvester || GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival;
        buggy.maxAccelerationTorque *= upgraded ? UpgradeSystem.EngineFactor : 1f;
        buggy.maxAccelerationTorque *= VehicleSystem.TorqueMul;
        buggy.maxBoost *= upgraded ? UpgradeSystem.BoostFactor : 1f;
        buggy.maxBoost *= VehicleSystem.BoostMul;
        buggy.RechargeToMaxBoost();
        buggyHealth.ApplyMaxHealthMultiplier((upgraded ? UpgradeSystem.HullFactor : 1f) * VehicleSystem.HullMul);
        buggyHealth.damagePerSpeed *= upgraded ? UpgradeSystem.HullDamageFactor : 1f;
        buggyHealth.damagePerSpeed *= VehicleSystem.HullDamageMul;

        // Lunar Race baseline: the same stock rover, but set up for the downhill canyon. A
        // lighter anti-roll lets a hard wall-carve swing the nose (real risk and reward on the
        // drop), and a higher top speed makes the straights fast.
        if (GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival)
        {
            buggy.maxSpeedKmh = 38f;
            buggy.boostSpeedKmh = 48f;
            // The canyon drops and the ridge crest were all laid out against Earth's
            // gravity, and every world but Venus now runs lighter - which costs real time on the
            // way down. Hand back a share of each world's lost gravity so the courses keep the
            // pace they were built for; on the ridge it is also what keeps the oxygen canisters
            // reachable on a stock buggy. This is applied to the top-speed SHELVES, not as a
            // shove down the course: the shelves clamp plane speed every step, so a forward
            // force would simply be fought back off once the buggy was at the cap and would
            // only speed up the run-up. On the CANYON RACE the gravity itself has already been
            // pulled close to Earth (see ApplyPlanetGravity), so only the small residual deficit
            // is topped up here; Ridge Survival still reads the planet's own gravity.
            float lift = LevelManager.DownhillSpeedLift(
                LevelManager.CurrentLevelIndex, GameFlow.Mode == GameMode.Race);
            buggy.maxSpeedKmh *= lift;
            buggy.boostSpeedKmh *= lift;
            buggy.antiRollTorque = 11f;
            buggy.antiRollThreshold = 20f;
            // Canyon race only: a gentle speed-scaled ground-glue that keeps the tyres planted
            // over the bumpy descent for controlled turns (see BuggyController.raceDownforce).
            // Ridge Survival keeps its lighter, hoppier feel.
            buggy.raceDownforce = GameFlow.Mode == GameMode.Race ? 0.8f : 0f;
            buggy.maxTravelFromSpawn = 1700f; // the canyon runs well past the plain's leash
        }

        // Top-speed shelves scale with the garage in BOTH modes, by the SAME amounts: the
        // ENGINE raises the flat-out cruise speed, the BOOST CELLS raise the sprint cap.
        // Applied after the race baseline, so the downhill course keeps its 38/48 edge over
        // Harvester's 32/42 while upgrading identically. HOP ROCKETS scale the jump too.
        if (upgraded)
        {
            buggy.maxSpeedKmh *= UpgradeSystem.EngineFactor;
            buggy.boostSpeedKmh *= UpgradeSystem.BoostFactor;
            buggy.jumpFactor = UpgradeSystem.JumpFactor;
        }

        // The equipped vehicle's pace + hop sit on TOP of all of the above and apply in every
        // mode (the buggy is identity, so a stock Lunar Buggy is untouched): the Mars Rover
        // cruises and sprints slower and hops lower - it is a harvester, not a racer.
        buggy.maxSpeedKmh *= VehicleSystem.SpeedMul;
        buggy.boostSpeedKmh *= VehicleSystem.BoostMul;
        buggy.jumpFactor *= VehicleSystem.JumpMul;

        // How high this world's hop rockets throw the chassis. Taken from the planet so each
        // world can float higher without its downhill handling being touched; the buggy turns
        // it into a launch speed against whatever gravity ApplyPlanetGravity installed.
        LevelDefinition planet = LevelManager.CurrentLevel;
        if (planet != null) buggy.jumpApex = planet.jumpApex;

        // Physical body collider: this is essential. A buggy with ONLY trigger colliders
        // will fall straight through the terrain (triggers don't collide). This box rests
        // the chassis on the ground and prevents the buggy from ever falling through.
        BoxCollider bodyCollider = buggyGo.AddComponent<BoxCollider>();
        bodyCollider.center = new Vector3(0f, 0.75f, 0f);
        bodyCollider.size = new Vector3(2.4f, 1.1f, 3.6f);

        // A trigger volume around the buggy so oxygen pod pickups register reliably,
        // independent of the wheel colliders (which are special-cased in physics).
        // The radius stays FIXED here: the plow upgrade makes harvestables easier to sweep
        // up (see Harvestable.CurrentPickupRadius below), and must NOT loosen the pods'
        // 3.0 m pickup, which already has generous reach.
        SphereCollider pickupTrigger = buggyGo.AddComponent<SphereCollider>();
        pickupTrigger.isTrigger = true;
        pickupTrigger.radius = 2.2f;
        pickupTrigger.center = new Vector3(0f, 1f, 0f);
        buggyGo.gameObject.tag = "Player";

        // SCOOP = the gather blade. Every rover collects with its own stock blade - a 1.0 m
        // radius on the stock buggy, widened/tightened by the equipped vehicle's own rating
        // (Mars Rover 2x, Venus sail 1.4x, Lunar Sprint 0.3x). The Garage Scoop stat is gone:
        // collecting is a stock ability, the line (not a wider blade) is the harvest skill.
        // Configured here BEFORE any nuggets Awake so their colliders get it.
        Harvestable.CurrentPickupRadius = 1.0f * VehicleSystem.ScoopMul;

        // Vehicle cosmetics: build the equipped vehicle's own procedural body and skins
        // (Lunar Buggy = the Apollo LRV paint; Mars Rover = its own Perseverance-style body).
        // The physics built above is untouched - every part is decorative.
        bool isRover = VehicleSystem.IsRover;
        vehicleMats = isRover
            ? MarsRoverArt.CreateMats(MarsRoverArt.SkinOf(VehicleSystem.SkinIndex), true)
            : LunarRoverArt.CreateMats(LunarRoverArt.SkinOf(VehicleSystem.SkinIndex), true);
        int hullLevel = upgraded ? UpgradeSystem.InfoOf("hull").level : 0;
        if (hullLevel > 0)
        {
            // Hull Plate tiers tarnish the frame toward forged steel, as the old box did.
            Color tarnish = Color.Lerp(vehicleMats.primary.color, new Color(0.62f, 0.63f, 0.66f, 1f), Mathf.Clamp01(0.12f * hullLevel));
            vehicleMats.primary.color = tarnish;
        }

        // Build the chassis + wheels. LUNAR TRIALS: the Sprint slot is the TRIALS BIKE -
        // its own model rides on a 2-wheel in-line collider rig (no LRV chassis at all).
        if (VehicleSystem.IsSprint)
        {
            WheelCollider bikeFront = MakeWheel(buggyGo.transform, new Vector3(0f, 0f, 0.85f), 0.45f, 0.3f);
            WheelCollider bikeRear = MakeWheel(buggyGo.transform, new Vector3(0f, 0f, -0.75f), 0.45f, 0.3f);
            buggy.frontLeftWheel = bikeFront;
            buggy.frontRightWheel = bikeFront;
            buggy.rearLeftWheel = bikeRear;
            buggy.rearRightWheel = bikeRear;

            var rig = buggyGo.AddComponent<TrialsBikeRig>();
            rig.Build(buggy, buggyGo.transform, buggyGo.transform.position.y - 0.45f);
            buggy.frontLeftVisual = rig.frontSpin;
            buggy.frontRightVisual = rig.frontSpin;
            buggy.rearLeftVisual = rig.rearSpin;
            buggy.rearRightVisual = rig.rearSpin;
        }
        else
        {
            Transform chassis = BuildChassis(buggyGo.transform);
            BuildWheelPair(buggyGo.transform, chassis, false, new Vector3(1.1f, 0f, 1.55f), new Vector3(-1.1f, 0f, 1.55f));   // front
            BuildWheelPair(buggyGo.transform, chassis, true, new Vector3(1.1f, 0f, -1.55f), new Vector3(-1.1f, 0f, -1.55f));   // rear
        }
        // Lunar/planetary regolith kicks up behind the rear wheels the faster the buggy goes.
        Color reg = LevelManager.CurrentLevel.regolithColor;
        DustTrail dust = buggyGo.AddComponent<DustTrail>();
        dust.Attach(buggy, buggyGo.transform, Color.Lerp(reg, Color.white, 0.3f));

        // Passive proximity sonar: pings as the rover nears a field of visible ore clusters.
        buggyGo.AddComponent<ClusterSonar>();

        buggyGo.transform.position = startPosition;
        // Rest the buggy gently on the actual terrain surface so it doesn't spawn embedded
        // in a hill or fall far.
        if (terrain != null)
        {
            float groundY = terrain.SampleHeight(startPosition);
            buggyGo.transform.position = new Vector3(startPosition.x, groundY + 2f, startPosition.z);
        }
    }

    private Transform BuildChassis(Transform parent)
    {
        // The equipped vehicle's static body: Apollo LRV (tubular frame, crew seats, etc.),
        // the speed-built Lunar sprint buggy, the Venus landsail rover, or Mars Rover
        // (boxy avionics box, mast, RTG).
        Transform sailPivot = null;
        if (VehicleSystem.IsRover) MarsRoverArt.BuildChassis(parent, vehicleMats);
        else if (VehicleSystem.IsSprint) LunarRoverArt.BuildSprintChassis(parent, vehicleMats);
        else if (VehicleSystem.IsSail) VenusSailArt.BuildChassis(parent, vehicleMats, out sailPivot);
        else LunarRoverArt.BuildChassis(parent, vehicleMats);

        // The Favonius's wingsail is worked by a rig that solves the apparent wind every physics
        // step and turns the very pivot returned above - so the wing the player watches is the
        // wing the thrust is coming off. The rig is only live where the world has standing wind:
        // Venus, its home atmosphere, and the Mars ridge. The wing is still built and visible
        // anywhere (a static sail with no wind to work it elsewhere) but carries no thrust, and
        // the normal drive controls stay in charge.
        if (VehicleSystem.IsSail)
        {
            SailRig rig = buggy.gameObject.AddComponent<SailRig>();
            rig.sailPivot = sailPivot;
            Transform flap = sailPivot != null ? sailPivot.Find("Fav_SailFlap") : null;
            rig.sailFlap = flap;

            PlanetTheme sailTheme = CourseHazards.ThemeFor(LevelManager.CurrentLevel.planetName);
            // Mirrors the level setup's own "ridge" test, so the sail's idea of the ridge can
            // never disagree with the wind the level actually spawned.
            bool sailRidge = (GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival)
                          && GameFlow.CurrentCourse == RaceCourse.Ridge;
            rig.enabled = SailRig.SailWorld(sailTheme, sailRidge);
            rig.ApplyAtmosphere(sailTheme, sailRidge);
        }

        // The Garage Hull Plate upgrade adds a front plow: it protects the hull and sweeps
        // harvestables in wide arcs (the pickup trigger radius above is also enlarged).
        // Two-tone: dark steel blade with an orange warning lip over it. Survival stays stock.
        int hullLevel = GameFlow.Mode == GameMode.Harvester ? UpgradeSystem.InfoOf("hull").level : 0;
        if (hullLevel > 0)
        {
            GameObject plow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plow.name = "Plow";
            plow.transform.SetParent(parent, false);
            plow.transform.localScale = new Vector3(2.6f, 0.5f, 0.28f);
            plow.transform.localPosition = new Vector3(0f, 0.42f, 2.15f);
            MeshRenderer mr4 = plow.GetComponent<MeshRenderer>();
            mr4.sharedMaterial = MakeSolid(new Color(0.62f, 0.63f, 0.66f, 1f), 0.75f);
            DestroyImmediate(plow.GetComponent<Collider>());

            GameObject plowLip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plowLip.name = "PlowLip";
            plowLip.transform.SetParent(parent, false);
            plowLip.transform.localScale = new Vector3(2.6f, 0.12f, 0.3f);
            plowLip.transform.localPosition = new Vector3(0f, 0.63f, 2.2f);
            MeshRenderer mr5 = plowLip.GetComponent<MeshRenderer>();
            mr5.sharedMaterial = MakeSolid(new Color(0.9f, 0.45f, 0.18f, 1f), 0.85f);
            DestroyImmediate(plowLip.GetComponent<Collider>());
        }

        return parent;
    }

    private void BuildWheelPair(Transform parent, Transform chassis, bool drive, Vector3 leftLocal, Vector3 rightLocal)
    {
        // Wheel colliders.
        WheelCollider left = MakeWheel(parent, leftLocal, 0.76f, 0.47f);
        WheelCollider right = MakeWheel(parent, rightLocal, 0.76f, 0.47f);

        // Visual wheels.
        Transform leftVis = MakeWheelVisual(parent, leftLocal);
        Transform rightVis = MakeWheelVisual(parent, rightLocal);

        if (drive)
        {
            buggy.rearLeftWheel = left;
            buggy.rearRightWheel = right;
            buggy.rearLeftVisual = leftVis;
            buggy.rearRightVisual = rightVis;
        }
        else
        {
            buggy.frontLeftWheel = left;
            buggy.frontRightWheel = right;
            buggy.frontLeftVisual = leftVis;
            buggy.frontRightVisual = rightVis;
        }
    }

    private WheelCollider MakeWheel(Transform parent, Vector3 localPos, float radius, float width)
    {
        GameObject go = new GameObject("WheelCollider");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        WheelCollider wc = go.AddComponent<WheelCollider>();
        wc.radius = radius;
        wc.suspensionDistance = 0.4f;
        wc.mass = 12f;
        var spring = wc.suspensionSpring;
        spring.spring = 10000f;
        spring.damper = 200f;
        spring.targetPosition = 0.5f;
        wc.suspensionSpring = spring;

        // Friction so it can grip the surface. BASE GRIP IS DELIBERATELY HIGH AND UNIFORM now
        // (1.0 forward / 0.9 sideways) so every chassis reads the same: the handling is tuned
        // around one shared, grippy baseline, and the HANDBRAKE (BuggyController's drift) is what
        // deliberately breaks traction - not loose stock tyres. The Garage Grip Treads stat is
        // gone: stock IS the good level. The planet's gripMultiplier only trims the feel (Moon
        // a touch more bite for its lighter tyre load, Io a touch less for sulphur crumble, the
        // rest near 1), and GripMul is the vehicle's own rating folded in last (every vehicle
        // right now runs 1; a future terrain/off-road grip variant could lean on it again).
        float grip = gripMultiplier * VehicleSystem.GripMul;
        var fwd = wc.forwardFriction;
        fwd.stiffness = 1.0f * grip;
        wc.forwardFriction = fwd;
        var side = wc.sidewaysFriction;
        side.stiffness = 0.9f * grip;
        wc.sidewaysFriction = side;

        // Match the visual's local offset: collider origin sits at the axle.
        return wc;
    }

    private LunarRoverArt.RoverMats vehicleMats;
    private const float WheelRadius = 0.76f;

    private Transform MakeWheelVisual(Transform parent, Vector3 localPos)
    {
        // The equipped vehicle's wheel visual on the same axle positions: Apollo LRV wire-mesh
        // tyre or Mars Rover cleated barrel. Axle = X so the spin rotation in BuggyController
        // rolls it. Radius matches the WheelCollider.
        if (VehicleSystem.IsRover) return MarsRoverArt.BuildWheelVisual(parent, localPos, vehicleMats, WheelRadius, 0.35f);
        return LunarRoverArt.BuildWheelVisual(parent, localPos, vehicleMats, WheelRadius, 0.35f);
    }

    // --------------------------------------------------------------- boulders
    private void BuildBoulders()
    {
        // Scatter big rocks beside the route (mostly +Z, weaving like the pods) so the
        // player must steer around them. Kept off the exact centre line so they are
        // avoidable, with a small chance of a tighter rock for a spicy mid-route dodge.
        // PROSPECTOR RUN is a harvest-shaped descent, so it keeps the scattered rocks.
        if (!GameFlow.IsHarvestStyle) return;

        // PROSPECTOR RUN: the canyon IS the obstacle course. The open-field scatter below would
        // throw rocks 18-70 m off the line - straight up the cliff walls (the lane is only
        // ~12-14 m wide), which is why so few rocks showed on the floor. Instead plant a slalom
        // of boulders ACROSS the lane between the cannisters: each one hugs one side of the
        // centre line with a clear gap on the far side, so every rock is a real steer-around
        // obstacle and the descent is a careful drive, not a full-speed hill.
        if (GameFlow.Mode == GameMode.Prospector && terrain != null)
        {
            BuildProspectorBoulders();
            return;
        }

        // HARVEST grades densify this open-field scatter: each grade above 1 adds
        // LevelManager.HarvestGradeBoulders rocks, packed into the SAME total route length as
        // grade 1 (the spacing shrinks as the count grows) so the extra rocks land along the
        // driven corridor instead of marching past the terrain edge. Grade 1 is unchanged.
        // LUNAR TRIALS: the challenge's own boulder count IS the obstacle set - campaign
        // grade densification must not sneak extra rocks in.
        int count = ChallengeRunner.Active != null
            ? boulderCount
            : boulderCount + LevelManager.HarvestGradeBoulders(LevelManager.CurrentLevelIndex);
        float spacing = count > 0 ? boulderSpacing * boulderCount / count : boulderSpacing;
        float jitter = Mathf.Min(25f, spacing * 0.25f);

        Vector3 prev = startPosition;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = prev;
            pos.z = prev.z + spacing + Random.Range(-jitter, jitter);

            float lateral = Random.Range(18f, 70f);
            if (Random.value < 0.3f) lateral = Random.Range(7f, 18f); // occasional close call
            if (Random.value < 0.5f) lateral = -lateral;
            pos.x = prev.x + lateral;
            pos.y = terrain.SampleHeight(pos);

            CreateBoulder(pos, Random.Range(4.5f, 8.5f), i);
            prev = pos;
        }
    }

    /// <summary>PROSPECTOR RUN's own slalom: boulders planted ACROSS the carved canyon lane
    /// (never off into the walls) so the descent becomes an obstacle drive. Each rock hugs one
    /// side of the centre line with the far side left clear, so every one is avoidable but forces
    /// a real steer-around line; stations are denser than the open-field scatter. Their count
    /// rises by two per Prospector grade above grade 1.</summary>
    private void BuildProspectorBoulders()
    {
        float half = Mathf.Max(2f, terrain.raceCourseHalfWidth);
        float startZ = MoonTerrainGen.RaceStartZ + 80f;
        float finishZ = terrain.RaceFinishZ - 50f;
        int grade = LevelManager.GetProspectorGrade(LevelManager.CurrentLevelIndex);
        int count = Mathf.Max(16, Mathf.RoundToInt(boulderCount * 1.5f))
            + ProspectorBouldersAddedPerGrade * (grade - 1);
        if (count > 0 && finishZ - startZ < 60f) return;

        float step = (finishZ - startZ) / count;
        float jitter = Mathf.Min(10f, step * 0.25f);
        for (int i = 0; i < count; i++)
        {
            float z = startZ + (i + 0.5f) * step + Random.Range(-jitter, jitter);
            float cx = terrain.RaceCenterX(z);
            // Alternate sides down the run (random, so the slalom bends, never a straight
            // picket fence down one wall).
            float side = Random.value < 0.5f ? -1f : 1f;
            float off = half * Random.Range(0.55f, 0.85f);
            float x = cx + side * off;
            float y = terrain.SampleHeight(new Vector3(x, 0f, z));
            CreateBoulder(new Vector3(x, y, z), Random.Range(2.0f, 4.0f), i);
        }
    }

    private GameObject CreateBoulder(Vector3 pos, float radius, int index)
    {
        // A varied, textured rock instead of a bare ball: pick a blob / cluster / fragment
        // shape and paint it with the planet's regolith-mottled texture so it sits naturally
        // on the surface (and breaks away the "row of identical balls" look).
        LevelDefinition def = LevelManager.CurrentLevel;
        string planetKey = def != null ? def.planetName : "default";
        Color regolith = def != null ? def.regolithColor : new Color(0.60f, 0.60f, 0.63f);

        float heightScale = Random.Range(0.65f, 0.95f);
        float baseScale = Random.Range(0.85f, 1.2f);
        float brightness = Random.Range(0.7f, 1.0f);

        GameObject rockGo = new GameObject("Rock_" + index);
        rockGo.transform.position = new Vector3(pos.x, pos.y + radius * heightScale * 0.5f, pos.z);
        rockGo.transform.rotation = Random.rotation;

        // Worlds with a concrete combo (Moon/Mars/Io/Venus/Titan) wear the blended swatches,
        // tinted to the world; everyone else keeps the planet's regolith-mottled texture.
        Material mat = Yfcm.World(planetKey, Yfcm.WorldTint(planetKey, regolith, brightness),
            new Vector2(2f, 2f), 0.12f, true);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            mat.mainTexture = RockArt.TextureFor(regolith, planetKey);
            mat.color = new Color(brightness, brightness, brightness, 1f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Glossiness", 0.12f);
        }

        RockArt.BuildRockShape(rockGo.transform, radius * baseScale, mat, index);
        rockGo.AddComponent<Boulder>();
        // Slick rock faces so the wheels bounce off instead of sticking (see Boulder.MakeSlick).
        Boulder.MakeSlick(rockGo);
        return rockGo;
    }

    // ------------------------------------------------------------ dust patches
    // Loose regolith patches beside (and occasionally across) the route: visible darker
    // ellipses that cut the buggy's traction, so a skilled driver routes around them.
    private void BuildDustPatches()
    {
        if (podPositions.Count < 2) return;

        Color reg = LevelManager.CurrentLevel.regolithColor;
        int count = Mathf.Max(4, Mathf.RoundToInt(boulderCount / 2));
        Transform holder = new GameObject("DustPatches").transform;
        holder.SetParent(transform, false);

        for (int i = 0; i < count; i++)
        {
            int seg = Random.Range(0, podPositions.Count - 1);
            Vector3 a = podPositions[seg];
            Vector3 b = podPositions[seg + 1];
            ComputeSegment(a, b, out Vector3 along, out Vector3 side, out float len);

            float t = Random.value * 0.8f + 0.1f;
            Vector3 pos = a + along * (len * t);
            // One in four sits across the route to force a decision; the rest flank it.
            float lateral = Random.value < 0.25f
                ? Random.Range(-8f, 8f)
                : Random.Range(6f, 26f) * (Random.value < 0.5f ? -1f : 1f);
            pos += side * lateral;
            pos.y = terrain.SampleHeight(pos) + 0.06f;

            DustPatch.Build(holder, pos, Random.Range(7f, 13f), Random.Range(5f, 9f),
                Random.Range(0f, 360f), Color.Lerp(reg, Color.black, 0.45f));
        }
    }

    // ------------------------------------------------------------------- pods
    /// <summary>Scatters a HARVEST route's oxygen canisters. (Lunar Race lays its own checkpoint
    /// gates in BuildRaceGates and Ridge Survival its own crest canisters in BuildRidgeCanisters,
    /// so this is the only pod route in the game.) The route runs mostly +Z, weaving left and
    /// right for interest, and its corridor WIDENS with the harvest grade - see below.</summary>
    private void BuildPods()
    {
        // LUNAR TRIALS course: the canisters sit ON the course line at even fractions of the
        // loop - mid-route O2 you pass without leaving the tyre line.
        if (TrialsCourse.Active != null)
        {
            BuildCoursePods();
            return;
        }

        Vector3 prev = startPosition;
        podPositions.Clear();

        // The corridor the canisters weave across, widening with the grade
        // (LevelManager.HarvestPodLateral): 40 m at grade 1 (a gentle near-straight first run),
        // up to 124 m at grade 5 - higher grades put the air further off the line and reaching
        // it costs a longer, twistier detour.
        float lateral = LevelManager.HarvestPodLateral(LevelManager.CurrentLevelIndex);
        // PROSPECTOR RUN weaves down a carved CANYON whose drivable lane is only the
        // race-course half-width (14 m on the Moon, 12-13 on Venus/Io/Titan): the open-field
        // lateral spread (40 m up to 124 m) would bury canisters in the cliff walls. Clamp the
        // whole weave to a tight centred corridor inside the actual lane.
        if (GameFlow.Mode == GameMode.Prospector)
        {
            lateral = Mathf.Max(2f, terrain.raceCourseHalfWidth * 0.45f);
        }

        // The weave SNAKES around a slowly wandering route centre rather than compounding a fresh
        // random offset every pod. A plain random walk of +/-lateral drifts the route a long way
        // sideways by the last pod - and at the top grade far enough to strand a canister near
        // the edge of the generated terrain (half-extent 1440 m). Alternating sides keeps the
        // whole route inside a corridor about +/-lateral wide, which is both what "weaving" was
        // always meant to be and what lets the grade widen it safely. Grade also eases the
        // TWISTINESS itself, not just the corridor width: a fresh grade-1 field cross-sides
        // rarely and parks some canisters close to the line (the air comes with a small sway),
        // while a top-grade course cross-sides almost every pod and keeps every canister out
        // near the corridor's edge - so the first run is a calm near-straight and only the
        // re-runs snake.
        const float CentreDrift = 18f;   // how far the centre line itself wanders per pod
        // The weave is a HARVEST field on the open plains and a lane-following run in the
        // Prospector canyon, so its twistiness must follow the grade of whichever mode is
        // playing: an open-field Harvester run ramps on the HARVEST grade, not the (independent)
        // Prospector grade it used to read.
        int grade = GameFlow.Mode == GameMode.Prospector
            ? LevelManager.GetProspectorGrade(LevelManager.CurrentLevelIndex)
            : LevelManager.GetHarvestGrade(LevelManager.CurrentLevelIndex);
        float gradeT = Mathf.Clamp01((grade - 1f) / (LevelManager.MaxHarvestGrade - 1f));
        float flipChance = Mathf.Lerp(0.50f, 0.80f, gradeT); // odds of crossing sides per pod
        float innerMin = Mathf.Lerp(0.30f, 0.55f, gradeT);    // how close a canister can sit to the line
        Vector3 routeCentre = startPosition;
        int side = 1;

        for (int i = 0; i < podCount; i++)
        {
            bool isFinal = (i == podCount - 1);

            // Drift the centre line first, then hang the canister off one side of it. Holding a
            // side now and then (the 22%) lets the route double back instead of ticking like a
            // metronome, and a partial offset keeps consecutive pods from forming a hard line.
            routeCentre.x += Random.Range(-CentreDrift, CentreDrift);
            if (Random.value < flipChance) side = -side;

            Vector3 next = prev;
            next.z = prev.z + podSpacing + Random.Range(-30f, 30f);
            // In PROSPECTOR RUN the dart sits on the canyon's carved centre line (RaceCenterX)
            // so the corridor can never leak into a cliff; the open-field weave hangs pods off
            // its own wandering route centre instead.
            if (GameFlow.Mode == GameMode.Prospector)
            {
                next.x = terrain.RaceCenterX(next.z)
                    + side * lateral * Random.Range(innerMin, 1f);
            }
            else
            {
                next.x = routeCentre.x + side * lateral * Random.Range(innerMin, 1f);
            }
            next.y = terrain.SampleHeight(next) + 2.0f;

            CreatePod(next, isFinal, i);
            podPositions.Add(new Vector3(next.x, 0f, next.z));
            prev = next;
        }
    }

    /// <summary>LUNAR TRIALS course pods: even fractions of the loop, skipping the pad zone
    /// at the start/finish seam (s near 0).</summary>
    private void BuildCoursePods()
    {
        podPositions.Clear();
        int count = podCount;
        for (int i = 0; i < count; i++)
        {
            float s = (i + 1) / (float)(count + 1);
            Vector2 p = TrialsCourse.Active.PointAt(s);
            Vector3 pos = new Vector3(p.x, terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + 2f, p.y);
            CreatePod(pos, i == count - 1, i);
            podPositions.Add(new Vector3(pos.x, 0f, pos.z));
        }
    }

    // -------------------------------------------------------------- harvestables
    // Scatters the planet's harvestable nuggets (Harvester Mode) as VISIBLE ore clusters
    // just off the oxygen route: they sit 15-70m to the side of the gap between consecutive
    // pods, so they are found while driving the route rather than requiring a dedicated hunt
    // (and a costly oxygen budget). Every nugget is visible from the start; the whole field
    // is what the proximity sonar pings when the rover gets near.
    private void BuildHarvestables()
    {
        // LUNAR TRIALS course (Model A): the field IS the racing line - nuggets sit on the
        // course between the tyre edges, so driving the line collects the score and only a
        // drift wide leaves ore behind.
        if (TrialsChallenge != null && terrain != null && TrialsCourse.Active != null)
        {
            BuildCourseOre();
            return;
        }

        // LUNAR TRIALS: the field hides inside crater bowls spread around the arena - the
        // skill is navigating to them and handling the crater terrain, not scooping a pile
        // that happens to sit beside the final canister.
        if (TrialsChallenge != null && terrain != null && buggy != null
            && BuildTrialsHarvestables())
        {
            return;
        }

        if (harvestableCount <= 0 || podPositions.Count < 2) return;

        int segments = podPositions.Count - 1;
        int index = 0;
        int clusterId = 0;
        int remaining = harvestableCount;

        // Survey features claim the first nuggets: on Io they ring the volcanic vents. The
        // rings are SURVEY-only - in PROSPECTOR RUN the little field stays a tight lane sweep,
        // so the features are built as hazards but never eat the ore budget (see BuildIoVolcanoes).
        // Titan's methane lakes + mud flats are gone entirely (they read badly and forced ugly
        // detours), so Titan scatters its field across the plain like the other worlds.
        string harvestPlanet = LevelManager.CurrentLevel.planetName;
        if (harvestPlanet == "IO")
        {
            remaining -= BuildIoVolcanoes(ref index, ref clusterId);
            remaining -= BuildIoLavaLakes(ref index, ref clusterId);
        }
        remaining = Mathf.Max(0, remaining);

        while (remaining > 0)
        {
            int clusterSize = Random.Range(3, 6);
            clusterSize = Mathf.Min(clusterSize, remaining);
            if (clusterSize <= 0) break;

            int seg = Random.Range(0, segments);
            Vector3 a = podPositions[seg];
            Vector3 b = podPositions[seg + 1];
            ComputeSegment(a, b, out Vector3 along, out Vector3 side, out float len);
            // PROSPECTOR RUN keeps the field on the canyon floor: a third of the open-field
            // off-route spread (the lane is only ~12-14 m each way) so nuggets stay out of the
            // cliff walls, and their pads cluster ALONG the lane rather than sideways.
            float offMin = GameFlow.Mode == GameMode.Prospector ? 3f : harvestableOffRouteMin;
            float offMax = GameFlow.Mode == GameMode.Prospector
                ? Mathf.Min(harvestableOffRouteMax, Mathf.Max(5f, terrain.raceCourseHalfWidth * 0.8f))
                : harvestableOffRouteMax;
            Vector3 site;
            if (GameFlow.Mode == GameMode.Prospector)
            {
                // PROSPECTOR RUN fairness: pick the FLATTEST of several lane candidates so a
                // nugget never lands on the canyon wall or a steep break the buggy cannot climb.
                site = PickProspectorOreSite(a, along, side, len, offMin, offMax);
            }
            else
            {
                site = a + along * (len * Random.value)
                    + side * Random.Range(offMin, offMax) * (Random.value < 0.5f ? -1f : 1f);
                // Realism: perch the ore where it would actually outcrop - inside a crater bowl
                // or up the flank of the nearest hill - instead of a random flat spot. In the
                // canyon that siting would climb the cliffs, so it is skipped there.
                site = ChooseClusterSite(site, a, side);
            }
            site.y = terrain.SampleHeight(site) + 0.6f;

            for (int m = 0; m < clusterSize; m++)
            {
                Vector3 pos = site;
                // In PROSPECTOR RUN the pad jitter sprays along the lane (z) instead of across
                // it (x), so the field reads as a seam of ore running down the canyon.
                if (GameFlow.Mode == GameMode.Prospector)
                {
                    // Keep the pad jitter small across the lane so a nugget never jitters onto
                    // the sloped shoulder; spray it ALONG the lane (z) instead.
                    pos.x += Random.Range(-1.5f, 1.5f);
                    pos.z += Random.Range(-7f, 7f);
                }
                else
                {
                    pos.x += Random.Range(-7f, 7f);
                    pos.z += Random.Range(-7f, 7f);
                }
                pos.y = terrain.SampleHeight(pos) + 0.6f;
                CreateHarvestable(pos, harvestableScore, index++, clusterId);
            }
            clusterId++;
            remaining -= clusterSize;
        }

        if (OxygenManager.Instance != null)
        {
            OxygenManager.Instance.RegisterTotalHarvestables(harvestableCount);
        }
    }

    /// <summary>LUNAR TRIALS course ore (Model A): small clusters strung ALONG the loop at
    /// seeded fractions, hugging the line (within ~2 m of the centre). The buggy collects
    /// the score by driving the line; clusters are placed away from the pad seam.</summary>
    private void BuildCourseOre()
    {
        TrialsCourseData course = TrialsCourse.Active;
        int index = 0;
        int remaining = harvestableCount;
        while (remaining > 0)
        {
            int clusterSize = Mathf.Min(Random.Range(2, 4), remaining);
            float s = Random.Range(0.08f, 0.92f);   // never in the pad seam at s = 0
            for (int m = 0; m < clusterSize; m++)
            {
                float si = Mathf.Repeat(s + m * 0.014f, 1f);
                Vector2 p = course.PointAt(si);
                Vector2 jitter = Random.insideUnitCircle * 2.2f;
                Vector3 pos = new Vector3(p.x + jitter.x, 0f, p.y + jitter.y);
                pos.y = terrain.SampleHeight(pos) + 0.6f;
                CreateHarvestable(pos, harvestableScore, index++, 0);   // one sonar cluster
            }
            remaining -= clusterSize;
        }
        if (OxygenManager.Instance != null)
        {
            OxygenManager.Instance.RegisterTotalHarvestables(harvestableCount);
        }
    }

    /// <summary>LUNAR TRIALS ore siting: hides the field inside crater bowls spread around
    /// the arena. Craters are chosen in angular sectors around the spawn (so the clusters
    /// force real navigation between them) and nuggets sit low in each bowl - below the rim
    /// line, hidden until the player drops in and handles the crater terrain. The seeded RNG
    /// makes every attempt lay the field out identically. Returns false when no usable
    /// craters were generated (the caller falls back to the route scatter).</summary>
    private bool BuildTrialsHarvestables()
    {
        if (terrain.CraterCount == 0) return false;

        Vector3 spawnFlat = buggy.SpawnPosition;
        spawnFlat.y = 0f;

        // 1. Usable craters: big enough to drive into, a real trip from the spawn, and always
        //    in the FORWARD arc - nothing behind the starting line to backtrack for.
        var usable = new System.Collections.Generic.List<int>();
        for (int i = 0; i < terrain.CraterCount; i++)
        {
            Vector3 c = terrain.CraterCentre(i);
            float r = terrain.CraterRadius(i);
            if (r < 14f) continue;
            float d = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(spawnFlat.x, spawnFlat.z));
            if (d < 35f || d > terrain.extent * 0.45f) continue;
            // Spawn faces +Z (bearing 0); the buggy starts looking down-course, so keep the
            // field within +/-100 degrees of that heading.
            float bearing = Mathf.Atan2(c.x - spawnFlat.x, c.z - spawnFlat.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(bearing) > 100f) continue;
            usable.Add(i);
        }
        if (usable.Count == 0) return false;

        float Bearing(int i) => Mathf.Atan2(
            terrain.CraterCentre(i).x - spawnFlat.x,
            terrain.CraterCentre(i).z - spawnFlat.z) * Mathf.Rad2Deg;

        var chosen = new System.Collections.Generic.List<int>();
        int needed;

        if (TrialsChallenge.oreBowls > 0)
        {
            // DESIGNED siting: the challenge names the bowl count and the preferred bearing;
            // the craters closest to that bearing win. FIRST TRAX: one bowl to the left.
            needed = Mathf.Min(TrialsChallenge.oreBowls, usable.Count);
            usable.Sort((a, b) =>
            {
                float da = Mathf.Abs(Mathf.DeltaAngle(Bearing(a), TrialsChallenge.oreBearing));
                float db = Mathf.Abs(Mathf.DeltaAngle(Bearing(b), TrialsChallenge.oreBearing));
                return da.CompareTo(db);
            });
            for (int i = 0; i < needed; i++) chosen.Add(usable[i]);
        }
        else
        {
            // AUTO spread: sort by bearing, greedily pick craters at least 40 degrees apart
            // (circular) so the clusters spread around the arena instead of bunching.
            usable.Sort((a, b) => Bearing(a).CompareTo(Bearing(b)));
            needed = Mathf.Clamp(Mathf.CeilToInt((float)harvestableCount / 3f), 2, 8);
            for (float minSep = 40f; minSep >= 0f && chosen.Count < needed; minSep -= 15f)
            {
                chosen.Clear();
                foreach (int i in usable)
                {
                    if (chosen.Count >= needed) break;
                    float a = Bearing(i);
                    bool tooClose = false;
                    foreach (int j in chosen)
                    {
                        if (Mathf.Abs(Mathf.DeltaAngle(a, Bearing(j))) < minSep) { tooClose = true; break; }
                    }
                    if (!tooClose) chosen.Add(i);
                }
            }
        }
        if (chosen.Count == 0) return false;

        // 3. Distribute the nuggets across the chosen craters.
        int index = 0;
        int remaining = harvestableCount;
        for (int c = 0; c < chosen.Count && remaining > 0; c++)
        {
            int crater = chosen[c];
            Vector3 centre = terrain.CraterCentre(crater);
            float radius = terrain.CraterRadius(crater);
            int share = Mathf.Min(remaining, Mathf.CeilToInt((float)remaining / (chosen.Count - c)));
            remaining -= share;

            for (int m = 0; m < share; m++)
            {
                // Mid-bowl placement: below the rim line, so the nuggets are hidden from
                // outside the crater until the player drops in and navigates the terrain.
                float frac = Random.Range(0.35f, 0.6f) * radius;
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector3 pos = centre + new Vector3(Mathf.Sin(a) * frac, 0f, Mathf.Cos(a) * frac);
                pos.y = terrain.SampleHeight(pos) + 0.6f;
                CreateHarvestable(pos, harvestableScore, index++, crater);   // crater index = cluster id (sonar groups by it)
            }
        }

        if (OxygenManager.Instance != null)
        {
            OxygenManager.Instance.RegisterTotalHarvestables(harvestableCount);
        }
        return true;
    }

    /// <summary>Prospector ore siting: sample several points along the lane segment and return
    /// whichever sits on the FLATTEST ground, so the nugget is always reachable by a short,
    /// drivable route. Retries a handful of times and stops early once a spot is flat enough.</summary>
    private Vector3 PickProspectorOreSite(Vector3 a, Vector3 along, Vector3 side,
        float len, float offMin, float offMax)
    {
        Vector3 best = a;
        float bestSlope = float.MaxValue;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector3 candidate = a + along * (len * Random.value)
                + side * Random.Range(offMin, offMax) * (Random.value < 0.5f ? -1f : 1f);
            float slope = TerrainSlope(candidate);
            if (slope < bestSlope)
            {
                bestSlope = slope;
                best = candidate;
            }
            if (slope <= ProspectorMaxOreSlope) break;
        }
        return best;
    }

    /// <summary>Local ground gradient (rise/run) at a world XZ, from four terrain samples.</summary>
    private float TerrainSlope(Vector3 p)
    {
        const float e = 1.5f;
        float dx = terrain.SampleHeight(new Vector3(p.x + e, 0f, p.z))
            - terrain.SampleHeight(new Vector3(p.x - e, 0f, p.z));
        float dz = terrain.SampleHeight(new Vector3(p.x, 0f, p.z + e))
            - terrain.SampleHeight(new Vector3(p.x, 0f, p.z - e));
        return Mathf.Sqrt(dx * dx + dz * dz) / (2f * e);
    }

    // ------------------------------------------------------------------ survey features
    // Io and Titan spend part of their harvest budget on working terrain: Io rears MINI
    // VOLCANOES off the canister path (cone + glowing magma pool in the crest) and rings each
    // with nuggets; Titan sits liquid methane lakes off the path and rims every shoreline
    // with nuggets. The feature also hazards its disc (MagmaPool burns, the lake slicks grip
    // and saps the hull a little), handled by the HarvestFeatureController.

    private void CreateFeatureController()
    {
        if (featureController == null)
        {
            GameObject go = new GameObject("HarvestFeatureController");
            featureController = go.AddComponent<HarvestFeatureController>();
        }
    }

    /// <summary>A lateral site for a survey feature, clear of the canister route and clear of
    /// every feature already placed, with a retry budget.</summary>
    private bool PickFeatureSite(out Vector3 site)
    {
        int segments = podPositions.Count - 1;
        for (int attempt = 0; attempt < 14; attempt++)
        {
            int seg = Random.Range(0, segments);
            Vector3 a = podPositions[seg];
            Vector3 b = podPositions[seg + 1];
            ComputeSegment(a, b, out Vector3 along, out Vector3 side, out float len);
            Vector3 candidate = a + along * (len * Random.Range(0.15f, 0.85f))
                + side * Random.Range(harvestableOffRouteMin, 90f) * (Random.value < 0.5f ? -1f : 1f);
            bool clear = true;
            for (int i = 0; i < featureCenters.Count; i++)
            {
                Vector2 d = new Vector2(candidate.x - featureCenters[i].x, candidate.z - featureCenters[i].z);
                if (d.sqrMagnitude < 70f * 70f) { clear = false; break; }
            }
            if (clear) { site = candidate; return true; }
        }
        site = Vector3.zero;
        return false;
    }

    /// <summary>Io: mini volcanoes off the path, each with a glowing magma pool in the crater
    /// crest and a ring of nuggets. Returns the number of nuggets this consumed.</summary>
    private int BuildIoVolcanoes(ref int index, ref int clusterId)
    {
        if (ioVolcanoCount <= 0 || podPositions.Count < 2) return 0;
        CreateFeatureController();
        int placed = 0;
        for (int i = 0; i < ioVolcanoCount; i++)
        {
            Vector3 site;
            if (!PickFeatureSite(out site)) continue;
            Vector3 centre = new Vector3(site.x, terrain.SampleHeight(site) + 0.02f, site.z);
            float poolRadius = Random.Range(4.5f, 6f);
            float coneBase = poolRadius * 3.2f;
            featureCenters.Add(centre);
            BuildVolcano(centre, poolRadius);
            featureController.Add(new HarvestFeature
            {
                kind = HarvestFeatureKind.MagmaPool,
                center = centre,
                radius = poolRadius,
                traction = 0.5f,
                speedCap = 0.6f,
                dps = 5f,
                tick = 0.4f,
            });
            // Ring the nuggets OUTSIDE the cone's base (base radius ~3.2x pool), so they sit on
            // open ground rather than being buried inside the volcano's solid skirt. The ring is
            // SURVEY-only: Prospector builds the volcano as a hazard but keeps its tiny lane field.
            if (GameFlow.Mode != GameMode.Prospector)
                placed += RingNuggets(centre, coneBase + 4f, coneBase + 18f, ioVolcanoNuggets, ref index, clusterId);
            clusterId++;
        }
        return placed;
    }

    /// <summary>Io: big BUBBLING lava lakes off the canister path - wide molten ponds that
    /// roar damage while the rover skirts or crosses them, bigger than Titan's methane lakes.
    /// Nuggets rim each shoreline. Returns the number of nuggets this consumed.</summary>
    private int BuildIoLavaLakes(ref int index, ref int clusterId)
    {
        if (ioLavaLakeCount <= 0 || podPositions.Count < 2) return 0;
        CreateFeatureController();
        int placed = 0;
        for (int i = 0; i < ioLavaLakeCount; i++)
        {
            Vector3 site;
            if (!PickFeatureSite(out site)) continue;
            Vector3 centre = new Vector3(site.x, terrain.SampleHeight(site) + 0.02f, site.z);
            float lakeRadius = Random.Range(20f, 30f);
            featureCenters.Add(centre);
            BuildSurveyLavaLake(centre, lakeRadius);
            featureController.Add(new HarvestFeature
            {
                kind = HarvestFeatureKind.MagmaPool,
                center = centre,
                radius = lakeRadius,
                traction = 0.45f,
                speedCap = 0.5f,
                dps = 3f,
                tick = 0.4f,
            });
            // Nuggets rim each shoreline (SURVEY-only; Prospector keeps its tight lane field).
            if (GameFlow.Mode != GameMode.Prospector)
                placed += RingNuggets(centre, lakeRadius + 5f, lakeRadius + 18f, ioLavaLakeNuggets, ref index, clusterId);
            clusterId++;
        }
        return placed;
    }
    /// <summary>A big glowing lava lake: a molten disc pressed into the ground that billows
    /// bubbles (LavaBubbler) so the pond reads alive rather than a flat orange sticker.</summary>
    private void BuildSurveyLavaLake(Vector3 centre, float lakeRadius)
    {
        GameObject lake = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lake.name = "LavaLake_" + (featureObjectIndex++);
        lake.transform.position = centre;
        lake.transform.localScale = new Vector3(lakeRadius * 2f, 0.06f, lakeRadius * 2f);
        Material mat = LavaArt.Melt(Color.white, new Vector2(6f, 6f), 0.15f, new Color(1.5f, 0.9f, 0.5f));
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(1f, 0.35f, 0.05f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(1f, 0.32f, 0.04f) * 1.3f);
        }
        lake.GetComponent<MeshRenderer>().sharedMaterial = mat;
        DestroyImmediate(lake.GetComponent<Collider>());
        LavaBubbler b = lake.AddComponent<LavaBubbler>();
        b.halfExtents = new Vector2(lakeRadius * 0.8f, lakeRadius * 0.8f);
        b.bubbleColor = new Color(1f, 0.5f, 0.12f);
        b.spawnInterval = 0.5f;
    }

    /// <summary>Titan: liquid methane lakes off the path, nuggets rimming each shoreline.
    /// Returns the number of nuggets this consumed.</summary>
    private int BuildTitanLakes(ref int index, ref int clusterId)
    {
        if (titanLakeCount <= 0 || podPositions.Count < 2) return 0;
        CreateFeatureController();
        int placed = 0;
        for (int i = 0; i < titanLakeCount; i++)
        {
            Vector3 site;
            if (!PickFeatureSite(out site)) continue;
            Vector3 centre = new Vector3(site.x, terrain.SampleHeight(site) + 0.02f, site.z);
            float lakeRadius = Random.Range(16f, 24f);
            featureCenters.Add(centre);
            BuildMethaneLake(centre, lakeRadius);
            featureController.Add(new HarvestFeature
            {
                kind = HarvestFeatureKind.MethaneLake,
                center = centre,
                radius = lakeRadius,
                traction = 0.5f,
                speedCap = 0.55f,
                dps = 1.5f,
                tick = 0.4f,
            });
            placed += RingNuggets(centre, lakeRadius + 4f, lakeRadius + 16f, titanLakeNuggets, ref index, clusterId);
            clusterId++;
        }
        return placed;
    }

    /// <summary>Titan survey mud flats: darker brown slurry discs laid CLOSE to the canister
    /// trail (the opposite of the decorated lakes' detour) so the rover has to wallow through
    /// them. They grab the wheels hard - lower grip and top-speed, no damage - the survey
    /// version of the race course's mud bands.</summary>
    private void BuildTitanMudPatches()
    {
        if (titanMudCount <= 0 || podPositions.Count < 2) return;
        CreateFeatureController();
        int segments = podPositions.Count - 1;
        int placed = 0;
        for (int attempt = 0; attempt < titanMudCount * 10 && placed < titanMudCount; attempt++)
        {
            int seg = Random.Range(0, segments);
            Vector3 a = podPositions[seg];
            Vector3 b = podPositions[seg + 1];
            ComputeSegment(a, b, out Vector3 along, out Vector3 side, out float len);
            Vector3 candidate = a + along * (len * Random.Range(0.10f, 0.90f))
                + side * Random.Range(2f, 26f) * (Random.value < 0.5f ? -1f : 1f);
            bool clear = true;
            for (int i = 0; i < featureCenters.Count; i++)
            {
                Vector2 d = new Vector2(candidate.x - featureCenters[i].x, candidate.z - featureCenters[i].z);
                if (d.sqrMagnitude < 30f * 30f) { clear = false; break; }
            }
            if (!clear) continue;

            Vector3 centre = new Vector3(candidate.x, terrain.SampleHeight(candidate) + 0.02f, candidate.z);
            // Titan's survey wallows are proper mud flats: big pads (13-20 m) the stray rover
            // really sinks into, dropped right on the canister trail.
            float radius = Random.Range(13f, 20f);
            featureCenters.Add(centre);
            BuildSurveyMud(centre, radius);
            featureController.Add(new HarvestFeature
            {
                kind = HarvestFeatureKind.MudPatch,
                center = centre,
                radius = radius,
                traction = 0.35f,
                speedCap = 0.4f,
                dps = 0f,
                tick = 0.4f,
            });
            placed++;
        }
    }

    /// <summary>A dark, matte crumbly-brown mud patch moulded into the terrain with a feathered
/// rim. No collider - the feature controller applies the traction wallow while the rover is
/// inside the disc.</summary>
    private void BuildSurveyMud(Vector3 centre, float radius)
    {
        Material mat;
        GameObject mud = TerrainDecal.Build(terrain, new Vector2(centre.x, centre.z),
            radius, radius, 0.05f,
            new Color(0.30f, 0.19f, 0.10f), Color.black,
            TerrainDecal.Style.Mud, true, out mat);
        if (mud == null) return;
        mud.name = "TitanMud_" + (featureObjectIndex++);
    }

    /// <summary>Places a ring of nuggets around a feature, each sharing the cluster id so a
    /// clean sweep of the ring pays the cluster bonus.</summary>
    private int RingNuggets(Vector3 centre, float minR, float maxR, int count, ref int index, int clusterId)
    {
        int placed = 0;
        for (int n = 0; n < count; n++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Random.Range(minR * minR, maxR * maxR));
            Vector3 pos = new Vector3(centre.x + Mathf.Cos(a) * r, 0f, centre.z + Mathf.Sin(a) * r);
            pos.y = terrain.SampleHeight(pos) + 0.6f;
            CreateHarvestable(pos, harvestableScore, index++, clusterId);
            placed++;
        }
        return placed;
    }

    /// <summary>A squat volcanic cone (crater rim plateau at the crest) with an orange magma pool
    /// sitting in the crater, plus a soft glow under the pool so it reads at night. The cone is
    /// SOLID and MOUNTABLE: a closed mesh + MeshCollider with gentle slopes and a touch of
    /// bounce, so the buggy can ride up and over it (or, driven into too hard, comes off it
    /// vigorously).</summary>
    /// <summary>The volcanic cone surface: the honeycombing concrete swatch (a real diffuse +
    /// normal texture from the Yughues pack) tinted across the dark basalt-brown flank, so the
    /// sides read as sheared lava crust instead of a flat hue. Tiles around the rim and flanks
    /// (see BuildVolcanoMesh UVs). Falls back to the procedural regolith rock if the swatch is
    /// missing so the game still runs.</summary>
    private Material BuildVolcanoRockMaterial()
    {
        Material rock = Yfcm.Rock("T_YFCM_Honeycombing",
            new Color(0.68f, 0.44f, 0.24f),
            new Vector2(5f, 4f),
            0.08f,
            new Color(0.10f, 0.03f, 0.01f));
        if (rock != null) return rock;

        rock = new Material(Shader.Find("Standard"));
        rock.mainTexture = RockArt.TextureFor(new Color(0.45f, 0.27f, 0.14f), "IO_VOLCANO");
        rock.mainTextureScale = new Vector2(7f, 5f);
        rock.color = Color.white;
        rock.SetFloat("_Glossiness", 0.05f);
        rock.EnableKeyword("_EMISSION");
        rock.SetColor("_EmissionColor", new Color(0.10f, 0.03f, 0.01f));
        return rock;
    }

    private void BuildVolcano(Vector3 centre, float poolRadius)
    {
        // The whole outer cone is a DRIVE-UP RAMP: a shallow (-13°) base skirt and crest fillet
        // curve through a ~28° mid wall (see BuildVolcanoMesh's radial profile), so wheels roll
        // straight on from the terrain and over the rim with no lip to wedge on. Height is a
        // fixed fraction of the base-to-rim run selected for that grade.
        float coneBase = poolRadius * 3.2f;
        float rimRadius = poolRadius * 1.6f;
        float coneHeight = (coneBase - rimRadius) * 0.36f;

        GameObject cone = new GameObject("IoVolcano_" + (featureObjectIndex++));
        cone.transform.position = centre;
        MeshFilter mf = cone.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildVolcanoMesh(coneBase, rimRadius, coneHeight, 24, 14);
        MeshRenderer mr = cone.AddComponent<MeshRenderer>();
        mr.sharedMaterial = BuildVolcanoRockMaterial();

        // Solid surface the wheels can climb: closed cone mesh + MeshCollider. Low bounce and
        // high friction so the buggy holds the 19° grade instead of skating off the flank.
        MeshCollider col = cone.AddComponent<MeshCollider>();
        PhysicsMaterial surf = new PhysicsMaterial("VolcanoRock");
        surf.bounciness = 0.08f;
        surf.staticFriction = 0.9f;
        surf.dynamicFriction = 0.9f;
        col.sharedMaterial = surf;

        float poolScale = poolRadius * 1.55f;
        GameObject pool = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pool.name = "MagmaPool";
        pool.transform.position = new Vector3(centre.x, centre.y + coneHeight, centre.z);
        pool.transform.localScale = new Vector3(poolScale, 0.07f, poolScale);
        Material lava = LavaArt.Melt(Color.white, new Vector2(4f, 4f), 0.12f, new Color(1.5f, 0.9f, 0.5f));
        if (lava == null)
        {
            lava = new Material(Shader.Find("Standard"));
            lava.color = new Color(1f, 0.35f, 0.05f);
            lava.EnableKeyword("_EMISSION");
            lava.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.05f) * 1.4f);
        }
        pool.GetComponent<MeshRenderer>().sharedMaterial = lava;
        DestroyImmediate(pool.GetComponent<Collider>());

        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "MagmaGlow";
        glow.transform.position = new Vector3(centre.x, centre.y + coneHeight - 0.15f, centre.z);
        glow.transform.localScale = new Vector3(poolScale * 2f, 0.25f, poolScale * 2f);
        Material glowMat = new Material(Shader.Find("Standard"));
        glowMat.color = new Color(1f, 0.4f, 0.1f, 0.5f);
        glowMat.EnableKeyword("_EMISSION");
        glowMat.SetColor("_EmissionColor", new Color(1f, 0.4f, 0.1f) * 0.8f);
        glow.GetComponent<MeshRenderer>().sharedMaterial = glowMat;
        DestroyImmediate(glow.GetComponent<Collider>());

        // The crater pool BUBBLES: a lazy lava billow pops out of the pool so the vent reads
        // restless even between eruptions.
        LavaBubbler bubbler = pool.AddComponent<LavaBubbler>();
        bubbler.halfExtents = new Vector2(poolScale * 0.85f, poolScale * 0.85f);
        bubbler.bubbleColor = new Color(1f, 0.5f, 0.12f);
        bubbler.spawnInterval = 0.8f;

        // Io volcanoes are ALIVE: a vent at the crater top periodically throws a plume of lava
        // globules over the surrounding ground (LavaGlobule, shared with the race surges).
        GameObject vent = new GameObject("IoVolcanoVent");
        vent.transform.position = new Vector3(centre.x, centre.y + coneHeight, centre.z);
        IoVolcanoEruptor eruptor = vent.AddComponent<IoVolcanoEruptor>();
        eruptor.vent = vent.transform.position;
        eruptor.poolRadius = poolRadius;
        eruptor.lavaColor = new Color(1f, 0.35f, 0.05f);
        eruptor.lavaGlow = new Color(1f, 0.35f, 0.05f);
    }

    /// <summary>Io Ridge vista: large volcanic peaks raised on the surrounding plains, far off
    /// the crest line, purely scenic (no colliders, no eruptors, no contribution to the run).
    /// The Ridge drops ~70m from its crest to these plains, so each peak reads as a distant
    /// caldera on the horizon - the same cone silhouette as the harvesting level's mini
    /// volcanoes, scaled up for distance. Rebuilt fresh each Ridge Survival leg.</summary>
    private void BuildRidgeScenicVolcanoes()
    {
        if (terrain == null) return;
        string planet = LevelManager.CurrentLevel != null ? LevelManager.CurrentLevel.planetName : "";
        if (CourseHazards.ThemeFor(planet) != PlanetTheme.Io) return;

        float startZ = MoonTerrainGen.RaceStartZ;
        float finishZ = terrain.RaceFinishZ;
        int placed = 0;
        for (int i = 0; i < 24 && placed < 6; i++)
        {
            float z = Random.Range(startZ + 120f, finishZ - 120f);
            float cx = terrain.RaceCenterX(z);
            // Sit well out on the flanking plains, split across both sides of the ridge path.
            float lateral = Random.Range(320f, 640f);
            float x = cx + lateral * (Random.value < 0.5f ? -1f : 1f);
            float groundY = terrain.SampleHeight(new Vector3(x, 0f, z));
            if (groundY < -500f) continue; // off-map fallback reading; pick another spot

            float poolRadius = Random.Range(34f, 54f); // big enough to see from the crest
            BuildScenicVolcano(new Vector3(x, groundY, z), poolRadius, placed);
            placed++;
        }
    }

    /// <summary>A purely cosmetic volcano peak: the shared cone mesh in planet rock with a
    /// glowing magma pool crown so the crest bather in molten light on Io's ridge horizon.
    /// No collider (it sits far outside the course), no eruptor (the run owns the hazards).
    /// The pool crowns sit proud of the cone flange as the harvest volcanoes do.</summary>
    private void BuildScenicVolcano(Vector3 centre, float poolRadius, int index)
    {
        float coneBase = poolRadius * 3.2f;
        float rimRadius = poolRadius * 1.6f;
        float coneHeight = (coneBase - rimRadius) * 0.36f;

        GameObject cone = new GameObject("RidgeScenicVolcano_" + (featureObjectIndex++));
        cone.transform.position = centre;
        MeshFilter mf = cone.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildVolcanoMesh(coneBase, rimRadius, coneHeight, 24, 14);
        MeshRenderer mr = cone.AddComponent<MeshRenderer>();
        mr.sharedMaterial = BuildVolcanoRockMaterial();

        float poolScale = poolRadius * 1.55f;
        GameObject pool = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pool.name = "ScenicMagmaPool";
        pool.transform.position = new Vector3(centre.x, centre.y + coneHeight * 0.94f, centre.z);
        pool.transform.localScale = new Vector3(poolScale, 0.1f, poolScale);
        Material lava = LavaArt.Melt(Color.white, new Vector2(4f, 4f), 0.12f, new Color(1.5f, 0.9f, 0.5f));
        if (lava == null)
        {
            lava = new Material(Shader.Find("Standard"));
            lava.color = new Color(1f, 0.35f, 0.05f);
            lava.EnableKeyword("_EMISSION");
            lava.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.05f) * 1.4f);
        }
        pool.GetComponent<MeshRenderer>().sharedMaterial = lava;
        DestroyImmediate(pool.GetComponent<Collider>());

        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "ScenicMagmaGlow";
        glow.transform.position = new Vector3(centre.x, centre.y + coneHeight * 0.94f - 0.15f, centre.z);
        glow.transform.localScale = new Vector3(poolScale * 2f, 0.25f, poolScale * 2f);
        Material glowMat = new Material(Shader.Find("Standard"));
        glowMat.color = new Color(1f, 0.4f, 0.1f, 0.5f);
        glowMat.EnableKeyword("_EMISSION");
        glowMat.SetColor("_EmissionColor", new Color(1f, 0.4f, 0.1f) * 0.8f);
        glow.GetComponent<MeshRenderer>().sharedMaterial = glowMat;
        DestroyImmediate(glow.GetComponent<Collider>());

        // The distant caldera still lives: the crater pool pops lazy oversize bubbles so the
        // vista shows the volcano breathing from the crest - flare only, no hazard colliders.
        LavaBubbler bubbler = pool.AddComponent<LavaBubbler>();
        bubbler.halfExtents = new Vector2(poolScale * 0.85f, poolScale * 0.85f);
        bubbler.bubbleColor = new Color(1f, 0.45f, 0.1f);
        bubbler.spawnInterval = 1.6f;
        bubbler.bubbleScale = 24f; // far-off pool: bubbles must be huge to be seen from the crest
    }

    /// <summary>A shallow liquid methane lake: a soft-metallic slate disc. No collider so the
    /// rover can drive across it (the feature controller applies the slick + hull sap).</summary>
    private void BuildMethaneLake(Vector3 centre, float lakeRadius)
    {
        GameObject lake = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lake.name = "MethaneLake_" + (featureObjectIndex++);
        lake.transform.position = centre;
        lake.transform.localScale = new Vector3(lakeRadius * 2f, 0.07f, lakeRadius * 2f);
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.16f, 0.30f, 0.36f);
        mat.SetFloat("_Metallic", 0.85f);
        mat.SetFloat("_Glossiness", 0.7f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.02f, 0.08f, 0.1f));
        lake.GetComponent<MeshRenderer>().sharedMaterial = mat;
        DestroyImmediate(lake.GetComponent<Collider>());
    }

    /// <summary>Builds a CLOSED volcano mesh sized in metres: a constant ~24° flank from the
    /// base skirt up to the rim (climbable rather than cliff-like), a short rounded crest, then
    /// a flat crater floor at ~80% of the rim radius where the magma pool sits. Closed (sides +
    /// bottom and top caps) so a non-convex MeshCollider treats it as solid ground the wheels
    /// can climb over, with no 90° lip at the rim to wedge on.</summary>
    private static Mesh BuildVolcanoMesh(float radiusBottom, float radiusTop, float height, int segments, int rings)
    {
        const float rimFrac = 0.80f;   // flank climbs this far up before the crest fillet
        const float floorFrac = 0.94f; // flat crater floor begins here (rest of the height)
        float floorRadius = radiusTop * 0.80f;

        var verts = new System.Collections.Generic.List<Vector3>((rings + 1) * (segments + 1) + 2);
        var uvs = new System.Collections.Generic.List<Vector2>((rings + 1) * (segments + 1) + 2);
        var tris = new System.Collections.Generic.List<int>((rings + 2) * segments * 3);
        for (int rr = 0; rr <= rings; rr++)
        {
            float u = (float)rr / rings;
            float r;
            if (u <= rimFrac)
                r = Mathf.Lerp(radiusBottom, radiusTop, u / rimFrac);
            else if (u <= floorFrac)
            {
                float t = (u - rimFrac) / (floorFrac - rimFrac);
                float e = t * t * (3f - 2f * t); // rounded crest rolls the wheels over the lip
                r = Mathf.Lerp(radiusTop, floorRadius, e);
            }
            else
                r = floorRadius;
            float h = height * u;
            for (int i = 0; i <= segments; i++)
            {
                float ang = i * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(ang);
                float s = Mathf.Sin(ang);
                verts.Add(new Vector3(c * r, h, s * r));
                uvs.Add(new Vector2((float)i / segments, u));
            }
        }
        int stride = segments + 1;
        for (int rr = 0; rr < rings; rr++)
        {
            for (int i = 0; i < segments; i++)
            {
                int a = rr * stride + i;
                int b = rr * stride + i + 1;
                int c = (rr + 1) * stride + i;
                int d = (rr + 1) * stride + i + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }

        // Bottom cap (fan from the base centre, facing down) and top cap (fan from the crest
        // centre, facing up) close the volume for solid collision.
        int bottomCentre = verts.Count;
        verts.Add(new Vector3(0f, 0f, 0f));
        uvs.Add(new Vector2(0.5f, 0f));
        int topCentre = verts.Count;
        verts.Add(new Vector3(0f, height, 0f));
        uvs.Add(new Vector2(0.5f, 1f));
        for (int i = 0; i < segments; i++)
        {
            int b0 = i;
            int b1 = i + 1;
            tris.Add(bottomCentre); tris.Add(b1); tris.Add(b0);
        }
        int topRow = rings * stride;
        for (int i = 0; i < segments; i++)
        {
            int t0 = topRow + i;
            int t1 = topRow + i + 1;
            tris.Add(topCentre); tris.Add(t0); tris.Add(t1);
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Moves an ore-cluster site to terrain that reads as a believable outcrop: a crater bowl
    /// if one is nearby (an impact exposes the seam), otherwise the flank of the nearest local
    /// hill (ore follows the slope). Stays on the same side of the route as the candidate.
    /// </summary>
    private Vector3 ChooseClusterSite(Vector3 candidate, Vector3 segmentAnchor, Vector3 sideDir)
    {
        // 1) A nearby crater: settle the cluster against the bowl wall (0.55-0.85 x radius from
        //    the crater centre, toward the candidate) so it rings the rim rather than dead centre.
        float nearestD = float.MaxValue;
        int nearest = -1;
        for (int i = 0; i < terrain.CraterCount; i++)
        {
            Vector2 c = terrain.CraterCenter(i);
            float dx = c.x - candidate.x;
            float dz = c.y - candidate.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < nearestD) { nearestD = d; nearest = i; }
        }
        if (nearest >= 0 && nearestD < 70f)
        {
            Vector2 c = terrain.CraterCenter(nearest);
            float r = terrain.CraterRadius(nearest);
            Vector2 toward = new Vector2(candidate.x - c.x, candidate.z - c.y);
            float m = toward.magnitude;
            float t = Random.Range(0.55f, 0.85f) * r;
            Vector2 spot = m > 0.01f ? c + toward / m * t : c + Random.insideUnitCircle * t;
            return new Vector3(spot.x, 0f, spot.y);
        }

        // 2) No crater handy: lean the site onto one flank of the local relief. Probe the
        //    height gradient a short distance out and step up-hill so the ore nests on a slope.
        Vector3 p = new Vector3(candidate.x, 0f, candidate.z);
        const float delta = 14f;
        float hPosX = terrain.SampleHeight(new Vector3(p.x + delta, 0f, p.z));
        float hNegX = terrain.SampleHeight(new Vector3(p.x - delta, 0f, p.z));
        float hPosZ = terrain.SampleHeight(new Vector3(p.x, 0f, p.z + delta));
        float hNegZ = terrain.SampleHeight(new Vector3(p.x, 0f, p.z - delta));
        Vector3 uphill = new Vector3(hPosX - hNegX, 0f, hPosZ - hNegZ);
        Vector3 shifted = p;
        if (uphill.magnitude > 0.4f)
        {
            shifted = p + uphill.normalized * Random.Range(8f, 22f);
        }

        // Whatever happened above, keep the cluster a healthy distance off the route line.
        Vector3 anchor = new Vector3(segmentAnchor.x, 0f, segmentAnchor.z);
        Vector3 side = new Vector3(sideDir.x, 0f, sideDir.z);
        float siteLat = Vector3.Dot(p - anchor, side);
        float lat = Vector3.Dot(shifted - anchor, side);
        if (Mathf.Abs(lat) < 8f)
        {
            shifted += side * (Mathf.Sign(siteLat) * 8f - lat);
        }
        return shifted;
    }

    private static void ComputeSegment(Vector3 a, Vector3 b, out Vector3 along, out Vector3 side, out float len)
    {
        Vector3 d = b - a;
        d.y = 0f;
        len = d.magnitude;
        if (len < 0.01f) { d = Vector3.forward; len = 1f; }
        d /= len;
        along = d;
        side = Vector3.Cross(along, Vector3.up);
    }

    // ------------------------------------------------- end-of-level cinematic
    // Listens for the final pod and runs the "lander touches down / buggy boards" sequence.
    private void BuildLevelEnd()
    {
        GameObject seqGo = new GameObject("LevelEndSequence");
        LevelEndSequence seq = seqGo.AddComponent<LevelEndSequence>();
        seq.buggy = buggy;
        seq.terrain = terrain;
        seq.oxygen = OxygenManager.Instance;
    }

    // ------------------------------------------------------------- creates pod
    private GameObject CreatePod(Vector3 pos, bool isFinal, int index)
    {
        GameObject pod = new GameObject("OxygenPod_" + index);
        pod.transform.position = pos;

        // In a normal Race the gate is a literal ARCH the buggy drives through (built by
        // RaceGateManager.BuildArch), so the core/pillar/beacon visuals are skipped - only
        // the OxygenPod trigger state survives to back the arch's gate logic. Ridge Survival
        // has NO arches: its canisters keep the full visible O2 tank look.
        GameObject beacon = null;
        if (GameFlow.Mode != GameMode.Race || GameFlow.IsRidgeSurvival)
        {
            // Spherical glowing core (the oxygen tank).
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Core";
            core.transform.SetParent(pod.transform, false);
            core.transform.localScale = Vector3.one * 1.0f;
            core.transform.localPosition = Vector3.zero;
            Material coreMat = new Material(Shader.Find("Standard"));
            if (isFinal)
            {
                coreMat.color = new Color(0.2f, 1f, 0.55f, 1f);
                coreMat.EnableKeyword("_EMISSION");
                coreMat.SetColor("_EmissionColor", new Color(0.1f, 0.8f, 0.3f));
            }
            else
            {
                coreMat.color = new Color(0.25f, 0.75f, 1f, 1f);
                coreMat.EnableKeyword("_EMISSION");
                coreMat.SetColor("_EmissionColor", new Color(0.1f, 0.4f, 0.9f));
            }
            core.GetComponent<MeshRenderer>().sharedMaterial = coreMat;
            DestroyImmediate(core.GetComponent<Collider>());

            // Central pillar.
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Pillar";
            pillar.transform.SetParent(pod.transform, false);
            pillar.transform.localScale = new Vector3(1.6f, 1.4f, 1.6f);
            pillar.transform.localPosition = new Vector3(0f, 0f, 0f);
            MeshRenderer pMr = pillar.GetComponent<MeshRenderer>();
            pMr.sharedMaterial = YFMetal.Get(37);
            DestroyImmediate(pillar.GetComponent<Collider>());

            // Spinning beacon light.
            beacon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beacon.name = "Beacon";
            beacon.transform.SetParent(pod.transform, false);
            beacon.transform.localScale = new Vector3(0.25f, 0.25f, 2.2f);
            beacon.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            Material bMat = new Material(Shader.Find("Standard"));
            bMat.EnableKeyword("_EMISSION");
            bMat.SetColor("_EmissionColor", isFinal ? new Color(0.2f, 1f, 0.4f) : new Color(0.2f, 0.6f, 1f));
            bMat.color = isFinal ? new Color(0.6f, 1f, 0.8f) : new Color(0.6f, 0.9f, 1f);
            beacon.GetComponent<MeshRenderer>().sharedMaterial = bMat;
            DestroyImmediate(beacon.GetComponent<Collider>());
        }

        // OxygenPod behaviour.
        OxygenPod podComp = pod.AddComponent<OxygenPod>();
        podComp.isFinalPod = isFinal;
        podComp.beaconLight = beacon;
        spawnedPods.Add(podComp);

        // Register the total count with the game manager (Harvester counts them for the
        // oxygen pods readout; Race counts the checkpoint gates the timer runs through).
        if (OxygenManager.Instance != null)
        {
            OxygenManager.Instance.RegisterTotalPods(podCount);
        }
        return pod;
    }

// --------------------------------------------------- creates harvestable
    private Harvestable CreateHarvestable(Vector3 pos, int score, int index, int clusterId = -1)
    {
        GameObject nugget = new GameObject("Harvestable_" + index);
        nugget.transform.position = pos;

        // All nuggets share the planet's harvestable colour - no rock-type variety.

        // PROSPECTOR RUN: the ore must be HUNTED, not advertised. The pads are dimmed hard and
        // their glow footprints shrunk to faint pips, so you find them by the sonar ping and the
        // headlight's sweep rather than a glowing carpet running down the canyon.
        bool dimmed = GameFlow.Mode == GameMode.Prospector;

        // Low ground pad: a flattened cylinder tinted the planet's harvestable colour. No tall
        // beacon - it reads as a subtle spot you must drive the rover over to collect.
        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "Pad";
        pad.transform.SetParent(nugget.transform, false);
        pad.transform.localScale = new Vector3(1.3f, 0.08f, 1.3f);
        pad.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        Material padMat = new Material(Shader.Find("Standard"));
        padMat.color = dimmed ? harvestableColor * 0.35f : harvestableColor;
        padMat.EnableKeyword("_EMISSION");
        padMat.SetColor("_EmissionColor", dimmed ? harvestableColor * 0.08f : harvestableColor * 0.5f);
        pad.GetComponent<MeshRenderer>().sharedMaterial = padMat;
        DestroyImmediate(pad.GetComponent<Collider>());

        // Faint glowing footprint under the pad so it is readable at a glance on the regolith.
        // In PROSPECTOR RUN the footprint is a shadow of itself - readable up close or under the
        // beam, invisible from any distance.
        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "PadGlow";
        glow.transform.SetParent(nugget.transform, false);
        glow.transform.localScale = dimmed ? new Vector3(1.25f, 0.16f, 1.25f)
            : new Vector3(2.2f, 0.3f, 2.2f);
        glow.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        Material glowMat = new Material(Shader.Find("Standard"));
        glowMat.color = dimmed
            ? new Color(harvestableColor.r, harvestableColor.g, harvestableColor.b, 0.18f)
            : new Color(harvestableColor.r, harvestableColor.g, harvestableColor.b, 0.45f);
        glowMat.EnableKeyword("_EMISSION");
        glowMat.SetColor("_EmissionColor", dimmed ? harvestableColor * 0.08f : harvestableColor * 0.45f);
        glow.GetComponent<MeshRenderer>().sharedMaterial = glowMat;
        DestroyImmediate(glow.GetComponent<Collider>());

        Harvestable comp = nugget.AddComponent<Harvestable>();
        comp.scoreValue = score;
        comp.pad = pad;
        comp.padGlow = glow;
        comp.popupColor = harvestableColor;
        comp.tintColor = harvestableColor;
        comp.ClusterId = clusterId;
        spawnedHarvestables.Add(comp);
        return comp;
    }

    // ------------------------------------------------------------------ camera
    private void BuildCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 6500f; // clears the distant horizon backdrops even when driven toward them (Saturn sits ~4400m out)

        BuggyCamera follow = cam.gameObject.GetComponent<BuggyCamera>();
        if (follow == null) follow = cam.gameObject.AddComponent<BuggyCamera>();
        follow.target = buggy.transform;
    }

    // ------------------------------------------------------------------- HUD
    private void BuildHUD()
    {
        GameObject hudGo = new GameObject("HUD");
        hud = hudGo.AddComponent<HUD>();
        hud.buggy = buggy;
        hud.oxygen = OxygenManager.Instance;
        hud.health = buggy.GetComponent<BuggyHealth>();
        hud.levelIndex = LevelManager.LevelNumber;
        hud.planetName = LevelManager.CurrentLevel.planetName;
        // Survival (Ridge) reuses the race-style HUD shell (no score line, tokens hidden), but
        // ridgeSurvivalMode then overrides the time/timer/gates pieces inside it.
        hud.raceMode = GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival;
        hud.ridgeSurvivalMode = GameFlow.IsRidgeSurvival;
        hud.resourceName = GameFlow.IsHarvestStyle && harvestableCount > 0 ? harvestableName : "";
        hud.harvestColor = harvestableColor;
        hud.harvestableOre = LevelManager.CurrentLevel.harvestableOre;

        // Compass driver (Harvester + Race + Prospector): every frame it points the HUD dial at
        // the next LIVE oxygen pod / checkpoint gate only, so the route is the single instrument
        // reading while nuggets and other pickups are found by exploring the terrain by eye.
        if ((GameFlow.IsHarvestStyle || GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival) && hud.showCompass)
        {
            GameObject driverGo = new GameObject("HarvesterCompassDriver");
            HarvesterCompassDriver driver = driverGo.AddComponent<HarvesterCompassDriver>();
            driver.hud = hud;
            driver.buggy = buggy;
            driver.pods = spawnedPods;
        }

        // PROSPECTOR RUN navigational lifeline: the dark descent hides the canyon walls, so
        // flank probes + off-route drift feed an amber warning line under the compass dial.
        if (GameFlow.Mode == GameMode.Prospector)
        {
            GameObject wallGo = new GameObject("ProspectorWallWarning");
            ProspectorWallWarning wall = wallGo.AddComponent<ProspectorWallWarning>();
            wall.hud = hud;
            wall.buggy = buggy;
            wall.terrain = terrain;
        }

        // Pause/mute buttons + the pause overlay (P and hidden-tab auto-pause), and the
        // on-screen touch controls on phones/tablets.
        hudGo.AddComponent<PauseSystem>();
        hudGo.AddComponent<MobileControlsUI>();
    }

    // --------------------------------------------------------------- hazards
    // Active environmental dangers, per planet. In Harvester the storm comes from the level's
    // declared danger (see LevelDefinition.danger; None = calm worlds): Moon/Mars/Io/Titan
    // all run meteor storm weather, Venus keeps its atmospheric peril instead. In Lunar Race
    // it's theme-driven (see CourseHazards): Lunar/Mars run meteor barrages + rock/landslide
    // course events, Venus runs lava flows + acid rain, Io runs lava flows + rock falls (+ a
    // ridge lava avalanche), Titan runs methane rain + lakes.
    // Race storms are lighter and director-owned. Lunar/Mars Prospector Canyon also hands
    // storm timing to that director so its meteor barrages naturally alternate with rockfalls.
    // All director hazards stand down whenever the run is no longer drivable.
    private void BuildHazards()
    {
        if (!GameFlow.IsHarvestStyle && GameMode.Race != GameFlow.Mode && !GameFlow.IsRidgeSurvival) return;

        bool race = GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival;
        bool ridge = race && GameFlow.CurrentCourse == RaceCourse.Ridge;
        PlanetTheme theme = CourseHazards.ThemeFor(LevelManager.CurrentLevel.planetName);
        // Prospector Canyon hands storm + rockfall timing to the director on every world but
        // Venus (whose atmosphere runs its own hazards and never builds the director).
        bool prospectorCanyonWeather = GameFlow.Mode == GameMode.Prospector
            && GameFlow.CurrentCourse == RaceCourse.Canyon
            && theme != PlanetTheme.Venus;
        bool directorDriven = race || prospectorCanyonWeather;

        // Ambient wind: Venus carries the only real wind (firmest on the exposed Ridge),
        // Titan's Ridge a near-imaginary breeze, and Mars' Ridge faint dust-moving breezes.
        // Both modes. Tuned LOW - the gusts should spice the line, never dominate it.
        if (theme == PlanetTheme.Venus)
        {
            // Venus' punishing jump is no longer faked with a halved fling: the planet now runs
            // its own gravity (ApplyPlanetGravity) and its LevelDefinition.jumpApex is set low
            // enough to reproduce exactly the hop this line used to force. One dial, one place.
            WindGusts gusts = buggy.gameObject.AddComponent<WindGusts>();
            gusts.buggy = buggy;
            // MUCH gentler than the old 5/8 shove, which pinned half a g sideways and made the
            // ridge - and the wingsail Favonius on the canyon - effectively undriveable. The
            // exposed ridge keeps a firm breeze, the open survey plains a light one, and the
            // walled canyon barely a nudge (its cliffs shelter the lane, which is also the very
            // reason the ridge is the windiest of the three).
            gusts.gustStrength = ridge ? 3.5f : (race ? 1.5f : 2.5f);
            gusts.gustShake = ridge ? 0.07f : (race ? 0.03f : 0.05f);

            // Venus rides under a live thunderhead in EVERY mode, not just the ridge: bolts stroke
            // the ground ahead with blink-disc telegraphs, and a bolt that lands near the buggy
            // chips the hull and hard-stalls the car (all momentum gone - drive it back up from
            // rest). Only the placement differs per course - the ridge has a crest band to fall
            // along, while survey and canyon aim across the buggy's own line.
            VenusLightning lightning = buggy.gameObject.AddComponent<VenusLightning>();
            lightning.buggy = buggy;
            lightning.hull = buggy.GetComponent<BuggyHealth>();
            lightning.terrain = terrain;
            lightning.usesRidgeCrest = ridge;
        }
        else if (theme == PlanetTheme.Titan && ridge)
        {
            WindGusts gusts = buggy.gameObject.AddComponent<WindGusts>();
            gusts.buggy = buggy;
            gusts.gustStrength = 2.5f;
            gusts.gustShake = 0.07f;
        }

        // The Io sulfur ECLIPSE runs on EVERY Io level: periodic chill that frosts the whole
        // surface extra slippery (grip + top speed drop via the buggy's global scales) with a
        // warning + storm alert ahead of each pass. SWITCHED OFF for the PROSPECTOR RUN: that
        // mode already sits in a permanent (dimmer) eclipse glow - the look the run was tuned to
        // - so the hazard would only darken it further and frost the floor on top.
        if (theme == PlanetTheme.Io && GameFlow.Mode != GameMode.Prospector)
        {
            IoEclipse eclipse = buggy.gameObject.AddComponent<IoEclipse>();
            eclipse.buggy = buggy;
            eclipse.hud = hud;
        }

        // Mars dust devils roam every Mars mode. HUNT frequency: harvest + canyon races throw
        // funnels around regularly; the Ridge trades tall funnels for low drifting dust clouds
        // (a linked tiny gust supplies the push, like Titan's breeze).
        if (theme == PlanetTheme.Mars)
        {
            DustDevilDirector devils = buggy.gameObject.AddComponent<DustDevilDirector>();
            devils.buggy = buggy;
            if (race && GameFlow.CurrentCourse == RaceCourse.Ridge)
            {
                devils.ridgeMode = true;
                devils.calmMin = 16f;
                devils.calmMax = 30f;
                WindGusts gusts = buggy.gameObject.AddComponent<WindGusts>();
                gusts.buggy = buggy;
                gusts.gustStrength = 2.8f;
                gusts.gustShake = 0.05f;
            }
            else if (race && GameFlow.CurrentCourse == RaceCourse.Canyon)
            {
                // Canyon races: a steady rhythm of funnels right through the pack.
                devils.calmMin = 6f;
                devils.calmMax = 11f;
            }
            else
            {
                // Harvester: plenty of devils to dodge across the open plains.
                devils.calmMin = 6f;
                devils.calmMax = 12f;
            }
        }

        // Surface debris spray: the buggy flings coloured droplets (lava/mud/lake/ice) over
        // hazard ground and splashes the chase-cam lens. Runs whole races; no-ops on clean ground.
        buggy.gameObject.AddComponent<SurfaceSpray>();

        // Titan Survey weather: a permanent methane snow blizzard. (The old timed cold-damage
        // cryo-geyser vents are gone - Titan now runs the drive-over methane geyser pools.)
        if (!race && theme == PlanetTheme.Titan)
        {
            MethaneSnowstorm snow = buggy.gameObject.AddComponent<MethaneSnowstorm>();
            snow.buggy = buggy;
        }

        // Drive-over launch geysers on every Io and Titan level, in every mode: pale sulphur
        // dioxide pools (Io) and pale methane pools (Titan). Crossing a pool's centre flings
        // the buggy skyward - a fun detour ramp, never damage.
        if (theme == PlanetTheme.Io || theme == PlanetTheme.Titan)
        {
            DriveOverGeysers geysers = buggy.gameObject.AddComponent<DriveOverGeysers>();
            geysers.buggy = buggy;
            geysers.liquid = theme == PlanetTheme.Titan ? GeyserLiquid.Methane : GeyserLiquid.SulfurDioxide;
        }

        // Harvester storms only exist on worlds that declare a danger; calm planets stay bare.
        if (!directorDriven && LevelManager.CurrentLevel.danger != PlanetDanger.Meteor) return;

        RaceHazardDirector dir = null;
        if (directorDriven)
        {
            // Race modes use the director for their theme hazards. Prospector Canyon on
            // Lunar/Mars also uses it so wall rockfalls and meteor barrages share one schedule.
            GameObject dirGo = new GameObject("CourseHazards");
            // Ridge Survival must hand Start its ramp settings BEFORE it runs, so the
            // director spawns DISABLED, the fields are set, then it wakes (Start fires
            // on re-enable: theme defaults first, then the leg ramp, then the first timer).
            bool ridgeSurvival = GameFlow.IsRidgeSurvival;
            if (ridgeSurvival) dirGo.SetActive(false);
            dir = dirGo.AddComponent<RaceHazardDirector>();
            dir.buggy = buggy;
            dir.terrain = terrain;
            dir.hud = hud;
            dir.oxygen = OxygenManager.Instance;
            dir.checkpointZs = raceCheckpointZs.ToArray();
            if (ridge)
            {
                dir.firstHazardDelay = 7f; // collapses + denser meteors
                dir.rampLoops = ridgeSurvival ? GameFlow.RidgeSurvivalLoops : 0; // apply the leg ramp
            }
            if (ridgeSurvival) dirGo.SetActive(true);
        }

        // The storm builds for themes that throw meteor barrages (Lunar/Mars), and for a
        // Prospector Canyon on ANY world - there the director shares meteor rounds with wall
        // rockfalls, even on Io/Titan (whose lava/rain still stands as their own hazard).
        bool needsStorm = CourseHazards.UsesMeteorStorms(theme)
            || (GameFlow.Mode == GameMode.Prospector && GameFlow.CurrentCourse == RaceCourse.Canyon)
            || (TrialsChallenge != null
                && (TrialsChallenge.profile == ChallengeDef.Profile.MeteorShower
                    || TrialsChallenge.profile == ChallengeDef.Profile.IceAndMeteors));
        // LUNAR TRIALS: a Calm-profile challenge overrides the planet's default barrage -
        // the first challenge runs storm-free while the player learns the ropes.
        if (TrialsChallenge != null && TrialsChallenge.profile == ChallengeDef.Profile.Calm)
            needsStorm = false;
        if (directorDriven && !needsStorm) return;

        GameObject stormGo = new GameObject("MeteorStorm");
        MeteorStorm storm = stormGo.AddComponent<MeteorStorm>();
        storm.buggy = buggy;
        storm.terrain = terrain;
        storm.hud = hud;
        storm.oxygen = OxygenManager.Instance;

        if (directorDriven)
        {
            // The Course HazardDirector owns the cadence and alternates this storm with the
            // course's own hazard (boulder/rock falls on Canyon, collapse/landslide on Ridge),
            // so the storm never schedules its own windows.
            storm.manualControl = true;
            storm.firstStormDelay = float.MaxValue;
            storm.calmMin = 999999f;
            storm.calmMax = 999999f;
            // Mars rounds come slightly harder than the Moon's: a touch more barrage, volleys
            // and meteors and tighter gaps. The Ridge runs noticeably more meteor rain than
            // the Canyon (longer barrages, more volleys and meteors, tighter gaps). Tweak
            // these lines (plus volleyGapMin/Max in MeteorStorm) to retune the rain.
            bool mars = theme == PlanetTheme.Mars;
            if (race)
            {
                storm.barrageDuration = ridge ? 10f : (mars ? 11f : 8f);
                storm.volleys = ridge ? 12 : (mars ? 14 : 8);
                storm.maxMeteorsPerVolley = ridge ? 5 : (mars ? 6 : 5);
                storm.volleyGapMin = ridge ? 0.7f : (mars ? 0.6f : 1.0f);
                storm.volleyGapMax = ridge ? 1.15f : (mars ? 1.05f : 1.8f);
            }
            storm.blastRadius = 10f;
            storm.maxBlastDamage = race ? (mars ? 12f : 10f) : storm.maxBlastDamage;
            if (race)
            {
                storm.telegraphTime = 1.2f;
                storm.meteorTravelTime = 0.9f;
            }

            if (prospectorCanyonWeather)
            {
                // Keep the current Prospector harvest-grade storm ramp when moving its timing
                // under the shared director: grade 1 starts at 75% on Moon, then +33% per grade.
                float hazardBase = mars ? 1f : 0.75f;
                int grade = LevelManager.GetProspectorGrade(LevelManager.CurrentLevelIndex);
                storm.meteorMultiplier = hazardBase * (1f + 0.33f * (grade - 1));
            }

            if (dir != null) dir.storm = storm;

            // Ridge Survival: the cleared-leg ramp also shoves the rain itself - more volleys,
            // a denser barrage and less time to read the telegraph on every cleared leg.
            if (GameFlow.IsRidgeSurvival)
            {
                int loops = GameFlow.RidgeSurvivalLoops;
                if (loops > 0)
                {
                    storm.volleys += 2 * loops;
                    storm.maxMeteorsPerVolley += 1 * loops;
                    storm.telegraphTime = Mathf.Max(0.8f, storm.telegraphTime - 0.1f * loops);
                }
            }
        }
        else
        {
            // Self-scheduling Harvester barrage. The Moon, Io and Titan open their storms at
            // 75% of the stock meteor amount (those worlds carry other dangers on top - boulders,
            // dust, survey vents and blizzards), so a friendlier field there still stacks with
            // everything else; Mars keeps the full stock barrage. Each grade above 1 adds 33%
            // of the world's baseline so a cleared, upgraded field bites back harder.
            float hazardBase = theme == PlanetTheme.Mars ? 1f : 0.75f;
            int grade = LevelManager.GetHarvestGrade(LevelManager.CurrentLevelIndex);
            // The very first Moon field (world 0, grade 1) is the game's opening level, so its
            // meteor barrage is HALVED to meet a brand-new player gently. Every other world and
            // grade keeps its full baseline - only the intro field is eased.
            if (LevelManager.CurrentLevelIndex == 0 && grade <= 1) hazardBase *= 0.5f;
            storm.meteorMultiplier = hazardBase * (1f + 0.33f * (grade - 1));
            storm.firstStormDelay = 16f;
        }
    }

    // ---------------------------------------------------------- prospector darkness
    /// <summary>PROSPECTOR RUN's buggy lamp: a short, warm spot light on the nose that barely
    /// outruns the run's tight fog, so the dark canyon reads one stretch at a time and the
    /// compass (with its wall warnings) carries the navigation. Built at runtime after the
    /// buggy, exactly like the rest of the level - no prefab.</summary>
    private void BuildProspectorHeadlight()
    {
        if (buggy == null) return;
        GameObject lamp = new GameObject("ProspectorHeadlight");
        lamp.transform.SetParent(buggy.transform, false);
        lamp.transform.localPosition = new Vector3(0f, 1.1f, 2.6f);
        lamp.transform.localRotation = Quaternion.Euler(16f, 0f, 0f); // dip onto the road ahead
        Light light = lamp.AddComponent<Light>();
        light.type = LightType.Spot;
        light.spotAngle = 58f;
        light.range = 44f;
        light.intensity = 2.8f;
        light.color = new Color(1f, 0.94f, 0.82f);
        light.shadows = LightShadows.Soft;
    }

    // ------------------------------------------------------- sky and lighting
    private void BuildSkyAndLighting()
    {
        // Each planet gets its own sun colour/brightness, ambient light and haze so the
        // worlds don't just look like the same scene recoloured red.
        LevelDefinition def = LevelManager.CurrentLevel;

        // A very long directional light to emulate the planet's star.
        GameObject dir = new GameObject("Directional Light");
        Light light = dir.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = def.sunColor;
        light.intensity = def.sunIntensity;
        light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

        // A dim ambient light so the night side isn't pitch black.
        RenderSettings.ambientLight = def.ambientColor;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

        // Distance haze tuned per planet (Venus and Titan are wrapped in thick atmospheres).
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogColor = def.fogColor;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = def.fogStartDistance;
        RenderSettings.fogEndDistance = def.fogEndDistance;

        // PROSPECTOR RUN plays in the DARK: the sun is dialled down to a cold faint moonlight,
        // ambient near-black, and the fog pulls in so tight that the buggy's own headlight is the
        // only reliable view. The compass (next-pod dial) and its wall warnings become the
        // navigational lifeline - same reason the briefing calls them out.
        // The palette is PER WORLD (LevelDefinition.prospector*) rather than one shared blackout,
        // so Mars can run a bright dusty dusk, Io can hold its green eclipse glow and Titan a
        // sunset. Tune the brightness/colours there in Levels.cs.
        if (GameFlow.Mode == GameMode.Prospector)
        {
            light.intensity = def.prospectorSunIntensity;
            light.color = def.prospectorSunColor;
            RenderSettings.ambientLight = def.prospectorAmbient;
            RenderSettings.fogColor = def.prospectorFogColor;
            RenderSettings.fogStartDistance = def.prospectorFogStart;
            RenderSettings.fogEndDistance = def.prospectorFogEnd;
        }

        // A distant planet hanging on the horizon (optional, per-level data). Unlit materials
        // ignore the fog AND the sun, so the body reads as a hard backdrop in every mode that
        // builds it - including PROSPECTOR RUN. The world darkens down there, but a real planet
        // hanging in that black sky would still catch the light, so it stays BRIGHT on purpose
        // rather than being dimmed by the night palette.
        if ((GameFlow.Mode == GameMode.Harvester || GameFlow.Mode == GameMode.Prospector
             || GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival)
            && def.backdrop != null && def.backdrop.enabled)
        {
            BuildBackdrop(def.backdrop);
        }

        // Worlds that declare a misty sky (Venus, Mars) get a procedural cloud dome in place of
        // the black void. The Moon, Io and Titan leave this off and keep their night sky.
        // Skipped for PROSPECTOR RUN: a bright cloud dome would punch through the darkness the
        // mode is built around (MistySky itself is never touched).
        if (GameFlow.Mode != GameMode.Prospector)
        {
            MistySky.Build(def);
        }

        // Worlds that declare a night-sky panorama (the Moon's Milky Way) get a starscape dome
        // draped over the void. Pure background: it follows the camera, never occludes terrain.
        NightSky.Build(def);

        // Any moons this world hangs in its own sky, placed clear of the backdrop above.
        BuildSkyMoons(def);
    }

    // --------------------------------------------------------------- horizon backdrop
    // A far-off unlit object either as a sphere (planets) or a flat textured card (2D
    // cutouts like a UFO or sun graphic). Unlit materials ignore the level's fog, so they
    // read as crisp fixtures beyond the playable terrain. Purely cosmetic: no collider.
    private void BuildBackdrop(LevelBackdrop bd)
    {
        bool card = bd.isSkyCard;
        GameObject planetGo = card
            ? GameObject.CreatePrimitive(PrimitiveType.Quad)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);
        planetGo.name = card ? "BackdropSkyCard" : "BackdropPlanet";
        planetGo.transform.position = bd.offset;
        if (card)
        {
            planetGo.transform.localScale = new Vector3(bd.cardScale.x, bd.cardScale.y, 1f);
        }
        else
        {
            planetGo.transform.localScale = Vector3.one * bd.radius * 2f;
        }
        // Cards are single-sided Quads: keep their yaw fixed so they always face the camera's
        // +Z view, and apply only tilt/roll unless a specific yaw is required.
        if (card)
        {
            planetGo.transform.eulerAngles = new Vector3(bd.startRotation.x, 0f, bd.startRotation.z);
        }
        else
        {
            planetGo.transform.eulerAngles = bd.startRotation;
        }
        DestroyImmediate(planetGo.GetComponent<Collider>());

        MeshRenderer mr = planetGo.GetComponent<MeshRenderer>();
        Material mat;
        Texture2D texture = null;
        // A card is flat artwork, so it arrives with the black surround it was saved on. Colour-
        // keying punches that out and lets the sky show through; a sphere is a real lit body and
        // keeps the plain textured shader.
        string texShader = card ? "Hidden/ColorKey" : "Unlit/Texture";
        if (!string.IsNullOrEmpty(bd.texturePath))
        {
            texture = Resources.Load<Texture2D>(bd.texturePath);
            if (texture != null)
            {
                TryClampWrap(texture);
                mat = new Material(FindShader(texShader, "Unlit/Texture"));
                mat.mainTexture = texture;
            }
            else
            {
                Debug.LogWarning("Backdrop texture '" + bd.texturePath
                    + "' not found in Assets/Resources; using flat colour instead.");
                mat = new Material(Shader.Find("Unlit/Color"));
                mat.color = bd.color;
            }
        }
        else
        {
            mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = bd.color;
        }
        mr.sharedMaterial = mat;

        // Saturn-style ring: only meaningful on a sphere (a card already is a flat graphic).
        if (!card && bd.hasRings)
        {
            GameObject ringGo = new GameObject("BackdropRing");
            ringGo.transform.SetParent(planetGo.transform, false);
            // Ring sized in local units (the built-in sphere is unit radius 0.5 before scaling).
            // Outer radius is generous so the band reads from the starting line, and the tilt is
            // kept face-on enough that the ellipse is visible early rather than edge-on.
            MeshFilter mf = ringGo.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildRingMesh(0.75f, 1.72f);
            MeshRenderer rmr = ringGo.AddComponent<MeshRenderer>();
            Material ringMat = new Material(Shader.Find("Unlit/Color"));
            ringMat.color = bd.ringColor;
            rmr.sharedMaterial = ringMat;
            // Tilt the ring plane so it is seen as a wide band across the planet.
            ringGo.transform.localRotation = Quaternion.Euler(50f, 0f, 18f);
        }

        // A slow spin (about the planet's own axis, after any startRotation) so the fixture
        // reads as alive. Cards are single-sided, so spinning them would swing them edge-on
        // to the camera; they stay fixed.
        if (!card)
        {
            planetGo.AddComponent<BackdropSpin>();
        }
    }

    // ------------------------------------------------------------- sky moons
    // How far apart two moons must hang before they stop reading as the same object, in degrees.
    private const float SkyMoonSeparation = 25f;
    // Extra clearance beyond the two discs' own edges, so a moon never grazes the backdrop.
    private const float SkyMoonClearance = 4f;

    /// <summary>
    /// Hangs a world's moons in its sky. Each is a small unlit sphere on a random bearing, kept
    /// low over the horizon and steered well clear of the level's main backdrop, so a moon can
    /// never end up in front of it.
    /// </summary>
    private void BuildSkyMoons(LevelDefinition def)
    {
        if (def.skyMoons == null || def.skyMoons.Length == 0 || def.moonRadius <= 0f) return;

        // What the main backdrop already fills: the direction to stay away from, and how wide it
        // reads. Zero angular radius means there is no backdrop to avoid.
        Vector3 anchorDir = Vector3.zero;
        float anchorAngularRadius = 0f;
        if (def.backdrop != null && def.backdrop.enabled && def.backdrop.offset.sqrMagnitude > 1f)
        {
            float anchorDistance = def.backdrop.offset.magnitude;
            anchorDir = def.backdrop.offset / anchorDistance;
            float anchorExtent = def.backdrop.isSkyCard
                ? Mathf.Max(def.backdrop.cardScale.x, def.backdrop.cardScale.y) * 0.5f
                : def.backdrop.radius;
            anchorAngularRadius = Mathf.Atan(anchorExtent / anchorDistance);
        }

        List<Vector3> placed = new List<Vector3>();
        for (int i = 0; i < def.skyMoons.Length; i++)
        {
            SkyMoon moon = def.skyMoons[i];
            if (moon == null || !moon.enabled) continue;

            float distance = Mathf.Max(200f, moon.distance);
            float radius = def.moonRadius * Mathf.Max(0.01f, moon.sizeScale);
            float moonAngularRadius = Mathf.Atan(radius / distance);
            float clearance = anchorAngularRadius + moonAngularRadius + SkyMoonClearance;

            Vector3 dir = PlaceMoonDirection(moon, anchorDir, clearance, placed);
            placed.Add(dir);

            // A flat card is a unit Quad, so both sides take the full width directly; a sphere is
            // unit-diameter, so it takes the width as its diameter. Either way width == 2 * radius,
            // which keeps every sizeScale comparable between the two shapes.
            Texture2D texture = string.IsNullOrEmpty(moon.texturePath) ? null : Resources.Load<Texture2D>(moon.texturePath);
            GameObject go = GameObject.CreatePrimitive(
                moon.flatImage ? PrimitiveType.Quad : PrimitiveType.Sphere);
            go.name = "SkyMoon_" + (string.IsNullOrEmpty(moon.texturePath) ? "plain" : moon.texturePath);
            go.transform.position = dir * distance;
            if (moon.flatImage)
            {
                // Hold the picture's own proportions so the disc isn't stretched. These portraits
                // are all but square, so this is a nudge rather than a correction.
                float aspect = texture != null ? (float)texture.width / texture.height : 1f;
                go.transform.localScale = new Vector3(radius * 2f * aspect, radius * 2f, 1f);
            }
            else
            {
                go.transform.localScale = Vector3.one * radius * 2f;
                // A random starting longitude so three moons never show the same face twice.
                go.transform.eulerAngles = new Vector3(0f, Random.Range(0f, 360f), 0f);
            }
            DestroyImmediate(go.GetComponent<Collider>());

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            Material mat;
            if (texture != null)
            {
                TryClampWrap(texture);
                mat = new Material(FindShader(
                    moon.flatImage ? "Hidden/ColorKey" : "Unlit/Texture", "Unlit/Texture"));
                mat.mainTexture = texture;
            }
            else
            {
                if (!string.IsNullOrEmpty(moon.texturePath))
                {
                    Debug.LogWarning("Sky moon texture '" + moon.texturePath
                        + "' not found in Assets/Resources; using flat colour instead.");
                }
                mat = new Material(Shader.Find("Unlit/Color"));
                mat.color = moon.color;
            }
            DrawBehindBackdrop(mat);
            mr.sharedMaterial = mat;

            // A card is only ever convincing while it faces the viewer, and a moon can sit up to
            // 150 degrees off the buggy's nose, so copy the camera's rotation every frame (the same
            // trick SurvivalDirector's labels use). A sphere needs no help and would only tumble.
            if (moon.flatImage) go.AddComponent<SkyMoonBillboard>();

            go.AddComponent<BackdropSpin>().spinSpeed = moon.spinSpeed;
        }
    }

    /// <summary>First of the given shader names that actually resolves, so a name missing from a
    /// stripped build hands back a usable shader instead of null (new Material(null) throws).</summary>
    private static Shader FindShader(params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            Shader s = Shader.Find(names[i]);
            if (s != null) return s;
        }
        return Shader.Find("Unlit/Texture");
    }

    /// <summary>
    /// Queues a moon to be painted just before the world's backdrop, so the backdrop always covers
    /// it. Ordinary depth testing cannot promise that on its own: it only compares real distances,
    /// and a moon parked nearer the map centre than the backdrop (Europa sits closer than Io's
    /// Jupiter) would win the test and draw in front. By skipping depth writes and drawing in the
    /// gap below the opaque queue, the backdrop - and any terrain - is painted afterwards and
    /// simply covers the moon, however the two happen to line up or wherever the rover drives.
    /// </summary>
    private static void DrawBehindBackdrop(Material mat)
    {
        mat.renderQueue = 1900; // Geometry is 2000, so backdrops and terrain still draw over us
        // Unlit/Color and Unlit/Texture expose the standard _ZWrite/_ZTest properties, which is how
        // these are set on a built-in shader - Material has no zWrite/zTest of its own.
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        // A Quad is single-sided. Culling off means a card still reads as a disc from behind, which
        // matters for the frame it takes to swing round on the first LateUpdate.
        mat.SetInt("_Cull", (int)CullMode.Off);
    }

    /// <summary>Keeps a flat moon card square to the camera, the way SurvivalDirector's labels
    /// do. Without it a card parked well off to one side would be seen edge-on and vanish.</summary>
    private sealed class SkyMoonBillboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;
        }
    }

    /// <summary>
    /// Rolls a bearing and a low elevation for one moon, keeping the best of the rolls so a moon
    /// is still placed - just as clear as it can manage - however crowded the sky turns out.
    /// </summary>
    private static Vector3 PlaceMoonDirection(SkyMoon moon, Vector3 anchorDir, float anchorClearance,
                                              List<Vector3> placed)
    {
        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            float bearing = Random.Range(-moon.azimuthSpread, moon.azimuthSpread);
            float elevation = Random.Range(moon.minElevation, moon.maxElevation);
            Vector3 dir = SkyDirection(bearing, elevation);

            // Score how far this bearing keeps the moon from everything already in the sky. Zero
            // or more means it is clear of all of it.
            float score = 180f;
            if (anchorDir != Vector3.zero)
            {
                score = Mathf.Min(score, Vector3.Angle(dir, anchorDir) - anchorClearance);
            }
            for (int i = 0; i < placed.Count; i++)
            {
                score = Mathf.Min(score, Vector3.Angle(dir, placed[i]) - SkyMoonSeparation);
            }

            if (score > bestScore) { bestScore = score; best = dir; }
            if (score >= 0f) return dir;
        }
        return best;
    }

    /// <summary>Direction for a bearing (0 = due north / +Z, either side) and an elevation above the horizon.</summary>
    private static Vector3 SkyDirection(float bearingDeg, float elevationDeg)
    {
        float bearing = bearingDeg * Mathf.Deg2Rad;
        float elevation = elevationDeg * Mathf.Deg2Rad;
        float horizontal = Mathf.Cos(elevation);
        return new Vector3(Mathf.Sin(bearing) * horizontal, Mathf.Sin(elevation), Mathf.Cos(bearing) * horizontal);
    }

    /// <summary>
    /// Clamp wrap hides the equirectangular seam at a sphere's pole edges. Guarded because
    /// mutating wrap mode on a non-readable texture throws in player builds (it works in the
    /// editor, which made this look like a disappearing backdrop). The seam is cosmetic, so it
    /// must never be able to take a sky body down with it.
    /// </summary>
    private static void TryClampWrap(Texture2D texture)
    {
        try
        {
            texture.wrapMode = TextureWrapMode.Clamp;
        }
        catch (System.Exception)
        {
            // Never let texture settings kill a sky body.
        }
    }

    /// <summary>Builds a flat annulus mesh (Saturn's rings) in the XZ plane, sized 0.5 = unit sphere radius.</summary>
    private Mesh BuildRingMesh(float inner, float outer)
    {
        const int seg = 96;
        int ring = seg + 1;
        Vector3[] verts = new Vector3[ring * 2];
        Vector2[] uvs = new Vector2[ring * 2];
        for (int i = 0; i < ring; i++)
        {
            float a = (float)i / seg * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i] = new Vector3(c * inner, 0f, s * inner);
            verts[i + ring] = new Vector3(c * outer, 0f, s * outer);
            uvs[i] = new Vector2(0f, 0f);
            uvs[i + ring] = new Vector2(1f, 0f);
        }
        int[] tris = new int[seg * 6];
        for (int i = 0, ti = 0; i < seg; i++)
        {
            int a = i, b = i + 1, c = i + ring, d = i + 1 + ring;
            tris[ti++] = a; tris[ti++] = b; tris[ti++] = d;
            tris[ti++] = a; tris[ti++] = d; tris[ti++] = c;
        }
        Mesh m = new Mesh { name = "BackdropRingMesh" };
        m.SetVertices(verts);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    private Material MakeSolid(Color color, float glossiness)
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        mat.SetFloat("_Glossiness", glossiness);
        return mat;
    }

    // -------------------------------------------------------------- CrazyGames bridge
    private void Update()
    {
        // The menu (MenuController.Garage.Update) ticks this poll too, so the bridge's once-only
        // LoadingStop + first-ready settings reload fires at the FIRST interactive screen of the
        // session (typically the menu), no matter which scene the SDK becomes ready in.
        // GameplayStart is deliberately NOT sent here: LevelIntroSequence.HandOff emits it when
        // a level actually hands control to the player.
        CrazyGamesBridge.Poll();
    }
}
