using UnityEngine;

/// <summary>
/// Optional distant body drawn on a level's horizon (e.g. Earth above the Moon, Jupiter over Io,
/// Saturn over Titan). Rendered with an Unlit material so it ignores the level's distance fog and
/// reads as a fixed backdrop beyond the playable terrain.
/// </summary>
[System.Serializable]
public class LevelBackdrop
{
    [Tooltip("Draw this backdrop on the level's horizon? (leave any centre field zero to skip).")]
    public bool enabled = false;
    [Tooltip("Base colour of the planet's disc.")]
    public Color color = new Color(0.5f, 0.6f, 0.7f);
    [Tooltip("World-space centre of the planet, relative to the map centre (far beyond the route).")]
    public Vector3 offset = new Vector3(0f, 400f, 2400f);
    [Tooltip("Radius of the planet's disc (world units).")]
    public float radius = 400f;
    [Tooltip("Optional equirectangular texture inside Assets/Resources/ to wrap onto the disc (e.g. \"EarthMoon\"). Empty = flat colour.")]
    public string texturePath = "";
    [Tooltip("Draw a ring around the planet (e.g. Saturn).")]
    public bool hasRings = false;
    [Tooltip("Colour of the ring (outer disc).")]
    public Color ringColor = new Color(0.9f, 0.85f, 0.75f);

    [Header("2D sky card (flat textured plane instead of a sphere)")]
    [Tooltip("Render as a flat card (Quad) instead of a sphere. Use for 2D cutouts like a UFO or sun graphic.")]
    public bool isSkyCard = false;
    [Tooltip("Width and height of the card in world units (ignored for spheres).")]
    public Vector2 cardScale = new Vector2(200f, 150f);

    [Header("Orientation")]
    [Tooltip("Initial rotation applied at build time before the slow spin begins (Euler angles).")]
    public Vector3 startRotation = Vector3.zero;
}

/// <summary>
/// One of a planet's own moons on the sky. Built as a small unlit sphere beside the level's main
/// backdrop, placed at run time on a random bearing but kept low near the horizon, and always
/// pushed clear of whatever the main backdrop already fills.
/// </summary>
[System.Serializable]
public class SkyMoon
{
    [Tooltip("Draw this moon?")]
    public bool enabled = false;
    [Tooltip("Texture in Assets/Resources/ (e.g. \"Europa\"). Empty or missing = flat colour.")]
    public string texturePath = "";
    [Tooltip("Base colour of the disc, used only when the texture is missing.")]
    public Color color = new Color(0.7f, 0.7f, 0.72f);
    [Tooltip("Size relative to the world's reference moon radius (1 = the biggest moon there).")]
    public float sizeScale = 1f;
    [Tooltip("Draw the picture flat on a card that turns to face the camera, instead of wrapped " +
             "round a sphere. Right for a round portrait of a moon - wrapping one of those over a " +
             "sphere smears it. Leave off only for a genuine equirectangular surface map.")]
    public bool flatImage = true;
    [Tooltip("How far around the sky this moon may land, in degrees either side of due north.")]
    public float azimuthSpread = 150f;
    [Tooltip("Lowest it may hang above the horizon, in degrees.")]
    public float minElevation = 6f;
    [Tooltip("Highest it may hang above the horizon, in degrees.")]
    public float maxElevation = 30f;
    [Tooltip("Distance from the map centre, in metres.")]
    public float distance = 2600f;
    [Tooltip("Spin rate (degrees per second).")]
    public float spinSpeed = 1.2f;
}

/// <summary>
/// The distinct environmental hazard a planet wields against the rover. Each danger is a
/// self-contained director component built by GameBootstrap when a planet declares it, so
/// adding a new planet threat is data-only: declare it here, give the planet a value below,
/// and supply the matching builder (like MeteorStorm for <see cref="Meteor"/>).
/// </summary>
public enum PlanetDanger
{
    None,   // calm world: nothing special to dodge
    Meteor, // the Moon: transient meteor barrages, telegraphed by glowing target rings
    // (future stages: Sandstorm, AcidRain, LavaFlows, MethaneSnow)
}

/// <summary>
/// A single playable planet. Every knob that makes a level feel different lives here,
/// from the oxygen drain rate to the terrain shape and the sky colours, so new worlds
/// are easy to add or balance by editing data only.
/// </summary>
[System.Serializable]
public class LevelDefinition
{
    public string planetName;

    // --- Difficulty: oxygen and grip ---
    [Tooltip("Oxygen lost per second on this planet (higher = harder).")]
    public float drainPerSecond;
    [Tooltip("Multiplier on tyre grip (wheel friction stiffness). Stock grip is high, so this "
            + "only trims the feel: the Moon takes a touch more bite (lighter gravity leaves less "
            + "tyre load) and Io a touch less (sulphur crumble), with the rest near 1.")]
    public float gripMultiplier;

    // --- Difficulty: terrain shaping (copied onto MoonTerrainGen before it builds) ---
    public float hillAmplitude;
    public float hillScale;
    public float bumpAmplitude;
    public float bumpScale;
    public int craterCount;
    public float craterMinRadius;
    public float craterMaxRadius;
    public float craterDepthFactor;
    public float craterRimFactor;
    public int rockCount;

    // --- Route difficulty ---
    public int boulderCount;
    public int podCount;
    public float podSpacing;

    // --- Harvestable resource (Harvester Mode) ---
    [Tooltip("Display name of the resource to harvest (e.g. Helium-3). Empty = no harvestables on this planet.")]
    public string harvestableName;
    [Tooltip("Number of harvestable nuggets scattered off the oxygen route.")]
    public int harvestableCount;
    [Tooltip("Tokens awarded per nugget harvested.")]
    public int harvestableScore;
    [Tooltip("Theme colour of the harvested nuggets (core, glow and beacon).")]
    public Color harvestableColor;
    [Tooltip("Which material this world harvests (narrative identity; the wallet spends uniform tokens).")]
    public UpgradeSystem.OreType harvestableOre;

    // --- Environmental hazard (Harvester Mode) ---
    [Tooltip("Active danger for this planet's Harvester run (None on calm worlds).")]
    public PlanetDanger danger = PlanetDanger.None;

    // --- Race course (Lunar Race mode) ---
    [Tooltip("Whole-course drop in metres from the start line to the finish (steeper = faster + more dangerous).")]
    public float raceDrop;
    [Tooltip("Height of the canyon cliffs above the floor/course, in metres (towering = walled gorge feel).")]
    public float raceWallHeight;
    [Tooltip("Half-width of the canyon floor the race drives along (metres).")]
    public float raceCourseHalfWidth;
    [Tooltip("Lateral weave amplitude of the canyon centre line (metres).")]
    public float raceWeaveAmplitude;
    [Tooltip("Wavelength of the canyon weave (metres).")]
    public float raceWeaveWavelength;

