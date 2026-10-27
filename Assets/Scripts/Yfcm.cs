using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime access to the Yughues Free Concrete Material pack swatches (diffuse + normal,
/// imported under Resources/Textures/YFCM - see YfcmImporter), plus the photographic lunar
/// ground set imported under Resources/Textures/LunarGround (see LunarSceneryImporter).
/// One swatch = a small pair of textures loaded lazily on first use, so worlds that don't use
/// a swatch cost nothing.
///
/// Three worlds dress themselves in these materials:
///  - each world with a COMBO swaps its procedural regolith surface and rock texture for a
///    blend of two swatches - see <see cref="ComboFor"/> and <see cref="World"/>; the Moon
///    blends the two lunar ground photographs (or just Ground01 alone under the MOBILE_LITE
///    build, which stashes Ground02 out of Resources), Io and Venus blend the honeycombing +
///    mouldy concrete;
///  - the Io volcano cones use a single swatch (see <see cref="Rock"/>).
/// Everything falls back to the procedural look when a swatch is missing.
/// </summary>
public static class Yfcm
{
    /// <summary>Folders (under Resources) searched for a named swatch texture.</summary>
    private static readonly string[] LoadRoots = { "Textures/YFCM/", "Textures/LunarGround/" };

    public static Texture2D Diffuse(string swatch)
    {
        return Load(swatch + "_d");
    }

    public static Texture2D Normal(string swatch)
    {
        return Load(swatch + "_n");
    }

    private static Texture2D Load(string name)
    {
        for (int i = 0; i < LoadRoots.Length; i++)
        {
            Texture2D t = Resources.Load<Texture2D>(LoadRoots[i] + name);
            if (t != null) return t;
        }
        return null;
    }

    /// <summary>
    /// A Standard surface built from a YFCM swatch, tinted to fit the world and tiled for the
    /// mesh UVs (e.g. the volcano cone flank). Returns null when the swatch is missing so
    /// callers keep their procedural fallback.
    /// </summary>
    public static Material Rock(string swatch, Color tint, Vector2 tiling, float glossiness, Color emission)
    {
        Texture2D d = Diffuse(swatch);
        if (d == null) return null;

        Material m = new Material(Shader.Find("Standard"));
        m.mainTexture = d;
        m.mainTextureScale = tiling;
        Texture2D n = Normal(swatch);
        if (n != null)
        {
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_BumpMap", n);
        }
        m.color = tint;
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", glossiness);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
        }
        return m;
    }

    // ---------------------------------------------------------------- per-world combinations

    /// <summary>Tiling used when a swatch is baked into a terrain-conforming decal.</summary>
    public static readonly Vector2 DecalTiling = new Vector2(4f, 4f);

    /// <summary>
    /// The two swatches that dress a world's surface and rocks, plus which of the two
    /// supplies the normal map. False for worlds that keep their procedural rock (none just
    /// now - the Moon runs the two ground photos, Io and Venus run honeycombing + mouldy,
    /// Mars its plain sand alone, Titan its crystal + fibres).
    /// </summary>
    public static bool ComboFor(string planetName, out string swatchA, out string swatchB, out string normalSwatch)
    {
        switch (CourseHazards.ThemeFor(planetName))
        {
            case PlanetTheme.Lunar:
#if MOBILE_LITE
                // Mobile-light build: the Moon dresses in the single ground photo alone. The
                // Ground02 blend is stashed out of Resources for that variant (mobile-lite build
                // menu), so the Moon keeps the heavier texture's space budget in exchange for
                // its pattern only appearing where the primary ground is not already dominant.
                swatchA = "T_LunarGround_Ground01"; swatchB = swatchA; normalSwatch = swatchA;
#else
                swatchA = "T_LunarGround_Ground01"; swatchB = "T_LunarGround_Ground02"; normalSwatch = swatchA;
#endif
                return true;
            case PlanetTheme.Mars:
                // Mars keeps its plain sand only: the salt finish was a salt-and-pepper
                // duller that read as grime on the red dune, so the world dresses in just the
                // one swatch (a == b, which CombinedAlbedo hands back without any blend).
                swatchA = "T_YFCM_ColoredSand"; swatchB = swatchA; normalSwatch = swatchA;
                return true;
            case PlanetTheme.Io:
            case PlanetTheme.Venus:
                // Both worlds bed their terrain and rocks in the honeycombing + mouldy concrete
                // blend, tinted to each world's own regolith hue so they read as distinct planets.
                swatchA = "T_YFCM_Honeycombing"; swatchB = "T_YFCM_Moldy"; normalSwatch = swatchA;
                return true;
            case PlanetTheme.Titan:
                swatchA = "T_YFCM_Crystallized"; swatchB = "T_YFCM_Fibers"; normalSwatch = swatchA;
                return true;
            default:
                swatchA = swatchB = normalSwatch = null;
                return false;
        }
    }

    /// <summary>
    /// Tint multiplier for a world's combo albedo. The photographic lunar ground set ships
    /// already in its own regolith greys, so the Moon passes the albedo through at its own
    /// brightness; the painted YFCM sand/concrete swatches need the world's regolith tint to
    /// read as that planet.
    /// </summary>
    public static Color WorldTint(string planetName, Color regolith, float brightness)
    {
        if (CourseHazards.ThemeFor(planetName) == PlanetTheme.Lunar)
            return new Color(brightness, brightness, brightness);
        return regolith * brightness;
    }

    /// <summary>
    /// A Standard material for a world surface or rock: the two swatches' diffuses blended into
    /// one albedo (B modulating A around A's brightness so both patterns read) tinted to the
    /// world, with an optional normal map. Returns null when the world has no combo or a swatch
    /// is missing/not readable, so callers keep their procedural fallback.
    /// </summary>
    public static Material World(string planetName, Color tint, Vector2 tiling, float glossiness,
                                 bool normalMap = true)
    {
        if (!ComboFor(planetName, out string a, out string b, out string normalSwatch)) return null;
        Texture2D albedo = CombinedAlbedo(a, b);
        if (albedo == null) return null;

        Material m = new Material(Shader.Find("Standard"));
        m.mainTexture = albedo;
        m.mainTextureScale = tiling;
        m.color = tint;
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", glossiness);

        if (normalMap)
        {
            Texture2D n = Normal(normalSwatch);
            if (n != null)
            {
                m.EnableKeyword("_NORMALMAP");
                m.SetTexture("_BumpMap", n);
                m.SetTextureScale("_BumpMap", tiling);
            }
        }
        return m;
    }

    private static readonly Dictionary<string, Texture2D> combos = new Dictionary<string, Texture2D>();

    /// <summary>Blends two swatch diffuses into one cached albedo (B modulates A around A's own
    /// brightness, so a salt/fibre/mould speckle rides the base material's tone).</summary>
    public static Texture2D CombinedAlbedo(string swatchA, string swatchB)
    {
        string key = swatchA + "|" + swatchB;
        if (combos.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

        Texture2D ta = Diffuse(swatchA);
        if (ta == null || !ta.isReadable) return null;
        // A world dressing in a single swatch (a == b, e.g. Mars's plain sand) needs no blend:
        // hand the one diffuse straight back so the combo costs nothing and keeps its own detail.
        if (swatchA == swatchB)
        {
            combos[key] = ta;
            return ta;
        }

        Texture2D tb = Diffuse(swatchB);
        if (tb == null || !tb.isReadable) return null;

        const int size = 512;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
        tex.name = "YFCM_" + swatchA + "_" + swatchB;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.anisoLevel = 6;

        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                Color ca = ta.GetPixelBilinear(u, v);
                Color cb = tb.GetPixelBilinear(u, v);
                tex.SetPixel(x, y, new Color(ca.r * (0.5f + cb.r),
                                             ca.g * (0.5f + cb.g),
                                             ca.b * (0.5f + cb.b)));
            }
        }
        tex.Apply(true, false);
        combos[key] = tex;
        return tex;
    }

    /// <summary>Albedo colour of a single swatch at a decal UV (tiled), or <paramref name="fallback"/>
    /// when the swatch is missing or not CPU-readable.</summary>
    public static Color SampleDiffuse(string swatch, float u, float v, Color fallback)
    {
        Texture2D t = Diffuse(swatch);
        if (t == null || !t.isReadable) return fallback;
        return t.GetPixelBilinear(Mathf.Repeat(u * DecalTiling.x, 1f), Mathf.Repeat(v * DecalTiling.y, 1f));
    }
}
