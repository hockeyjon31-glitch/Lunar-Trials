using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedurally generates a lunar-style terrain mesh beneath the buggy: a large,
/// gently rolling grey plane dotted with circular craters and scattered rocks.
/// The terrain is generated once at runtime from height data so no art assets are needed.
/// </summary>
public class MoonTerrainGen : MonoBehaviour
{
    [Header("Terrain")]
    [Tooltip("Half-extent of the generated terrain along X and Z (total size is 2x this).")]
    public float extent = 1440f;
    [Tooltip("Number of grid quads per side. Higher = smoother but slower to generate.")]
    public int segments = 480;
    [Tooltip("Base height of the flat surface.")]
    public float baseHeight = 0f;

    [Header("Rolling hills")]
    [Tooltip("Amplitude of the low-frequency rolling waves.")]
    public float hillAmplitude = 6f;
    [Tooltip("Wavelength of the rolling waves (metres).")]
    public float hillScale = 120f;
    [Tooltip("Random small jitter added to heights for a textured feel.")]
    public float noiseAmplitude = 0.6f;
    [Tooltip("Noise frequency for the small jitter.")]
    public float noiseScale = 0.015f;

    [Header("Micro relief (bumpy ride)")]
    [Tooltip("Amplitude of the frequent small bumps that make driving over the surface rough.")]
    public float bumpAmplitude = 1.5f;
    [Tooltip("Noise frequency for the small bumps; higher = more frequent bumps.")]
    public float bumpScale = 0.045f;

    // Harvest-grade terrain ramp (HARVEST only): each cleared run roughens the surface so a
    // re-run fights a lumpier course. Weights are per world (index = LevelManager planet order
    // Moon, Mars, Venus, Io, Titan) - Moon and Mars climb the most, Venus a bit less, and Io /
    // Titan barely move because they are already the roughest worlds in the campaign.
    private static readonly float[] HarvestRoughPerGradeWorld = { 0.10f, 0.10f, 0.07f, 0.03f, 0.03f };

    /// <summary>Per-world CRATER-count ramp per Harvest grade above 1, in Planets order
    /// (Moon, Mars, Venus, Io, Titan). Craters are stamped once at build, so unlike the rolling
    /// hills they cannot be re-rolled cheaply - the grade simply stamps MORE of them. The
    /// already-cratered worlds ramp least so a top-grade field does not become a filled pit:
    /// grade 5 tops out at 1.4x (Moon/Mars), 1.3x (Venus), 1.2x (Io) and 1.1x (Titan) of base.</summary>
    private static readonly float[] HarvestCraterPerGradeWorld = { 0.10f, 0.10f, 0.075f, 0.05f, 0.025f };

    [Header("Regolith ripples")]
    [Tooltip("Amplitude of the mid-scale ground mottle that breaks the rolling hills into lumpier terrain.")]
    public float mottleAmplitude = 0.7f;
    [Tooltip("Wavelength of the mid-scale mottle (metres).")]
    public float mottleWavelength = 46f;
    [Tooltip("Amplitude of the fine anisotropic regolith ripples (visible streak texture).")]
    public float rippleAmplitude = 0.45f;
    [Tooltip("Wavelength of the fine regolith ripples (metres).")]
    public float rippleWavelength = 16f;

    [Header("Craters")]
    [Tooltip("Number of conical craters to stamp into the surface.")]
    public int craterCount = 220;
    [Tooltip("Minimum crater radius (metres).")]
    public float craterMinRadius = 4f;
    [Tooltip("Maximum crater radius (metres).")]
    public float craterMaxRadius = 26f;
    [Tooltip("How deep craters can get relative to their radius (0..1).")]
    public float craterDepthFactor = 0.3f;
    [Tooltip("Height of the raised rim relative to the crater depth.")]
    public float craterRimFactor = 0.35f;

    [Header("Rocks")]
    [Tooltip("Number of scattered rock boulders.")]
    public int rockCount = 260;

    [Header("Race canyon (Lunar Race mode)")]
    [Tooltip("True while the terrain is carved into a downhill canyon used by Lunar Race "
            + "(set from GameFlow.Mode in Build, never configured by hand).")]
    public bool raceEnabled = false;
    [Tooltip("Length of the carved downhill course, from the start line to the finish line (metres).")]
    public float raceLength = 1144f;
    [Tooltip("Depth of the canyon floor at the start line (metres).")]
    public float raceDepthStart = 4f;
    [Tooltip("Whole-course drop of the canyon floor from start to finish (metres).")]
    public float raceDrop = 20f;
    [Tooltip("How far the canyon cliffs tower above the course, so the route reads as a walled "
            + "gorge start to finish (metres). The walls rise past the surrounding rolling ground "
            + "level, so a deep-descending floor never swallows them.")]
    public float raceWallHeight = 42f;
    [Tooltip("How far past the finish line the canyon keeps its walls, so the lander touchdown "
            + "and boarding run stay inside the gully (metres).")]
    public float raceBeyondFinish = 200f;
    [Tooltip("Half-width of the flat canyon-floor drive line (metres). Narrow and gate-matched: "
            + "the wall ramps rise just outside the gate arches' pylons.")]
    public float raceCourseHalfWidth = 14f;
    [Tooltip("Lateral weave amplitude of the canyon centre line (metres).")]
    public float raceWeaveAmplitude = 36f;
    [Tooltip("Wavelength of the canyon centre line weave (metres).")]
    public float raceWeaveWavelength = 245f;
    [Tooltip("Half-width of the flat ridge crest (metres). Narrower than the canyon floor.")]
    public float raceRidgeCrestHalfWidth = 7f;
    [Tooltip("How high the ridge crest sits above the base terrain at the start (metres). The "
            + "whole course declines from here to raceRidgeEndHeadroom, so it stays an elevated "
            + "ridge start to finish instead of flattening out.")]
    public float raceRidgeCrestHeight = 70f;
    [Tooltip("How far the crest stays above the surrounding plain at the finish line (metres). "
            + "The ridge never sinks below the terrain - the decline is a long gentle slide high "
            + "above the ground, not a trench.")]
    public float raceRidgeEndHeadroom = 10f;
    [Tooltip("Minimum dropoff carved below the crest edge, so the Ridge keeps steep walls to the "
            + "end even once the descending crest passes level with the surrounding plain (metres).")]
    public float raceRidgeMinDrop = 10f;

    [Header("Ridge jump crevices (survival legs)")]
    [Tooltip("Crevices cut clean across the crest on the first leg of a Ridge survival run. "
            + "They take the whole ridge width, so there is no line round them: hop the gap or "
            + "drop into it.")]
    public int ridgeCreviceBaseCount = 2;
    [Tooltip("Extra crevices per completed leg, so a run gets harder the further it gets.")]
    public int ridgeCrevicePerLeg = 1;
    [Tooltip("Ceiling on the crevices in one course, however many legs have been banked.")]
    public int ridgeCreviceMaxCount = 6;
    [Tooltip("Depth of a crevice on the first leg (metres). Deep enough that falling in is not "
            + "a bump you can drive out of.")]
    public float ridgeCreviceDepthBase = 13f;
    [Tooltip("Extra depth per completed leg (metres).")]
    public float ridgeCreviceDepthPerLeg = 3.5f;
    [Tooltip("Legs after which the depth stops growing, so a very long run cannot carve the "
            + "crest into something unrecognisable.")]
    public int ridgeCreviceDepthLegCap = 4;
    [Tooltip("Smallest gap between two crevices down the course (metres). Also keeps the buggy's "
            + "stuck-rescue (which lifts it 20m back up the centre line) clear of the next one.")]
    public float ridgeCreviceSpacing = 140f;
    [Tooltip("How far inside the start and finish lines the first/last crevice may sit (metres).")]
    public float ridgeCreviceEdgeMargin = 90f;
    [Tooltip("How far before each crevice its GAP AHEAD signpost stands (metres). Roughly five "
            + "seconds at ridge pace - enough to line the jump up, not so much that it stops "
            + "being a warning.")]
    public float ridgeSignLeadDistance = 50f;