    // --- Gravity and jumping ---
    // Gravity is NOT the real planetary value. It is tuned for how the world DRIVES: it sets
    // the slope acceleration on the canyon and ridge descents (a = g * sin theta) and the load
    // the tyres have to work with. Dropping these to the true figures (Moon 1.62, Titan 1.40)
    // would make every descent feel glacial, so each sits well ABOVE its real gravity to keep
    // the downhill run-up and top speeds roughly where they have always been. Real planetary
    // gravity lives in each world's own description, not in the handling.
    //
    // The CANYON RACE is the one exception: it ignores this value and runs at RaceGravity (the
    // figure below pulled 80% of the way to Earth - see RaceGravityPull) so the tyres stay
    // planted on its bumpy descent. Harvester and Ridge Survival read this value as written.
    [Tooltip("Gravity used for driving on this world (m/s^2). TUNED, not the real planetary "
            + "value: chosen so slope acceleration and tyre load stay close to the long-standing "
            + "feel. Venus keeps Earth's figure; lighter worlds sit well above their true g to "
            + "keep the descents lively. (The canyon race overrides this - see RaceGravity.)")]
    public float planetGravity = 9.81f;
    [Tooltip("Height the hop rockets throw the chassis (metres). This is a DESIGN number, "
            + "deliberately independent of gravity: the hop launches at sqrt(2 * g * apex), so "
            + "gravity sets the launch speed and airtime while this sets how high it actually "
            + "goes. That is what lets a light world float higher without making its downhill "
            + "handling worse, and it is the dial to raise when a world should have bigger jumps.")]
    public float jumpApex = 0.90f;

    // --- Look and atmosphere ---
    public Color regolithColor;
    public Color sunColor;
    public float sunIntensity;
    public Color ambientColor;
    public Color fogColor;
    public float fogStartDistance;
    public float fogEndDistance;

    // --- Prospector Run lighting (the dark canyon-descent look) ---
    // Each world casts its OWN "moonlit" palette for the Prospector Run instead of one shared
    // blackout. When the mode is active GameBootstrap.BuildSkyAndLighting swaps the normal day
    // look for these: a faint directional "moonlight", a flat ambient fill and a tight fog that
    // keeps the headlight and the compass the lifeline. THIS is the block to tune per world:
    //   - raise prospectorSunIntensity for a stronger key light,
    //   - raise prospectorAmbient to lift the whole run out of the dark (the main lever),
    //   - shift prospectorSunColor / prospectorFogColor to change the mood (e.g. Titan's sunset).
    [Tooltip("Directional 'moonlight' brightness during this world's Prospector Run.")]
    public float prospectorSunIntensity = 0.16f;
    [Tooltip("Colour of that moonlight.")]
    public Color prospectorSunColor = new Color(0.55f, 0.62f, 0.82f);
    [Tooltip("Flat ambient fill during the Prospector Run - the main lever for how dark the run reads.")]
    public Color prospectorAmbient = new Color(0.045f, 0.055f, 0.09f);
    [Tooltip("Fog colour during the Prospector Run.")]
    public Color prospectorFogColor = new Color(0.012f, 0.014f, 0.03f);
    [Tooltip("Fog start distance during the Prospector Run (nearer = the headlight matters more).")]
    public float prospectorFogStart = 28f;
    [Tooltip("Fog end distance during the Prospector Run.")]
    public float prospectorFogEnd = 170f;

    // --- Surface / skid ---
    [Tooltip("Absolute handbrake-skid volume (0-1) on this world. Leave NEGATIVE to use the "
            + "gravity-based default; set a value on SOFT surfaces (Titan's methane mud, Mars' "
            + "dust) so the wheel scrub reads quieter there than the gravity alone would give.")]
    public float slideVolumeOverride = -1f;

    // --- Misty cloud sky (replaces the black night sky) ---
    [Tooltip("Drape a procedural misty cloud over the sky instead of showing the black night sky?")]
    public bool mistySky = false;
    [Tooltip("Tint of the cloud away from the horizon.")]
    public Color cloudColor = new Color(0.86f, 0.83f, 0.72f);
    [Tooltip("How far up the dome the cloud lifts off the planet's own haze colour (fraction of the dome).")]
    [Range(0.05f, 1f)] public float cloudHazeFalloff = 0.45f;
    [Tooltip("Contrast of the cloud billows (0 = flat haze, 1 = deep cloud).")]
    [Range(0f, 1f)] public float cloudDensity = 0.5f;

    // --- Extra moons on the sky (optional) ---
    [Tooltip("Reference radius for this world's moons in metres; each SkyMoon scales off this.")]
    public float moonRadius = 135f;
    [Tooltip("Moons hung around this world's sky, placed on a random bearing at run time.")]
    public SkyMoon[] skyMoons;

    // --- Distant horizon backdrop (optional) ---
    public LevelBackdrop backdrop;

    // --- Night-sky panorama (optional) ---
    [Tooltip("Texture in Assets/Resources/ (e.g. \"Milkyway\") draped over the black night sky "
             + "as a latitude-longitude starscape dome. Empty keeps the plain black void.")]
    public string skyTexture = "";
    [Tooltip("Fixed world yaw (degrees) of the night-sky panorama.")]
    public float skyYaw = 0f;
    [Tooltip("Pitch (degrees) tipping the night-sky dome so the panorama's own centre band "
             + "(normally the image's horizon row) lifts off the terrain horizon and arcs across "
             + "the sky. The chase camera only frames the LOW sky just ahead, so a NEGATIVE pitch "
             + "carries the Milky Way's bright band up into that forward view; large values swing "
             + "it out of sight overhead and behind, where the void reads as plain black.")]
    public float skyPitch = -22f;
}

/// <summary>
/// Static progression through the five planets, in difficulty order.
/// Level state survives scene reloads (restarts) because it never leaves memory,
/// so the game can restart the current planet after a death and advance after a win.
/// </summary>
public static class LevelManager
{
    // NOTE: planets must be declared BEFORE the Planets array. C# runs static field
    // initializers in declaration order, so declaring the array first would capture
    // the still-uninitialised (null) planet fields.

    private static readonly LevelDefinition Moon = new LevelDefinition
    {
        planetName = "THE MOON",
        drainPerSecond = 2.2f,
        // The Moon runs light, so the tyres have less load to work with. A small lift over the
        // shared stock figure keeps it from feeling looser than the heavier worlds.
        gripMultiplier = 1.15f,

        hillAmplitude = 7.5f,
        hillScale = 120f,
        bumpAmplitude = 1.8f,
        bumpScale = 0.045f,
        craterCount = 300,
        craterMinRadius = 4f,
        craterMaxRadius = 32f,
        craterDepthFactor = 0.3f,
        craterRimFactor = 0.35f,
        rockCount = 520,

        boulderCount = 30,
        podCount = 10,
        podSpacing = 100f,

        raceDrop = 44f,
        raceWallHeight = 42f,
        raceCourseHalfWidth = 14f,
        raceWeaveAmplitude = 36f,
        raceWeaveWavelength = 245f,
        // True Moon gravity is 1.62. Driven well above it so the descents still run; the
        // float comes from the longer airtime this buys, not from a weaker push.
        planetGravity = 5.2f,
        jumpApex = 1.1f,

        harvestableName = "HELIUM-3",
        harvestableCount = 180,
        harvestableScore = 2,
        harvestableColor = new Color(0.35f, 0.95f, 0.8f),
        harvestableOre = UpgradeSystem.OreType.Helium3,

        danger = PlanetDanger.Meteor,

        regolithColor = new Color(0.27f, 0.27f, 0.29f),
        sunColor = new Color(0.90f, 0.95f, 1.00f),
        sunIntensity = 1.0f,
        ambientColor = new Color(0.15f, 0.18f, 0.22f),
        fogColor = new Color(0.03f, 0.03f, 0.05f),
        fogStartDistance = 300f,
        fogEndDistance = 960f,

        // A large blue Earth hangs above the Moon's horizon, centred over the route ahead.
        backdrop = new LevelBackdrop
        {
            enabled = true,
            color = new Color(0.22f, 0.5f, 0.92f),
            offset = new Vector3(300f, 430f, 2500f),
            radius = 300f, // ~0.6x: smaller Earth in the Moon sky
            texturePath = "Earth", // Assets/Resources/Earth.jpg (equirectangular)
        },

        // The Lunar Landscape pack's Milky Way panorama was removed for build size (it never read
        // correctly either). The Moon's night sky is left black; re-add a panorama here if sourced.
        skyTexture = null,
        skyYaw = 14f,
        // The panorama's bright galactic band sits on the image's own centre row (its horizon),
        // so a GENTLE NEGATIVE pitch tips that band up into the low forward sky the chase camera
        // actually frames. A large positive pitch swung the band behind and overhead, out of the
        // forward view entirely, which is why the sky read as black. ~22 degrees puts the band's
        // bright core just above the horizon dead ahead, where it reads as a starfield arc.
        skyPitch = -22f,
    };

