using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Small runtime UI toolkit shared by the menu and leaderboard screens. Everything is
/// built from code so the screens are just an empty camera + one bootstrap script in the
/// scene; no prefabs, no assets. Also owns the cross-version font resolution.
/// </summary>
public static class UiKit
{
    // ------------------------------------------------------------- font
    /// <summary>Text roles for the bundled type system (both SIL OFL, free for commercial use):
    /// Body = Rajdhani SemiBold - condensed, techy, excellent for HUD numerals, briefings and
    /// hints. Display = Russo One - chunky sci-fi face for titles, headers and button labels.
    /// Both ship in Resources/Fonts and load at runtime (the project builds its UI from code,
    /// so there are no font asset references anywhere). Their Latin coverage spans every
    /// glyph the UI renders (middots, guillemet arrows, bullets, ellipses, degrees); if the
    /// Resources load ever fails, resolution falls back to Unity's built-in font so text can
    /// never render blank.</summary>
    public enum UiFontKind { Body, Display }

    private static Font _bodyFont;
    private static Font _displayFont;
    private static bool _fontsReady;

    /// <summary>Back-compat: the default text font (the Body face).</summary>
    public static Font ResolveDefaultFont()
    {
        return ResolveFont(UiFontKind.Body);
    }

    /// <summary>Resolves a face for the given role, falling back through the bundled fonts to
    /// Unity's built-ins and finally an OS font so text can never render blank.</summary>
    public static Font ResolveFont(UiFontKind kind)
    {
        EnsureFonts();
        Font font = kind == UiFontKind.Display ? _displayFont : _bodyFont;
        if (font != null) return font;
        return ResolveLegacyFont();
    }

    private static void EnsureFonts()
    {
        if (_fontsReady) return;
        _fontsReady = true;

        _bodyFont = Resources.Load<Font>("Fonts/Rajdhani-SemiBold");
        _displayFont = Resources.Load<Font>("Fonts/RussoOne-Regular");
        if (_bodyFont == null || _displayFont == null)
            Debug.LogWarning("Lunar Harvest UI: bundled font(s) missing from Resources/Fonts - "
                + "falling back to the built-in font for the missing face(s).");
    }

    /// <summary>Unity's built-in font, resolved defensively across editor versions (the original
    /// cross-version lookup, kept as the universal fallback).</summary>
    private static Font ResolveLegacyFont()
    {
        Font font = null;
        try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (font == null)
        {
            try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        }
        if (font == null)
        {
            string[] candidates = { "Arial", "Segoe UI", "Liberation Sans", "DejaVu Sans", "Tahoma", "Verdana" };
            foreach (string name in candidates)
            {
                try
                {
                    font = Font.CreateDynamicFontFromOSFont(name, 16);
                    if (font != null) break;
                }
                catch { }
            }
        }
        if (font == null) Debug.LogWarning("Lunar Harvest UI: no usable font found, text will render blank.");
        return font;
    }

    // ------------------------------------------------------------- base canvas
    /// <summary>Creates a screen-space overlay canvas with a scaler and a raycast target.</summary>
    public static Canvas CreateRootCanvas(string name, int sortingOrder)
    {
        GameObject go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        // Screen sizes diverge wildly from the 1920x1080 design reference: the CrazyGames
        // mobile app and portrait side panels are much taller, ultrawide desktops much wider.
        // MatchWidth (0) crops the vertical on wide screens; MatchHeight (1) shrinks the design
        // width below 1920 on portrait so wide panels overflow the edges. The 0.5 geometric
        // mean scales both axes together - no crop either way, and the centre-anchored menus
        // stay inside the visible area on a phone.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    /// <summary>Ensures one EventSystem exists (needed for buttons to be clickable).</summary>
    public static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    // ------------------------------------------------------------- primitives
    /// <summary>Places a RectTransform child using a single anchor corner/pivot (like the HUD labels).</summary>
    public static Vector2 AnchorFor(Vector2 pos)
    {
        float x = pos.x < 0f ? 1f : (pos.x > 0f ? 0f : 0.5f);
        float y = pos.y < 0f ? 1f : 0f;
        return new Vector2(x, y);
    }

    public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    public static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 anchoredPos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.color = color;
        return rt;
    }

