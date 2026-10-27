using UnityEngine;

/// <summary>
/// A Lunar Race rival: a computer-controlled buggy that drives the same downhill canyon
/// course as the player. Every race places the SAME THREE rivals - Bronze (slow), Silver
/// (medium) and Gold (fast) - but the course is raced at a GRADE (1..5, shown as gold stars
/// on the world select). Each harder grade makes every rival 5 seconds quicker, so clearing
/// a grade proves a genuinely faster run. Grades unlock in order per world: beating the
/// field (the GOLD rival) at your current grade unlocks the next, faster one.
///
/// Rivals are now REAL bodies, not ghosts: each carries a kinematic rigidbody + box
/// collider so the player buggy physically bounces off them. They ride the course exactly
/// like the player: boulder/overhang-rock hits slam them (TakeObstacleHit), methane mud
/// bogs them (SetMudSlow), collapse pits they fall into grab them while they climb out
/// (SetCollapseSlow), and meteor blasts bounce them sideways off line (TakeMeteorKick).
/// The moment a rival crosses the line it GHOSTS OUT (collider + physics detection off) so
/// a buggy still racing the course can never be physically blocked by rivals parked in the
/// finish area.
/// Pure burn effects - acid, methane snow, lava - never slow a rival. Because obstacles
/// make their times unpredictable, each rival records its ACTUAL finish time from its own
/// race clock, and the result screen scores the player against those real times (a rival
/// still on course when the player crosses has finished strictly later, so it counts as
/// beaten).
///
/// Beating a tier pays tokens (see TokensEarnedFor): each tier banks its own TOTAL pot -
/// Bronze 60, Silver 100, Gold 200; a last-place FINISH pays a 30-token consolation and a
/// run that never crossed the line pays nothing. The result screen uses ResultFor to name the
/// best tier beaten ("RACE WON · BEAT GOLD!" / "... SILVER" / "... BRONZE" / "RACE LOST").
///
/// GameBootstrap creates and configures the trio for race loads (timed for the run's
/// current grade); RaceCountdown holds them frozen until GO.
/// </summary>
public class RivalRacer : MonoBehaviour
{
    public enum Tier
    {
        Bronze,
        Silver,
        Gold,
    }

    /// <summary>Overall outcome of a finished race versus the rival league.</summary>
    public enum RaceResult
    {
        Lost,
        BeatenBronze,
        BeatenSilver,
        BeatenGold,
    }

    /// <summary>Number of race grades (also the race star count on the world select).</summary>
    public const int GradeCount = 5;

    /// <summary>Whole seconds each harder race grade shaves off every rival's target time.</summary>
    public const float RaceSecondsPerGrade = 5f;

    /// <summary>Tiny per-grade rival trim on the rougher worlds (Mars/Venus/Io/Titan): their
    /// courses already harden through terrain and hazards, so the rivals barely quicken
    /// (1s/grade vs the Moon's <see cref="RaceSecondsPerGrade"/>).</summary>
    public const float RaceSecondsPerGradeSoft = 1f;

    [Header("References")]
    public MoonTerrainGen terrain;
    public BuggyController player;

    [Header("Tier")]
    public Tier tier = Tier.Silver;

    [Header("Pacing")]
    [Tooltip("The rival's target finish time (seconds): faster than an average run, slower than a clean one.")]
    public float rivalTargetSeconds = 118f;
    [Tooltip("Seconds the rival spends easing up from a standstill at the line. Without it the "
            + "field is instantly at racing pace and zooms out of sight while the player is still "
            + "getting going. The cruise speed is raised to match, so the target finish time - and "
            + "therefore every tier and medal - is unchanged.")]
    public float launchRampSeconds = 3.5f;
    [Tooltip("Lateral lane wander either side of the centre line (metres).")]
    public float laneSwing = 4f;
    [Tooltip("Speeds the rival bobs up to +/- this fraction (so it never feels robotic).")]
    public float speedWobble = 0.04f;

    [Header("Start grid")]
    [Tooltip("Starting grid Z offset AHEAD of the line (metres toward the finish): positive rows put the rival in front of the player so the whole field launches into a staggered pack - nobody sits on the player's bumper.")]
    public float startRow = 0f;
    [Tooltip("Starting grid lateral column (metres either side of the centre line). The column melts away over the first stretch so the rival works onto its normal racing line.")]
    public float startColumn = 0f;

