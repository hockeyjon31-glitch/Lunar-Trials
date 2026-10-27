using UnityEngine;

/// <summary>
/// Persistent garage economy. Every reward - the material harvested on any world, and the
/// tokens earned by beating rivals in Lunar Race - banks into ONE uniform TOKEN wallet.
/// Tokens buy any upgrade (and later any cosmetic); the material a part is "forged from"
/// is narrative only (storyline + realism), no longer a second currency. Planet-progression
/// purchase gates are deliberately deferred until the cosmetics list lands.
/// Saves to PlayerPrefs exactly like the username and leaderboards, so a WebGL run keeps
/// the wallet between sessions.
///
///   Engine       <- built on Silicon (Venus)
///   Boost Cells  <- charged with Liquid Methane (Titan)
///   O2 Cell      <- powered by Helium-3 (Moon)
///   Hull Plate   <- welded from Iron (Mars)
///   Hop Rockets  <- built on Silicon (Venus)
///
/// Five upgrades, each is one real vehicle stat clamped 0..10 levels (Build Level 0..50).
/// Cost of the next tier: FirstUpgradeCost + UpgradeCostStep * currentLevel.
/// </summary>
public static class UpgradeSystem
{
    // ------------------------------------------------------------------ material identity
    /// <summary>The five narrative materials each world harvests. Display/text only now:
    /// the wallet is uniform tokens, so a material never gates a purchase here.</summary>
    public enum OreType
    {
        Iron,
        Sulphur,
        Silicon,
        LiquidHelium,
        Helium3,
    }

    public static string OreNameOf(OreType t)
    {
        switch (t)
        {
            case OreType.Iron: return "IRON";
            case OreType.Sulphur: return "SULPHUR";
            case OreType.Silicon: return "SILICON";
            case OreType.LiquidHelium: return "LIQUID METHANE";
            case OreType.Helium3: return "HELIUM-3";
            default: return t.ToString().ToUpper();
        }
    }

    /// <summary>Compact code for the wallet strip (FE / S / SI / LME / HE3).</summary>
    public static string OreShortOf(OreType t)
    {
        switch (t)
        {
            case OreType.Iron: return "Fe";
            case OreType.Sulphur: return "S";
            case OreType.Silicon: return "Si";
            case OreType.LiquidHelium: return "LHe";
            case OreType.Helium3: return "He3";
            default: return "?";
        }
    }

    public static Color OreColorOf(OreType t)
    {
        switch (t)
        {
            case OreType.Iron: return new Color(0.85f, 0.45f, 0.22f);
            case OreType.Sulphur: return new Color(0.95f, 0.9f, 0.35f);
            case OreType.Silicon: return new Color(0.72f, 0.78f, 0.85f);
            case OreType.LiquidHelium: return new Color(0.35f, 0.85f, 0.95f);
            case OreType.Helium3: return new Color(0.35f, 0.95f, 0.8f);
            default: return Color.white;
        }
    }

    // ------------------------------------------------------------------ uniform token wallet
    private const string TokenKey = "lunar.tokens";

    public static Color TokenColor => new Color(1f, 0.84f, 0.25f, 1f);

    public static int GetTokens() => SaveBridge.GetInt(TokenKey, 0);

    /// <summary>Adds earned tokens (harvest conversion or race rival-beats) and saves.</summary>
    public static void AddTokens(int amount)
    {
        if (amount <= 0) return;
        SaveBridge.SetInt(TokenKey, GetTokens() + amount);
        SaveBridge.Save();
    }

    /// <summary>Spends tokens if affordable; returns true when the purchase went through.</summary>
    public static bool TrySpendTokens(int amount)
    {
        if (amount <= 0) return true;
        if (GetTokens() < amount) return false;
        SaveBridge.SetInt(TokenKey, GetTokens() - amount);
        SaveBridge.Save();
        return true;
    }

    // ------------------------------------------------------------------ tuning
    public const int MaxLevel = 10;
    // Upgrade staircase: the first tier of a part costs FirstUpgradeCost, then +UpgradeCostStep
    // per tier (100 / 110 / 120 / 130 ... / 190). The cheap first buy turns one cleared run - with
    // its first-clear bonus - straight into a purchase, and the gentler ramp keeps every tier
    // reachable across a run so the garage keeps feeling rewarding past tier 5.
    public const int FirstUpgradeCost = 100;
    public const int UpgradeCostStep = 10;

    // Token income balancing (all centred here so costs are tuned in one place):
    //   Harvest pays TOKENS DIRECTLY: every nugget banks its planet's token value 1:1
    //   (Moon 2, Mars 3, Venus 4, Io 5, Titan 6), and finishing with spare oxygen adds
    //   2 tokens per 10% saved. The run's token total IS its leaderboard score - there
    //   are no points separate from tokens anymore. World/mode progression is NOT token
    //   driven: it unlocks through LevelManager's sequential check-point chain instead.
    //   Race rival-beats: beating a tier pays its TOTAL award (Bronze 60, Silver 100, Gold 200;
    //   each is the full pot for that tier - Silver does not add Bronze's 60 on top). Finishing
    //   the race in LAST place (no rival beaten, inside the post-bronze cutoff window) pays a
    //   small consolation; failing to cross the line entirely (the cutoff, or a crash) pays 0.
    public const int RaceBronzeTokens = 60;
    public const int RaceSilverTokens = 100;
    public const int RaceGoldTokens = 200;
    public const int RaceLastPlaceTokens = 30;