    private static readonly LevelDefinition Mars = new LevelDefinition
    {
        planetName = "MARS",
        drainPerSecond = 2.6f,
        gripMultiplier = 1f,

        hillAmplitude = 9.5f,
        hillScale = 130f,
        bumpAmplitude = 2.3f,
        bumpScale = 0.05f,
        craterCount = 340,
        craterMinRadius = 5f,
        craterMaxRadius = 36f,
        craterDepthFactor = 0.35f,
        craterRimFactor = 0.4f,
        rockCount = 420,

        // Mars is the BOULDER world: a much denser rock field than the shared default, so
        // avoiding rock is a main skill on its survey plains and especially its Prospector run.
        boulderCount = 55,
        podCount = 10,
        podSpacing = 100f,

        raceDrop = 62f,
        raceWallHeight = 52f,
        raceCourseHalfWidth = 14f,
        raceWeaveAmplitude = 40f,
        raceWeaveWavelength = 235f,
        // True Mars gravity is 3.70; the lightest handling of the "rocky" worlds.
        planetGravity = 7.4f,

        harvestableName = "IRON",
        harvestableCount = 195,
        harvestableScore = 3,
        harvestableColor = new Color(0.8f, 0.42f, 0.2f),
        harvestableOre = UpgradeSystem.OreType.Iron,

        danger = PlanetDanger.Meteor,

        regolithColor = new Color(0.52f, 0.33f, 0.22f),
        sunColor = new Color(1.00f, 0.82f, 0.62f),
        sunIntensity = 0.95f,
        ambientColor = new Color(0.32f, 0.20f, 0.13f),
        fogColor = new Color(0.22f, 0.10f, 0.05f),
        fogStartDistance = 250f,
        fogEndDistance = 880f,

        // Prospector Run on Mars is a bright, dusty dusk rather than a blackout - the shared
        // darkening left the red floor unreadable. A much stronger moonlight, a warm ambient
        // lift and a lighter, wider haze so the terrain actually reads in the dark run.
        prospectorSunIntensity = 0.10f,
        prospectorSunColor = new Color(1.00f, 0.82f, 0.66f),
        prospectorAmbient = new Color(0.20f, 0.14f, 0.11f),
        prospectorFogColor = new Color(0.16f, 0.09f, 0.06f),
        prospectorFogStart = 45f,
        prospectorFogEnd = 260f,

        // Mars' dust is a soft, giving surface: the skid scrub reads much quieter than the
        // gravity alone would set it.
        slideVolumeOverride = 0.2f,

        // Mars's permanent dust haze reads better as a pale pink-yellow cloud deck than as a black
        // night sky, so the world asks for a misty dome. The UFO card (Assets/Resources/
        // MartianUFO.jpg) went with the night sky it used to hang against; it has no other use.
        mistySky = true,
        cloudColor = new Color(0.95f, 0.84f, 0.74f),
        cloudHazeFalloff = 0.42f,
        cloudDensity = 0.55f,
    };

    private static readonly LevelDefinition Venus = new LevelDefinition
    {
        planetName = "VENUS",
        drainPerSecond = 3.6f,
        gripMultiplier = 1.05f,

        hillAmplitude = 12.5f,
        hillScale = 140f,
        bumpAmplitude = 2.7f,
        bumpScale = 0.055f,
        craterCount = 380,
        craterMinRadius = 6f,
        craterMaxRadius = 42f,
        craterDepthFactor = 0.4f,
        craterRimFactor = 0.45f,
        rockCount = 460,

        boulderCount = 42,
        podCount = 10,
        podSpacing = 100f,

        raceDrop = 78f,
        raceWallHeight = 62f,
        raceCourseHalfWidth = 13f,
        raceWeaveAmplitude = 44f,
        raceWeaveWavelength = 225f,
        // Venus now runs its REAL gravity, the one world with no reason to inflate it: it is
        // already heavy, and the storm and the thick air are punishment enough. Dropping from
        // Earth's figure to the true one leaves jumpApex alone, so the hop keeps its punishing
        // height and simply hangs a touch longer in the thicker air.
        planetGravity = 8.87f,
        jumpApex = 0.225f,

        harvestableName = "SILICON",
        harvestableCount = 210,
        harvestableScore = 4,
        harvestableColor = new Color(0.72f, 0.78f, 0.85f),
        harvestableOre = UpgradeSystem.OreType.Silicon,

        regolithColor = new Color(0.62f, 0.48f, 0.30f),
        sunColor = new Color(1.00f, 0.95f, 0.80f),
        sunIntensity = 1.1f,
        ambientColor = new Color(0.38f, 0.33f, 0.18f),
        fogColor = new Color(0.42f, 0.34f, 0.16f),
        fogStartDistance = 55f,
        fogEndDistance = 240f,

        // Venus is the cloud world: a pale sulphur-yellow deck instead of a night sky. The twin-star
        // glow card (Assets/Resources/Sun.jpg) is gone with the black sky it peaked over, and
        // Venus keeps no horizon backdrop of its own.
        mistySky = true,
        cloudColor = new Color(0.97f, 0.94f, 0.74f),
        cloudHazeFalloff = 0.5f,
        cloudDensity = 0.45f,
    };