    [Header("Grade-scaled canyon (higher Lunar Race grades)")]
    [Tooltip("Fraction of the canyon half-width shaved off per race grade (index 0..4). Harder "
            + "grades carve a noticeably NARROWER lane under the same walls.")]
    public float raceNarrowPerGrade = 0.045f;
    [Tooltip("Fraction the canyon centre-line weave amplitude GROWS per grade - harder grades "
            + "snake the route across the terrain more (twistier).")]
    public float raceWeaveAmplitudePerGrade = 0.07f;
    [Tooltip("Fraction the weave wavelength SHRINKS per grade - harder grades cut tighter, more "
            + "frequent corners instead of long lazy sweeps.")]
    public float raceWeaveWavelengthPerGrade = 0.05f;

    // ---- Race course geometry, shared with the gate/finish builders ----
    [Tooltip("World Z where the canyon course begins (the start line).")]
    public const float RaceStartZ = -60f;

    [Tooltip("Absolute level of the canyon floor AT the start line: the base terrain sampled on "
            + "the centre line there, so the course launches exactly at ground level and only ever "
            + "descends from it (a flat pad at spawn, then a smooth ramp down the whole drop).")]
    public float raceStartFloor = 0f;

    // ---- Launch pad (guaranteed flat spawn) ----
    // Configured in Build(): races pin a flat disc over the floor/crest at the line (the coarse
    // terrain cells there can tilt across the floor/wall or crest/dropoff seam), while Harvest
    // and Survival flatten the map-origin spawn so the buggy never starts on a hill or crater rim.
    [Tooltip("World XZ the flat launch pad centres on (the buggy's spawn point).")]
    public Vector2 launchPadCenter = Vector2.zero;
    [Tooltip("Radius of the launch pad's dead-flat disc (metres); 0 disables the pad.")]
    public float launchPadRadius = 0f;
    [Tooltip("Smooth fall-off ring beyond the flat disc, easing the pad back into the surrounding "
            + "terrain so it reads as a natural clearing instead of a step (metres).")]
    public float launchPadBlend = 14f;
    [Tooltip("Absolute elevation the launch pad is flattened to (races: the floor/crest at the "
            + "start line; Harvest/Survival: the base surface at the map origin).")]
    public float launchPadHeight = 0f;
    public bool launchPadEnabled = false;

    // ---- Trials course start pad ----
    // LUNAR TRIALS: the landing pad moves to the course FINISH (a flat finish area to drive
    // into), so the START gets its own small flat disc - the buggy still launches flat.
    private Vector2 startPadCenter = Vector2.zero;
    private float startPadRadius = 0f;
    private float startPadHeight = 0f;
    private bool startPadEnabled = false;

    /// <summary>World Z where the finish line sits. The carved walls carry on past it for
    /// <see cref="raceBeyondFinish"/> metres so the lander touchdown stays inside the gully.</summary>
    public float RaceFinishZ => RaceStartZ + raceLength;

    /// <summary>Lateral X of the canyon centre line at world Z (the racing line the gates and rival follow).</summary>
    public float RaceCenterX(float wz) =>
        raceWeaveAmplitude * Mathf.Sin(wz / raceWeaveWavelength * Mathf.PI * 2f);

    /// <summary>True when the point sits on the race course's flat drive lane (canyon floor or
    /// ridge crest).</summary>
    public bool OnRaceFloor(float wx, float wz)
    {
        if (!raceEnabled) return false;
        float half = GameFlow.CurrentCourse == RaceCourse.Ridge
            ? raceRidgeCrestHalfWidth : raceCourseHalfWidth;
        return Mathf.Abs(wx - RaceCenterX(wz)) <= half;
    }

    [Header("Look")]
    [Tooltip("Base albedo colour of the regolith; the procedural rock texture is built around it.")]
    public Color regolithColor = new Color(0.60f, 0.60f, 0.63f);
    [Tooltip("Albedo texture tile size (metres): the grain pattern repeats every this distance on the ground.")]
    public float textureTileMetres = 50f;

    // Stored crater data so SampleHeight can match the generated surface.
    private struct Crater
    {
        public Vector2 center;
        public float radius;
        public float depth;
        public float rimHeight;

        public Crater(Vector2 c, float r, float d, float rim) { center = c; radius = r; depth = d; rimHeight = rim; }
    }

    private List<Crater> craters = new List<Crater>();
    private int seed = 12345;
    private bool built;
    // Key for the shared regolith rock texture (the planet name, so it stays in step with
    // the route rocks' texture from GameBootstrap).
    private string planetKey = "terrain";

    // Runtime terrain deformation for the Ridge course's collapsing sections. Each patch sinks
    // a boxed region by `depth` (eased toward its edges). Registered by a hazard director, then
    // the mesh + collider are rebuilt so the driving surface really falls away. `progress`
    // (0..1) lets a patch sink gradually: SinkOffset scales the depth by it, and progressive
    // collapse animates it to its full value without regenerating the whole terrain.
    private struct SinkPatch
    {
        public Vector2 center;
        public float halfEx;
        public float halfN;
        public float depth;
        public float progress;

        public SinkPatch(Vector2 c, float hx, float hz, float d)
        {
            center = c; halfEx = hx; halfN = hz; depth = d; progress = 1f;
        }

        public SinkPatch(Vector2 c, float hx, float hz, float d, float p)
        {
            center = c; halfEx = hx; halfN = hz; depth = d; progress = p;
        }
    }

    private readonly List<SinkPatch> sinkPatches = new List<SinkPatch>();

    /// <summary>
    /// A break cut clean across the ridge crest that the buggy has to hop. Unlike a collapse
    /// these are permanent and part of the height function, so the mesh, its collider and
    /// SampleHeight all agree on where the hole is without any rebuild.
    ///
    /// The GAP is always exactly one terrain cell wide, because that is the narrowest break a
    /// 6m grid can actually express: the cell under the centre row drops to full depth while the
    /// rows either side of it stay on the crest, which reads as a slot with near-vertical walls.
    /// Widening it would mean dropping whole extra cell rows, and the crest is only 14m across,
    /// so depth and count are what scale with difficulty instead of width.
    /// </summary>
    private struct RidgeCrevice
    {
        public float z;        // course Z of the gap's centre, snapped to a cell row
        public float halfWidth; // half the gap, along the course
        public float depth;

        public RidgeCrevice(float z, float halfWidth, float depth)
        {
            this.z = z; this.halfWidth = halfWidth; this.depth = depth;
        }
    }

    private readonly List<RidgeCrevice> ridgeCrevices = new List<RidgeCrevice>();

    /// <summary>Side of one terrain cell (metres). The ridge crevices are sized from this, so
    /// changing the mesh resolution moves them with it rather than stranding them.</summary>
    private float TerrainCell => (extent * 2f) / Mathf.Max(1, segments);

    /// <summary>Number of craters stamped into the terrain.</summary>
    public int CraterCount => craters.Count;

    /// <summary>World XZ centre of generated crater <paramref name="i"/> (y = 0; use
    /// SampleHeight for the surface). LUNAR TRIALS ore siting reads these to hide clusters
    /// inside crater bowls.</summary>
    public Vector3 CraterCentre(int i) => new Vector3(craters[i].center.x, 0f, craters[i].center.y);

    /// <summary>World-space centre (XZ) of crater <paramref name="i"/>.</summary>
    public Vector2 CraterCenter(int i) => craters[i].center;

    /// <summary>Radius of crater <paramref name="i"/>.</summary>
    public float CraterRadius(int i) => craters[i].radius;