    public static Text Label(Transform parent, string name, string text, int fontSize, Color color,
        Vector2 anchoredPos, TextAnchor alignment, float width = 560f, float height = 48f,
        UiFontKind fontKind = UiFontKind.Body)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        Vector2 a = AnchorFor(anchoredPos);
        rt.anchorMin = a;
        rt.anchorMax = a;
        rt.pivot = a;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(width, height);
        Text t = go.GetComponent<Text>();
        t.font = ResolveFont(fontKind);
        t.fontSize = fontSize;
        t.alignment = alignment;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        // Vertical OVERFLOW, never the Truncate default: the bundled Rajdhani/Russo faces have
        // taller line metrics than Arial, and Truncate culls a line to nothing whenever the
        // font's line height exceeds the box (the exact bug that once blanked the 46pt race
        // clock and later the 34pt speed readout). Every label here is placed by hand, so
        // nothing relies on being clipped.
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = text;
        return t;
    }

    /// <summary>A clickable button: image background + centred label, wired to the given action.
    /// Button labels use the Display face - buttons are the UI's primary call-to-action text.</summary>
    public static Button Button(Transform parent, string name, string label, int fontSize,
        Color bg, Color textColor, Vector2 anchoredPos, Vector2 size, System.Action onClick)
    {
        RectTransform rt = Panel(parent, name, AnchorFor(anchoredPos), anchoredPos, size, bg);
        Text text = Label(rt, "Label", label, fontSize, textColor, Vector2.zero, TextAnchor.MiddleCenter,
            size.x, size.y, UiFontKind.Display);
        RectTransform trt = text.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        Button b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = rt.GetComponent<Image>();
        ColorBlock colors = b.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.18f);
        colors.pressedColor = new Color(0.4f, 0.9f, 0.7f, 1f);
        colors.selectedColor = Color.white;
        b.colors = colors;
        b.onClick.AddListener(() => { if (onClick != null) onClick(); });
        return b;
    }

    /// <summary>Stretches a RectTransform to fill its parent.</summary>
    public static void StretchToFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ------------------------------------------------------------- image UI
    /// <summary>Full-screen background image behind a screen's UI, loaded from Resources.
    /// Raycast is off so it never blocks the controls above it.</summary>
    public static RawImage Background(Transform parent, string name, string textureKey)
    {
        RectTransform rt = Rect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero);
        StretchToFull(rt);
        RawImage img = rt.gameObject.AddComponent<RawImage>();
        img.raycastTarget = false;
        Texture2D tex = Resources.Load<Texture2D>(textureKey);
        if (tex != null) img.texture = tex;
        else img.color = new Color(0.015f, 0.015f, 0.035f, 1f);
        return img;
    }

    /// <summary>A clickable IMAGE button: a RawImage framed to <paramref name="size"/>, wired to
    /// the given action. The texture is stretched to fill the button's rectangle so every button
    /// in a grid keeps the same footprint regardless of its art's original aspect ratio.
    /// <paramref name="anchor"/> is the explicit pivot/anchor corner (pass a center anchor like
    /// (0.5f, 1f) to build a symmetric central block instead of edge-anchored rows).
    /// <paramref name="alpha"/> makes the art semi-transparent so a backdrop underneath bleeds
    /// through; <paramref name="borderWidth"/> (and its colour) draws a thin frame around the
    /// cell, lightening the hover edge over the artwork.</summary>
    public static Button ImageButton(Transform parent, string name, string textureKey,
        Vector2 anchor, Vector2 anchoredPos, Vector2 size, System.Action onClick,
        float alpha = 1f, float borderWidth = 0f, Color borderColor = default)
    {
        Vector2 outer = size + new Vector2(borderWidth * 2f, borderWidth * 2f);
        RectTransform rt = Rect(parent, name, anchor, anchoredPos, outer);

        // Invisible full-cell graphic: this is the raycast/hit area, so the whole cell
        // (art + border) is clickable. The visible art below owns the button's hover tint.
        Image hitbox = rt.gameObject.AddComponent<Image>();
        hitbox.color = new Color(0f, 0f, 0f, 0.003f);

        if (borderWidth > 0f)
        {
            // Four thin strips around the cell edge. Strips (not a backdrop) keep the centre
            // open so the semi-transparent art really lets the background show through, and
            // being edge-only they never sit underneath (and bleed through) the artwork.
            AddBorderStrip(rt, "BorderTop", new Vector2(outer.x, borderWidth),
                new Vector2(outer.x / 2f, outer.y - borderWidth / 2f), borderColor);
            AddBorderStrip(rt, "BorderBottom", new Vector2(outer.x, borderWidth),
                new Vector2(outer.x / 2f, borderWidth / 2f), borderColor);
            AddBorderStrip(rt, "BorderLeft", new Vector2(borderWidth, outer.y),
                new Vector2(borderWidth / 2f, outer.y / 2f), borderColor);
            AddBorderStrip(rt, "BorderRight", new Vector2(borderWidth, outer.y),
                new Vector2(outer.x - borderWidth / 2f, outer.y / 2f), borderColor);
        }

        RectTransform art = Inset(rt, borderWidth);
        RawImage image = art.gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        Texture2D tex = Resources.Load<Texture2D>(textureKey);
        if (tex != null)
        {
            image.texture = tex;
            image.color = new Color(1f, 1f, 1f, alpha);
        }
        else image.color = new Color(0.12f, 0.14f, 0.18f, 1f);

        Button b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = image;
        ColorBlock colors = b.colors;
        // Hover fades the art more transparent (base 0.82 x ~0.49 -> ~0.4) but NOT to the old
        // near-invisible ~0.15. Still more transparent than the resting state, just gentler.
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.49f);
        colors.pressedColor = new Color(0.4f, 0.9f, 0.7f, 1f);
        colors.selectedColor = Color.white;
        b.colors = colors;
        b.onClick.AddListener(() => { if (onClick != null) onClick(); });
        return b;
    }

    /// <summary>Wires hover events on a button to show/hide a tooltip label.</summary>
    public static void WireHover(Button button, Text tooltip, string label)
    {
        EventTrigger trigger = button.gameObject.AddComponent<EventTrigger>();
        EventTrigger.Entry enter = new EventTrigger.Entry();
        enter.eventID = EventTriggerType.PointerEnter;
        enter.callback.AddListener(delegate
        {
            tooltip.text = label;
            tooltip.gameObject.SetActive(true);
        });
        trigger.triggers.Add(enter);

        EventTrigger.Entry exit = new EventTrigger.Entry();
        exit.eventID = EventTriggerType.PointerExit;
        exit.callback.AddListener(delegate { tooltip.gameObject.SetActive(false); });
        trigger.triggers.Add(exit);
    }

    /// <summary>Child rect stretched over its parent, inset by <paramref name="amount"/> px on
    /// every edge. Used to nest a RawImage inside a bordered button cell.</summary>
    public static RectTransform Inset(RectTransform parent, float amount)
    {
        GameObject go = new GameObject("Art", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        StretchToFull(rt);
        rt.offsetMin = new Vector2(amount, amount);
        rt.offsetMax = new Vector2(-amount, -amount);
        return rt;
    }

    /// <summary>Thin coloured strip laid over its parent's edge (pivoted so the strip is centred
    /// on the edge line). Raycast is off - the cell underneath owns the click.</summary>
    private static void AddBorderStrip(RectTransform parent, string name, Vector2 stripSize,
        Vector2 centerPos, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = centerPos;
        rt.sizeDelta = stripSize;
        rt.GetComponent<Image>().raycastTarget = false;
        rt.GetComponent<Image>().color = color;
    }

    // ------------------------------------------------------------- baked star sprites
    // WebGL IL2CPP builds bake the default font without exotic glyphs, so text stars ("\u2605")
    // render blank on web while they work in the editor. Every star in the UI must be a baked
    // sprite instead, matching the coin disc the wallet badge already bakes.

    private static Sprite starSprite;

    /// <summary>Bakes a bright gold five-pointed star once and caches the sprite.</summary>
    public static Sprite StarSprite()
    {
        if (starSprite != null) return starSprite;
        const int S = 64;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[S * S];
        Vector2[] poly = StarPolygon(S * 0.5f, S * 0.5f, S * 0.46f, S * 0.28f);
        Color baseGold = new Color(1f, 0.86f, 0.3f, 1f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                if (!PointInPolygon(new Vector2(x + 0.5f, y + 0.5f), poly))
                {
                    px[y * S + x] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }
                // Brighten toward the upper-left sheen with a hot white core, like the coin disc.
                float distCorner = Vector2.Distance(new Vector2(x, y), new Vector2(S * 0.33f, S * 0.33f));
                float distCore = Vector2.Distance(new Vector2(x, y), new Vector2(S * 0.46f, S * 0.46f));
                float shine = Mathf.Clamp01(1f - distCorner / (S * 0.55f));
                float core = Mathf.Clamp01(1f - distCore / (S * 0.20f));
                Color col = baseGold * Mathf.Clamp01(0.52f + 0.26f * shine + 0.4f * core);
                col.a = 1f;
                px[y * S + x] = col;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        starSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        starSprite.name = "BakedStar";
        return starSprite;
    }

    /// <summary>Builds a left-to-right star ladder: the first <paramref name="bright"/> in gold,
    /// the rest dim, all sized <paramref name="size"/> and spaced by <paramref name="gap"/>.
    /// <paramref name="anchor"/> is the pivot/alignment corner (e.g. (0,1) below a top-left edge,
    /// (0.5f,0.5f) for a centred card); <paramref name="firstCenter"/> is the CENTRE of the first
    /// star measured in that anchor's pixel space.</summary>
    public static Image[] StarLadder(Transform parent, string name, int bright, int total,
        Vector2 anchor, Vector2 firstCenter, float size, float gap)
    {
        Color[] gold = new Color[Mathf.Max(1, total)];
        for (int i = 0; i < gold.Length; i++) gold[i] = new Color(1f, 0.88f, 0.32f, 1f);
        return StarLadder(parent, name, total, anchor, firstCenter, size, gap, gold, bright);
    }

    /// <summary>Star ladder where every slot shows its own colour (e.g. the four ridge grades):
    /// slot i glows in <paramref name="colors"/>[i] once reached (i &lt; <paramref name="bright"/>),
    /// otherwise it renders as the dim empty star used on harvest rows.</summary>
    public static Image[] StarLadder(Transform parent, string name, int total,
        Vector2 anchor, Vector2 firstCenter, float size, float gap, Color[] colors, int bright)
    {
        Image[] stars = new Image[total];
        for (int i = 0; i < total; i++)
        {
            // Offset from the star's centre back to the pivot point for this anchor corner.
            Vector2 pos = new Vector2(
                firstCenter.x + i * (size + gap) + (anchor.x == 0f ? -size / 2f : anchor.x == 0.5f ? 0f : size / 2f),
                firstCenter.y + (anchor.y == 1f ? size / 2f : anchor.y == 0f ? -size / 2f : 0f));
            RectTransform rt = Rect(parent, name + "_" + i, anchor, pos, new Vector2(size, size));
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = StarSprite();
            img.raycastTarget = false;
            Color c = i < colors.Length ? colors[i] : new Color(1f, 0.88f, 0.32f, 1f);
            img.color = i < bright ? c : new Color(0.3f, 0.32f, 0.4f, 0.6f);
            stars[i] = img;
        }
        return stars;
    }

    private static Vector2[] StarPolygon(float cx, float cy, float outer, float inner)
    {
        Vector2[] pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float rad = (i % 2 == 0) ? outer : inner;
            float a = (90f + i * 36f) * Mathf.Deg2Rad; // first point on top
            pts[i] = new Vector2(cx + Mathf.Cos(a) * rad, cy + Mathf.Sin(a) * rad);
        }
        return pts;
    }

    private static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                inside = !inside;
        }
        return inside;
    }
}