    private static readonly LevelDefinition Io = new LevelDefinition
    {
        planetName = "IO",
        drainPerSecond = 3.2f,
        // Sulphur crumble: the one world that should read a shade slipperier than stock.
        gripMultiplier = 0.95f,

        hillAmplitude = 12f,
        hillScale = 140f,
        bumpAmplitude = 2.8f,
        bumpScale = 0.06f,
        craterCount = 340,
        craterMinRadius = 6f,
        craterMaxRadius = 44f,
        craterDepthFactor = 0.45f,
        craterRimFactor = 0.5f,
        rockCount = 350,

        boulderCount = 50,
        podCount = 10,
        podSpacing = 100f,

        raceDrop = 98f,
        raceWallHeight = 70f,
        raceCourseHalfWidth = 13f,
        raceWeaveAmplitude = 48f,
        raceWeaveWavelength = 215f,
        // True Io gravity is 1.80.
        planetGravity = 5.6f,
        jumpApex = 1.1f,

        harvestableName = "SULPHUR",
        harvestableCount = 225,
        harvestableScore = 5,
        harvestableColor = new Color(0.95f, 0.9f, 0.35f),
        harvestableOre = UpgradeSystem.OreType.Sulphur,

        // Io's thin crust has no blanket atmosphere to burn incoming rocks either: meteor
        // storms harry the harvest like the Moon's (start at the Moon's friendly first level).
        danger = PlanetDanger.Meteor,

        regolithColor = new Color(0.72f, 0.55f, 0.20f),
        sunColor = new Color(0.95f, 0.90f, 0.85f),
        sunIntensity = 0.95f,
        ambientColor = new Color(0.26f, 0.20f, 0.10f),
        fogColor = new Color(0.28f, 0.14f, 0.06f),
        fogStartDistance = 330f,
        fogEndDistance = 1500f,

        // Prospector Run on Io keeps the JUPITER-ECLIPSE look the mode looked best in - a pale
        // sulfur-green haze and ambient glow - but dimmer and PERMANENT (the eclipse hazard is
        // switched off during Prospector, see GameBootstrap.BuildHazards). Nudge these to make the
        // green dusk brighter or softer.
        prospectorSunIntensity = 0.01f,
        prospectorSunColor = new Color(0.72f, 0.80f, 0.62f),
        // Blue channel pulled from 0.19 to 0.10: the ambient was lifted so high it capped how much
        // the rest of the run could be dimmed. Lower it further to darken Io's Prospector more.
        prospectorAmbient = new Color(0.22f, 0.27f, 0.10f),
        prospectorFogColor = new Color(0.44f, 0.54f, 0.40f),
        prospectorFogStart = 55f,
        prospectorFogEnd = 320f,

        // Banded peach-coloured Jupiter dominates Io's sky.
        backdrop = new LevelBackdrop
        {
            enabled = true,
            color = new Color(0.8f, 0.66f, 0.5f),
            offset = new Vector3(1550f, 430f, 2300f),
            radius = 750f, // ~1.5x: bigger Jupiter dominating Io's sky
            texturePath = "Jupiter", // Assets/Resources/Jupiter.jpg
            // Spin the visible longitude round so the bright equatorial bands face the route.
            startRotation = new Vector3(0f, 85f, 0f),
        },

        // Io's own three moons (Europa, Ganymede, Callisto) were ported as round portraits on
        // black, but stretched over flat cards beside the huge Jupiter they read as clipped discs
        // rather than moons, so they stay OFF. Jupiter keeps the sky single and uncluttered.
        skyMoons = new[]
        {
            new SkyMoon
            {
                enabled = false,
                texturePath = null,
                color = new Color(0.88f, 0.82f, 0.70f),
                sizeScale = 1f,
                flatImage = true,
            },
            new SkyMoon
            {
                enabled = false,
                texturePath = null,
                color = new Color(0.62f, 0.60f, 0.58f),
                sizeScale = 0.33f,
                flatImage = true,
            },
            new SkyMoon
            {
                enabled = false,
                texturePath = null,
                color = new Color(0.42f, 0.38f, 0.34f),
                sizeScale = 0.25f,
                flatImage = true,
            },
        },
    };

    private static readonly LevelDefinition Titan = new LevelDefinition
    {
        planetName = "TITAN",
        drainPerSecond = 3.4f,
        // Titan is the lightest world here, so it takes a small extra bite to stay driveable
        // rather than feeling like a skate.
        gripMultiplier = 1.05f,

        hillAmplitude = 16f,
        hillScale = 150f,
        bumpAmplitude = 3.6f,
        bumpScale = 0.065f,
        craterCount = 460,
        craterMinRadius = 8f,
        craterMaxRadius = 55f,
        craterDepthFactor = 0.5f,
        craterRimFactor = 0.55f,
        rockCount = 500,

        boulderCount = 55,
        podCount = 10,
        podSpacing = 100f,

        raceDrop = 120f,
        raceWallHeight = 78f,
        raceCourseHalfWidth = 12f,
        raceWeaveAmplitude = 52f,
        raceWeaveWavelength = 205f,
        // Titan is the lowest-gravity world here (true 1.40), so it floats the longest.
        planetGravity = 4.8f,
        jumpApex = 1.1f,

        harvestableName = "LIQUID METHANE",
        harvestableCount = 240,
        harvestableScore = 6,
        harvestableColor = new Color(0.35f, 0.85f, 0.95f),
        harvestableOre = UpgradeSystem.OreType.LiquidHelium,

        // Like Io, Titan's harvest can't dodge falling rock (no Mars-length defence), so it
        // joins the storm set at the Moon's friendly first level on top of the survey hazards.
        danger = PlanetDanger.Meteor,

regolithColor = new Color(0.50f, 0.36f, 0.22f),
        sunColor = new Color(0.90f, 0.70f, 0.50f),
        sunIntensity = 0.7f,
        ambientColor = new Color(0.30f, 0.22f, 0.12f),
        fogColor = new Color(0.20f, 0.13f, 0.07f),
        fogStartDistance = 80f,
        fogEndDistance = 340f,

        // Prospector Run on Titan is a dark ORANGE SUNSET: a warm low sun, amber haze and a
        // burnt-brown ambient so the methane dusk reads instead of falling to black.
        prospectorSunIntensity = 0.1f,
        prospectorSunColor = new Color(1.00f, 0.60f, 0.28f),
        prospectorAmbient = new Color(0.24f, 0.14f, 0.08f),
        prospectorFogColor = new Color(0.32f, 0.17f, 0.08f),
        prospectorFogStart = 45f,
        prospectorFogEnd = 300f,

        // Titan's methane mud/ice is the softest surface of all: the skid scrub reads louder-capped
        // down to a faint slide.
        slideVolumeOverride = 0.2f,

        // Titan's thick nitrogen haze is the orange-to-brown twilight sky the landing shots
        // made famous - a planet seen THROUGH the soup, not against a black night. So the world
        // drapes the same misty cloud dome as Mars/Venus, tuned to a pale-orange deck that fades
        // to the brown haze colour at the horizon. The glow card (Assets/Resources/Sun.jpg) is
        // gone with the black sky it peaked over, and Titan keeps no other horizon backdrop.
        mistySky = true,
        cloudColor = new Color(0.89f, 0.58f, 0.30f), // hazy orange methane deck
        cloudHazeFalloff = 0.5f,                     // brown haze clings to the horizon
        cloudDensity = 0.4f,

        // Saturn art lives in Saturn.png (your cropped 768x334 render, rings included). It reads best
        // as a flat 2D sky card - NO sphere wrap and NO rotation, because the rings are already
        // oriented in the art and any tilt looks wrong. Two things keep it right against the new
        // haze:
        //   - The card has to sit INSIDE the misty dome (radius 2000m, centred on the camera).
        //     The dome is an opaque shell, so a card out beyond it (the old 4500m) would be
        //     occluded by the far side of the shell and vanish. Pulled in to ~1370m and rescaled
        //     to hold the same apparent size, it hangs low in the orange haze exactly where the
        //     real moon pokes through the soup.
        //   - The ColorKey shader punches the card's background out so the haze shows through
        //     around the disc and rings instead of a hard rectangle. The new PNG already carries a
        //     real alpha background, but the shader still keys by luminance (and multiplies by the
        //     texture alpha), so both a transparent-background PNG and a black-surround JPEG read
        //     correctly. The PNG import sets Alpha Is Transparency so the RGB does not bleed a dark
        //     fringe into the transparent edge under filtering and its mips.
        backdrop = new LevelBackdrop
        {
            enabled = true,
            isSkyCard = true,
            texturePath = "Saturn", // Assets/Resources/Saturn.png (flat card, transparent background)
            color = new Color(0.88f, 0.78f, 0.58f),
            offset = new Vector3(-400f, 300f, 1280f), // inside the 2000m misty dome
            cardScale = new Vector2(840f, 365f),       // matches the art's own ~2.30 aspect (768x334), so it is not squashed
            startRotation = Vector3.zero, // rings must stay exactly as painted - no tilt
        },
    };

