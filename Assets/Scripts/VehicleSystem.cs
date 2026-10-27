using UnityEngine;

/// <summary>
/// The garage's vehicle bays: the stock Lunar Buggy (a balanced all-rounder you always own) and
/// the variants the SHOP sells, starting with the Mars Rover. A vehicle is a bundle of stat
/// MULTIPLIERS applied ON TOP of the garage upgrades, plus the 0-10 bar values the shop shows
/// (so the readout and the driving match). Choosing one changes how every run feels:
///   Lunar Buggy - all multipliers identity; the honest middle of every dial.
///   Mars Rover  - slower off the mark and down the straights, a weaker hop, and a slightly
///                 weaker boost sprint, but a rugged hull, a ~2x gather blade and a drain
///                 the airlock barely notices. Built for harvesting, not for racing.
/// Vehicles unlock as the campaign reaches their home world (Mars Rover at the first Mars gate)
/// and cost tokens in the shop. Whatever is equipped is used by Harvester, Race and Ridge
/// Survival alike (survival is still the stock 'fair' set - the buggy's identity multipliers
/// deliberately leave it untouched). Selection and ownership persist in PlayerPrefs like the
/// wallet. Each vehicle has its own procedural body and skin set: LunarRoverArt for the buggy
/// (Historic preset), MarsRoverArt NasaClassic for the rover. SkinIndex feeds the art's own
/// SkinOf lookup in the builders so in-game and showroom match.
/// </summary>
public static class VehicleSystem
{
    // ------------------------------------------------------------------ definitions
    /// <summary>One purchasable vehicle: identity multipliers for gameplay + the shop's bars.</summary>
    public sealed class Vehicle
    {
        public string id;
        public string displayName;
        public string blurb;
        /// <summary>World index whose unlock gates this vehicle in the shop (-1 = always open).</summary>
        public int unlockWorld = -1;
        /// <summary>When true this vehicle ignores its world gate and instead opens once the
        /// player has reached GOLD (or better) on ANY world's RIDGE SURVIVAL - the same run that
        /// awards the Ridge Runner medal. No single store is written on exactly that event the way
        /// the retired canyon-race gate was (Race is a dormant mode whose grade is never recorded
        /// now), so <see cref="IsUnlocked"/> scans every world's best ridge grade and opens on the
        /// first that is GOLD or higher (grade index 2; see GameFlow.RidgeGradeNames).</summary>
        public bool unlockOnRidgeGold;
        /// <summary>Token price in the shop (0 = never buyable, already owned).</summary>
        public int cost;

        // Shop stat bars, 1..10 (higher = better).
        public int speed, boost, jump, oxygen, scoop, hull;

        // Gameplay multipliers, applied on top of garage upgrades (buggy = all 1).
        public float torqueMul = 1f;
        public float boostMul = 1f;
        public float speedMul = 1f;
        public float drainMul = 1f;
        public float hullMul = 1f;
        public float hullDamageMul = 1f;
        public float scoopMul = 1f;
        public float jumpMul = 1f;
        /// <summary>Tyre grip multiplier, applied to the base WheelCollider stiffness. Kept as a
        /// hook for per-vehicle rubber, but every vehicle now runs the same stock bite (1): the
        /// handling is tuned around one shared baseline so the dynamics read the same in any
        /// chassis, and the handbrake (not loose tyres) is what breaks traction. A future slide /
        /// grip upgrade can lean on this again.</summary>
        public float gripMul = 1f;
        /// <summary>Multiplier on Venus lightning hull damage. 1 = takes the storm's full hit.</summary>
        public float lightningDamageMul = 1f;
        /// <summary>Multiplier on the lightning hard-stall duration. 0 = a bolt never stops it.</summary>
        public float lightningStallMul = 1f;
        /// <summary>How hard the wingsail can drive the vehicle (acceleration scale). 0 = no rig
        /// fitted, and the vehicle relies on wheels alone.</summary>
        public float windDriveMul;

