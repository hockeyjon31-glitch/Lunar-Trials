using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A mini sphere of a world, used as the launch button on the World Select rows.
///
/// Each world is a small procedurally-textured rotating globe: a seamless equirectangular-style
/// surface strip is scrolled horizontally under a circular mask (a globe turning on its axis,
/// seen from the side), with fixed directional sun shading and a limb shadow on top so it always
/// reads as a lit ball - the same feel as a 3D solar-system explorer. Venus scrolls the opposite
/// way for its retrograde spin. Surface detail is baked per planet (craters, bands, sulphur
/// blotches, haze streaks) and cached by planet key.
/// </summary>
public static class WorldOrb
{
    /// <summary>Same key mapping as LevelIntroSequence.PlanetKey (duplicated so that file stays
    /// untouched; keep the two in step if a world is added).</summary>
    private static string PlanetKey(string planetName)
    {
        if (string.IsNullOrEmpty(planetName)) return "";
        switch (planetName)
        {
            case "THE MOON": return "moon";
            case "MARS": return "mars";
            case "VENUS": return "venus";
            case "IO": return "io";
            case "TITAN": return "titan";
            default: return planetName.ToLowerInvariant().Replace(" ", "");
        }
    }

    // ------------------------------------------------------------------ public
    /// <summary>Builds the orb: a transparent hit area (the button target) and the sphere art
    /// ("Art"). Returns the root; add a Button to make it clickable. Raycast is on for the root
    /// only. Layering inside the disc mask: scrolling surface (Tex/Skim) under static sun shade
    /// and limb shadow, so the turning detail reads as a sunlit ball. Venus turns backwards.</summary>
    public static RectTransform Create(Transform parent, int worldIndex, LevelDefinition def,
        Vector2 anchor, Vector2 anchoredPos, float size, bool unlocked)
    {
        RectTransform root = UiKit.Rect(parent, "Orb_" + worldIndex, anchor, anchoredPos, new Vector2(size, size));
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0.003f);   // invisible, but catches clicks/hover

        Color dim = unlocked ? Color.white : new Color(0.3f, 0.3f, 0.32f, 1f);
        string key = PlanetKey(def.planetName);