    public static readonly LevelDefinition[] Planets =
    {
        Moon, Mars, Venus, Io, Titan,
    };

    public static int CurrentLevelIndex = 0;

    public static LevelDefinition CurrentLevel => Planets[CurrentLevelIndex];
    public static int LevelNumber => CurrentLevelIndex + 1;
    public static int PlanetCount => Planets.Length;

    // ------------------------------------------------------------- unlock state
    // Worlds unlock SEQUENTIALLY through the 15-slot chain - one HARVEST, one PROSPECTOR then
    // one RIDGE per planet, in that order:
    //   Moon Harvest -> Moon Prospector -> Moon Ridge -> Mars Harvest -> Mars Prospector -> ...
    // Only the Moon's HARVEST is open at the start. Each check point opens exactly the
    // NEXT slot in the chain:
    //   - surviving a world's HARVEST opens its PROSPECTOR RUN
    //   - surviving a world's PROSPECTOR RUN opens its RIDGE
    //   - completing the RIDGE opens the NEXT planet's HARVEST
    // The open slots are DERIVED from the persisted per-mode bests (never stored as a separate
    // count), so the current unlock level can always be recomputed on load and a single
    // good run cascades the chain naturally.
    private const string RaceBestTierKey = "lunar.racebest.w{0}";
    private const string RaceBestGradeKey = "lunar.racegrade.w{0}";
    private const string RidgeBestGradeKey = "lunar.ridgebest.w{0}";
    private const string RidgeClearedKey = "lunar.ridgeclear.w{0}";
    private const string HarvestBestKey = "lunar.harvestbest.w{0}";
    private const string ProspectorBestKey = "lunar.prospectorbest.w{0}";
    private const string ProspectorGradeKey = "lunar.prospectorgrade.w{0}";
    private const string ProspectorSitesKey = "lunar.prospectorsites.w{0}";
    private const string HarvestClearedKey = "lunar.harvestclear.w{0}";
    /// <summary>Per-world flag: the player has SURVIVED this world's Prospector Run (its canyon
    /// descent). This opens only this world's RIDGE SURVIVAL; the next planet stays locked until
    /// the ridge course is completed.</summary>
    private const string ProspectorClearedKey = "lunar.prospectorclear.w{0}";
    private const string HarvestGradeKey = "lunar.harvestgrade.w{0}";
    private const string HarvestSitesKey = "lunar.harvestsites.w{0}";

    /// <summary>Harvest grade cap per world: every cleared run raises the world's grade
    /// (1 = fresh field, 2..5 after clears), adding ore to the field, scaling the storm
    /// hazards up AND roughening the harvest terrain (see MoonTerrainGen). Purely cosmetic
    /// progression - a graded world never gates anything.</summary>
    public const int MaxHarvestGrade = 5;
    /// <summary>Prospector has its own independent five-clear difficulty/star ladder.</summary>
    public const int MaxProspectorGrade = 5;
    /// <summary>Ore deposits EACH grade above 1 adds to the world's field (grade 1 = the
    /// stock field trimmed by <see cref="HarvestGradeStartTrim"/>). ~+30 per grade makes the
    /// climb to the tougher courses pay noticeably better than the old +15 ladder.</summary>
    public const int HarvestGradeOrePerGrade = 30;
    /// <summary>The fresh (grade 1) field starts slightly LEANER than stock, so the +30/grade
    /// climb reads as a real reward - not just a bonus on top of an already-full course.</summary>
    public const int HarvestGradeStartTrim = 15;
    /// <summary>One-time token bonus for a world's very first harvest clear (the Moon's
    /// 150-token hook: a first clear alone funds the first garage upgrade immediately).</summary>
    public const int FirstClearBonusAmount = 150;
    /// <summary>One-time token bonus for the player's VERY FIRST canyon-race medal (Bronze up),
    /// the race's mirror of the harvest first-clear hook. Only ever paid once, for the first
    /// medal across any world's canyon.</summary>
    public const int FirstMedalBonusAmount = 150;
    private const string FirstMedalBonusKey = "lunar.firstmedalbonus";
    /// <summary>One-time 100-token reward for the first successfully completed Prospector Run.</summary>
    public const int ProspectorFirstClearBonusAmount = 100;
    private const string ProspectorFirstClearBonusKey = "lunar.prospector.firstclearbonus";
    /// <summary>Token bonus for a PERFECT Prospector Run (every ore nugget on the route collected).
    /// The run only requires at least half the field to clear (see LevelEndSequence), so a full
    /// sweep is its top result. Paid per run, and shown as its own line on the clear board.</summary>
    public const int ProspectorPerfectBonusAmount = 150;
    /// <summary>Token bonus for clearing ALL <see cref="MaxHarvestGrade"/> grades of a world
    /// (every site on the star ladder). Credited on the ceremony popup's OK press, like the
    /// first-clear bonus it mirrors.</summary>
    public const int HarvestAllClearBonusAmount = 100;
    /// <summary>One-time token WELCOME bonus for the player's VERY FIRST Moon harvest attempt, paid
    /// to the wallet the moment that attempt CONCLUDES - win or lose (100). Deliberately not paid on
    /// merely entering the level: it rewards a full effort, so a new player who pushes the run to
    /// its end (a clear, or a death) still banks some tokens even when the harvest itself failed.
    /// Moon harvest only (level 0) - never the prospector, the ridge, or another world's harvest.
    /// Paid exactly once ever (keyed below) and stacked on top of any first-clear bonus that same
    /// run earned.</summary>
    public const int FirstAttemptBonusAmount = 100;
    private const string FirstAttemptHarvestKey = "lunar.firstattempt.harvest";

    // Three slots per planet in the sequential chain: HARVEST, then PROSPECTOR RUN (the canyon),
    // then RIDGE SURVIVAL. Every step now opens by SURVIVING the previous one (like the harvest):
    // surviving a world's harvest opens its Prospector Run, surviving that opens this world's
    // ridge, and completing that ridge opens the next planet's harvest.
    public const int ModesPerWorld = 3;

    /// <summary>Slot position of a world+mode in the chain: Harvester 0, canyon (Prospector) 1,
    /// Survival 2. Race is a dormant mode kept for a future build; it shares the canyon slot.</summary>
    public static int ModeOffset(GameMode mode)
    {
        if (mode == GameMode.Prospector || mode == GameMode.Race) return 1;
        if (mode == GameMode.Survival) return 2;
        return 0;
    }

    /// <summary>Total slots in the progression chain (5 planets x 3 modes).</summary>
    public static int TotalSlots => PlanetCount * ModesPerWorld;

    /// <summary>Slots currently open (1 = just the Moon's HARVEST). Derived from persisted
    /// clear flags, so each completed gate advances the sequential chain.</summary>
    public static int UnlockedSlots
    {
        get
        {
            int count = 1;
            while (count < TotalSlots && SlotRequirementMet(count)) count++;
            return count;
        }
    }

    /// <summary>True when the slot at chain position <paramref name="count"/> (1-indexed)
    /// is earned: the check point that opens the NEXT slot past the open prefix.</summary>
    private static bool SlotRequirementMet(int count)
    {
        int world = count / ModesPerWorld;
        int off = count % ModesPerWorld;
        if (off == 0)
            return IsRidgeCleared(world - 1);
        if (off == 1)
            return IsHarvestCleared(world);              // this world's HARVEST survived
        // This world's RIDGE. Surviving this world's PROSPECTOR RUN opens it.
        return IsProspectorCleared(world);
    }