    // ------------------------------------------------------------------ save keys
    private static readonly string[] Ids = { "engine", "boost", "oxygen", "hull", "jump" };

    private static string LevelKey(string id) => "lunar.upg." + id;

    private static string DisplayNameOf(string id)
    {
        switch (id)
        {
            case "engine": return "ENGINE";
            case "boost": return "BOOST CELLS";
            case "oxygen": return "O2 CELL";
            case "hull": return "HULL PLATE";
            case "jump": return "HOP ROCKETS";
            default: return id.ToUpper();
        }
    }

    public static string DisplayName(string id)
    {
        return DisplayNameOf(id);
    }

    private static string DescOf(string id)
    {
        switch (id)
        {
            case "engine": return "More launch torque AND a higher flat-out cruise speed.";
            case "boost": return "Larger boost capacitor and a faster sprint - hold the burst longer and harder.";
            case "oxygen": return "A denser oxygen cell drips 10% slower right out of the crate, with a steady trim for every tier purchased.";
            case "hull": return "Reinforced hull plate and impact guard - more integrity, and boulder hits do less damage.";
            case "jump": return "Hop rockets shove the buggy skyward - a small hop at first, growing with every tier.";
            default: return "";
        }
    }

    // ------------------------------------------------------------------ garage display
    // ValueOf(id, level) is the headline number a part gives at a given level, in the unit the
    // player actually feels (see UnitOf). The garage shows "now" and "next tier" from it, and it
    // reads the same *At(level) formulas that drive gameplay, so the display cannot drift.

    private static string Signed(float pct)
    {
        return "+" + pct.ToString("0.#") + "%";
    }

    public static string ValueOf(string id, int level)
    {
        level = Mathf.Clamp(level, 0, MaxLevel);
        switch (id)
        {
            case "engine":
                return level == 0 ? "STOCK" : Signed((EngineFactorAt(level) - 1f) * 100f);
            case "boost":
                return level == 0 ? "STOCK" : Signed((BoostFactorAt(level) - 1f) * 100f);
            case "jump":
                // BuggyController launches the hop to peak at a fixed HEIGHT, taking off at
                // sqrt(2gh); the factor scales that launch speed, and height goes as v^2, so the
                // height gain is the SQUARE of the factor regardless of the world's gravity.
                return level == 0 ? "STOCK" : Signed((JumpFactorAt(level) * JumpFactorAt(level) - 1f) * 100f);
            case "oxygen":
                // Measured against the stock tank: the stock 0.9x drain is already free, so
                // quoting "-X% drain" would count that gift as the upgrade's own value.
                return level == 0 ? "STOCK" : Signed((DrainFactorAt(0) / DrainFactorAt(level) - 1f) * 100f);
            case "hull":
                // Integrity and impact-damage cut multiply: effective toughness vs stock.
                return level == 0 ? "STOCK" : "x" + (HullFactorAt(level) / HullDamageFactorAt(level)).ToString("0.0");
            default:
                return "";
        }
    }

    public static string UnitOf(string id)
    {
        switch (id)
        {
            case "engine": return "torque";   // say "torque & top speed" only once the speed cap scales with the tier
            case "boost": return "boost time";
            case "jump": return "hop height";
            case "oxygen": return "air time vs stock";
            case "hull": return "toughness";
            default: return "";
        }
    }

    /// <summary>Optional one-line footnote shown under the now/next boxes.</summary>
    private static string NoteOf(string id)
    {
        switch (id)
        {
            case "jump": return "Press J to fire.";
            case "oxygen": return "Compared with the stock tank, which already drains 10% slower.";
            case "hull": return "Extra integrity and reduced impact damage, combined.";
            default: return "";
        }
    }

    /// <summary>Compact row summary: "now > next unit", or "now unit (MAX)" when fully upgraded.</summary>
    private static string EffectOf(string id, int level)
    {
        string unit = UnitOf(id);
        if (level >= MaxLevel) return ValueOf(id, level) + " " + unit + "  (MAX)";
        return ValueOf(id, level) + " > " + ValueOf(id, level + 1) + " " + unit;
    }

    public static int LevelOf(string id)
    {
        return Mathf.Clamp(SaveBridge.GetInt(LevelKey(id), 0), 0, MaxLevel);
    }

    /// <summary>Which material the fiction forges this part from (narrative flavour only).</summary>
    private static OreType OreTypeFor(string id)
    {
        switch (id)
        {
            case "engine": return OreType.Silicon;
            case "boost": return OreType.LiquidHelium;
            case "oxygen": return OreType.Helium3;
            case "hull": return OreType.Iron;
            case "jump": return OreType.Silicon;
            default: return OreType.Iron;
        }
    }

