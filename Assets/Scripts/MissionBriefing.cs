using UnityEngine;

/// <summary>
/// Per-level mission briefing text shown on the pre-deploy screen below the hazard columns.
/// One entry for every one of the 15 levels - 5 worlds x Survey / Canyon / Ridge. Keys are
/// "&lt;PLANET&gt;|&lt;STAGE&gt;" where STAGE is SURVEY, CANYON or RIDGE (planet names match
/// Levels.cs / CourseHazards). Copy is written to fit the briefing panel at 24px with wrapping.
/// </summary>
public static class MissionBriefing
{
    private enum Stage { SURVEY, CANYON, RIDGE }

    private static readonly string SURVEY = Stage.SURVEY.ToString();
    private static readonly string CANYON = Stage.CANYON.ToString();
    private static readonly string RIDGE = Stage.RIDGE.ToString();

    private static readonly System.Collections.Generic.Dictionary<string, string> ByKey =
        new System.Collections.Generic.Dictionary<string, string>
        {
            // ---------------------------------------------------------------- THE MOON
            { "THE MOON|SURVEY",  "Survey the Moon's cratered plains for helium-3. Watch your oxygen, mine fast, and keep an eye on the sky: meteor storms sweep the field without warning." },
            { "THE MOON|CANYON",  "Descend the Moon's dark rim canyons for helium-3. Mine what your headlights reach and watch your oxygen - the walls close in quickly and the route is easy to lose." },
            { "THE MOON|RIDGE",   "Survive the Moon's unstable ridge crests. Hold the line through meteor storms and collapsing flanks, and beat the target distances." },

            // ------------------------------------------------------------------ MARS
            { "MARS|SURVEY",      "Scout Mars for iron oxide ore. Push through dust season, meteor storms and roving dust devils that shove the buggy off line." },
            { "MARS|CANYON",      "Work Mars' deep canyon floor in the dark. Heavy rock falls and a far harder line: every ore pocket must be earned before the tank runs dry." },
            { "MARS|RIDGE",       "Bigger landslides, a rougher crest. Ride the Mars ridge and beat the distances for tokens." },

            // ----------------------------------------------------------------- VENUS
            { "VENUS|SURVEY",     "Endure the crushing heat and pressure that chip the stock hull 1% every two seconds. Mine silicon fast and ride out the side gusts over open ground." },
            { "VENUS|CANYON",     "Descend the Venus canyon in the dark. Lava spills along the base, acid falls from above, and hull pressure runs hot around the bends." },
            { "VENUS|RIDGE",      "Pass the lava surges, outlast the acid rain, and hold the line as gusts sweep across the ridge." },

            // --------------------------------------------------------------------- IO
            { "IO|SURVEY",        "Sulphur vents and mini volcanoes line the route, meteor storms cross the plains, magma pools sit off the line - and a pale sulphur geyser pool flings you skyward if you cross its centre." },
            { "IO|CANYON",        "Descend the Io canyon in the dark. Lava surges ramp up, overhanging cliffs shed rocks, and the eclipse steals both the light and the grip." },
            { "IO|RIDGE",         "Ride the lava surges to the line. Watch the crest behind you: a molten avalanche rolls in from behind and chases you down the ridge. Eclipses and the low-grip sulphur ice keep the ground nasty." },

            // ------------------------------------------------------------------ TITAN
            { "TITAN|SURVEY",     "Methane snow drifts in from the polar plains, meteor storms break through the haze, and pale methane geyser pools dot the plains - crossing one flings you skyward. Darker methane mud flats grab the wheels and swallow your pace." },
            { "TITAN|CANYON",     "Descend the Titan canyon in the dark. Deep cold and methane snow chew the hull, muddy banks slow you down, and the haze swallows the headlight beam." },
            { "TITAN|RIDGE",      "Dodge the ice streams, survive the snow and mud, and ride out the wind gusts along the ridge." },
        };

    /// <summary>Briefing text for the level about to run (never null; unknown keys get a
    /// neutral waiting-for-brief line so the screen still reads cleanly).</summary>
    public static string For(GameMode mode, RaceCourse course, string planetName, string harvestableName)
    {
        string stage;
        if (mode == GameMode.Survival) stage = RIDGE;
        else if (mode == GameMode.Prospector) stage = CANYON;
        else if (mode != GameMode.Race) stage = SURVEY;
        else stage = (course == RaceCourse.Ridge ? RIDGE : (course == RaceCourse.Canyon ? CANYON : SURVEY));

        string key = planetName + "|" + stage;
        string text;
        if (ByKey.TryGetValue(key, out text)) return text;
        return "BRIEFING PLACEHOLDER: write the " + planetName + " " + stage.ToLower() + " brief in MissionBriefing.cs.";
    }
}