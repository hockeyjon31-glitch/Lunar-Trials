/// <summary>
/// Central Newgrounds.io identifiers for Lunar Buggy.
///
/// The App ID and AES-128 encryption key are configured. Each Medal / Score Board you create on
/// your project's "API Tools" page (https://www.newgrounds.com/projects/games/&lt;id&gt;/api) is
/// assigned its own numeric ID - paste those in below as you create them.
///
/// Any medal or board left at 0 is treated as "not configured" and skipped silently at runtime,
/// so the game builds and ships safely before the IDs exist - nothing is posted and no errors
/// are thrown. Once an ID is filled in, the matching award starts working on the next run with
/// no other change (secret awards that were earned while unconfigured are remembered locally and
/// flushed the moment their medal gets an ID).
/// </summary>
public static class NewgroundsConfig
{
    // -------------------------------------------------------------------- app identity
    public const string AppID   = "62978:a6Kvq5Yc";             // "<numeric id>:<app key>"
    public const string AesKey  = "2SnW/DF2Uh8K7yv2EKvgBg==";   // Base64 AES-128 key
    public const string Version = "1.0.0";          // optional X.X.X build version

    // -------------------------------------------------------------------- medals
    // Ridge Survival ladder (grade index 0 Bronze .. 3 Platinum).
    public const int MedalRidgeRookie    = 93277;   // Bronze+ on any world
    public const int MedalRidgeRunner    = 93278;   // Gold on any world
    public const int MedalRidgeRaider    = 93279;   // Bronze+ on every one of the five worlds
    public const int MedalRidgeLegend    = 93280;   // Platinum on any world
    public const int MedalRidgeAscendant = 93276;   // Platinum on every one of the five worlds

    // Harvest ladder - one medal per world. Earned by clearing GRADE (world + 1) on that world:
    // the Moon pays at grade 1, Mars at grade 2 ... Titan at grade 5. Since a world's grade is
    // the number of clears + 1, that is 1/2/3/4/5 clear(s) on Moon/Mars/Venus/Io/Titan.
    // NOT CREATED YET on Newgrounds (out of medal points) - left at 0, so they are skipped
    // silently. Fill these in once the medals exist and they start unlocking on the next run.
    public const int MedalLunarHarvester   = 0;  // world 0 - Moon,  grade 1
    public const int MedalMarsHarvester    = 0;  // world 1 - Mars,  grade 2
    public const int MedalVenusHarvester   = 0;  // world 2 - Venus, grade 3
    public const int MedalIoHarvester      = 0;  // world 3 - Io,    grade 4
    public const int MedalHarvestMoonTitan = 0;  // world 4 - Titan, grade 5

    // Prospector ladder - one medal per world (world 0 is covered by the generic FIRST CLAIM).
    // Same grade rule as Harvest: clear GRADE (world + 1) on that world.
    public const int MedalFirstClaim      = 93281;  // any world, grade 1
    public const int MedalMarsProspector  = 93282;  // world 1 - Mars,  grade 2
    public const int MedalVenusProspector = 93284;  // world 2 - Venus, grade 3
    public const int MedalIoProspector    = 93285;  // world 3 - Io,    grade 4
    public const int MedalTitanProspector = 93283;  // world 4 - Titan, grade 5

    // Per-world lookups used by the bridge; index == world index. World 0 has no Prospector medal.
    public static readonly int[] HarvestMedalsByWorld =
        { MedalLunarHarvester, MedalMarsHarvester, MedalVenusHarvester, MedalIoHarvester, MedalHarvestMoonTitan };
    public static readonly int[] ProspectorMedalsByWorld =
        { 0, MedalMarsProspector, MedalVenusProspector, MedalIoProspector, MedalTitanProspector };

    // Secret medals (ascending IDs from Prospector Perfection).
    /// <summary>Haul back every nugget on a Prospector run (100% ore).</summary>
    public const int MedalProspectorPerfection = 93267;
    /// <summary>Collect 80% of the ore on a Harvester world.</summary>
    public const int MedalHarvestingHero = 93268;
    /// <summary>Reach the landing pad with zero oxygen.</summary>
    public const int MedalLastGasp = 93269;
    /// <summary>Reach the landing pad with under 5% hull.</summary>
    public const int MedalRaggedEnd = 93270;

    // -------------------------------------------------------------------- score boards
    // Index 0..4 == world 0..4 (Moon, Mars, Venus, Io, Titan).
    public static readonly int[] HarvestBoards  = { 16341, 16342, 16343, 16344, 16345 };   // best Harvester token haul
    public static readonly int[] SurvivalBoards = { 16346, 16347, 16348, 16349, 16350 };   // best Ridge distance (metres)

    /// <summary>An ID only counts as configured once it has a positive value.</summary>
    public static bool Configured(int id) => id > 0;
}