    // ------------------------------------------------------------------ purchases (tokens)
    public static bool CanAfford(string id) => LevelOf(id) < MaxLevel && GetTokens() >= CostOfNext(id);

    /// <summary>True when at least one upgrade is not maxed and costs no more than the wallet.
    /// Drives the pulsing token badge on the menu screens.</summary>
    public static bool CanAffordAny()
    {
        for (int i = 0; i < Ids.Length; i++)
        {
            if (CanAfford(Ids[i])) return true;
        }
        return false;
    }

    public static int CostOfNext(string id) => FirstUpgradeCost + UpgradeCostStep * LevelOf(id);

    /// <summary>Spends tokens on the next upgrade tier for this part; returns true if bought.</summary>
    public static bool TryUpgrade(string id)
    {
        if (!CanAfford(id)) return false;
        int cost = CostOfNext(id);
        SaveBridge.SetInt(LevelKey(id), LevelOf(id) + 1);
        SaveBridge.SetInt(TokenKey, GetTokens() - cost);
        SaveBridge.Save();
        return true;
    }

    /// <summary>Grants one FREE upgrade tier (no token cost) - the race-last consolation ad's
    /// reward. Returns false when the part is already maxed (the grant never happens).</summary>
    public static bool GrantFreeLevel(string id)
    {
        if (LevelOf(id) >= MaxLevel) return false;
        SaveBridge.SetInt(LevelKey(id), LevelOf(id) + 1);
        SaveBridge.Save();
        return true;
    }

    /// <summary>Sum of all upgrade levels (0..50), shown on leaderboards for fairness.</summary>
    public static int BuildLevel
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < Ids.Length; i++) sum += LevelOf(Ids[i]);
            return sum;
        }
    }

    /// <summary>Number of purchaseable upgrades (drives leaderboard "Build Level x/y").</summary>
    public static int UpgradeCount => Ids.Length;

    // ------------------------------------------------------------------ effects (identity at lvl 0)
    // Every stat has an *At(level) formula (the garage uses it to preview the NEXT tier) and a
    // property that reads the player's current level. Change a number once, here, and gameplay
    // and the garage display both follow.
    public static float EngineFactorAt(int lv) => 1f + 0.075f * lv;
    public static float BoostFactorAt(int lv) => 1f + 0.1f * lv;
    // O2: stock drips at 0.9x (an 11% longer run out of the crate - a leaner gift than the
    // old 0.8x, so the starting tank bites ~10% harder), and each tier trims a 0.03 arc that
    // still lands on the same 0.6x max at level 10 the old curve produced.
    public static float DrainFactorAt(int lv) => Mathf.Max(0.4f, 0.9f - 0.03f * lv);
    public static float HullFactorAt(int lv) => 1f + 0.125f * lv;
    public static float HullDamageFactorAt(int lv) => Mathf.Max(0.5f, 1f - 0.04f * lv);
    // Hop rockets: small stock hop, launch speed up to 2x at tier 10 (height ~4x, see ValueOf).
    public static float JumpFactorAt(int lv) => 1f + 0.1f * lv;

    public static float EngineFactor => EngineFactorAt(LevelOf("engine"));
    public static float BoostFactor => BoostFactorAt(LevelOf("boost"));
    public static float DrainFactor => DrainFactorAt(LevelOf("oxygen"));
    public static float HullFactor => HullFactorAt(LevelOf("hull"));
    public static float HullDamageFactor => HullDamageFactorAt(LevelOf("hull"));
    public static float JumpFactor => JumpFactorAt(LevelOf("jump"));

    // ------------------------------------------------------------------ garage metadata
    public sealed class UpgradeInfo
    {
        public string id;
        public string name;
        public string desc;
        public int level;
        public int maxLevel;
        public int cost;        // token cost of the next tier
        public bool maxed;
        public string effect;      // compact row line: "now > next unit"
        public string nowValue;    // headline value at the current level ("STOCK" at 0)
        public string nextValue;   // headline value one tier up
        public string unit;        // what the value measures ("torque", "hop height", ...)
        public string note;        // optional footnote for the detail card
        // Narrative material this part is made from (flavour; the wallet is tokens).
        public OreType material;
        public string materialName;
        public Color materialColor;
    }

    public static UpgradeInfo InfoOf(string id)
    {
        int level = LevelOf(id);
        OreType t = OreTypeFor(id);
        return new UpgradeInfo
        {
            id = id,
            name = DisplayNameOf(id),
            desc = DescOf(id),
            level = level,
            maxLevel = MaxLevel,
            cost = CostOfNext(id),
            maxed = level >= MaxLevel,
            effect = EffectOf(id, level),
            nowValue = ValueOf(id, level),
            nextValue = ValueOf(id, level + 1),
            unit = UnitOf(id),
            note = NoteOf(id),
            material = t,
            materialName = OreNameOf(t),
            materialColor = OreColorOf(t),
        };
    }
}