    /// <summary>True when the player may launch this world+mode. All three modes walk the
    /// per-planet chain: HARVEST opens the world's PROSPECTOR RUN, that descent opens its RIDGE,
    /// and completing the ridge opens the next planet.</summary>
    public static bool IsModeUnlocked(int world, GameMode mode)
    {
        return world >= 0 && world < PlanetCount
            && world * ModesPerWorld + ModeOffset(mode) < UnlockedSlots;
    }

    /// <summary>True once this planet's HARVEST is open (always true for the Moon).</summary>
    public static bool IsUnlocked(int index) => IsModeUnlocked(index, GameMode.Harvester);

    /// <summary>True once this planet's HARVEST has been survived (its PROSPECTOR RUN is open).</summary>
    public static bool IsCleared(int index) => index >= 0 && index < PlanetCount && IsHarvestCleared(index);

    /// <summary>Earth's gravity (m/s^2): the reference the canyon drops were laid out against,
    /// and the figure race courses are pulled back toward.</summary>
    public const float EarthGravity = 9.81f;

    /// <summary>How far a CANYON RACE pulls its planet's gravity back toward Earth (0..1).
    /// Races differ from the survey drives: they run wheel-to-wheel lines down a bumpy descent,
    /// and a low-gravity world left even a stock rover skipping off every bump - hopeless for
    /// the controlled turns the canyon demands. So the canyon race runs on BLENDED gravity:
    /// 1 = Earth's pull on every world, 0 = the planet's tuned figure untouched. At 0.8 the Moon
    /// races at ~8.9, Io ~8.97 and Titan ~8.8 - close to Earth, still a touch lighter than a
    /// full-gravity world, so the drops keep a little character without the floaty bumps. This is
    /// the single dial for "how Earth-like do race courses feel"; raise it toward 1 to make races
    /// heavier still. Harvester drives and Ridge Survival keep the planet's own gravity
    /// (the ridge's long floaty hops are what keep its oxygen canisters reachable).</summary>
    public const float RaceGravityPull = 0.8f;

    /// <summary>Gravity actually in force on this world's CANYON RACE: the planet's tuned figure
    /// pulled toward Earth by <see cref="RaceGravityPull"/>. Both
    /// <c>GameBootstrap.ApplyPlanetGravity</c> (the physics world) and
    /// <see cref="DownhillSpeedLift"/> (the top-speed shelf) read this, so the physics and the
    /// compensation always agree.</summary>
    public static float RaceGravity(int world)
    {
        if (world < 0 || world >= PlanetCount) return EarthGravity;
        return Mathf.Lerp(Planets[world].planetGravity, EarthGravity, RaceGravityPull);
    }

    /// <summary>How much of a world's lost downhill acceleration to hand back on its
    /// race courses, as a fraction of the REMAINING deficit. On the canyon race
    /// <see cref="RaceGravity"/> has already lifted most worlds close to Earth, so only the small
    /// residual is topped up here; Ridge Survival is unchanged (it still reads the planet's raw
    /// gravity). Written as a fraction of the deficit rather than a flat per-planet number, so
    /// re-tuning a planet's gravity keeps its pace right without touching this. Raise it if the
    /// courses still feel slow.</summary>
    public const float DownhillAssist = 0.25f;

    /// <summary>Speed multiplier that puts <see cref="DownhillAssist"/> of a world's remaining
    /// lost gravity back on its race courses. <paramref name="raceBoost"/> is true on the canyon
    /// race (gravity already pulled toward Earth, so the lift is small) and false on Ridge
    /// Survival (raw planet gravity, so the lift is the same as before). 1 = untouched, so a
    /// world already at Earth gravity is unaffected and the lift scales itself as planets are
    /// re-tuned.</summary>
    public static float DownhillSpeedLift(int world, bool raceBoost)
    {
        if (world < 0 || world >= PlanetCount) return 1f;
        float g = raceBoost ? RaceGravity(world) : Planets[world].planetGravity;
        float deficit = 1f - Mathf.Clamp01(g / EarthGravity);
        return 1f + deficit * DownhillAssist;
    }

    /// <summary>One-line hint for what opens a locked world+mode ("" when already open).</summary>
    public static string UnlockGateText(int world, GameMode mode)
    {
        if (IsModeUnlocked(world, mode)) return "";
        if (mode == GameMode.Harvester)
        {
            if (world <= 0) return "";
            return "COMPLETE " + Planets[world - 1].planetName + " RIDGE";
        }
        if (mode == GameMode.Prospector || mode == GameMode.Race)
            return "SURVIVE " + Planets[world].planetName + " HARVEST";
        // The ridge opens after surviving this world's PROSPECTOR RUN.
        return "SURVIVE " + Planets[world].planetName + " PROSPECTOR RUN";
    }

    // ------------------------------------------------ per-mode progress stores
    // Tier indices are RivalRacer.Tier (Bronze 0 / Silver 1 / Gold 2); race grade indices are
    // 0 .. RivalRacer.GradeCount-1; ridge grade indices are GameFlow.RidgeGradeTokens
    // positions (0 Bronze .. 3 Platinum). -1 = none.
    /// <summary>Stores the best race TIER ever beaten on a world (persists across sessions).
    /// Beating BRONZE is the check point that opens the NEXT planet's HARVEST.</summary>
    public static void RecordRaceBestTier(int world, int tierIndex)
    {
        if (ChallengeRunner.Active != null) return;   // LUNAR TRIALS: no campaign side-effects
        if (world < 0) return;
        string k = string.Format(RaceBestTierKey, world);
        if (tierIndex > SaveBridge.GetInt(k, -1))
        {
            SaveBridge.SetInt(k, tierIndex);
            SaveBridge.Save();
            KongregateBridge.OnProgressChanged();   // new tier beaten -> board now
        }
    }

    /// <summary>Best race TIER beaten on a world (-1 = never).</summary>
    public static int GetRaceBestTier(int world) => world < 0 ? -1 : SaveBridge.GetInt(string.Format(RaceBestTierKey, world), -1);

    // ------------------------------------------------ race GRADE progress
    // The race GRADE (0 Bronze-grade .. 4 Diamond-grade) is the run's star level on a world:
    // clearing it (beating the whole field, the GOLD rival) unlocks the next, faster grade.
    // It is separate from the tier store above - a grade's rivals are each branded by their
    // tier, so the two channels are independent. -1 = grade one not cleared yet.
    /// <summary>Stores the highest race GRADE cleared on a world (persists across sessions).
    /// Clearing grade 0 (beating the Bronze-grade field) still counts as beating BRONZE for
    /// the chain's tier check point.</summary>
    public static void RecordRaceBestGrade(int world, int gradeIndex)
    {
        if (ChallengeRunner.Active != null) return;   // LUNAR TRIALS: no campaign side-effects
        if (world < 0 || gradeIndex < 0) return;
        string k = string.Format(RaceBestGradeKey, world);
        if (gradeIndex > SaveBridge.GetInt(k, -1))
        {
            SaveBridge.SetInt(k, gradeIndex);
            SaveBridge.Save();
            KongregateBridge.OnProgressChanged();   // faster grade cleared -> board now
        }
    }

    /// <summary>Highest race GRADE cleared on a world (-1 = none yet).</summary>
    public static int GetRaceBestGrade(int world) => world < 0 ? -1 : SaveBridge.GetInt(string.Format(RaceBestGradeKey, world), -1);