        // A circular mask clips the surface, shading and shadow to the disc.
        RectTransform art = UiKit.Rect(root, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        Image artImg = art.gameObject.AddComponent<Image>();
        artImg.sprite = DiscSprite();
        artImg.raycastTarget = false;
        Mask mask = art.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // Scrolling surface: the planet's baked detail, opaque, turned by OrbRevolve's uvRect.
        RectTransform skimRt = UiKit.Rect(art, "Skim", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        RawImage skim = skimRt.gameObject.AddComponent<RawImage>();
        skim.texture = SurfaceFor(key, worldIndex);
        skim.color = dim;
        skim.raycastTarget = false;
        OrbSpin spin = skimRt.gameObject.AddComponent<OrbSpin>();
        spin.direction = key == "venus" ? -1f : 1f;

        // Static sun shading: the night fold and limb darkening never move, so the turning
        // surface beneath reads as a sunlit ball (light from the right, like the viewer).
        RectTransform shadeRt = UiKit.Rect(art, "Shade", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        Image shadeImg = shadeRt.gameObject.AddComponent<Image>();
        shadeImg.sprite = ShadeSprite();
        shadeImg.raycastTarget = false;

        // Soft shadow toward the edge so the flat card reads as a ball.
        RectTransform limbRt = UiKit.Rect(art, "Limb", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        Image limbImg = limbRt.gameObject.AddComponent<Image>();
        limbImg.sprite = LimbSprite();
        limbImg.raycastTarget = false;

        return root;
    }

    /// <summary>Marks how the world turns: -1 for Venus' retrograde spin, +1 otherwise.</summary>
    public sealed class OrbSpin : MonoBehaviour
    {
        public float direction = 1f;
    }

    // ------------------------------------------------------------------ surface strips
    private static readonly Dictionary<string, Texture2D> stripCache = new Dictionary<string, Texture2D>();

    private static Texture2D SurfaceFor(string key, int index)
    {
        Texture2D cached;
        if (stripCache.TryGetValue(key, out cached) && cached != null) return cached;

        // 256x128 keeps the bake a quarter of the old 512x256 cost. The orb renders ~76px on
        // screen, so this is still ~3.4x oversampled - plenty under the blur of the baked
        // shading and scrolling - while the first-open bake drops from thousands of pixels of
        // per-pixel FBM noise to something even an old phone swallows in a single frame.
        const int W = 256, H = 128;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[W * H];

        switch (key)
        {
            case "mars": FillMars(px, W, H, index); break;
            case "venus": FillVenus(px, W, H, index); break;
            case "io": FillIo(px, W, H, index); break;
            case "titan": FillTitan(px, W, H, index); break;
            default: FillMoon(px, W, H, index); break;
        }

        tex.SetPixels(px);
        tex.Apply();
        tex.name = "OrbSurface_" + key;
        stripCache[key] = tex;
        return tex;
    }

    /// <summary>Pre-bakes the three shared sprites (disc mask, sun shade, limb shadow): a
    /// one-time cost of three 128x128 bakes. Call early so the first World Select open builds
    /// nothing but layout. WebGL has no worker threads, so boot warm-up (one planet per frame,
    /// MenuController.WarmWorldOrbs) is the only real cure for the first-open hitch.</summary>
    public static void PrimeSprites()
    {
        DiscSprite();
        ShadeSprite();
        LimbSprite();
    }

    /// <summary>Pre-bakes one planet's orb surface into stripCache so the World Select row asks
    /// for it later and finds it ready. The row index is the NOISE SEED, so this must be called
    /// with the exact index the row will render with - a different index here and the cached
    /// texture would be baked with the wrong world's terrain.</summary>
    public static void WarmUp(int worldIndex)
    {
        LevelDefinition def = LevelManager.Planets[worldIndex];
        SurfaceFor(PlanetKey(def.planetName), worldIndex);
    }

    // Seamless value noise: the horizontal wrap over <period> cells makes every strip tile
    // edge-to-edge so scrolling the uvRect never shows a seam.
    private static float Lattice(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x00FFFFFF) / 16777215f;
        }
    }

    private static float SmoothNoise(float u, float v, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(u * period);
        int y0 = Mathf.FloorToInt(v * period);
        float fu = u * period - x0;
        float fv = v * period - y0;
        fu = fu * fu * (3f - 2f * fu);
        fv = fv * fv * (3f - 2f * fv);
        int x1 = (x0 + 1) % period;
        float a = Mathf.Lerp(Lattice(x0, y0, seed), Lattice(x1, y0, seed), fu);
        float b = Mathf.Lerp(Lattice(x0, y0 + 1, seed), Lattice(x1, y0 + 1, seed), fu);
        return Mathf.Lerp(a, b, fv);
    }

    private static float Fbm(float u, float v, int seed, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        float freq = 1f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * SmoothNoise(u * freq, v * freq, (int)(32 * freq), seed + i * 1013);
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / norm;
    }

    private static float WrapDist(float a, float b)
    {
        float d = Mathf.Abs(a - b);
        return Mathf.Min(d, 1f - d);
    }

    // The Moon: mottled grey highlands, dark maria floors, bright crater rims and ray systems.
    private static void FillMoon(Color[] px, int W, int H, int seed)
    {
        System.Random rng = new System.Random(seed * 977 + 11);
        const int N = 26;
        float[] cu = new float[N], cv = new float[N], cr = new float[N];
        for (int i = 0; i < N; i++)
        {
            cu[i] = (float)rng.NextDouble();
            cv[i] = 0.07f + 0.86f * (float)rng.NextDouble();
            float s = (float)rng.NextDouble();
            cr[i] = 0.008f + 0.04f * s * s;
        }
        for (int y = 0; y < H; y++)
        {
            float v = (y + 0.5f) / H;
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float n = Fbm(u, v, seed, 4);
                float g = 0.47f + 0.17f * n;
                Color c = new Color(g, g, g + 0.02f, 1f);
                float floor = Fbm(u * 1.9f + 7.3f, v * 1.9f, seed + 40, 3);
                if (floor > 0.62f)   // dark maria
                    c = Color.Lerp(c, new Color(0.36f, 0.37f, 0.39f), (floor - 0.62f) / 0.16f);

                for (int i = 0; i < N; i++)
                {
                    float du = WrapDist(u, cu[i]);
                    float dv = v - cv[i];
                    float d = Mathf.Sqrt(du * du + dv * dv);
                    if (d < cr[i])
                        c = Color.Lerp(c, new Color(0.30f, 0.31f, 0.33f), Mathf.Clamp01(1f - d / cr[i]) * 0.85f);
                    else if (d < cr[i] * 1.22f)
                    {
                        float rim = 1f - (d - cr[i]) / (cr[i] * 0.22f);
                        c = Color.Lerp(c, new Color(0.72f, 0.73f, 0.76f), rim * 0.7f);
                    }
                    else if (i % 3 == 0 && d < cr[i] * 2.0f)
                    {
                        float ray = 1f - (d - cr[i] * 1.22f) / (cr[i] * 0.78f);
                        c = Color.Lerp(c, new Color(0.66f, 0.67f, 0.7f), ray * ray * 0.35f);
                    }
                }
                px[y * W + x] = c;
            }
        }
    }