    [Header("Obstacle damage")]
    [Tooltip("How long an obstacle hit (boulder, collapse pit...) keeps the rival slowed.")]
    public float slowDuration = 2.6f;
    [Tooltip("Slowest the rival drops to (as a fraction of its pace) right after an obstacle hit; recovers back to 1 over slowDuration.")]
    public float minSlowFactor = 0.45f;
    [Tooltip("How hard a land fall grips the rival while it is scrambling out of the pit (fraction of pace; slightly gentler than a solid mud bog).")]
    public float collapseSlowFactor = 0.6f;
    [Tooltip("How much an ice patch costs a rival crossing it, as a fraction of its pace. Ice never "
            + "slowed the PLAYER either - it froze the rover, holding the speed it arrived at and "
            + "taking away the wheels, the steering and the brakes. A rival cannot be frozen, so a "
            + "small pace dip is the nearest equivalent, and it is deliberately MILD: the natural "
            + "play against a patch is to hop it or ease onto it, and a gentle dip means that "
            + "caution is rewarded instead of punished. Crossing every patch on the course still "
            + "costs the field a little time, so a clean line is worth something.")]
    public float iceSlowFactor = 0.87f;

    [Header("Meteor bounce")]
    [Tooltip("How long a meteor blast shoves the rival sideways before it corrects back.")]
    public float meteorKickDuration = 1.5f;
    [Tooltip("Max sideways shove (metres) from a direct meteor blow, scaled down with distance.")]
    public float meteorKickMaxOffset = 4.5f;

    [Header("Player bump (two-wheel settle)")]
    [Tooltip("Duration of the transient two-wheel tilt after a player ram (seconds).")]
    public float bumpTiltDuration = 0.9f;
    [Tooltip("Peak roll (degrees) the rival leans onto its two OUTER wheels after a broadside "
            + "ram, away from the collision direction.")]
    public float bumpRollDeg = 14f;
    [Tooltip("Peak pitch (degrees) a nose-to-tail ram tips the LEAD buggy's rear up (and the "
            + "follower's nose up) for a moment.")]
    public float bumpPitchDeg = 8f;
    [Tooltip("Approach speed (m/s) below which a touch is just a kiss - no two-wheel tilt.")]
    public float bumpMinSpeed = 2.5f;
    [Tooltip("Cooldown before sustained contact can replay the tilt (seconds), so a stuck-on car "
            + "does not waggle the field every physics step.")]
    public float bumpCooldown = 0.8f;

    /// <summary>The rival's recorded finish time (centiseconds) once it has crossed the line; 0 until then.</summary>
    public int FinishCenti { get; private set; }

    /// <summary>The rival's current Z along the course (for the HUD route timeline).</summary>
    public float CurrentZ { get { return z; } }

    /// <summary>True once the rival has crossed the finish line this run.</summary>
    public bool CrossedFinish { get { return finished; } }