        /// <summary>How much of a mud bog this vehicle shrugs off, 0-1. 0 bogs down like any
        /// other; 1 means the slurry barely slows it. Lifted toward 1 for a mud flat and toward
        /// 0 for a clean surface, so it blends a flat's own grip and speed caps.</summary>
        public float mudResist;
        /// <summary>How much of an ice freeze this vehicle shrugs off, 0-1. 0 takes the full hold
        /// (no wheels, vague steering, useless brakes); 1 keeps driving through it. Scales the
        /// glaze the surface imposes, so a partial value still costs something.</summary>
        public float iceResist;

        /// <summary>How much of a lateral WIND GUST this vehicle shrugs off, 0-1. 0 takes the
        /// full shove; 1 is all but unmoved by one. High for a craft actually built to ride the
        /// wind (the Favonius), so a gale that can upset a normal buggy barely rocks it.</summary>
        public float windResist;

        /// <summary>Rigidbody centre-of-mass height (local Y) the controller applies at spawn.
        /// Lower = harder to roll. Every wheeled rover shares the stock -0.15; the Favonius carries
        /// its mass lower still (-0.5) because the tall wingsail leaves it far more easily upset in
        /// a turn, so the body is ballasted down to keep it on its wheels.</summary>
        public float comHeight = -0.15f;

        public int Stat(int index)
        {
            switch (index)
            {
                case 0: return speed;
                case 1: return boost;
                case 2: return jump;
                case 3: return oxygen;
                case 4: return scoop;
                default: return hull;
            }
        }
    }

    public static readonly Vehicle LunarBuggy = new Vehicle
    {
        id = "buggy",
        displayName = "LUNAR BUGGY",
        blurb = "The trusty Apollo LRV. A good all-rounder - no weakness, no standout, "
            + "just the same dials you have always driven.",
        unlockWorld = -1,
        cost = 0,
        speed = 6, boost = 6, jump = 6, oxygen = 5, scoop = 5, hull = 5,
        torqueMul = 1f, boostMul = 1f, speedMul = 1f, drainMul = 1f,
        hullMul = 1f, hullDamageMul = 1f, scoopMul = 1f, jumpMul = 1f,
    };

    public static readonly Vehicle MarsRover = new Vehicle
    {
        id = "rover",
        displayName = "MARS ROVER",
        blurb = "A heavy six-wheeler prospector. Slowest thing in the bay and with a weaker hop "
            + "and boost, but a rugged hull, a wide gather blade and an oxygen drain the airlock "
            + "barely notices - it simply keeps working where the lighter buggies are already "
            + "nursing damage. The hill country choice.",
        unlockWorld = 1,   // first Mars gate
        cost = 500,
        speed = 4, boost = 5, jump = 3, oxygen = 9, scoop = 8, hull = 7,
        torqueMul = 0.85f, boostMul = 0.9f, speedMul = 0.80f, drainMul = 0.5f,
        hullMul = 1.35f, hullDamageMul = 0.7f, scoopMul = 2f, jumpMul = 0.55f,
        gripMul = 1f,     // uniform stock grip across the bay; the rover's edge is its torque,
                           // hull and scoop, not its tyres (see the gripMul field notes)
    };