    /// <summary>Stores the highest ridge GRADE reached on a world (persists across sessions,
    /// driving that world's Survival star ladder). The ridge is the LAST step of a planet's
    /// chain: beating BRONZE here opens the NEXT planet's HARVEST.</summary>
    public static void RecordRidgeBestGrade(int world, int gradeIndex)
    {
        if (ChallengeRunner.Active != null) return;   // LUNAR TRIALS: no campaign side-effects
        if (world < 0) return;
        string k = string.Format(RidgeBestGradeKey, world);
        if (gradeIndex > SaveBridge.GetInt(k, -1))
        {
            SaveBridge.SetInt(k, gradeIndex);
            SaveBridge.Save();
            KongregateBridge.OnProgressChanged();   // BRONZE/SILVER/GOLD/PLATINUM step -> board now
            NewgroundsBridge.OnProgressChanged();   // ridge medal ladder step
        }
    }

    /// <summary>Highest ridge GRADE reached on a world (-1 = none).</summary>
    public static int GetRidgeBestGrade(int world) => world < 0 ? -1 : SaveBridge.GetInt(string.Format(RidgeBestGradeKey, world), -1);

    /// <summary>True once the one-time first-medal bonus has been paid (persists across sessions,
    /// so beating a canyon medal never pays it twice - even after a world wipe on the portal).</summary>
    public static bool FirstMedalBonusPaid => SaveBridge.GetInt(FirstMedalBonusKey, 0) > 0;

    /// <summary>Claims the one-time first-medal bonus; returns true ONLY the first time it is
    /// paid, so the caller banks <see cref="FirstMedalBonusAmount"/> exactly once ever.</summary>
    public static bool TryClaimFirstMedalBonus()
    {
        if (FirstMedalBonusPaid) return false;
        SaveBridge.SetInt(FirstMedalBonusKey, 1);
        SaveBridge.Save();
        return true;
    }

    /// <summary>Claims the one-time Prospector clear reward. Call only on a successfully cleared
    /// run, never when oxygen or hull failure ends an attempt.</summary>
    public static bool TryClaimProspectorFirstClearBonus()
    {
        if (SaveBridge.GetInt(ProspectorFirstClearBonusKey, 0) > 0) return false;
        SaveBridge.SetInt(ProspectorFirstClearBonusKey, 1);
        SaveBridge.Save();
        return true;
    }

    /// <summary>Claims the one-time first-Moon-harvest welcome bonus; returns true ONLY the first
    /// time it is paid, so the caller banks <see cref="FirstAttemptBonusAmount"/> exactly once ever.
    /// Call only when the player's first Moon harvest attempt has CONCLUDED (cleared or died), never
    /// on entering the level.</summary>
    public static bool TryClaimFirstAttemptBonus()
    {
        if (SaveBridge.GetInt(FirstAttemptHarvestKey, 0) > 0) return false;
        SaveBridge.SetInt(FirstAttemptHarvestKey, 1);
        SaveBridge.Save();
        return true;
    }

    // ------------------------------------------------- harvester score progress
    // The best harvest score on a world is its leaderboard score (tokens banked that run).
    // It no longer gates the next world - surviving the HARVEST is what opens its RACE -
    // but it is still tracked for the board and the intro's "best here".
    /// <summary>Stores the best harvest score ever banked on a world (persists across sessions).</summary>
    public static void RecordHarvestBest(int world, int score)
    {
        if (world < 0 || score <= 0) return;
        string k = string.Format(HarvestBestKey, world);
        if (score > SaveBridge.GetInt(k, 0))
        {
            SaveBridge.SetInt(k, score);
            SaveBridge.Save();
        }
    }

    /// <summary>Best harvest score banked on a world (0 = never scored there).</summary>
    public static int GetHarvestBest(int world) => world < 0 ? 0 : SaveBridge.GetInt(string.Format(HarvestBestKey, world), 0);

    /// <summary>Stores the best Prospector token score on a world (persists across sessions).</summary>
    public static void RecordProspectorBest(int world, int score)
    {
        if (world < 0 || score <= 0) return;
        string key = string.Format(ProspectorBestKey, world);
        if (score > SaveBridge.GetInt(key, 0))
        {
            SaveBridge.SetInt(key, score);
            SaveBridge.Save();
        }
    }

    /// <summary>Best Prospector token score on a world (0 = never scored there).</summary>
    public static int GetProspectorBest(int world) => world < 0
        ? 0 : SaveBridge.GetInt(string.Format(ProspectorBestKey, world), 0);

    /// <summary>Marks a world's HARVEST as survived (its PROSPECTOR RUN now opens) and moves the world
    /// up one harvest grade. Only a cleared run records this - a failed run never advances
    /// the chain. The grade bump (2..5) is what makes a re-run field richer AND deadlier.
    /// The site counter (1 per clear, capped at 5) feeds the map's star ladder.</summary>
    public static void RecordHarvestCleared(int world)
    {
        // LUNAR TRIALS: challenge clears never touch the campaign chain - the player's world
        // grades/stars/unlocks stay exactly as the main game left them.
        if (ChallengeRunner.Active != null) return;
        if (world < 0 || world >= PlanetCount) return;
        SaveBridge.SetInt(string.Format(HarvestClearedKey, world), 1);
        int g = GetHarvestGrade(world);
        if (g < MaxHarvestGrade) SaveBridge.SetInt(string.Format(HarvestGradeKey, world), g + 1);
        int sites = GetHarvestSitesPassed(world);
        if (sites < MaxHarvestGrade) SaveBridge.SetInt(string.Format(HarvestSitesKey, world), sites + 1);
        SaveBridge.Save();
        KongregateBridge.OnProgressChanged();   // harvest star banked -> board now
        NewgroundsBridge.OnProgressChanged();   // harvest medal ladder step
    }

    /// <summary>True once this world's HARVEST has been survived.</summary>
    public static bool IsHarvestCleared(int world) => world >= 0 && world < PlanetCount
        && SaveBridge.GetInt(string.Format(HarvestClearedKey, world), 0) > 0;

    /// <summary>Marks a world's PROSPECTOR grade as cleared (its canyon descent reached the end
    /// of the lane). Its own grade and star ladder advance independently from Harvest. It also
    /// opens this world's RIDGE SURVIVAL; the next planet's harvest remains locked until Ridge.</summary>
    public static void RecordProspectorCleared(int world)
    {
        if (ChallengeRunner.Active != null) return;   // LUNAR TRIALS: no campaign side-effects
        if (world < 0 || world >= PlanetCount) return;
        int grade = GetProspectorGrade(world);
        int sites = GetProspectorSitesPassed(world);
        SaveBridge.SetInt(string.Format(ProspectorClearedKey, world), 1);
        if (grade < MaxProspectorGrade)
            SaveBridge.SetInt(string.Format(ProspectorGradeKey, world), grade + 1);
        if (sites < MaxProspectorGrade)
            SaveBridge.SetInt(string.Format(ProspectorSitesKey, world), sites + 1);
        SaveBridge.Save();
        KongregateBridge.OnProgressChanged();   // canyon survived -> chain advanced, board now
        NewgroundsBridge.OnProgressChanged();   // prospector medal ladder step
    }

    /// <summary>True once this world's PROSPECTOR RUN has been survived.</summary>
    public static bool IsProspectorCleared(int world) => world >= 0 && world < PlanetCount
        && SaveBridge.GetInt(string.Format(ProspectorClearedKey, world), 0) > 0;