    public int TargetCenti
    {
        get { return Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1f, rivalTargetSeconds) * 100f)); }
    }

    private bool finished;
    private bool held = true;
    private bool initialized;
    private float z;
    private float raceStartZ;
    private float finishZ;
    private float runClock;
    private float slowUntil;
    private bool mudSlow;
    private bool collapseSlow;
    private bool iceSlow;
    private float meteorKickUntil;
    private float meteorKickX;
    private BoxCollider finishBox;
    private Rigidbody rb;
    private TextMesh tagMesh;
    private float bumpUntil = -10f;
    private float bumpRoll;
    private float bumpPitch;
    private float lastBumpTime = -10f;

    // ------------------------------------------------------------------ clan helpers
    public static string TierName(Tier t)
    {
        switch (t)
        {
            case Tier.Bronze: return "BRONZE";
            case Tier.Gold: return "GOLD";
            default: return "SILVER";
        }
    }

    public static Color TierColor(Tier t)
    {
        switch (t)
        {
            case Tier.Bronze: return new Color(0.8f, 0.5f, 0.19f, 1f);
            case Tier.Gold: return new Color(1f, 0.84f, 0.25f, 1f);
            default: return new Color(0.72f, 0.76f, 0.8f, 1f);
        }
    }

    /// <summary>Default target course time per tier at GRADE 1 (index 0, seconds). Difficulty
    /// pass: the whole field eased off for the player (Silver +2s, Gold +2s) and the opening
    /// grade's Bronze now runs a full 9s slower than its old booklet, so a fresh player can
    /// clear the first race and unlock the next world's HARVEST without stacking upgrades
    /// first.</summary>
    public static float DefaultSeconds(Tier t)
    {
        switch (t)
        {
            case Tier.Bronze: return 117f;
            case Tier.Gold: return 97f;
            default: return 103.5f;
        }
    }

    /// <summary>Target course time per tier at a given grade (0..4): on the Moon each harder
    /// grade makes every rival <see cref="RaceSecondsPerGrade"/> seconds quicker, so the field
    /// at grade 5 is Bronze 92 / Silver 83.5 / Gold 77. Beyond the opening grade, Bronze drops
    /// its next-world ease to a flat +4s line (108 + 4 = 112s base).
    ///
    /// MARS opens back on the Moon's proven first-grade pace (the field used to run that table
    /// before it was eased) - but its rivals only trim a gentle
    /// <see cref="RaceSecondsPerGradeSoft"/> per harder grade, because Mars's rougher terrain
    /// and heavier hazards supply the extra difficulty, not faster rivals.
    ///
    /// VENUS, IO and TITAN keep their friendlier campaign pacing: the field sits at its old
    /// "hardest grade" top run (Bronze 135 / Silver 125 / Gold 115 at grade 5) and each EASIER
    /// grade only peps it up by <see cref="RaceSecondsPerGradeSoft"/> per step - the top is
    /// exactly as before, the drop-off to the opening grade is just one second at a time.</summary>
    public static float DefaultSeconds(Tier t, int grade)
    {
        int g = Mathf.Max(0, grade);
        string planet = LevelManager.CurrentLevel != null
            ? LevelManager.CurrentLevel.planetName
            : null;
        if (planet == "MARS")
        {
            // Opening grade paces like the Moon (back to the pre-ease table), but the step
            // between grades is tiny - the terrain and hazards push later grades instead.
            switch (t)
            {
                case Tier.Bronze: return 108f + (g == 0 ? 9f : 4f) - g * RaceSecondsPerGradeSoft;
                case Tier.Gold: return 97f - g * RaceSecondsPerGradeSoft;
                default: return 103.5f - g * RaceSecondsPerGradeSoft;
            }
        }
        if (planet == "VENUS" || planet == "IO" || planet == "TITAN")
        {
            // Hardest grade keeps the old friendly table's top run; easier grades ease off by
            // just one second per grade, so later grades never become a rival-speed wall.
            int back = (GradeCount - 1) - g;
            switch (t)
            {
                case Tier.Bronze: return 135f + back * RaceSecondsPerGradeSoft;
                case Tier.Gold: return 115f + back * RaceSecondsPerGradeSoft;
                default: return 125f + back * RaceSecondsPerGradeSoft;
            }
        }
        switch (t)
        {
            case Tier.Bronze: return 108f + (g == 0 ? 9f : 4f) - g * RaceSecondsPerGrade;
            case Tier.Gold: return 97f - g * RaceSecondsPerGrade;
            default: return 103.5f - g * RaceSecondsPerGrade;
        }
    }

    public static Color ResultColor(RaceResult r)
    {
        switch (r)
        {
            case RaceResult.BeatenGold: return TierColor(Tier.Gold);
            case RaceResult.BeatenSilver: return TierColor(Tier.Silver);
            case RaceResult.BeatenBronze: return TierColor(Tier.Bronze);
            default: return new Color(1f, 0.35f, 0.3f, 1f);
        }
    }

    /// <summary>Title for the result board / HUD banner. <paramref name="finishedRace"/> is true
    /// when the player crossed the line (including last place inside the post-bronze window);
    /// a RunResult.Lost with finishedRace false means the cutoff concluded the run before the
    /// player reached the finish.</summary>
    public static string ResultTitle(RaceResult r, bool finishedRace = true)
    {
        switch (r)
        {
            case RaceResult.BeatenGold: return "Congratulations! You beat GOLD!";
            case RaceResult.BeatenSilver: return "Congratulations! You beat SILVER!";
            case RaceResult.BeatenBronze: return "Congratulations! You beat BRONZE!";
            default: return finishedRace
                ? "You Finished Last! " + UpgradeSystem.RaceLastPlaceTokens + " Tokens Consolation Prize!"
                : "Better Luck Next Time!";
        }
    }

    /// <summary>The player buggy of the current race (set by GameBootstrap on load), or null
    /// outside races. Used by the slipstream pocket so rivals can draft the player too.</summary>
    public static BuggyController PlayerBuggy;

    /// <summary>Live rivals on the current course (registered at Start, dropped OnDestroy).
    /// Cheap per-frame iteration for slipstream checks - never a FindObjectsByType.</summary>
    private static readonly System.Collections.Generic.List<RivalRacer> Registry = new System.Collections.Generic.List<RivalRacer>();

    /// <summary>Rivals currently on the course (replaces All() for hot-loop reads like the
    /// slipstream wake check).</summary>
    public static System.Collections.Generic.List<RivalRacer> Rivals { get { return Registry; } }

    /// <summary>All rivals currently on the course (guards against race loads racing old instances).</summary>
    public static RivalRacer[] All()
    {
        return FindObjectsByType<RivalRacer>();
    }

    /// <summary>Reads a rival's EFFECTIVE time for scoring. A rival that crossed the line is
    /// scored on its ACTUAL finish time (course obstacles may have slowed it); a rival still
    /// on the course when the race is settled ran longer than the player, so it scores a huge
    /// sentinel (strictly beaten) - never an assumed booklet pace.</summary>
    private static int EffectiveCenti(RivalRacer r)
    {
        if (r == null) return 0;
        if (r.finished) return Mathf.Max(1, r.FinishCenti);
        return 10000000;
    }

    private static void TargetCentiOf(out int bronze, out int silver, out int gold)
    {
        // Fallbacks use the run's CURRENT grade so scoring without a live field on screen
        // still matches the booklet the rivals were paced from.
        int grade = Mathf.Clamp(GameFlow.RaceGradeIndex, 0, GradeCount - 1);
        bronze = Mathf.RoundToInt(DefaultSeconds(Tier.Bronze, grade) * 100f);
        silver = Mathf.RoundToInt(DefaultSeconds(Tier.Silver, grade) * 100f);
        gold = Mathf.RoundToInt(DefaultSeconds(Tier.Gold, grade) * 100f);
        RivalRacer[] rivals = All();
        for (int i = 0; i < rivals.Length; i++)
        {
            switch (rivals[i].tier)
            {
                case Tier.Bronze: bronze = EffectiveCenti(rivals[i]); break;
                case Tier.Silver: silver = EffectiveCenti(rivals[i]); break;
                case Tier.Gold: gold = EffectiveCenti(rivals[i]); break;
            }
        }
    }

    /// <summary>The best tier this player's race time beat (Lost if none).</summary>
    public static RaceResult ResultFor(int playerCenti)
    {
        if (playerCenti <= 0) return RaceResult.Lost;
        int bronze, silver, gold;
        TargetCentiOf(out bronze, out silver, out gold);
        if (playerCenti <= gold) return RaceResult.BeatenGold;
        if (playerCenti <= silver) return RaceResult.BeatenSilver;
        if (playerCenti <= bronze) return RaceResult.BeatenBronze;
        return RaceResult.Lost;
    }

    /// <summary>True when the run finished ahead of the WHOLE field (the GOLD rival) - the
    /// definition of clearing the current race grade and unlocking the next, faster one.</summary>
    public static bool GradeClearedFor(int playerCenti)
    {
        return ResultFor(playerCenti) == RaceResult.BeatenGold;
    }

    /// <summary>Maps a race result to the best TIER beaten (RivalRacer.Tier order:
    /// Bronze 0, Silver 1, Gold 2), or -1 when nothing was beaten. Drives the persistent
    /// per-world best-tier progress that opens the next race/ridge world.</summary>
    public static int TierIndexOf(RaceResult r)
    {
        switch (r)
        {
            case RaceResult.BeatenGold: return (int)Tier.Gold;
            case RaceResult.BeatenSilver: return (int)Tier.Silver;
            case RaceResult.BeatenBronze: return (int)Tier.Bronze;
            default: return -1;
        }
    }

    /// <summary>Tokens a race awards for the best tier beaten (each tier pays its own TOTAL pot:
    /// Bronze 60, Silver 100, Gold 200 - not a sum of lower tiers). FINISHING the race in last
    /// place (no rival beaten, inside the post-bronze cutoff window) still pays a small
    /// consolation; a run that never crossed the line (the cutoff concluded, or a crash) pays
    /// nothing - <paramref name="finishedRace"/> is exactly that "crossed the line" flag.</summary>
    public static int TokensEarnedFor(int playerCenti, bool finishedRace)
    {
        if (!finishedRace) return 0;
        switch (ResultFor(playerCenti))
        {
            case RaceResult.BeatenGold:
                return UpgradeSystem.RaceGoldTokens;
            case RaceResult.BeatenSilver:
                return UpgradeSystem.RaceSilverTokens;
            case RaceResult.BeatenBronze:
                return UpgradeSystem.RaceBronzeTokens;
            default:
                return UpgradeSystem.RaceLastPlaceTokens;
        }
    }

    /// <summary>Builds the rival's body, visuals and grid slot IMMEDIATELY, so the whole field
    /// is stood on the grid and visible through the level intro and the 3-2-1 ceremony. The
    /// component stays enabled so its name tag keeps billboarding, but <see cref="SetHeld"/>
    /// keeps it planted until GO. GameBootstrap calls this right after it has configured the
    /// tier, pacing and grid fields; idempotent.</summary>
    public void Initialize()
    {
        if (initialized) return;
        initialized = true;

        if (terrain == null) terrain = FindAnyObjectByType<MoonTerrainGen>();
        if (player == null) player = FindAnyObjectByType<BuggyController>();

        if (!Registry.Contains(this)) Registry.Add(this);

        raceStartZ = MoonTerrainGen.RaceStartZ;
        finishZ = terrain != null ? terrain.RaceFinishZ : raceStartZ + 1144f;
        z = raceStartZ + startRow;
        runClock = 0f;

        BuildPhysics();
        BuildVisuals();
        PositionAtGrid();
    }

    private void Start()
    {
        Initialize();
    }

    /// <summary>Holds the rival on the grid (true) or releases it to drive (false). The body
    /// stays built and visible either way; only the driving Update is gated.</summary>
    public void SetHeld(bool on)
    {
        held = on;
    }

    private void OnDestroy()
    {
        Registry.Remove(this);
    }

    /// <summary>Plants the rival on its starting grid slot (row ahead of the line + lateral
    /// column) so it is visible and in place through the level intro and the countdown
    /// ceremony. The body is already built; the hold flag (see <see cref="SetHeld"/>) keeps it
    /// there until "GO" releases it.</summary>
    private void PositionAtGrid()
    {
        if (terrain == null) return;
        float gx = terrain.RaceCenterX(z) + startColumn;
        float gy = terrain.SampleHeight(new Vector3(gx, 0f, z)) + 0.55f;
        Vector3 pos = new Vector3(gx, gy, z);
        Quaternion rot = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        if (rb != null)
        {
            rb.position = pos;
            rb.rotation = rot;
        }
        transform.position = pos;
        transform.rotation = rot;
    }

    /// <summary>Gives the rival a real, impenetrable body: a kinematic rigidbody with a soft
    /// box collider, so the player's buggy physically bounces off it (kinematic bodies are
    /// never knocked around themselves - the buggy pushes away). The body is driven with
    /// <see cref="Rigidbody.MovePosition"/> / <see cref="Rigidbody.MoveRotation"/> so the
    /// physics engine sees it as a MOVING car: a ram then caps the buggy to the rival's speed
    /// (a fair trade) instead of slamming a teleporting near-static wall and wiping the buggy's
    /// momentum.</summary>
    private void BuildPhysics()
    {
        rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.95f, 0f);
        box.size = new Vector3(2.4f, 1.4f, 3.4f);
        finishBox = box;

        // Nearly inelastic so a ram that reaches the rival's bumper is capped to the rival's own
        // speed (a fair trade between two moving cars) rather than springing the buggy backward
        // off a "brick wall". The shove-y feel now comes from the scripted bump, not restitution.
        PhysicsMaterial mat = new PhysicsMaterial("RivalBody");
        mat.dynamicFriction = 0.25f;
        mat.staticFriction = 0.3f;
        mat.frictionCombine = PhysicsMaterialCombine.Minimum;
        mat.bounciness = 0.12f;
        mat.bounceCombine = PhysicsMaterialCombine.Maximum;
        box.sharedMaterial = mat;
    }

    private void BuildVisuals()
    {
        Color tierColor = TierColor(tier);
        Color accent = tier == Tier.Gold
            ? new Color(1f, 0.95f, 0.55f, 1f)
            : new Color(1f, 0.5f, 0.1f, 1f);

        LunarRoverArt.RoverMats mats = LunarRoverArt.CreateMats(LunarRoverArt.SkinOf(2), true);
        mats.primary.color = tierColor;
        mats.accent.color = accent;

        LunarRoverArt.BuildChassis(transform, mats);
        LunarRoverArt.BuildWheelVisual(transform, new Vector3(1.1f, 0f, 1.55f), mats, 0.5f, 0.5f);
        LunarRoverArt.BuildWheelVisual(transform, new Vector3(-1.1f, 0f, 1.55f), mats, 0.5f, 0.5f);
        LunarRoverArt.BuildWheelVisual(transform, new Vector3(1.1f, 0f, -1.55f), mats, 0.5f, 0.5f);
        LunarRoverArt.BuildWheelVisual(transform, new Vector3(-1.1f, 0f, -1.55f), mats, 0.5f, 0.5f);

        // Tier-coloured beacon column so the three rivals read apart at a glance.
        GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beacon.name = "RivalBeacon";
        beacon.transform.SetParent(transform, false);
        beacon.transform.localScale = new Vector3(0.18f, 2.2f, 0.18f);
        beacon.transform.localPosition = new Vector3(0f, 2.4f, 0f);
        Material beaconMat = new Material(Shader.Find("Standard"));
        beaconMat.EnableKeyword("_EMISSION");
        beaconMat.SetColor("_EmissionColor", tierColor * 0.8f);
        beaconMat.color = tierColor;
        beacon.GetComponent<MeshRenderer>().sharedMaterial = beaconMat;
        Destroy(beacon.GetComponent<Collider>());

        // Floating tier name tag, billboarded toward the camera each frame.
        GameObject tagGo = new GameObject("RivalTag");
        tagGo.transform.SetParent(transform, false);
        tagGo.transform.localPosition = new Vector3(0f, 3.4f, 0f);
        tagMesh = tagGo.AddComponent<TextMesh>();
        tagMesh.font = HUD.ResolveDefaultFont();
        tagMesh.text = TierName(tier);
        tagMesh.fontSize = 44;
        tagMesh.characterSize = 0.02f;
        tagMesh.anchor = TextAnchor.MiddleCenter;
        tagMesh.alignment = TextAlignment.Center;
        tagMesh.color = tierColor;
        MeshRenderer tagMr = tagGo.GetComponent<MeshRenderer>();
        if (tagMr != null && tagMesh.font != null)
        {
            tagMr.sharedMaterial = tagMesh.font.material;
        }
    }

    private float CourseSpeed()
    {
        float distance = finishZ - raceStartZ;
        // The rival's own pace plus any SLIPSTREAM it is running: tight in another racer's
        // wake (rival or the player) pulls free top speed from the draft, just like the buggy.
        // Cruise speed is the distance spread over the target time LESS the half-second credit
        // the launch ramp gives away (a smooth ease-in averages half of cruise), so easing the
        // start off does not make the rival slower overall - only later off the line.
        float cruise = distance / Mathf.Max(1f, rivalTargetSeconds - launchRampSeconds * 0.5f);
        float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(runClock / Mathf.Max(0.01f, launchRampSeconds)));
        float baseSpeed = cruise * ramp + Slipstream.BonusKmh(transform.position) / 3.6f;
        float wobble = 1f + speedWobble * Mathf.Sin(Time.time * 0.75f);

        // Obstacle damage: freshly hit rivals crawl, then recover back to full pace.
        float slow = 1f;
        if (slowUntil > Time.time)
        {
            float remain = Mathf.Clamp01((slowUntil - Time.time) / slowDuration);
            slow = Mathf.Lerp(1f, minSlowFactor, remain);
        }
        // Methane mud: while wallowing through a mud flat the rival is stuck at the same
        // crawl as the player - no damage, just the mud grabbing the wheels.
        if (mudSlow) slow = Mathf.Min(slow, minSlowFactor);
        // Ice: skating a patch costs the rival a MILD dip - the player is not slowed by ice at
        // all (it freezes them instead), so this is deliberately gentle. A player who hops the
        // patch or eases onto it keeps their pace and buys this back against the field.
        if (iceSlow) slow = Mathf.Min(slow, iceSlowFactor);
        // Land falls: a rival caught in a collapse pit scrambles out slower than clean ground.
        if (collapseSlow) slow = Mathf.Min(slow, collapseSlowFactor);
        return baseSpeed * wobble * slow;
    }

    private void Update()
    {
        if (tagMesh != null && Camera.main != null)
        {
            // Bill: keep the name tag readable from the chase camera.
            Vector3 toCam = Camera.main.transform.position - tagMesh.transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.01f)
            {
                tagMesh.transform.rotation = Quaternion.LookRotation(toCam, Vector3.up);
            }
        }

        if (terrain == null || player == null) return;
        if (held) return; // stand planted on the grid until GO (the tag still billboards above)

        OxygenManager oxygen = OxygenManager.Instance;
        if (oxygen == null || oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive)
        {
            return; // the run is over - the rival holds where it is
        }

        if (!finished)
        {
            runClock += Time.deltaTime;
            z += CourseSpeed() * Time.deltaTime;
            if (z >= finishZ)
            {
                z = finishZ;
                finished = true;
                FinishCenti = Mathf.RoundToInt(runClock * 100f);
                GhostFinishedBody();
            }
        }

        // Grid hand-off: the rival launches on its starting COLUMN and melts across onto its
        // normal weave over the first stretch, so the staggered pack fans out without everyone
        // piling into the same lane at GO.
        float gridMelt = Mathf.Clamp01((z - raceStartZ - startRow) / 30f);
        // Each tier races its OWN line down the course: slightly different weave phase and a
        // small lateral bias, so the three never sit nose-to-tail in the same wake and their
        // drafts stay occasional moments of close racing rather than a permanent slip train.
        float weave = laneSwing * Mathf.Sin(z / terrain.raceWeaveWavelength * 0.45f + RoutePhase(tier)) + RouteBias(tier);
        // Meteor bounce: a blast shoves the rival sideways and it slides back onto line
        // (no speed lost - it just got knocked off course for a moment).
        float kick = 0f;
        if (Time.time < meteorKickUntil)
        {
            float kT = Mathf.Clamp01((meteorKickUntil - Time.time) / meteorKickDuration);
            kick = meteorKickX * kT;
        }
        float cx = terrain.RaceCenterX(z) + weave + startColumn * (1f - gridMelt) + kick;
        float ground = terrain.SampleHeight(new Vector3(cx, 0f, z));
        Vector3 pos = new Vector3(cx, ground + 0.55f, z);

        // Facing down-course (numeric derivative of the same routed line).
        float d = 0.5f;
        float cxA = cx;
        float cxB = terrain.RaceCenterX(z + d) + laneSwing * Mathf.Sin((z + d) / terrain.raceWeaveWavelength * 0.45f + RoutePhase(tier)) + RouteBias(tier)
            + startColumn * (1f - Mathf.Clamp01((z + d - raceStartZ - startRow) / 30f)) + kick;
        Vector3 heading = new Vector3(cxB - cxA, 0f, d).normalized;
        Quaternion rot = Quaternion.LookRotation(heading, Vector3.up);
        // Player-ram settle: a brief two-wheel tilt (roll/pitch) decayed back to the racing line
        // so a bumper incumbent visibly rocks without leaving its lane.
        if (Time.time < bumpUntil)
        {
            float k = Mathf.Clamp01((bumpUntil - Time.time) / bumpTiltDuration);
            rot = Quaternion.LookRotation(heading, Vector3.up)
                * Quaternion.Euler(bumpPitch * k, 0f, bumpRoll * k);
        }

        // Drive through the physics system so the collider carries the real velocity (see
        // BuildPhysics): the body reads as a moving car to any buggy that rams it.
        if (rb != null)
        {
            rb.MovePosition(pos);
            rb.MoveRotation(rot);
        }
        else
        {
            transform.position = pos;
            transform.rotation = rot;
        }
    }

    /// <summary>Weave phase per tier: a third of a cycle apart each, so gold, silver and bronze
    /// are never on the same lateral line at the same point of the course (they cross, but do
    /// not ride the same glue line - the pack naturally spreads instead of train-drafting).</summary>
    public static float RoutePhase(Tier t)
    {
        switch (t)
        {
            case Tier.Bronze: return 0f;
            case Tier.Silver: return 2.0944f;   // 120 degrees
            case Tier.Gold: return 4.1888f;     // 240 degrees
            default: return 0f;
        }
    }

    /// <summary>Persistent lateral bias per tier (metres off the centre line, its own lane):
    /// bronze holds an inside line, silver the outside, gold a whisper outside the other way -
    /// apart enough that a tailing rival is rarely dead in the leader's wake.</summary>
    public static float RouteBias(Tier t)
    {
        switch (t)
        {
            case Tier.Bronze: return 1.8f;
            case Tier.Silver: return -2.2f;
            case Tier.Gold: return 0.6f;
            default: return 0f;
        }
    }

    /// <summary>
    /// A PHYSICAL course event bit the rival: a boulder (or overhang rock) slams its body and
    /// it loses momentum for a moment, then recovers. Rivals never die - the slow is what makes
    /// the field honest. Weather, lava and acid never slow them; they only pay for real impacts.
    /// </summary>
    public void TakeObstacleHit(float dmg)
    {
        if (finished || dmg <= 0f) return;
        slowUntil = Mathf.Max(slowUntil, Time.time + slowDuration);
    }

    /// <summary>The moment a rival crosses the line its body goes GHOST: collider off, physics
    /// detection off. A finished rival parks on the finish line, so without this its parked body
    /// could block a buggy still racing for the line (stuck behind a box in the finish area
    /// while the clock runs) - ghosting guarantees the finish is never physically blocked.
    /// Obstacle interactions already gate on <see cref="finished"/>, so nothing else changes.</summary>
    private void GhostFinishedBody()
    {
        if (finishBox != null) finishBox.enabled = false;
        if (rb != null) rb.detectCollisions = false;
    }

    /// <summary>Sets whether the rival is currently wallowing through a methane mud flat
    /// (set every frame by the race director's surface check; rivals only exist in races).</summary>
    public void SetMudSlow(bool on)
    {
        if (finished) return;
        mudSlow = on;
    }

    /// <summary>Sets whether the rival is inside a collapse-pit wake it is scrambling out of
    /// (set every frame by the race director; they ride the course exactly like the player).</summary>
    public void SetCollapseSlow(bool on)
    {
        if (finished) return;
        collapseSlow = on;
    }

    /// <summary>Sets whether the rival is skating an ice patch this frame (set every frame by
    /// the race director, exactly like the mud flag). Costs the rival the mild iceSlowFactor
    /// dip - gentle on purpose, so a player who hops or eases across a patch outpaces the
    /// field rather than trading time with it.</summary>
    public void SetIceSlow(bool on)
    {
        if (finished) return;
        iceSlow = on;
    }

    /// <summary>A meteor blast bounces the rival sideways off line for a moment - shoved, not
    /// slowed (kinematic body, so the shove plays out as a lateral nudge that slides back).</summary>
    public void TakeMeteorKick(Vector3 fromPoint, float strength)
    {
        if (finished || strength <= 0f) return;
        Vector3 delta = transform.position - fromPoint;
        float side = Vector3.Dot(delta, Vector3.right);
        float sign = side >= 0f ? 1f : -1f;
        meteorKickX = sign * meteorKickMaxOffset * Mathf.Clamp01(strength);
        meteorKickUntil = Time.time + meteorKickDuration;
    }

    /// <summary>Rival bodies: boulders and overhang rocks physically slam the rival's box, and a
    /// player ram plays the two-wheel wobble (both cars tip away from the collision direction).
    /// Physics already dealt the bounce (kinematic wall) - this adds the flavour.</summary>
    private void OnCollisionEnter(Collision c)
    {
        if (finished) return;
        if (c.gameObject.GetComponentInParent<Boulder>() != null)
        {
            TakeObstacleHit(c.relativeVelocity.magnitude);
            return;
        }
        BuggyController pc = c.gameObject.GetComponentInParent<BuggyController>();
        if (pc != null)
        {
            RivalPlayerBump(pc, c);
        }
    }

    /// <summary>
    /// A player ram: the bodies already bounced apart (the rival is kinematic), so this paints
    /// the moment - BOTH buggies tip onto their two outer wheels away from the side being hit,
    /// and a nose-to-tail bump lifts the LEAD buggy's rear slightly (the follower's nose goes up
    /// to match). The player gets a real angular kick via BuggyController.RivalBump; the rival
    /// (kinematic) settles with a brief visual tilt that decays back to its racing line.
    /// </summary>
    private void RivalPlayerBump(BuggyController pc, Collision c)
    {
        if (c.relativeVelocity.magnitude < bumpMinSpeed) return; // gentle touches just kiss
        if (Time.time - lastBumpTime < bumpCooldown) return;     // no waggle on sustained contact
        lastBumpTime = Time.time;

        // The player is shoved AWAY from this rival: out from our centre toward theirs.
        Vector3 shove = pc.transform.position - transform.position;
        shove.y = 0f;
        if (shove.sqrMagnitude < 0.0001f) shove = -transform.forward;
        shove.Normalize();

        pc.RivalBump(shove);

        float lateral = Vector3.Dot(shove, transform.right);
        float along = Vector3.Dot(shove, transform.forward);
        bumpUntil = Time.time + bumpTiltDuration;
        if (Mathf.Abs(lateral) >= Mathf.Abs(along))
        {
            // Broadside: the side FACING the player lifts - this rival rides its far wheels.
            bumpRoll = lateral * bumpRollDeg;
            bumpPitch = 0f;
        }
        else
        {
            // Nose-to-tail: shoved FORWARD (the 'buggy ahead') lifts its rear; shoved backward
            // (the tail-collar) pops its nose. Mirror of the player's RivalBump.
            bumpPitch = along > 0f ? bumpPitchDeg : -bumpPitchDeg;
            bumpRoll = 0f;
        }
    }

    /// <summary>Bounces every rival near a point (meteor blasts) - knock falls off with distance.</summary>
    public static void KickNear(Vector3 center, float radius, float strength)
    {
        RivalRacer[] rivals = All();
        for (int i = 0; i < rivals.Length; i++)
        {
            RivalRacer r = rivals[i];
            if (r.finished) continue;
            float d = Vector3.Distance(r.transform.position, center);
            if (d < radius)
            {
                r.TakeMeteorKick(center, strength * (1f - d / radius));
            }
        }
    }
}