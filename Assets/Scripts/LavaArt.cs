using UnityEngine;

/// <summary>
/// Runtime access to the "Magma" swatch (base + emissive + normal) imported under
/// Resources/Textures/Lava - see LavaImporter. A single dark-crust / glowing-seam texture set
/// dresses Io's molten surfaces: the scrolling race flow, the standing lava lakes and volcano
/// crater pools, and the lava avalanche crust. Each map loads lazily so worlds without lava
/// cost nothing, and every builder returns null / no-ops when the swatch is missing so callers
/// keep their procedural fallback.
/// </summary>
public static class LavaArt
{
    private const string Root = "Textures/Lava/";
    private const string Swatch = "T_Lava_Magma";

    public static Texture2D Base() { return Resources.Load<Texture2D>(Root + Swatch + "_d"); }
    public static Texture2D Emissive() { return Resources.Load<Texture2D>(Root + Swatch + "_e"); }
    public static Texture2D Normal() { return Resources.Load<Texture2D>(Root + Swatch + "_n"); }
    public static bool Available { get { return Base() != null; } }

    /// <summary>Albedo tiling baked into a terrain decal's feathered texture (per patch).</summary>
    public static readonly Vector2 DecalAlbedoTiling = new Vector2(3f, 1.6f);

    /// <summary>
    /// A fully opaque molten surface: the magma albedo tinted, the emissive seam map driving the
    /// glow (scales with <paramref name="emissionTint"/>) and the normal adding crust relief.
    /// Returns null if the swatch is missing.
    /// </summary>
    public static Material Melt(Color tint, Vector2 tiling, float glossiness, Color emissionTint)
    {
        Texture2D d = Base();
        if (d == null) return null;

        Material m = new Material(Shader.Find("Standard"));
        m.mainTexture = d;
        m.mainTextureScale = tiling;
        m.color = tint;
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", glossiness);

        Texture2D n = Normal();
        if (n != null)
        {
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_BumpMap", n);
            m.SetTextureScale("_BumpMap", tiling);
        }

        Texture2D e = Emissive();
        if (e != null && emissionTint.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", e);
            m.SetTextureScale("_EmissionMap", tiling);
            m.SetColor("_EmissionColor", emissionTint);
        }
        return m;
    }

    /// <summary>
    /// Adds the magma normal + emissive seam maps onto an existing material WITHOUT touching its
    /// albedo or alpha - used by the terrain-conforming lava decal, which keeps its own feathered
    /// albedo/alpha and just borrows the swatch's relief and glowing seams. No-op when missing.
    /// </summary>
    public static void Dress(Material mat, Vector2 tiling, Color emissionTint)
    {
        if (mat == null) return;

        Texture2D n = Normal();
        if (n != null)
        {
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_BumpMap", n);
            mat.SetTextureScale("_BumpMap", tiling);
        }

        Texture2D e = Emissive();
        if (e != null && emissionTint.maxColorComponent > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", e);
            mat.SetTextureScale("_EmissionMap", tiling);
            mat.SetColor("_EmissionColor", emissionTint);
        }
    }

    /// <summary>Albedo colour of the magma swatch at a decal UV (tiled), or <paramref name="fallback"/>
    /// when the swatch is missing or not CPU-readable.</summary>
    public static Color AlbedoAt(float u, float v, Color fallback)
    {
        Texture2D d = Base();
        if (d == null || !d.isReadable) return fallback;
        return d.GetPixelBilinear(Mathf.Repeat(u * DecalAlbedoTiling.x, 1f),
                                  Mathf.Repeat(v * DecalAlbedoTiling.y, 1f));
    }

    private static Material moltenFx;

    /// <summary>Shared bright molten skin for flung lava globules and popped bubbles: the swatch's
    /// relief with a strong emissive seam glow so a small body still reads as white-hot rock.
    /// Cached (one material for every globule) and null when the swatch is missing.</summary>
    public static Material MoltenFx()
    {
        if (moltenFx == null)
            moltenFx = Melt(Color.white, new Vector2(1.5f, 1.5f), 0.05f, new Color(2.2f, 1.2f, 0.5f));
        return moltenFx;
    }
}