    /// <summary>The sprint buggy: the LRV re-proportioned for speed rather than utility. A longer
    /// nose, a low wraparound screen, side skirts and a rear wing, in the dormant Artemis white
    /// and gold. Vastly quicker and the strongest boost in the bay, but it pays for all of it -
    /// a thin hull and a gather blade so small it barely qualifies as one. Its tyres carry the
    /// stock grip the whole bay shares, and its light, stiff suspension keeps them biting, so all
    /// that speed actually holds the line. Built for the
    /// racing line; miserable in the harvest field.</summary>
    public static readonly Vehicle LunarSprint = new Vehicle
    {
        id = "lunar_sprint",
        displayName = "TRIALS BIKE",
        blurb = "The Apollo frame rebuilt as a racer. Hardest launch and the strongest boost "
            + "in the bay, and it holds a hard corner under that power - but the hull is thin and "
            + "the scoop is all but absent. A fast way to lose a harvest run. "
            + "Its light suspension does skate the mud and keep the wheels biting on ice though, "
            + "so the Titan ridge and the Moon and Mars canyon races suit it far better than "
            + "their mud and ice would suggest.",
        unlockWorld = 0,        // context only: the gate below is the medal, not the world
        unlockOnRidgeGold = true, // gold on any world's ridge survival (the Ridge Runner run)
        cost = 500,
        speed = 10, boost = 10, jump = 5, oxygen = 5, scoop = 2, hull = 3,
        torqueMul = 1.45f, boostMul = 1.6f, speedMul = 1.3f, drainMul = 1f,
        hullMul = 0.65f, hullDamageMul = 1.5f, scoopMul = 0.3f, jumpMul = 0.95f,
        gripMul = 1.35f,  // the sports touch: strong bite (only the rover's 1.5 is stronger), so
                           // the highest-speed chassis in the bay still holds a hard corner
        // The sprint's one real environmental edge. A light racer frame skates the Titan slurry
        // instead of wallowing in it, and on the ice its short wheelbase and stiff suspension keep
        // the wheels biting. This is what makes the buggy worth taking to the Titan ridge race and
        // to the Moon and Mars canyon races, which all carry patches - it trades the thin hull and
        // weak scoop for a suspension that shrugs off bad ground.
        mudResist = 0.65f,
        iceResist = 0.6f,
    };

    /// <summary>The Favonius (the Roman wind god): a landsail rover for the cloud world. Its
    /// wheels are the least capable thing about it - what actually moves it is a 12 m^2 rigid
    /// wingsail reading the supercritical CO2, which is dense enough to turn a gentle breeze
    /// into real thrust. Tuned to its home world: the titanium body ignores the crushing
    /// atmosphere that bleeds every other hull, shrugs off the thunderhead's bolts and stall,
    /// and carries a wide gather blade just short of the prospector's. Slow on the flat, and
    /// the only vehicle that wants a headwind.</summary>
    public static readonly Vehicle VenusSail = new Vehicle
    {
        id = "venus_sail",
        displayName = "FAVONIUS",
        blurb = "A landsail rover. The wheels are the least of it - a 12 square metre rigid "
            + "wingsail in a 65 kg/m3 atmosphere does the driving, and it wants a headwind. "
            + "A hull the crushing atmosphere can't bleed and the thunderhead can't stop, and "
            + "a wide gather blade. Slow on the flat, at home in a squall.",
        // Venus. IsUnlocked reads the world's HARVEST, so this opens the moment Venus Harvest
        // does - i.e. on beating Bronze on the MOON RIDGE.
        unlockWorld = 2,
        cost = 750,
        // Speed and boost sit a notch below the buggy on purpose. The wind is what makes this
        // quick - on Venus the breeze is the engine, so a modest motor is all it should need.
        speed = 4, boost = 4, jump = 4, oxygen = 8, scoop = 7, hull = 8,
        torqueMul = 0.95f, boostMul = 0.85f, speedMul = 0.9f, drainMul = 0.7f,
        hullMul = 1.5f, hullDamageMul = 0.6f, scoopMul = 1.4f, jumpMul = 0.8f,
        gripMul = 1f,
        lightningDamageMul = 0.2f,   // minor chip, not a wound
        lightningStallMul = 0f,       // grounded titanium: a bolt never stops it
        windDriveMul = 1f,
        // Built to ride the wind, so it takes the weather far better than the wheeled rovers:
        // the gust shove is cut to 40% (see WindGusts), which is what keeps a Venus squall from
        // tipping a tall wingsail over sideways. The forward sail thrust is untouched - this only
        // tames the sideways slam.
        windResist = 0.6f,
        // The tall wingsail leaves the Favonius far more easily rolled in a turn than a wheeled
        // rover, so its mass is carried low (well below the stock -0.15) to keep the wheels down.
        comHeight = -0.5f,
    };

