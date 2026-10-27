using System.Collections.Generic;

/// <summary>
/// One short, focused challenge level for the Lunar Trials edition: a single Moon venue, a
/// concrete objective, a hazard profile and an unlock slot - the Trials edition's
/// replacement for the world/grade campaign. Data only; gameplay systems (terrain gen,
/// oxygen, ore, landing pad, end cinematic) are shared with the main game and
/// parameterised from here by ChallengeRunner (to come).
///
/// Medal thresholds are COMPLETION TIME in seconds (the trials-genre scoring: Gold/Silver/
/// Bronze by how fast the challenge is finished). 0 disables a tier; challenges intended
/// to be scored purely by completion can leave all three at 0.
/// </summary>
public class ChallengeDef
{
    public enum Mode { Harvester, Prospector, Ridge, Race }
    public enum Profile { Calm, MeteorShower, BoulderField, EclipseDark, Ice, IceAndMeteors, Landslide }

    public readonly string id;
    public readonly string title;
    public readonly string brief;          // one-line objective for the select screen + briefing
    public readonly Mode mode;
    public readonly Profile profile;
    public readonly int oreTarget;         // nuggets that must be banked (Harvester/Prospector)
    public readonly int fieldSize;         // nuggets actually scattered (>= oreTarget)
    public readonly int canisterCount;     // mid-route O2 canisters (0 = none)
    public readonly int timeLimitSeconds;  // 0 = untimed
    public readonly int boulderCount;      // static/moving rock obstacles to weave
    public readonly int tokenReward;       // paid once on first clear
    public readonly string requiresId;     // challenge that must be cleared first ("" = none)
    public readonly int medalGold;         // completion-time medal tiers, seconds
    public readonly int medalSilver;
    public readonly int medalBronze;
    public readonly int seed;              // fixed generation seed: every attempt lays the
                                           // challenge out identically, so retrying is skill
    public readonly int oreBowls;          // designed siting: number of crater bowls holding
                                           // the field (0 = auto-spread across the arena)
    public readonly int oreBearing;        // designed siting: preferred bearing (degrees from
                                           // spawn heading) for the bowls
    public readonly int courseLength;      // metres of tyre-lined course run (0 = no course -
                                           // free-arena siting as before)
    public readonly float rampAtS;         // jump ramp ON the course line at this progress
                                           // (0..1); -1 = no ramp

    public ChallengeDef(string id, string title, string brief, Mode mode, Profile profile,
        int oreTarget, int fieldSize, int canisterCount, int timeLimitSeconds,
        int boulderCount, int tokenReward, string requiresId,
        int medalGold = 0, int medalSilver = 0, int medalBronze = 0, int seed = 0,
        int oreBowls = 0, int oreBearing = 0, int courseLength = 0, float rampAtS = -1f)
    {
        this.id = id;
        this.title = title;
        this.brief = brief;
        this.mode = mode;
        this.profile = profile;
        this.oreTarget = oreTarget;
        this.fieldSize = fieldSize;
        this.canisterCount = canisterCount;
        this.timeLimitSeconds = timeLimitSeconds;
        this.boulderCount = boulderCount;
        this.tokenReward = tokenReward;
        this.requiresId = requiresId;
        this.medalGold = medalGold;
        this.medalSilver = medalSilver;
        this.medalBronze = medalBronze;
        this.seed = seed;
        this.oreBowls = oreBowls;
        this.oreBearing = oreBearing;
        this.courseLength = courseLength;
        this.rampAtS = rampAtS;
    }

    /// <summary>"GOLD" / "SILVER" / "BRONZE" for the elapsed seconds, or "" when the
    /// challenge has no medal tiers configured (or the time misses all three).</summary>
    public string MedalFor(float elapsedSeconds)
    {
        if (medalGold <= 0 && medalSilver <= 0 && medalBronze <= 0) return "";
        if (medalGold > 0 && elapsedSeconds <= medalGold) return "GOLD";
        if (medalSilver > 0 && elapsedSeconds <= medalSilver) return "SILVER";
        if (medalBronze > 0 && elapsedSeconds <= medalBronze) return "BRONZE";
        return "";
    }
}