    /// <summary>Current independent Prospector difficulty grade (1..5). Old saves that had
    /// already cleared Prospector inherit that first clear as grade 2.</summary>
    public static int GetProspectorGrade(int world)
    {
        if (world < 0 || world >= PlanetCount) return 1;
        int legacyDefault = IsProspectorCleared(world) ? 2 : 1;
        return Mathf.Clamp(SaveBridge.GetInt(string.Format(ProspectorGradeKey, world), legacyDefault),
            1, MaxProspectorGrade);
    }

    /// <summary>Prospector grades cleared on a world (0..5), independent of Harvest stars.</summary>
    public static int GetProspectorSitesPassed(int world)
    {
        if (world < 0 || world >= PlanetCount) return 0;
        int legacyDefault = IsProspectorCleared(world) ? 1 : 0;
        return Mathf.Clamp(SaveBridge.GetInt(string.Format(ProspectorSitesKey, world), legacyDefault),
            0, MaxProspectorGrade);
    }

    /// <summary>Marks the world's Ridge Survival course complete and unlocks the next planet.</summary>
    public static void RecordRidgeCleared(int world)
    {
        if (ChallengeRunner.Active != null) return;   // LUNAR TRIALS: no campaign side-effects
        if (world < 0 || world >= PlanetCount) return;
        SaveBridge.SetInt(string.Format(RidgeClearedKey, world), 1);
        SaveBridge.Save();
        KongregateBridge.OnProgressChanged();
    }

    /// <summary>True once the player has completed this world's Ridge Survival course.</summary>
    public static bool IsRidgeCleared(int world) => world >= 0 && world < PlanetCount
        && SaveBridge.GetInt(string.Format(RidgeClearedKey, world), 0) > 0;

    /// <summary>Opens a world's PROSPECTOR gate without advancing its harvest progress. Test-only:
    /// unlocks all modes quickly without inflating the harvest star ladder (RecordHarvestCleared
    /// bumps grade AND sites, which would fill stars the player never earned).</summary>
    public static void UnlockHarvestGate(int world)
    {
        if (world < 0 || world >= PlanetCount) return;
        SaveBridge.SetInt(string.Format(HarvestClearedKey, world), 1);
        SaveBridge.Save();
    }

    /// <summary>The harvest grade of a world (1 = fresh field). Defaults to 1 for worlds the
    /// player has never touched (older saves included).</summary>
    public static int GetHarvestGrade(int world) => world >= 0
        ? Mathf.Clamp(SaveBridge.GetInt(string.Format(HarvestGradeKey, world), 1), 1, MaxHarvestGrade)
        : 1;

    /// <summary>Ore deposits at the current Prospector grade (same reward ramp, separate grade).</summary>
    public static int ProspectorGradeOre(int world) =>
        HarvestGradeOrePerGrade * (GetProspectorGrade(world) - 1) - HarvestGradeStartTrim;

    /// <summary>How many harvest SITES have been passed on a world (0 = none, up to
    /// MaxHarvestGrade). One site is passed per cleared run - the ONLY thing that fills a
    /// harvest star. The grade (run difficulty) is deliberately NOT folded in here: legacy or
    /// stale grade states (an old save, a cloud value that survived a wipe) would otherwise
    /// light stars the player never survived.</summary>
    public static int GetHarvestSitesPassed(int world)
    {
        if (world < 0) return 0;
        return Mathf.Clamp(SaveBridge.GetInt(string.Format(HarvestSitesKey, world), 0), 0, MaxHarvestGrade);
    }

    /// <summary>Ore deposits the world's current harvest grade scatters. Grade 1 starts slightly
    /// below stock (the fresh field is lean), then climbs with a ~+30 step per grade - so the
    /// bumpy, storm-ridden re-runs are the ones that pay the most.</summary>
    public static int HarvestGradeOre(int world) => HarvestGradeOrePerGrade * (GetHarvestGrade(world) - 1) - HarvestGradeStartTrim;

    /// <summary>Extra damaging boulders each Harvest grade above 1 adds to the world's field.
    /// The build packs them into the SAME route length as grade 1 (denser, not longer), so the
    /// rocks actually sit along the driven corridor instead of marching off the terrain. Grade 5
    /// therefore runs the base field plus 4x this many rocks. Grade 1 is untouched.</summary>
    public const int HarvestGradeBouldersPerGrade = 20;

    /// <summary>Additional Harvest-grade boulders for a world (0 at grade 1).</summary>
    public static int HarvestGradeBoulders(int world) => HarvestGradeBouldersPerGrade * (GetHarvestGrade(world) - 1);

    /// <summary>Extra oxygen burned per second for each Harvest or Prospector grade above 1, as a
    /// FRACTION of the world's base drain. Grade ramps add ore and pressure; this is the air tax
    /// on top, so a re-run pays for its richer field in tank pressure as well as in
    /// effort. Deliberately slight: grade 5 burns 4 x this much extra (20% at the default), which
    /// a stocked O2 upgrade can absorb - the garage, not the grade, stays the real answer to a
    /// thin tank. Grade 1 is untouched.</summary>
    public const float HarvestGradeDrainPerGrade = 0.05f;

    /// <summary>The grade ramp on the tank, as a multiplier: 1.0 at grade 1, 1.05 at grade 2 ...
    /// 1.20 at grade 5. Applied to HARVEST only - a race runs no oxygen clock and Ridge Survival
    /// keeps its own carried balance, so neither is touched.</summary>
    public static float HarvestGradeDrainMul(int world) =>
        1f + HarvestGradeDrainPerGrade * (GetHarvestGrade(world) - 1);

    /// <summary>Oxygen drain multiplier for the independent Prospector grade.</summary>
    public static float ProspectorGradeDrainMul(int world) =>
        1f + HarvestGradeDrainPerGrade * (GetProspectorGrade(world) - 1);

    /// <summary>Base lateral spread of a HARVEST route's oxygen canisters, in metres (the
    /// half-width of the corridor they weave across either side of the route centre line).
    /// 40 m on a fresh grade-1 field keeps the first run a gentle near-straight - air comes
    /// with a small sway, not a full detour - and only the grades widen it.</summary>
    public const float HarvestPodLateralBase = 40f;

    /// <summary>How much WIDER that corridor gets per harvest grade above 1, in metres. Higher
    /// grades put the canisters further off the racing line, so reaching the air costs a longer,
    /// twistier detour instead of a straight hop - the route's answer to a bigger oxygen bill.
    /// The weave is a bounded snake around the centre line (see GameBootstrap.BuildPods), so a
    /// wider corridor can never walk the route off the generated terrain.</summary>
    public const float HarvestGradePodSpreadPerGrade = 21f;

    /// <summary>Half-width of the corridor a world's harvest route's canisters weave across,
    /// widening with the grade: 40 m at grade 1, 61 m at grade 2 ... 124 m at grade 5.</summary>
    public static float HarvestPodLateral(int world) =>
        HarvestPodLateralBase + HarvestGradePodSpreadPerGrade * (GetHarvestGrade(world) - 1);

    /// <summary>The one-time first-clear token bonus for a world's harvest (the Moon only for now).</summary>
    public static int FirstClearBonus(int world) => world == 0 ? FirstClearBonusAmount : 0;

    /// <summary>Moves on to the next planet, wrapping back to the Moon after Titan.</summary>
    public static void AdvanceLevel()
    {
        CurrentLevelIndex = (CurrentLevelIndex + 1) % Planets.Length;
    }
}
