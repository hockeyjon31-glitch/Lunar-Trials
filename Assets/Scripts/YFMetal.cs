using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime access to the Yughues Free Metal Material pack swatches (diffuse + normal +
/// spec/gloss), imported under Resources/Textures/YFMetal. One numbered swatch = a small
/// set of textures loaded lazily on first use, so a build that never reaches for a metal
/// pays nothing for it.
///
/// Each swatch is built as a Standard "Specular" surface, matching the pack's own
/// M_YFMeM materials: the _s map carries the specular colour in RGB and the smoothness in
/// its alpha (M_YFMeM_23 is a bright brushed steel, M_YFMeM_37 a darker weathered steel).
/// Falls back to a plain grey metal when a swatch is missing, so a caller never renders a
/// pink material.
/// </summary>
public static class YFMetal
{
    private const string Root = "Textures/YFMetal/T_YFMeM_";

    private static readonly Dictionary<int, Material> cache = new Dictionary<int, Material>();

    /// <summary>The shared Standard material for a YFMeM swatch (e.g. 23, 37). Cached, so
    /// every user of the same swatch shares one material. Returns a grey metal fallback when
    /// the swatch is missing.</summary>
    public static Material Get(int id)
    {
        if (cache.TryGetValue(id, out Material cached) && cached != null) return cached;
        Material m = Build(id);
        cache[id] = m;
        return m;
    }

    /// <summary>A private copy of a swatch material, so a caller can tint or otherwise tweak
    /// one object without changing every other user of the same swatch.</summary>
    public static Material New(int id) => new Material(Get(id));

    private static Material Build(int id)
    {
        Texture2D d = Resources.Load<Texture2D>(Root + id + "_d");
        if (d == null)
        {
            Material fallback = new Material(Shader.Find("Standard"));
            fallback.color = new Color(0.7f, 0.71f, 0.73f);
            fallback.SetFloat("_Metallic", 0.8f);
            fallback.SetFloat("_Glossiness", 0.5f);
            return fallback;
        }

        Material m = new Material(Shader.Find("Standard"));
        m.mainTexture = d;
        // 23 ships white; 37 ships at 0.5, which reads as too dark to see its texture at game
        // scale, so it is lifted toward the lander's body grey (0.82) so the weathered grain
        // actually shows. Both are the single brightness knob for the metal look.
        m.color = id == 23 ? Color.white : new Color(0.92f, 0.92f, 0.95f);
        m.SetFloat("_Metallic", 0f);

        Texture2D n = Resources.Load<Texture2D>(Root + id + "_n");
        if (n != null)
        {
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_BumpMap", n);
            m.SetFloat("_BumpScale", 1f);
        }

        Texture2D s = Resources.Load<Texture2D>(Root + id + "_s");
        if (s != null)
        {
            m.EnableKeyword("_SPECGLOSSMAP");
            m.SetTexture("_SpecGlossMap", s);
            m.SetFloat("_GlossMapScale", id == 23 ? 1f : 0.8f);
            m.SetFloat("_Glossiness", 0.5f);
            m.SetColor("_SpecColor", new Color(0.2f, 0.2f, 0.2f));
        }
        else
        {
            m.SetFloat("_Metallic", 0.7f);
            m.SetFloat("_Glossiness", 0.5f);
        }
        return m;
    }
}