    // Mars: rust deserts, darker volcanic basalt provinces and dirty polar ice caps.
    private static void FillMars(Color[] px, int W, int H, int seed)
    {
        System.Random rng = new System.Random(seed * 977 + 13);
        const int B = 5;
        float[] bu = new float[B], bv = new float[B], br = new float[B];
        for (int i = 0; i < B; i++)
        {
            bu[i] = (float)rng.NextDouble();
            bv[i] = 0.18f + 0.64f * (float)rng.NextDouble();
            br[i] = 0.10f + 0.16f * (float)rng.NextDouble();
        }
        for (int y = 0; y < H; y++)
        {
            float v = (y + 0.5f) / H;
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float n = Fbm(u, v, seed, 4);
                Color c = new Color(0.58f, 0.33f, 0.17f, 1f);
                c *= 0.82f + 0.35f * n;

                float blob = 0f;
                for (int i = 0; i < B; i++)
                {
                    float du = WrapDist(u, bu[i]);
                    float dv = v - bv[i];
                    blob += Mathf.Exp(-(du * du + dv * dv) / (br[i] * br[i]));
                }
                Color dark = new Color(0.30f, 0.14f, 0.07f, 1f);
                c = Color.Lerp(c, dark, Mathf.Clamp01(blob - 0.55f) * 0.9f);

                // Dirty polar caps (jagged top/bottom fringes).
                float pole = Mathf.Clamp01(Mathf.Min(v, 1f - v) / 0.055f);
                float jag = 0.55f + 0.45f * SmoothNoise(u * 4f, v * 6f, 128, seed + 9);
                Color ice = new Color(0.95f, 0.95f, 0.96f, 1f);
                c = Color.Lerp(c, ice, Mathf.Clamp01(1f - pole * jag));
                px[y * W + x] = c;
            }
        }
    }

    // Venus: creamy sulfate cloud deck with six contorted bands and a searing bright collar.
    private static void FillVenus(Color[] px, int W, int H, int seed)
    {
        for (int y = 0; y < H; y++)
        {
            float v = (y + 0.5f) / H;
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float bands = 0f;
                for (int b = 0; b < 6; b++)
                {
                    float pb = (b + 0.5f) / 6f;
                    float wob = 0.045f * Mathf.Sin(Mathf.PI * 2f * (u * 2f + b * 0.5f))
                        + 0.02f * SmoothNoise(u * 1.5f, v * 2.5f, 48, seed + 7);
                    float dy = v - (pb + wob);
                    float wdt = 0.0022f + 0.0014f * SmoothNoise(u * 3f, v * 3f, 96, seed + 11);
                    bands += Mathf.Exp(-(dy * dy) / wdt);
                }
                Color baseC = new Color(0.88f, 0.81f, 0.63f, 1f);
                Color c = baseC;
                c = Color.Lerp(c, new Color(0.64f, 0.54f, 0.36f, 1f), Mathf.Clamp01(bands * 0.32f));
                float bright = SmoothNoise(u * 0.5f, v * 0.8f, 16, seed + 3);
                c = Color.Lerp(c, new Color(0.98f, 0.93f, 0.76f, 1f), Mathf.Clamp01((bright - 0.62f) / 0.15f));
                px[y * W + x] = c;
            }
        }
    }

    // Io: sulphur yellow-orange with white SO2 frost blotches and dark volcanic calderas.
    private static void FillIo(Color[] px, int W, int H, int seed)
    {
        System.Random rng = new System.Random(seed * 977 + 17);
        const int P = 8;
        float[] pu = new float[P], pv = new float[P], pr = new float[P];
        for (int i = 0; i < P; i++)
        {
            pu[i] = (float)rng.NextDouble();
            pv[i] = 0.1f + 0.8f * (float)rng.NextDouble();
            pr[i] = 0.011f + 0.02f * (float)rng.NextDouble();
        }
        for (int y = 0; y < H; y++)
        {
            float v = (y + 0.5f) / H;
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float n = Fbm(u, v, seed, 4);
                Color c = Color.Lerp(new Color(0.92f, 0.75f, 0.30f, 1f), new Color(0.70f, 0.40f, 0.15f, 1f), n * 0.7f);

                float m = Fbm(u * 1.6f + 7.7f, v * 1.6f, seed + 31, 3);
                if (m > 0.7f)
                    c = Color.Lerp(c, new Color(0.95f, 0.93f, 0.86f, 1f), (m - 0.7f) / 0.16f);

                for (int i = 0; i < P; i++)
                {
                    float du = WrapDist(u, pu[i]);
                    float dv = v - pv[i];
                    float d = Mathf.Sqrt(du * du + dv * dv);
                    if (d < pr[i] * 1.3f)
                        c = Color.Lerp(c, new Color(0.26f, 0.15f, 0.07f, 1f), Mathf.Clamp01(1f - d / (pr[i] * 1.3f)));
                }
                px[y * W + x] = c;
            }
        }
    }

    // Titan: thick golden haze with dark equatorial dune streaks and bright icy banks.
    private static void FillTitan(Color[] px, int W, int H, int seed)
    {
        for (int y = 0; y < H; y++)
        {
            float v = (y + 0.5f) / H;
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float n = Fbm(u, v, seed, 3);
                Color c = new Color(0.78f, 0.60f, 0.45f, 1f) * (0.9f + 0.25f * n);

                float dunes = Fbm(u * 2f, v * 0.4f + 3.1f, seed + 17, 3);
                if (dunes > 0.66f)
                    c = Color.Lerp(c, new Color(0.40f, 0.29f, 0.19f, 1f), (dunes - 0.66f) / 0.14f * Mathf.Abs(Mathf.Cos(Mathf.PI * 2f * (u * 6f + v * 0.5f))) * 2f);

                float banks = SmoothNoise(u * 0.4f, v * 0.6f, 12, seed + 23);
                if (banks > 0.68f)
                    c = Color.Lerp(c, new Color(0.92f, 0.76f, 0.58f, 1f), (banks - 0.68f) / 0.18f);
                px[y * W + x] = c;
            }
        }
    }

    // ------------------------------------------------------------------ baked sprites
    // Baked once, no font glyphs or asset dependencies (same approach as UiKit's star sprite).
    private static Sprite disc, shade, limb;

    private static Sprite DiscSprite()
    {
        if (disc == null) disc = BakeCircle(128, "WorldOrbDisc");
        return disc;
    }

    /// <summary>Static sun shading baked over the disc: a night fold on the left (light from the
    /// right, like the solar-system viewer), plus the limb darkening at the edge. Never moves,
    /// so the scrolling surface beneath reads as the planet turning under its sun.</summary>
    private static Sprite ShadeSprite()
    {
        if (shade != null) return shade;
        const int S = 128;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[S * S];
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float nx = (x + 0.5f - half) / half;
                float ny = (y + 0.5f - half) / half;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                if (dist >= 1f) { px[y * S + x] = new Color(0f, 0f, 0f, 0f); continue; }
                float night = Mathf.Clamp01(0.16f - nx * 0.55f) * 0.9f;
                float edge = Mathf.Pow(dist, 2.6f) * 0.45f;
                float a = (night + edge) * Mathf.Clamp01((1f - dist) * 6f);
                px[y * S + x] = new Color(0f, 0f, 0f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        shade = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        shade.name = "WorldOrbShade";
        return shade;
    }

    /// <summary>Black radial shadow: clear in the middle, darkening toward the rim, circular edge.</summary>
    private static Sprite LimbSprite()
    {
        if (limb != null) return limb;
        const int S = 128;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[S * S];
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                float edge = Mathf.Clamp01((1f - d) * half / 1.5f);
                float t = Mathf.Clamp01((d - 0.55f) / 0.45f);
                px[y * S + x] = new Color(0f, 0f, 0f, t * t * 0.6f * edge);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        limb = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        limb.name = "WorldOrbLimb";
        return limb;
    }

    private static Sprite BakeCircle(int S, string name)
    {
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[S * S];
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                float a = Mathf.Clamp01((1f - d) * half / 1.5f);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        s.name = name;
        return s;
    }
}