/// <summary>
/// The ordered challenge ladder for the Lunar Trials edition: a linear unlock chain of
/// short Moon trials across all four modes (harvester, dark prospector, ice, ridge, race).
/// The regular game's Levels/LevelManager campaign is untouched - this list only drives the
/// Trials edition's select screen and ChallengeRunner. More prospector and high-ore
/// harvester challenges, plus ramp/rock-wall/climbable-rock obstacle levels, are planned
/// additions (see LunarTrials/DESIGN.md).
/// </summary>
public static class ChallengePack
{
    public static readonly List<ChallengeDef> All = new List<ChallengeDef>
    {
        new ChallengeDef("first-trax", "FIRST TRAX",
            "Follow the tyre line, scoop the 2 ore on the way and cross into the landing zone at its end.",
            ChallengeDef.Mode.Harvester,
            ChallengeDef.Profile.Calm, 2, 2, 1, 0, 6, 50, "", 45, 60, 90, 9101, 0, 0, 420),

        new ChallengeDef("crater-bowl", "CRATER BOWL RUN",
            "Follow the line, load up on 10 ore - and try the ramp - into the landing zone.",
            ChallengeDef.Mode.Harvester,
            ChallengeDef.Profile.Calm, 10, 12, 1, 0, 2, 75, "first-trax", 70, 90, 120, 9102,
            0, 0, 520, 0.5f),

        new ChallengeDef("rim-ride", "RIM RIDE",
            "8 ore strung along a narrow crater rim. Do not fall.", ChallengeDef.Mode.Harvester,
            ChallengeDef.Profile.Calm, 8, 10, 0, 0, 0, 100, "crater-bowl", 60, 80, 110, 9103),

        new ChallengeDef("meteor-rain", "METEOR RAIN",
            "8 ore while the sky falls. Keep moving.", ChallengeDef.Mode.Harvester,
            ChallengeDef.Profile.MeteorShower, 8, 11, 1, 0, 1, 125, "rim-ride", 60, 80, 110, 9104),

        new ChallengeDef("fuel-critical", "FUEL CRITICAL",
            "One tank, one long traverse. Bank 10 ore before the oxygen runs out.",
            ChallengeDef.Mode.Harvester, ChallengeDef.Profile.Calm, 10, 14, 0, 0, 2, 150,
            "meteor-rain", 90, 115, 150, 9105),

        new ChallengeDef("blackout-bowl", "BLACKOUT BOWL",
            "Eclipse dark. 50% of the field, sonar only.", ChallengeDef.Mode.Prospector,
            ChallengeDef.Profile.EclipseDark, 6, 12, 1, 0, 1, 175, "fuel-critical", 70, 95, 130, 9106),

        new ChallengeDef("deep-dark-descent", "DEEP DARK DESCENT",
            "Dark, narrow, boulder-strewn. 60% quota under time.", ChallengeDef.Mode.Prospector,
            ChallengeDef.Profile.EclipseDark, 9, 15, 1, 240, 4, 200, "blackout-bowl", 150, 190, 230, 9107),

        new ChallengeDef("frozen-crossing", "FROZEN CROSSING",
            "Glazed ground. Slalom the pressure ridges to the pad.", ChallengeDef.Mode.Harvester,
            ChallengeDef.Profile.Ice, 8, 10, 1, 0, 3, 225, "deep-dark-descent", 75, 100, 135, 9108),

        new ChallengeDef("ice-cap-sprint", "ICE CAP SPRINT",
            "8 ore under time on ice - while it rains meteors.", ChallengeDef.Mode.Harvester,
            ChallengeDef.Profile.IceAndMeteors, 8, 10, 1, 150, 2, 250, "frozen-crossing", 90, 115, 150, 9109),

        new ChallengeDef("crest-apprentice", "CREST APPRENTICE",
            "Ride the crest 400 m. Landslides are live.", ChallengeDef.Mode.Ridge,
            ChallengeDef.Profile.Landslide, 0, 0, 1, 0, 0, 275, "ice-cap-sprint", 50, 70, 100, 9110),

        new ChallengeDef("crest-master", "CREST MASTER",
            "700 m of crest. Tighter line, faster rockfall.", ChallengeDef.Mode.Ridge,
            ChallengeDef.Profile.Landslide, 0, 0, 1, 0, 0, 300, "crest-apprentice", 80, 105, 140, 9111),

        new ChallengeDef("canyon-duel", "CANYON DUEL",
            "One rival. One short canyon. Beat them through every gate.",
            ChallengeDef.Mode.Race, ChallengeDef.Profile.Calm, 0, 0, 0, 0, 0, 350,
            "crest-master", 60, 75, 95, 9112),
    };

    /// <summary>The challenge a given id unlocks (or null at the end of the chain).</summary>
    public static ChallengeDef NextAfter(string id)
    {
        for (int i = 0; i < All.Count - 1; i++)
            if (All[i].id == id) return All[i + 1];
        return null;
    }

    /// <summary>True when the challenge's requirement is cleared (or has none).</summary>
    public static bool IsUnlocked(ChallengeDef def, HashSet<string> clearedIds)
    {
        if (def == null) return false;
        if (string.IsNullOrEmpty(def.requiresId)) return true;
        return clearedIds != null && clearedIds.Contains(def.requiresId);
    }
}