    public static Vehicle[] All => GameEdition.IsTrials
        ? new[] { LunarBuggy, LunarSprint }                              // Trials: lunar two
        : new[] { LunarBuggy, MarsRover, LunarSprint, VenusSail };

    public static Vehicle Of(string id)
    {
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i].id == id) return All[i];
        }
        return LunarBuggy;
    }

    // ------------------------------------------------------------------ persistence
    private const string SelectedKey = "lunar.vehicle.selected";
    private static string OwnKey(string id) => "lunar.vehicle.own." + id;

    /// <summary>Every save key this system writes - the equipped choice plus one ownership flag
    /// per vehicle. Exposed so the local-to-cloud migration can enumerate them; PlayerPrefs has
    /// no key iterator, so the list has to be spelled out somewhere.</summary>
    public static string[] SaveKeys
    {
        get
        {
            Vehicle[] all = All;
            string[] keys = new string[all.Length + 1];
            keys[0] = SelectedKey;
            for (int i = 0; i < all.Length; i++) keys[i + 1] = OwnKey(all[i].id);
            return keys;
        }
    }

    public static Vehicle Selected
    {
        get
        {
            string id = SaveBridge.GetString(SelectedKey, LunarBuggy.id);
            return IsOwned(id) ? Of(id) : LunarBuggy;
        }
    }

    public static bool IsRover => Selected.id == MarsRover.id;

    /// <summary>True for the speed-built Lunar variant, which shares the buggy's art class but
    /// builds a different body and wears the dormant Artemis livery.</summary>
    public static bool IsSprint => Selected.id == LunarSprint.id;

    /// <summary>True for the Venus landsail rover, which carries a wingsail rig and shrugs off
    /// the storm.</summary>
    public static bool IsSail => Selected.id == VenusSail.id;

    public static bool IsEquipped(Vehicle v) => v != null && Selected.id == v.id;

    public static bool IsOwned(string id)
    {
        if (id == LunarBuggy.id) return true;
        // LUNAR TRIALS: the Trials Bike is flagged unlocked for now (unlock rule TBD).
        if (GameEdition.IsTrials && id == LunarSprint.id) return true;
        return SaveBridge.GetInt(OwnKey(id), 0) == 1;
    }

    public static bool IsOwned(Vehicle v) => v != null && IsOwned(v.id);

    /// <summary>A vehicle is buyable once its gate is open, and once owned it goes on the card as
    /// OWNED/EQUIP. Two kinds of gate: a world index (Mars Rover = reach Mars) or a ridge medal
    /// (Lunar Sprint = Gold on any world's ridge survival).</summary>
    public static bool IsUnlocked(Vehicle v)
    {
        if (v == null) return false;
        // LUNAR TRIALS: the Trials Bike is flagged unlocked for now (unlock rule TBD).
        if (GameEdition.IsTrials && v.id == LunarSprint.id) return true;
        if (v.unlockOnRidgeGold) return HasRidgeGold();
        if (v.unlockWorld < 0) return true;
        return LevelManager.IsUnlocked(v.unlockWorld);
    }

    /// <summary>True once the player holds a GOLD (or better) ridge grade on ANY world - exactly
    /// the event that awards the Ridge Runner medal. Grade index 2 is GOLD (BRONZE 0, SILVER 1,
    /// GOLD 2, PLATINUM 3 in GameFlow.RidgeGradeNames), so >= 2 covers Gold and Platinum.</summary>
    private static bool HasRidgeGold()
    {
        for (int w = 0; w < LevelManager.PlanetCount; w++)
            if (LevelManager.GetRidgeBestGrade(w) >= 2) return true;
        return false;
    }

    /// <summary>Equips a vehicle you own (persists). Falls back to the buggy if not owned.</summary>
    public static void Select(string id)
    {
        string safe = IsOwned(id) ? id : LunarBuggy.id;
        SaveBridge.SetString(SelectedKey, safe);
        SaveBridge.Save();
    }

    /// <summary>Buys the vehicle if its world is unlocked AND the wallet covers the price.
    /// Returns the cost spent (0 if not bought).</summary>
    public static int TryBuy(Vehicle v)
    {
        if (v == null || v.cost <= 0 || IsOwned(v) || !IsUnlocked(v)) return 0;
        if (!UpgradeSystem.TrySpendTokens(v.cost)) return 0;
        SaveBridge.SetInt(OwnKey(v.id), 1);
        SaveBridge.Save();
        return v.cost;
    }

    /// <summary>[TEST] UNLOCK ALL hook: owns every vehicle without spending.</summary>
    public static void OwnAllForTesting()
    {
        for (int i = 0; i < All.Length; i++)
        {
            Vehicle v = All[i];
            if (v.cost <= 0) continue;
            SaveBridge.SetInt(OwnKey(v.id), 1);
        }
        SaveBridge.Save();
    }

    // ------------------------------------------------------------------ gameplay multipliers
    // These are what gameplay reads; the buggy is identity so equipping it changes nothing.
    public static float TorqueMul => Selected.torqueMul;
    public static float BoostMul => Selected.boostMul;
    public static float SpeedMul => Selected.speedMul;
    /// <summary>O2 drain multiplier - lower drains longer. Applied with the garage O2 cell.</summary>
    public static float DrainMul => Selected.drainMul;
    public static float HullMul => Selected.hullMul;
    public static float HullDamageMul => Selected.hullDamageMul;
    /// <summary>Harvestable gather radius multiplier (the scoop blade's reach).</summary>
    public static float ScoopMul => Selected.scoopMul;
    public static float JumpMul => Selected.jumpMul;
    /// <summary>Tyre grip multiplier, applied to the base WheelCollider stiffness at build time.
    /// Read once when the wheels are made - BuggyController's reverse-grip overlay scales the
    /// live stiffness, so folding this in there instead would compound every frame.</summary>
    public static float GripMul => Selected.gripMul;
    /// <summary>Rigidbody centre-of-mass height the controller applies at spawn (lower = harder to
    /// roll). The Favonius runs it lower to settle its tall sail.</summary>
    public static float ComHeight => Selected.comHeight;
    /// <summary>Multiplier on lightning hull damage (the Favonius barely notices a bolt).</summary>
    public static float LightningDamageMul => Selected.lightningDamageMul;
    /// <summary>Multiplier on the lightning hard-stall duration (0 = the storm cannot stop it).</summary>
    public static float LightningStallMul => Selected.lightningStallMul;
    /// <summary>Wingsail drive scale. 0 for every vehicle without a rig fitted.</summary>
    public static float WindDriveMul => Selected.windDriveMul;
    /// <summary>How much of a mud bog this vehicle shrugs off, 0-1. 0 bogs it down like any
    /// other; 1 means the slurry barely slows it at all.</summary>
    public static float MudResist => Selected != null ? Selected.mudResist : 0f;
    /// <summary>How much of an ice freeze this vehicle shrugs off, 0-1. 0 takes the full hold;
    /// 1 means it keeps its wheels, steering and brakes on the ice.</summary>
    public static float IceResist => Selected != null ? Selected.iceResist : 0f;
    /// <summary>How much of a lateral wind gust this vehicle shrugs off, 0-1 (see
    /// <see cref="Vehicle.windResist"/>). Applied by WindGusts to every shove.</summary>
    public static float WindResist => Selected != null ? Selected.windResist : 0f;

    /// <summary>Which skin preset the equipped vehicle uses in-world and in the showroom.
    /// Each vehicle reads this through its own art class (LunarRoverArt.SkinOf for the buggy,
    /// MarsRoverArt.SkinOf for the rover).</summary>
    public static int SkinOfVehicle(Vehicle v)
    {
        if (v != null && v.id == MarsRover.id) return 0;   // Nasa Classic
        if (v != null && v.id == LunarSprint.id) return 1; // Artemis white/gold - was dormant
        if (v != null && v.id == VenusSail.id) return 4;   // Favonius titanium/cyan
        return 0;                                          // Historic
    }

    public static int SkinIndex => SkinOfVehicle(Selected);
}