    /// <summary>
    /// Generates the whole terrain (surface, material, collider and scattered rocks)
    /// using the given planet's tuning. Call this once, after any terrain fields have
    /// been configured; GameBootstrap calls it with the current LevelDefinition.
    /// </summary>
    public void Build(LevelDefinition def)
    {
        if (built) return;
        built = true;

        if (def != null)
        {
            hillAmplitude = def.hillAmplitude;
            hillScale = def.hillScale;
            bumpAmplitude = def.bumpAmplitude;
            bumpScale = def.bumpScale;
            craterCount = def.craterCount;
            craterMinRadius = def.craterMinRadius;
            craterMaxRadius = def.craterMaxRadius;
            craterDepthFactor = def.craterDepthFactor;
            craterRimFactor = def.craterRimFactor;
            rockCount = def.rockCount;
            regolithColor = def.regolithColor;
            planetKey = def.planetName;

            // Lunar Race, PROSPECTOR RUN and Ridge Survival carve a downhill canyon / crest
            // straight through this world's terrain. Prospector launches with CurrentCourse
            // pinned to Canyon, so it carves the canyon lane like a race does.
            raceEnabled = GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival
                || GameFlow.Mode == GameMode.Prospector;
            // A fair race course must not be beaten up by the ground itself, so basic terrain
            // bumpiness is capped at the Mars grade (the hardest surface a fair race should
            // still fight through). Venus/Io/Titan's rougher hill and bump amplitudes, denser
            // craters, deeper bowls and rock scatter all drop to Mars' levels. The Moon and Mars
            // already sit at or under that average, so Mathf.Min leaves their exact terrain
            // untouched. This is a temporary ceiling: races are meant to gain their own
            // difficulty from future elements (hazards, rival pace), not rougher ground.
            if (raceEnabled)
            {
                hillAmplitude = Mathf.Min(hillAmplitude, 8f);
                bumpAmplitude = Mathf.Min(bumpAmplitude, 2.0f);
                bumpScale = Mathf.Min(bumpScale, 0.05f);
                craterCount = Mathf.Min(craterCount, 260);
                craterMinRadius = Mathf.Min(craterMinRadius, 5f);
                craterMaxRadius = Mathf.Min(craterMaxRadius, 30f);
                craterDepthFactor = Mathf.Min(craterDepthFactor, 0.35f);
                craterRimFactor = Mathf.Min(craterRimFactor, 0.4f);
                rockCount = Mathf.Min(rockCount, 300);

                // Io and Titan are the roughest worlds in the campaign (the biggest rolling
                // hills and the densest micro-relief), so even the generic race clamp above
                // leaves their courses bumpy. Soften their surfaces far harder - the drive
                // lane and ridge crest on those worlds read smooth plateaus instead of a
                // roller ride, while the open survey fields keep their volcanic feel.
                if (def.planetName == "IO" || def.planetName == "TITAN")
                {
                    hillAmplitude = Mathf.Min(hillAmplitude, 6f);
                    bumpAmplitude = Mathf.Min(bumpAmplitude, 1.2f);
                    bumpScale = Mathf.Min(bumpScale, 0.035f);
                    mottleAmplitude = Mathf.Min(mottleAmplitude, 0.3f);
                    rippleAmplitude = Mathf.Min(rippleAmplitude, 0.18f);
                    noiseAmplitude = Mathf.Min(noiseAmplitude, 0.25f);
                }

                // PROSPECTOR RUN gives the two roughest worlds some of their character back -
                // but only MILDLY. The dark descent is already tight and rockfall-pressed, so a
                // FULL restore of Io/Titan's volcanic surface (on top of the narrow lane and the
                // wall boulders) left no clean line through. Io and Titan therefore regain about
                // 70% of their roller hills and a gentler slice of the dense micro-relief, and
                // their lane is widened a touch below. Mars and Venus stay fully smooth.
                if (GameFlow.Mode == GameMode.Prospector
                    && (def.planetName == "IO" || def.planetName == "TITAN"))
                {
                    hillAmplitude = def.hillAmplitude * 0.7f;
                    bumpAmplitude = def.bumpAmplitude * 0.7f;
                    bumpScale = def.bumpScale;
                    mottleAmplitude = Mathf.Max(mottleAmplitude, 0.5f);
                    rippleAmplitude = Mathf.Max(rippleAmplitude, 0.32f);
                    noiseAmplitude = Mathf.Max(noiseAmplitude, 0.4f);
                }

                // RIDGE SURVIVAL trades the race's flat fair ceiling for a richer flanking
                // landscape. The crest drive line is CARVED to its own crestY and never samples
                // the rolling fields below, so the surrounding highlands can keep the world's
                // full hills, craters, radii and rock scatter - the rovers never drive them, but
                // crest drivers now look out over a busy range instead of the clamped race plain.
                // Only the bumpy micro-relief stays softened so the dropoff shoulders read smooth
                // when the wheels hug the crest edge.
                if (GameFlow.IsRidgeSurvival)
                {
                    hillAmplitude = Mathf.Max(hillAmplitude, def.hillAmplitude);
                    bumpAmplitude = Mathf.Max(bumpAmplitude, def.bumpAmplitude * 0.7f);
                    bumpScale = Mathf.Max(bumpScale, def.bumpScale * 0.5f);
                    craterCount = Mathf.Max(craterCount, def.craterCount);
                    craterMinRadius = Mathf.Max(craterMinRadius, def.craterMinRadius);
                    craterMaxRadius = Mathf.Max(craterMaxRadius, def.craterMaxRadius);
                    craterDepthFactor = Mathf.Max(craterDepthFactor, def.craterDepthFactor);
                    craterRimFactor = Mathf.Max(craterRimFactor, def.craterRimFactor);
                    rockCount = Mathf.Max(rockCount, def.rockCount);
                }
            }
            // HARVEST grades roughen the ground (races never do - their courses stay clamped
            // above): the rolling hills and frequent bumps grow with every cleared grade on a
            // per-world strength, so re-runs drive a lumpier field. The richer ore and denser
            // storm the grade also brings are what make the tougher course pay.
            if (!raceEnabled && LevelManager.CurrentLevelIndex >= 0
                && LevelManager.CurrentLevelIndex < HarvestRoughPerGradeWorld.Length)
            {
                int grade = LevelManager.GetHarvestGrade(LevelManager.CurrentLevelIndex);
                float rough = 1f + HarvestRoughPerGradeWorld[LevelManager.CurrentLevelIndex] * (grade - 1);
                hillAmplitude *= rough;
                bumpAmplitude *= rough;
                // More craters per grade too (the world's own ramp), stamped on top of the
                // rougher hills so a re-run reads as a busier, more broken field.
                float craterRough = 1f + HarvestCraterPerGradeWorld[LevelManager.CurrentLevelIndex] * (grade - 1);
                craterCount = Mathf.RoundToInt(craterCount * craterRough);
            }
            raceDrop = def.raceDrop;
            raceWallHeight = def.raceWallHeight;
            raceCourseHalfWidth = def.raceCourseHalfWidth;
            // Prospector's rockfall-pressed descent gets a slightly WIDER lane on the two
            // roughest worlds (Io 13 -> 15, Titan 12 -> 14), so a volley of wall boulders +
            // meteors always leaves a drivable line instead of pinching the buggy into the wall.
            if (GameFlow.Mode == GameMode.Prospector
                && (def.planetName == "IO" || def.planetName == "TITAN"))
            {
                raceCourseHalfWidth += 2f;
            }
            raceWeaveAmplitude = def.raceWeaveAmplitude;
            raceWeaveWavelength = def.raceWeaveWavelength;
            // Every race grade keeps the SAME wide, smooth canyon as grade 1: tighter lanes and
            // twistier centre lines made the later grades read messy and lucky more than skilful.
            // Difficulty ramps through the FIELD instead - RivalRacer paces each grade faster and
            // the RaceHazardDirector drops more, bigger events (the only scale lever left). The
            // grade constants (raceNarrowPerGrade etc.) are retained for tuning, deliberately
            // unused. The Ridge survival never scales either (its crest is a fixed hand-tuned path).
            // Anchor the canyon floor to the terrain exactly at the start line so the course
            // launches flat and only ever drops from there (never an upslope at the start).
            raceStartFloor = BaseHeight(RaceCenterX(RaceStartZ), RaceStartZ);
        }

        // Flat launch pad under the buggy's spawn so it always launches on level ground:
        // coarse terrain cells near a race mouth can tilt across the floor/wall (or
        // crest/dropoff) seam - leaving the rover parked on a subtle upslope - and Harvest /
        // Survival spawn at the raw map origin, which land them on whatever hill slope or
        // crater rim the noise rolled there. Flatten a disc to the local elevation.
        if (raceEnabled)
        {
            launchPadCenter = new Vector2(RaceCenterX(RaceStartZ), RaceStartZ);
            launchPadRadius = GameFlow.CurrentCourse == RaceCourse.Ridge
                ? raceRidgeCrestHalfWidth : raceCourseHalfWidth;
            launchPadHeight = GameFlow.CurrentCourse == RaceCourse.Ridge
                ? raceStartFloor + raceRidgeCrestHeight : raceStartFloor;
        }
        else if (TrialsCourse.Active != null)
        {
            // LUNAR TRIALS course: the landing pad IS the finish area - a flat disc at the
            // run's far end for the buggy to drive into (the old mid-run pad placement is
            // gone; the pad stands here from the start).
            launchPadCenter = TrialsCourse.Active.FinishXZ;
            launchPadRadius = 16f;
            launchPadHeight = BaseHeight(launchPadCenter.x, launchPadCenter.y);
        }
        else
        {
            launchPadCenter = Vector2.zero;
            launchPadRadius = 18f;
            launchPadHeight = BaseHeight(0f, 0f);
        }
        launchPadEnabled = launchPadRadius > 0f;

        // LUNAR TRIALS course: a matching small flat disc at the START, so the buggy still
        // launches from level ground now that the launch pad lives at the finish.
        if (TrialsCourse.Active != null)
        {
            startPadEnabled = true;
            startPadCenter = TrialsCourse.Active.StartXZ;
            startPadRadius = 12f;
            startPadHeight = BaseHeight(startPadCenter.x, startPadCenter.y);
        }

        // Ridge survival legs get their hop gaps cut into the crest before the surface is built.
        // Gated on IsRidgeSurvival (MODE and course together) rather than the course alone: the
        // gaps are cut into the ridge crest, and the crest mesh only exists when raceEnabled is
        // true - so on a stale-Ridge harvest map this used to spawn the crevices and their GAP
        // AHEAD towers with no crest to cut them into, leaving warning signs on open ground.
        if (GameFlow.IsRidgeSurvival) BuildRidgeCrevices();

        Generate();
        SpawnRocks();
    }

