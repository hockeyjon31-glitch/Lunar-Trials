using UnityEngine;

/// <summary>
/// The per-planet hazard identity used by Lunar Race. Each theme picks which course
/// hazards the RaceHazardDirector schedules (meteor/rock/landslide, lava, rain or lakes),
/// so adding a future world is a map entry plus the director knobs.
/// </summary>
public enum PlanetTheme
{
    None,   // unknown world: no course hazards
    Lunar,  // the Moon: meteor barrages + canyon boulders / ridge collapses (baseline)
    Mars,   // Mars: rock falls + land slides + meteor storms + roaming dust devils, ramped a bit
    Venus,  // Venus: lava into the canyon base + acid rain + strong persistent side gusts (heaviest on the ridge)
    Io,     // Io: heavier lava surges + overhanging rock falls + a molten avalanche down the ridge crest (Venus theme, ramped)
    Titan,  // Titan: liquid methane lakes, methane snow (freezing cold) + canyon overhead rocks; survey swaps the old vents for drive-over methane geyser pools
}

/// <summary>Registry mapping planet display names to their race hazard theme.</summary>
public static class CourseHazards
{
    public static PlanetTheme ThemeFor(string planetName)
    {
        switch (planetName)
        {
            case "THE MOON": return PlanetTheme.Lunar;
            case "MARS": return PlanetTheme.Mars;
            case "VENUS": return PlanetTheme.Venus;
            case "IO": return PlanetTheme.Io;
            case "TITAN": return PlanetTheme.Titan;
            default: return PlanetTheme.None;
        }
    }

    /// <summary>True when the theme's Director drives the meteor storm (storm is created).</summary>
    public static bool UsesMeteorStorms(PlanetTheme theme) =>
        theme == PlanetTheme.Lunar || theme == PlanetTheme.Mars;

    /// <summary>True while this world's crushing atmosphere chips the hull every second it
    /// runs (Venus, in every mode). The pressure perk is a second clock on top of the hazard
    /// weather, so the run is a race against the planet itself.</summary>
    public static bool HasCrushingPressure(string planetName) => planetName == "VENUS";

    /// <summary>
    /// The dangers of the ACTUAL level about to run, shown on the level start briefing screen
    /// (never the menus). Matches exactly what the builders spawn:
    ///  - Harvester keeps only what the declared <see cref="LevelDefinition.danger"/> triggers
    ///    (meteor storms on Moon/Mars AND Io/Titan; Venus keeps its own atmospheric peril).
    ///  - Race is course-aware: Canyon = meteor + boulder rock falls, Ridge = meteor + land
    ///    falls (collapse / landslide); Venus/Io/Titan hazards are the same on both courses.
    /// </summary>
    public static string[] DangersFor(GameMode mode, RaceCourse course, string planetName)
    {
        if (mode == GameMode.Prospector && (planetName == "THE MOON" || planetName == "MARS"))
        {
            return new[] { "Meteor storms", "Canyon wall rockfalls" };
        }

        if (mode != GameMode.Race && mode != GameMode.Survival)
        {
// Update: harvest worlds keep their declared weather (Moon/Mars/Io/Titan meteors, Venus
            // pressure), and Io/Titan also run survey features - volcanic vents and magma pools,
            // methane lakes - which are their own active hazards.
            switch (planetName)
            {
                // Harvest runs only trigger a weather system the level explicitly declares.
                case "THE MOON":
                    return new[] { "Meteor storms" };
                case "MARS":
                    return new[] { "Meteor storms", "Dust devils scour the plains" };
                case "VENUS":
                    return new[] { "Crushing atmosphere and heat damage the hull over time", "Side gusts nudge the rover off line" };
                case "IO":
                    return new[] { "Meteor storms", "Volcanic vents and magma pools off the canister path" };
                case "TITAN":
                    return new[] { "Meteor storms", "Freezing methane snow blizzards", "Methane geyser pools", "Liquid methane lakes", "Methane mud flats" };
                // Venus apart, harvest worlds with no declared danger are instrument calm.
                default:
                    return new[] { "No active hazards - calm survey weather" };
            }
        }

        bool ridge = course == RaceCourse.Ridge;
        bool canyon = course == RaceCourse.Canyon;
        switch (planetName)
        {
            case "THE MOON":
            case "MARS":
                // Same weather as the Canyon, but the course hazard swaps: rock falls off the
                // canyon walls vs the ridge's collapsing flanks (land falls).
                return ridge
                    ? new[] { "Meteor storms", "Land falls" }
                    : new[] { "Meteor storms", "Boulder falls" };
            case "VENUS":
                // The canyon adds gravity-assisted boulder falls off the walls to the solar
                // pressure, lava flows and acid rain; the Ridge keeps the pure weather mix.
                return canyon
                    ? new[] { "Crushing atmosphere and heat damage the hull", "Boulder falls", "Acid rain falls from the sky", "Lava flows" }
                    : new[] { "Crushing atmosphere and heat damage the hull", "Acid rain falls from the sky", "Lava flows" };
            case "IO":
                // Io hits with lava surges AND the course's own rock falls - overhanging rocks
                // raining straight down on the canyon lane, land slides shearing the Ridge's
                // flanks. Io never rains acid: on the Ridge a molten avalanche rolls in from
                // behind and chases the buggy down the crest.
                return ridge
                    ? new[] { "Lava surges", "Ridge land falls", "Lava avalanche chases you down the crest", "Icy eclipses" }
                    : new[] { "Lava surges", "Overhanging rock falls" };
            case "TITAN":
                // Titan's precipitation is methane SNOW - freezing cold is the damage, not acid.
                return ridge
                    ? new[] { "Freezing methane snow", "Icy patches on the crest", "Methane mud flats" }
                    : new[] { "Freezing methane snow", "Overhanging rock falls", "Land slides", "Icy lake hazards", "Methane mud flats" };
            default:
                return new[] { "Unknown world - no hazards on record" };
        }
    }
}