    private void Generate()
    {
        int n = segments + 1;
        float cell = (extent * 2f) / segments;

        // Build heightmap first.
        float[,] heights = new float[n, n];
        // Seed is sticky: runtime rebuilds (sink patches) must not re-roll the surface noise.
        if (seed == 12345) seed = Random.Range(0, 100000);
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                float wx = -extent + x * cell;
                float wz = -extent + z * cell;
                heights[z, x] = ComputeHeight(wx, wz);
            }
        }

        // Trials course dive: grade a guaranteed downhill run-in into the corridor before
        // the jump ramp (corridor-internal, lateral falloff at the edges).
        if (TrialsCourse.Active != null && TrialsCourse.Active.HasDive)
        {
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    Vector2 dpos = new Vector2(-extent + x * cell, -extent + z * cell);
                    float drop = TrialsCourse.Active.DiveDropAt(dpos);
                    if (drop != 0f) heights[z, x] += drop;
                }
            }
        }

        // Stamp craters (a wide, shallow bowl with a raised rim, like a real lunar crater).
        for (int c = 0; c < craterCount; c++)
        {
            float crx = Random.Range(-extent, extent);
            float crz = Random.Range(-extent, extent);
            float crR = Random.Range(craterMinRadius, craterMaxRadius);
            float depth = crR * craterDepthFactor;
            float rimH = depth * craterRimFactor;

            Crater crater = new Crater(new Vector2(crx, crz), crR, depth, rimH);
            craters.Add(crater);

            int x0 = Mathf.Max(0, Mathf.FloorToInt((crx - crR + extent) / cell));
            int x1 = Mathf.Min(n - 1, Mathf.CeilToInt((crx + crR + extent) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((crz - crR + extent) / cell));
            int z1 = Mathf.Min(n - 1, Mathf.CeilToInt((crz + crR + extent) / cell));

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float wx = -extent + x * cell;
                    float wz = -extent + z * cell;
                    // Lunar Race: craters never stamp the course's drive lane (canyon floor or
                    // ridge crest), so the racing surface stays clean and predictable.
                    float laneHalf = GameFlow.CurrentCourse == RaceCourse.Ridge
                        ? raceRidgeCrestHalfWidth : raceCourseHalfWidth;
                    if (raceEnabled && Mathf.Abs(wx - RaceCenterX(wz)) <= laneHalf) continue;
                    // LUNAR TRIALS course: the tyre-lined loop stays crater-free the same way,
                    // so the drive corridor is always clean and the line always drivable.
                    Vector2 vpos = new Vector2(wx, wz);
                    if (TrialsCourse.Active != null && TrialsCourse.Active.InsideCorridor(vpos, 0f)) continue;
                    Vector2 pos = new Vector2(wx, wz);
                    float d = Vector2.Distance(pos, crater.center);
                    if (d > crater.radius) continue;
                    heights[z, x] += CraterOffset(pos, crater);
                }
            }
        }

        // Launch pad: pin every vertex inside the pad to the pad elevation, applied LAST so it
        // overrides any crater/seam influence around the spawn. SampleHeight() applies the same
        // blend, so mesh and queries stay in lockstep and the buggy always spawns on flat ground.
        if (launchPadEnabled)
        {
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    float wx = -extent + x * cell;
                    float wz = -extent + z * cell;
                    float w = LaunchPadBlend(wx, wz);
                    if (w > 0f) heights[z, x] = Mathf.Lerp(heights[z, x], launchPadHeight, w);
                }
            }
        }

        // Trials start pad: pin the launch disc flat (mirrors the launch pad pinning above;
        // SampleHeight applies the same blend, so mesh and queries stay in lockstep).
        if (startPadEnabled)
        {
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    float wx = -extent + x * cell;
                    float wz = -extent + z * cell;
                    float w = StartPadBlend(wx, wz);
                    if (w > 0f) heights[z, x] = Mathf.Lerp(heights[z, x], startPadHeight, w);
                }
            }
        }

        // Build mesh.
        Vector3[] verts = new Vector3[n * n];
        Vector2[] uv = new Vector2[n * n];
        int[] tris = new int[segments * segments * 6];
        int vi = 0;
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                verts[vi] = new Vector3(-extent + x * cell, heights[z, x], -extent + z * cell);
                uv[vi] = new Vector2((float)x / segments, (float)z / segments);
                vi++;
            }
        }

        int ti = 0;
        for (int z = 0; z < segments; z++)
        {
            for (int x = 0; x < segments; x++)
            {
                int i0 = z * n + x;
                int i1 = z * n + x + 1;
                int i2 = (z + 1) * n + x;
                int i3 = (z + 1) * n + x + 1;

                tris[ti++] = i0; tris[ti++] = i2; tris[ti++] = i1;
                tris[ti++] = i1; tris[ti++] = i2; tris[ti++] = i3;
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "MoonTerrain";
        // This terrain exceeds 65,534 vertices, so 16-bit indices would silently strip the
        // rest of the surface from the cooked MeshCollider (the buggy would fall through).
        // Force 32-bit indices before any triangles are assigned.
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uv);
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Surface collider.
        MeshCollider collider = gameObject.GetComponent<MeshCollider>();
        if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;

        // Renderer.
        MeshFilter filter = gameObject.GetComponent<MeshFilter>();
        if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = CreateMoonMaterial();
    }

    private Material CreateMoonMaterial()
    {
        // Worlds with a concrete combo (Moon/Mars/Io/Venus/Titan) dress the ground in the
        // blended swatches; every other world keeps the grey procedural regolith below.
        float surfaceTiles = (extent * 2f) / textureTileMetres;
        Material yfcm = Yfcm.World(planetKey, Yfcm.WorldTint(planetKey, regolithColor, 1f),
            new Vector2(surfaceTiles, surfaceTiles), 0f, false);
        if (yfcm != null) return yfcm;

        // Realistic grey lunar regolith. Albedo comes entirely from the mottled texture
        // (mat.color is white) so the surface reads as grey rock, not washed-out white.
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = Color.white;
        mat.SetFloat("_Glossiness", 0f);
        mat.mainTexture = CreateRockTexture();
        // Repeat the grain over the ground at a real-world scale instead of smearing it once
        // across the whole map, so the surface shows detail at slow driving speeds.
        float tiles = (extent * 2f) / textureTileMetres;
        mat.mainTextureScale = new Vector2(tiles, tiles);
        return mat;
    }

    private Texture2D CreateRockTexture()
    {
        // Multi-band regolith albedo: broad soft tonal patches, mid mottling, fine mineral
        // grain, faint directional streaks and bright/dark flecks (anorthosite/spatter), so
        // the exact same shade never tiles visibly when repeated across the ground.
        int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
        Color baseCol = regolithColor;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Two low bands: big soft regions + medium patches.
                float patch = Mathf.PerlinNoise(x * 0.021f, y * 0.021f);
                float mid = Mathf.PerlinNoise(x * 0.07f, y * 0.07f);
                // Fine grain on two shear-offset lattices so it reads as granular, not gridlike.
                float grainA = Mathf.PerlinNoise(x * 0.16f, y * 0.16f);
                float grainB = Mathf.PerlinNoise(x * 0.30f, y * 0.13f + x * 0.02f);
                // Faint directional smear (eolian dust streaks).
                float streak = 0.5f + 0.5f * Mathf.Sin(x * 0.9f + y * 0.15f);

                float f = 0.70f
                    + (patch - 0.5f) * 0.22f
                    + (mid - 0.5f) * 0.14f
                    + (grainA - 0.5f) * 0.10f
                    + (grainB - 0.5f) * 0.08f
                    + (streak - 0.5f) * 0.04f;

                // Scattered shadowed grains and bright mineral flecks.
                float r = Random.value;
                if (r < 0.006f) f *= 0.70f;
                else if (r > 0.994f) f *= 1.20f;

                tex.SetPixel(x, y, baseCol * Mathf.Clamp(f, 0.42f, 1.30f));
            }
        }
        tex.Apply(true);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.anisoLevel = 9;
        return tex;
    }

    private void SpawnRocks()
    {
        Transform holder = new GameObject("Rocks").transform;
        holder.SetParent(transform, false);
        holder.position = Vector3.zero;

        for (int i = 0; i < rockCount; i++)
        {
            float p = Random.Range(0.3f, 1f);
            float carry = Mathf.Pow(p, 0.6f);
            float scale = 0.15f + carry * 0.9f;

            float rx = Random.Range(-extent, extent);
            float rz = Random.Range(-extent, extent);
            float height = SampleHeight(rx, rz);

            // RockArt builds the real rock mesh (see RockArt.BuildRockShape) so the scatter
            // wears the imported rock prefab meshes as well as the route boulders do.
            GameObject rock = new GameObject("Rock_" + i);
            rock.transform.SetParent(holder, false);
            rock.transform.position = new Vector3(rx, height + scale * 0.2f, rz);
            rock.transform.rotation = Random.rotation;

            RockArt.BuildRockShape(rock.transform, scale, CreateRockShader(scale), i);

            // Scatter rocks are decorative: they sample terrain only, never collide (the route
            // boulders keep their hurt-on-hit colliders instead).
            var cols = rock.GetComponentsInChildren<Collider>();
            for (int c = 0; c < cols.Length; c++)
            {
                if (cols[c] != null) Destroy(cols[c]);
            }
        }
    }

    private Material CreateRockShader(float scale)
    {
        float v = 0.7f + Random.value * 0.3f;
        // World rocks wear the same blended concrete as the ground so they bed into the surface.
        Material yfcm = Yfcm.World(planetKey, Yfcm.WorldTint(planetKey, regolithColor, v),
            new Vector2(2f, 2f), 0f, true);
        if (yfcm != null) return yfcm;

        Material mat = new Material(Shader.Find("Standard"));
        // Rocks share the planet's regolith-mottled texture so they sit naturally on the surface.
        mat.mainTexture = RockArt.TextureFor(regolithColor, planetKey);
        mat.color = regolithColor * v;
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", 0.0f);
        return mat;
    }

    /// <summary>
    /// The height the terrain mesh has at (wx, wz) BEFORE craters are stamped on top.
    /// Used by both mesh generation and SampleHeight so placed objects always match the mesh.
    /// </summary>
    private float ComputeHeight(float wx, float wz)
    {
        float height = BaseHeight(wx, wz);

        if (raceEnabled)
        {
            if (GameFlow.CurrentCourse == RaceCourse.Ridge)
            {
                float ridge = RaceRidgeCrest(wx, wz, height);
                if (ridge >= 0f) height = ridge;
            }
            else
            {
                float floor = RaceCanyonFloor(wx, wz, height);
                if (floor >= 0f) height = floor;
            }
        }

        // Runtime sink patches (ridge-collapse deformation) lower the surface inside a box.
        float sink = SinkOffset(wx, wz);
        if (sink > 0f) height -= sink;

        return height;
    }

    /// <summary>Total depth this point is sunk by active collapse patches (0 outside them).
    /// Each patch's contribution is scaled by its animation progress (0..1).</summary>
    private float SinkOffset(float wx, float wz)
    {
        float total = 0f;
        for (int i = 0; i < sinkPatches.Count; i++)
        {
            SinkPatch p = sinkPatches[i];
            if (p.progress <= 0f) continue;
            if (Mathf.Abs(wx - p.center.x) > p.halfEx) continue;
            if (Mathf.Abs(wz - p.center.y) > p.halfN) continue;
            float ex = 1f - Mathf.Abs(wx - p.center.x) / p.halfEx;
            float ez = 1f - Mathf.Abs(wz - p.center.y) / p.halfN;
            float t = Mathf.Clamp01(ex) * Mathf.Clamp01(ez);
            total += p.depth * p.progress * t * t;
        }
        return total;
    }

    /// <summary>Base surface height at (wx, wz) with no race carving applied.</summary>
    private float BaseHeight(float wx, float wz)
    {
        // Rolling lunar hills (sum of a couple of sine waves).
        float hill =
            Mathf.Sin(wx / hillScale) * 0.6f +
            Mathf.Cos(wz / (hillScale * 0.7f)) * 0.6f +
            Mathf.Sin((wx + wz) / (hillScale * 0.5f)) * 0.4f;
        hill *= hillAmplitude;

        // Frequent small bumps so the ride stays rough across the whole surface.
        float bounce = Mathf.PerlinNoise(
            (wx + seed) * bumpScale,
            (wz + seed) * bumpScale
        ) * 2f - 1f;
        bounce *= bumpAmplitude;

        // Mid-scale mottle: quietly lumps the ground up so slow driving reads more relief.
        float mottle = Mathf.PerlinNoise(
            (wx + seed) / mottleWavelength,
            (wz * 0.8f + seed) / mottleWavelength
        ) * 2f - 1f;
        mottle *= mottleAmplitude;

        // Fine anisotropic regolith ripples (two crossed sine fields, like wind-blown dust
        // streaks in the repo's LunarEnvironment/MartianTerrain fine octaves).
        float rw = Mathf.PI * 2f / rippleWavelength;
        float ripple = (
            Mathf.Sin(wx * rw + wz * rw * 0.7f) * 0.6f +
            Mathf.Sin(wz * rw - wx * rw * 0.55f) * 0.4f
        );
        ripple *= rippleAmplitude;

        // Fine noise dirt for surface texture.
        float noise = Mathf.PerlinNoise(
            (wx + seed) * noiseScale,
            (wz + seed) * noiseScale
        ) * 2f - 1f;
        noise *= noiseAmplitude;

        return baseHeight + hill + bounce + mottle + ripple + noise;
    }

    /// <summary>
    /// The race course's controlling surface at (wx, wz), or -1 outside it. On the flat
    /// floor this is a single absolutely-planned elevation (start-line level, then a smooth
    /// monotone descent), so the driving lane can NEVER climb. The walls are built as
    /// TOWERING CLIFFS: a short embankment ramps off the floor, then a quadratic cliff face
    /// climbs to the per-planet wall height (plus ridge noise) and plateaus - so the gorge
    /// reads as a walled canyon the whole way down, never a shallow gutter that falls back
    /// into the rolling highlands.
    /// </summary>
    private float RaceCanyonFloor(float wx, float wz, float untouched)
    {
        const float startZ = RaceStartZ;
        float endZ = startZ + raceLength + raceBeyondFinish;
        float zIn = wz - startZ;

        // Blend the canyon in ahead of the mouth and out WELL past the finish line, so the
        // line itself (and the lander touchdown beyond it) stays on flat floor surrounded by
        // walls before the gully eases back into the open terrain.
        if (zIn < -140f || zIn > raceLength + raceBeyondFinish + 100f) return -1f;
        float enterBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((zIn + 140f) / 140f));
        float exitBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((endZ + 24f - wz) / 64f));
        float zf = enterBlend * exitBlend;
        if (zf <= 0f) return -1f;

        float cx = RaceCenterX(wz);
        float lat = Mathf.Abs(wx - cx);
        float half = raceCourseHalfWidth;

        // Monotone descent profile: a short flat launch pad right at the line, then a smooth
        // ramp that reaches the full course drop at the finish. Never increases with z.
        float t = Mathf.Clamp01(zIn / raceLength);
        float rampStart = 0.05f;
        float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - rampStart) / (1f - rampStart)));
        float floorY = raceStartFloor - raceDrop * ramp;

        float y;
        float d = lat - half;
        if (d <= 0f)
        {
            // Flat floor: the whole lane is one planned elevation at this z.
            y = floorY;
        }
        else
        {
            // Wall. Embankment run: 14m of smooth ramp that lifts the ground from the floor
            // onto the surrounding highland level (so the cliff stands on solid ground even
            // where the floor has sunk deep). Then a quadratic cliff face (36m) climbs to
            // the wall height and plateaus, textured with organic ridge noise.
            const float wallRamp = 14f;
            const float cliffWidth = 36f;

            float highland = Mathf.Max(floorY, untouched);
            float baseRise = highland - floorY;
            float rampT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / wallRamp));
            float cliffT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - wallRamp) / cliffWidth));
            float rise = raceWallHeight * cliffT;
            float noise = (
                Mathf.Sin(wx * 0.05f + d * 0.15f) * 3f +
                Mathf.Cos(wz * 0.05f) * 2f +
                Mathf.Sin(d * 0.11f - wz * 0.02f) * 1.5f
            ) * (0.35f + 0.5f * cliffT);

            y = floorY + baseRise * rampT + rise + noise * cliffT;
        }

        return Mathf.Lerp(untouched, y, zf);
    }

    /// <summary>
    /// The Ridge course's surface at (wx, wz), or -1 outside it. A narrow raised crest
    /// descends monotonically from start to finish; steep dropoffs fall away on both sides
    /// into the rolling highlands. The centre line weaves laterally just like the canyon.
    /// </summary>
    private float RaceRidgeCrest(float wx, float wz, float untouched)
    {
        const float startZ = RaceStartZ;
        float endZ = startZ + raceLength + raceBeyondFinish;
        float zIn = wz - startZ;

        if (zIn < -140f || zIn > raceLength + raceBeyondFinish + 100f) return -1f;
        float enterBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((zIn + 140f) / 140f));
        float exitBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((endZ + 24f - wz) / 64f));
        float zf = enterBlend * exitBlend;
        if (zf <= 0f) return -1f;

        float cx = RaceCenterX(wz);
        float lat = Mathf.Abs(wx - cx);
        float half = raceRidgeCrestHalfWidth;

        // Long gentle decline from a starting crest high above the mouth. The drive line falls
        // from raceRidgeCrestHeight above the ground down to raceRidgeEndHeadroom at the
        // finish, so it stays an ELEVATED ridge the whole course - the late-course never
        // flattens out into the plain. The ramp eases off the start, holds a steady downhill
        // grade, and keeps descending (no end-plateau) so the finish arrives on the lowered
        // crest with its walls intact.
        float t = Mathf.Clamp01(zIn / raceLength);
        float rampStart = 0.05f;
        float raw = Mathf.Clamp01((t - rampStart) / (1f - rampStart));
        float ramp = Mathf.Pow(raw, 1.1f);
        float crestY = Mathf.Lerp(raceStartFloor + raceRidgeCrestHeight, raceStartFloor + raceRidgeEndHeadroom, ramp);

        // Long-wavelength undulation on the crest drive line: a gentle ~+/-0.3 m roll over 60 m
        // and 97 m so the top reads as a natural rocky ridge instead of a ruler-straight table.
        // The wavelengths are many times the 6 m mesh cell, so the mesh, the collider, the
        // canister/rock placements and the camera (all fed by this same height) stay in
        // lockstep, and the roll is far too shallow to be a driving obstacle. Applied to the
        // whole carved crest profile so the flanks stay continuous with it.
        crestY += Mathf.Sin(wz * (Mathf.PI * 2f / 60f)) * 0.18f
                + Mathf.Sin(wz * (Mathf.PI * 2f / 97f) + 0.7f) * 0.12f;

        float y;
        float d = lat - half;
        if (d <= 0f)
        {
            // Flat crest: the narrow drive line.
            y = crestY;
        }
        else
        {
            // Steep dropoff on both sides. A short embankment smooths the edge, then the
            // terrain falls away. The drop below the crest is at least raceRidgeMinDrop, so
            // the sides stay walled even after the crest sinks past the surrounding ground.
            const float edgeRamp = 9f;
            const float dropWidth = 30f;

            float drop = Mathf.Max(raceRidgeMinDrop, crestY - untouched);
            float edgeT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / edgeRamp));
            float dropT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - edgeRamp) / dropWidth));
            float t2 = edgeT * dropT;
            // Gentle organic jitter on the flanks only - heavily damped so the shoulders stay
            // almost smooth under the wheels when hugging the edge of the crest.
            float noise = (
                Mathf.Sin(wx * 0.05f + d * 0.10f) * 1.6f +
                Mathf.Cos(wz * 0.04f) * 1.2f +
                Mathf.Sin(d * 0.08f - wz * 0.02f) * 0.8f
            ) * (0.12f + 0.22f * dropT);

            y = crestY - drop * t2 + noise * t2;
        }

        // Cut the survival leg's hop gaps out of the finished crest profile. Applied here rather
        // than as a separate mesh pass so the dent is part of the surface itself: the mesh, its
        // collider and SampleHeight all read this one function, so the buggy, the camera, the
        // rocks and the spawn heights can never disagree about where the hole is. Only where the
        // crest is fully blended in - in the entry/exit ramps the gap is not there to cut.
        if (zf >= 1f) y -= RidgeCreviceDrop(wz);

        return Mathf.Lerp(untouched, y, zf);
    }

    /// <summary>
    /// Chooses where the Ridge's hop gaps go for this run. The number and depth come from how
    /// many legs the player has already banked (GameFlow.RidgeSurvivalLoops), so the very first
    /// leg of a fresh run is the gentlest and a long run keeps escalating; the choices are made
    /// ONCE in Build, so the gaps stay put across every later mesh rebuild the collapses trigger.
    ///
    /// The Ridge has no gates or pods (it is survival: no time board), so unlike the hazard
    /// director's placements there are no checkpoints to steer around - only the start line, the
    /// finish line and each other.
    /// </summary>
    private void BuildRidgeCrevices()
    {
        ridgeCrevices.Clear();

        int leg = Mathf.Max(0, GameFlow.RidgeSurvivalLoops);
        int count = Mathf.Min(ridgeCreviceMaxCount, ridgeCreviceBaseCount + ridgeCrevicePerLeg * leg);
        if (count <= 0) return;

        float startZ = RaceStartZ + ridgeCreviceEdgeMargin;
        float endZ = RaceFinishZ - ridgeCreviceEdgeMargin;
        float span = endZ - startZ;
        // Too short a course to space them out: better none than a hop every few seconds.
        if (span < count * ridgeCreviceSpacing) return;

        float cell = TerrainCell;
        float depth = ridgeCreviceDepthBase
            + ridgeCreviceDepthPerLeg * Mathf.Min(leg, ridgeCreviceDepthLegCap);

        float stride = span / (count + 1);
        float z = startZ + Random.Range(stride * 0.35f, stride * 0.65f);
        for (int i = 0; i < count && z < endZ; i++)
        {
            // Snap to a cell row so the gap lands exactly one vertex row deep with crest either
            // side of it. An unsnapped gap can fall between rows and come out as a soft dip, or
            // clip the neighbouring row and read as a step instead of a hole.
            ridgeCrevices.Add(new RidgeCrevice(SnapToCellRow(z, cell), cell * 0.5f, depth));
            z += stride * Random.Range(0.85f, 1.15f);
        }

        BuildCreviceSigns();
    }

    /// <summary>How far a hop gap drops the crest at this course Z (0 outside every gap).</summary>
    public float RidgeCreviceDropAt(float z) => RidgeCreviceDrop(z);

    /// <summary>Height of the crest at this Z with any hop gap filled back in - the surface the
    /// buggy would be riding if the gap were not there.</summary>
    public float RidgeCrestLipHeight(float z)
    {
        float cx = RaceCenterX(z);
        return SampleHeight(new Vector3(cx, 0f, z)) + RidgeCreviceDrop(z);
    }

    /// <summary>True if this world point is down inside one of the hop gaps. Tests against the
    /// FILLED-IN crest, not the gap floor, so a buggy mid-jump over the gap (well above the lip)
    /// is correctly not "in" it.</summary>
    public bool InRidgeCrevice(Vector3 p)
    {
        if (ridgeCrevices.Count == 0) return false;
        if (RidgeCreviceDrop(p.z) < 1f) return false;
        return p.y < RidgeCrestLipHeight(p.z) - 2f;
    }

    /// <summary>A "GAP AHEAD!" signpost on the crest shoulder <see cref="ridgeSignLeadDistance"/>
    /// before each gap, standing on the surface so it sits at the right height and is visible
    /// from the far side of the straight.</summary>
    private void BuildCreviceSigns()
    {
        for (int i = 0; i < ridgeCrevices.Count; i++)
        {
            RidgeCrevice c = ridgeCrevices[i];
            float z = c.z - ridgeSignLeadDistance;
            // The gap cuts the WHOLE crest, so the sign goes on the far shoulder and is simply
            // walked past - there is no ground beside the crest to stand it on. Collider-free,
            // so clipping it costs nothing.
            float x = RaceCenterX(z) + (raceRidgeCrestHalfWidth - 2f);
            float y = SampleHeight(new Vector3(x, 0f, z));
            CourseWarningSign.Build(transform, new Vector3(x, y, z), 0f, "GAP AHEAD!");
        }
    }

    private float SnapToCellRow(float z, float cell)
    {
        return -extent + Mathf.Round((z + extent) / cell) * cell;
    }

    /// <summary>How far a crevice drops the crest at this course Z (0 outside every gap). Flat
    /// bottomed, with the walls easing back up to the lip inside the gap's own width, so the
    /// 6m of gap carries the whole depth change and nothing spills onto the run-up.</summary>
    private float RidgeCreviceDrop(float wz)
    {
        if (ridgeCrevices.Count == 0) return 0f;
        float drop = 0f;
        for (int i = 0; i < ridgeCrevices.Count; i++)
        {
            RidgeCrevice c = ridgeCrevices[i];
            float d = Mathf.Abs(wz - c.z);
            if (d >= c.halfWidth) continue;
            float inner = c.halfWidth * 0.45f;
            float t = d <= inner ? 0f
                : Mathf.SmoothStep(0f, 1f, (d - inner) / Mathf.Max(0.01f, c.halfWidth - inner));
            drop = Mathf.Max(drop, Mathf.Lerp(c.depth, 0f, t));
        }
        return drop;
    }

    /// <summary>Height adjustment of a crater at position pos: the bowl dip plus the raised rim.</summary>
    private float CraterOffset(Vector2 pos, Crater c)
    {
        float d = Vector2.Distance(pos, c.center);
        if (d >= c.radius) return 0f;

        float t = d / c.radius; // 0 centre -> 1 rim
        float bowl = 1f - t * t; // parabolic bowl, deepest at the centre
        float rimProfile = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) * 3f)); // ramp right at the rim
        return -c.depth * bowl + c.rimHeight * rimProfile;
    }

    /// <summary>
    /// Re-generates the surface with a new micro-roughness profile (used by Survival mode
    /// whenever the difficulty steps up). The rolling-hill wavelengths are unchanged so the
    /// foreground keeps its shape; only the bump amplitude/scale and the crater layout are
    /// re-rolled. Decorative rocks are left in place (survival hazards are separate).
    /// Callers should pause the buggy around this call; it rebuilds mesh + collider + normals.
    /// </summary>
    public void RebuildWithNewRoughness(float newBumpAmplitude, float newBumpScale)
    {
        bumpAmplitude = newBumpAmplitude;
        bumpScale = newBumpScale;
        craters.Clear();
        Generate();
    }

    /// <summary>
    /// Sinks a boxed section of the surface by <paramref name="depth"/> metres and rebuilds
    /// mesh + collider (used by the Ridge course's collapsing sections). The depression eases
    /// toward the box edges. Rebuilt geometry and SampleHeight stay in sync because both flow
    /// through ComputeHeight. Safe to call repeatedly at runtime.
    /// </summary>
    public void CollapseSection(Vector2 center, float halfEx, float halfN, float depth)
    {
        sinkPatches.Add(new SinkPatch(center, halfEx, halfN, depth));
        Generate();
    }

    /// <summary>
    /// Staged landfall for the Ridge's collapses. Instead of sinking the whole flank in one
    /// frame (a single full-mesh rebuild is the hitching step on modest CPUs), a small chunk
    /// gives way first, then a few more chunks grow over it, ending with the full collapse.
    /// Each frame only the affected vertices + their normals are retouched in place (no full
    /// terrain regeneration), so the transition stays smooth; the MeshCollider is re-cooked
    /// once when the landfall completes.
    /// </summary>
    public void StartProgressiveCollapse(Vector2 center, float halfEx, float halfN, float depth)
    {
        if (!gameObject.activeInHierarchy) { sinkPatches.Add(new SinkPatch(center, halfEx, halfN, depth)); Generate(); return; }
        StartCoroutine(AnimatedCollapseRoutine(center, halfEx, halfN, depth));
    }

    private IEnumerator AnimatedCollapseRoutine(Vector2 center, float halfEx, float halfN, float depth)
    {
        MeshFilter filter = GetComponent<MeshFilter>();
        MeshCollider collider = GetComponent<MeshCollider>();
        if (filter == null || filter.sharedMesh == null || collider == null)
        {
            sinkPatches.Add(new SinkPatch(center, halfEx, halfN, depth));
            Generate();
            yield break;
        }

        Mesh mesh = filter.sharedMesh;
        // Snapshot the pre-collapse surface so the patch can be added on top (it starts at
        // progress 0, contributing nothing, then ramps its own depth/excent each frame).
        Vector3[] verts = mesh.vertices;                 // working copy we mutate
        float[] baseY = new float[verts.Length];
        Vector3[] baseN = mesh.normals;
        for (int i = 0; i < verts.Length; i++) baseY[i] = verts[i].y;

        SinkPatch patch = new SinkPatch(center, halfEx * 0.08f, halfN * 0.08f, depth, 0f);
        sinkPatches.Add(patch);
        int idx = sinkPatches.Count - 1;

        const float totalTime = 1.35f;          // whole landfall takes ~1.3s
        const float depthDeadline = 0.85f;      // depth reaches full before the box finishes
        float start = Time.time;
        while (Time.time - start < totalTime)
        {
            float u = Mathf.Clamp01((Time.time - start) / totalTime);
            // Depth arrives first so the final phase reads as the "final landfall" widening.
            float dT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / depthDeadline));
            float eT = Mathf.SmoothStep(0f, 1f, u);
            patch.progress = Mathf.Lerp(0f, 1f, dT);
            patch.halfEx = Mathf.Lerp(halfEx * 0.08f, halfEx, eT);
            patch.halfN = Mathf.Lerp(halfN * 0.08f, halfN, eT);
            sinkPatches[idx] = patch;
            RefreshSinkRegion(patch, verts, baseY, baseN, mesh);
            yield return null;
        }

        patch.progress = 1f;
        patch.halfEx = halfEx;
        patch.halfN = halfN;
        sinkPatches[idx] = patch;
        RefreshSinkRegion(patch, verts, baseY, baseN, mesh);
        mesh.RecalculateBounds();
        collider.sharedMesh = mesh;             // one re-cook, once the landfall has landed
    }

    /// <summary>Retouches only the vertices inside <paramref name="patch"/> (plus a one-vertex
    /// ring so normals blend) against the pre-collapse snapshot, then uploads mesh + normals.</summary>
    private void RefreshSinkRegion(SinkPatch patch, Vector3[] verts, float[] baseY, Vector3[] normals, Mesh mesh)
    {
        int n = segments + 1;
        float cell = (extent * 2f) / segments;

        int x0 = Mathf.Max(0, Mathf.FloorToInt((patch.center.x - patch.halfEx + extent) / cell));
        int x1 = Mathf.Min(n - 1, Mathf.CeilToInt((patch.center.x + patch.halfEx + extent) / cell));
        int z0 = Mathf.Max(0, Mathf.FloorToInt((patch.center.y - patch.halfN + extent) / cell));
        int z1 = Mathf.Min(n - 1, Mathf.CeilToInt((patch.center.y + patch.halfN + extent) / cell));

        // Sink the region's heights (from the pre-collapse snapshot, minus this patch only).
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int vi = z * n + x;
                float wx = -extent + x * cell;
                float wz = -extent + z * cell;
                verts[vi].y = baseY[vi] - PatchSink(wx, wz, patch);
            }
        }

        // Recompute normals over the region + a ring so lighting blends with untouched terrain.
        int nx0 = Mathf.Max(0, x0 - 1), nx1 = Mathf.Min(n - 1, x1 + 1);
        int nz0 = Mathf.Max(0, z0 - 1), nz1 = Mathf.Min(n - 1, z1 + 1);
        for (int z = nz0; z <= nz1; z++)
        {
            for (int x = nx0; x <= nx1; x++)
            {
                int vi = z * n + x;
                float hL = verts[Mathf.Max(0, x - 1) + z * n].y;
                float hR = verts[Mathf.Min(n - 1, x + 1) + z * n].y;
                float hD = verts[x + Mathf.Max(0, z - 1) * n].y;
                float hU = verts[x + Mathf.Min(n - 1, z + 1) * n].y;
                Vector3 nm = new Vector3(hL - hR, 2f * cell, hD - hU); // up-positive heightfield normal
                normals[vi] = nm.normalized;
            }
        }

        mesh.vertices = verts;
        mesh.normals = normals;
    }

    private float PatchSink(float wx, float wz, SinkPatch p)
    {
        if (p.progress <= 0f) return 0f;
        if (Mathf.Abs(wx - p.center.x) > p.halfEx) return 0f;
        if (Mathf.Abs(wz - p.center.y) > p.halfN) return 0f;
        float ex = 1f - Mathf.Abs(wx - p.center.x) / p.halfEx;
        float ez = 1f - Mathf.Abs(wz - p.center.y) / p.halfN;
        float t = Mathf.Clamp01(ex) * Mathf.Clamp01(ez);
        return p.depth * p.progress * t * t;
    }

    /// <summary>
    /// Blend weight (0..1) of the flat launch pad at a world position: 1 inside the flat disc,
    /// easing to 0 across the fall-off ring so the pad merges with the surrounding terrain.
    /// </summary>
    private float LaunchPadBlend(float wx, float wz)
    {
        if (!launchPadEnabled) return 0f;
        float d = Vector2.Distance(new Vector2(wx, wz), launchPadCenter);
        if (d <= launchPadRadius) return 1f;
        if (d >= launchPadRadius + launchPadBlend) return 0f;
        float u = (d - launchPadRadius) / Mathf.Max(0.01f, launchPadBlend);
        return 1f - Mathf.SmoothStep(0f, 1f, u);
    }

    /// <summary>Blend weight (0..1) of the trials start pad's flattening at a world XZ -
    /// same shape as <see cref="LaunchPadBlend"/> (full flat inside, smooth fall-off ring).</summary>
    private float StartPadBlend(float wx, float wz)
    {
        if (!startPadEnabled) return 0f;
        float d = Vector2.Distance(new Vector2(wx, wz), startPadCenter);
        if (d <= startPadRadius) return 1f;
        if (d >= startPadRadius + launchPadBlend) return 0f;
        float u = (d - startPadRadius) / Mathf.Max(0.01f, launchPadBlend);
        return 1f - Mathf.SmoothStep(0f, 1f, u);
    }

    /// <summary>
    /// Public helper so other systems (spawning the buggy/pods) can place things on the surface.
    /// Returns the terrain height at a world position, matching the generated mesh exactly.
    /// </summary>
    public float SampleHeight(Vector3 worldPos) => SampleHeight(worldPos.x, worldPos.z);

    private float SampleHeight(float wx, float wz)
    {
        float height = ComputeHeight(wx, wz);

        // Subtract the same crater bowls (plus raised rims) for height consistency.
        Vector2 pos = new Vector2(wx, wz);
        bool trialsCorridor = TrialsCourse.Active != null
            && TrialsCourse.Active.InsideCorridor(pos, 0f);
        for (int i = 0; i < craters.Count; i++)
        {
            if (trialsCorridor) break;   // corridor mirrors the mesh: no crater offsets inside
            if (raceEnabled)
            {
                float laneHalf = GameFlow.CurrentCourse == RaceCourse.Ridge
                    ? raceRidgeCrestHalfWidth : raceCourseHalfWidth;
                if (Mathf.Abs(wx - RaceCenterX(wz)) <= laneHalf) break;
            }
            height += CraterOffset(pos, craters[i]);
        }

        // Trials course dive: same lockstep drop for samples, so mesh and queries agree.
        if (TrialsCourse.Active != null && TrialsCourse.Active.HasDive)
            height += TrialsCourse.Active.DiveDropAt(pos);

        // Flat launch pad (mirrors the vertex-level pinning in Generate(), so sample and mesh
        // stay in lockstep - the buggy always spawns on level ground).
        float padW = LaunchPadBlend(wx, wz);
        if (padW > 0f) height = Mathf.Lerp(height, launchPadHeight, padW);

        // Trials start pad: same lockstep for the launch disc at the run's start.
        float startW = StartPadBlend(wx, wz);
        if (startW > 0f) height = Mathf.Lerp(height, startPadHeight, startW);

        return height;
    }
}
