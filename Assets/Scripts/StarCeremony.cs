using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pulsing / flash-in animation for a row of star Images (see UiKit.StarLadder). The world-select
/// map uses this to FLASH THE FRONTIER STAR - the level the player is currently on: stars already
/// passed stay lit, the next one breathes gently, every visit (autoHide &lt; 0 = breathe forever).
/// The end-of-run ceremony does NOT use this class any more - see StarCeremony, which streaks the
/// earned stars across the screen and settles them into an acknowledged popup.
/// </summary>
public sealed class StarBurst : MonoBehaviour
{
    /// <summary>First star slot animated (0-based, inclusive).</summary>
    public int fromSlot;
    /// <summary>Last star slot animated (exclusive); range is [fromSlot, toSlot).</summary>
    public int toSlot;
    [Tooltip("Breathing: scale bumps by this amount around 1.")]
    public float pulseAmp = 0.20f;
    [Tooltip("Breathing frequency (Hz). A slow rate reads as 'the star you're on'.")]
    public float pulseHz = 0.9f;

    private Image[] stars;
    private Color[] baseColors;

    public void Begin(Image[] images, int from, int toExclusive)
    {
        stars = images;
        fromSlot = Mathf.Max(0, from);
        toSlot = Mathf.Clamp(toExclusive, fromSlot, images != null ? images.Length : 0);
        baseColors = null;
        if (stars != null)
        {
            baseColors = new Color[stars.Length];
            for (int i = 0; i < stars.Length; i++)
                baseColors[i] = stars[i] != null ? stars[i].color : Color.white;
        }
    }

    private void OnDestroy()
    {
        if (stars == null || baseColors == null) return;
        for (int s = fromSlot; s < toSlot && s < stars.Length; s++)
        {
            Image img = stars[s];
            if (img == null) continue;
            img.transform.localScale = Vector3.one;
            if (s < baseColors.Length) img.color = baseColors[s];
        }
    }

    private void Update()
    {
        if (stars == null) return;
        float now = Time.time;
        for (int s = fromSlot; s < toSlot && s < stars.Length; s++)
        {
            Image img = stars[s];
            if (img == null) continue;
            float w = Mathf.Sin(now * pulseHz * 2f * Mathf.PI + s * 0.9f) * 0.5f + 0.5f;
            img.transform.localScale = Vector3.one * (1f + pulseAmp * w);
            Color bc = baseColors[s];
            img.color = Color.Lerp(bc, new Color(1f, 0.9f, 0.35f, bc.a), w * 0.85f);
        }
    }
}

/// <summary>
/// "You earned a new grade" celebration for the results boards (Harvest / Race / Ridge). The
/// popup box fades in FIRST and stays anchored on screen; THEN the earned stars streak in from
/// off-screen one by one and settle into their slots on it, with an OK button to acknowledge and
/// return to the regular board navigation. Nothing auto-hides; the player owns the closure (an
/// early dismiss would swallow the "nice" beat).
/// </summary>
public static class StarCeremony
{
    private static Canvas activeCanvas;

    /// <summary>Celebrates <paramref name="litCount"/> newly-earned stars of a <paramref name="total"/>
    /// ladder in their own colours. <paramref name="colors"/>[i] is the colour star slot i displays
    /// (gold for harvest/race, the four ridge grade colours for ridge). Replaces any earlier
    /// ceremony so two runs can never stack overlays.</summary>
    public static void Show(int litCount, int total, Color[] colors, string caption, string subtitle)
    {
        if (litCount <= 0 || total <= 0) return;
        if (activeCanvas != null) Object.Destroy(activeCanvas.gameObject);

        Canvas canvas = UiKit.CreateRootCanvas("StarCeremonyCanvas", 250);
        activeCanvas = canvas;
        canvas.gameObject.AddComponent<CeremonyPlayer>().Begin(litCount, total, colors, caption, subtitle);
    }

    /// <summary>Drives the popup-first, streak-after choreography on the ceremony canvas. All
    /// artwork is built here so the results board beneath stays a normal, navigable screen.</summary>
    private sealed class CeremonyPlayer : MonoBehaviour
    {
        [Tooltip("Pause after the popup is fully in before the first star streaks (seconds).")]
        public float preDelay = 0.45f;
        [Tooltip("Stagger between consecutive streaking stars (seconds).")]
        public float perStarDelay = 0.16f;
        [Tooltip("How long one star takes to streak into place (seconds).")]
        public float streakDuration = 0.70f;

        private RectTransform popup;
        private CanvasGroup popupGroup;
        private Text caption;
        private Text subtitle;
        private Image[] settledStars;
        private Image okImage;
        private Text okText;

        private struct Flyer
        {
            public Image img;
            public Vector2 from;
            public Vector2 ctrl;
            public Vector2 to;
            public float t0;
        }
        private Flyer[] flyers;
        private float popupAt;
        private float[] settleAt;
        private float startAt;
        private bool allSettled;
        private bool done;

        public void Begin(int litCount, int total, Color[] colors, string captionText, string subText)
        {
            BuildUi(litCount, total, colors, captionText, subText);

            int n = Mathf.Clamp(litCount, 0, total);
            flyers = new Flyer[n];
            settleAt = new float[n];
            RectTransform popupRt = popup;
            RectTransform[] slots = new RectTransform[total];
            for (int i = 0; i < total; i++)
            {
                Transform c = popupRt.Find("CerStars_" + i);
                slots[i] = c != null ? c as RectTransform : null;
            }

            for (int i = 0; i < n; i++)
            {
                RectTransform slot = slots[i];
                Vector2 target = slot != null
                    ? slot.anchoredPosition + popupRt.anchoredPosition
                    : new Vector2((i - total * 0.5f + 0.5f) * 90f, -40f);
                Image star = NewStar("CerFlight_" + i, colors != null && i < colors.Length ? colors[i] : Color.yellow);
                star.transform.localScale = Vector3.one * 0.7f;
                Vector2 from = new Vector2(-1500f, 140f + (i % 3) * 90f);
                Vector2 to = target;
                flyers[i] = new Flyer
                {
                    img = star,
                    from = from,
                    to = to,
                    ctrl = new Vector2((from.x + to.x) * 0.5f, Mathf.Max(from.y, to.y) + 150f),
                    t0 = startAt + preDelay + i * perStarDelay
                };
                settleAt[i] = flyers[i].t0 + streakDuration;
            }

            // The popup comes up FIRST (a short beat after start) so the stars have a visible
            // box to fly into; the streak begins only once it's fully in.
            popupAt = startAt + 0.05f;
            popupGroup.alpha = 0f;
            ApplyPopup(0f);
        }

        private void BuildUi(int litCount, int total, Color[] colors, string captionText, string subText)
        {
            startAt = Time.time;
            Color gold = new Color(1f, 0.88f, 0.32f, 1f);
            Color silver = new Color(0.85f, 0.88f, 0.96f, 1f);

            // The small popup: caption, subtitle, the star ladder, and the OK button. It fades in
            // first and sits anchored while the stars streak in, so the board beneath is only
            // ever lightly veiled.
            popup = UiKit.Rect(transform, "CerPopup", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 560f));
            Image panel = popup.gameObject.AddComponent<Image>();
            panel.color = new Color(0.045f, 0.075f, 0.095f, 0.97f);
            popupGroup = popup.gameObject.AddComponent<CanvasGroup>();

            caption = UiKit.Label(popup, "CerCaption", captionText, 34, gold,
                new Vector2(0f, -96f), TextAnchor.MiddleCenter, 840f, 54f);
            // Grade-5 clears have nothing left to unlock: the subtitle is simply omitted.
            if (!string.IsNullOrEmpty(subText))
            {
                subtitle = UiKit.Label(popup, "CerSub", subText, 22, silver,
                    new Vector2(0f, -148f), TextAnchor.MiddleCenter, 840f, 40f);
            }

            float size = total <= 4 ? 92f : 74f;
            float gap = total <= 4 ? 20f : 14f;
            Vector2 firstCenter = new Vector2(-(total - 1) * (size + gap) * 0.5f, -195f);
            Image[] ladder = UiKit.StarLadder(popup, "CerStars", total, new Vector2(0.5f, 0.5f),
                firstCenter, size, gap, colors, litCount);
            settledStars = ladder;
            // The box opens with every slot INVISIBLE - earned stars fly in and take their slots
            // as they land; the remaining slots are revealed dim once all of them are home.
            for (int i = 0; i < ladder.Length; i++)
            {
                Color c = ladder[i].color;
                c.a = 0f;
                ladder[i].color = c;
            }

            Button ok = UiKit.Button(popup, "B_CerOk", "OK", 24, new Color(0.16f, 0.42f, 0.30f, 1f),
                new Color(1f, 1f, 0.9f, 1f), new Vector2(0f, -270f), new Vector2(180f, 52f),
                () => { if (activeCanvas != null) Object.Destroy(activeCanvas.gameObject); });
            okImage = ok.GetComponent<Image>();
            okText = ok.GetComponentInChildren<Text>();
        }

        private Image NewStar(string name, Color tint)
        {
            RectTransform rt = UiKit.Rect(transform, name, new Vector2(0.5f, 0.5f), new Vector2(-1600f, 0f), new Vector2(64f, 64f));
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiKit.StarSprite();
            img.color = tint;
            img.raycastTarget = false;
            return img;
        }

        private void Update()
        {
            if (done) return;
            float now = Time.time;

            // Stage 1: the popup fades in FIRST - the stars wait off-screen (preDelay) and
            // streak afterwards, so there is always a visible box for them to fly into.
            float pop = Mathf.Clamp01((now - popupAt) / 0.30f);
            ApplyPopup(pop);

            // Stage 2: the earned stars streak across the screen into their slots on the popup.
            bool allLanding = true;
            for (int i = 0; i < flyers.Length; i++)
            {
                Flyer f = flyers[i];
                if (f.img == null) continue;
                float t = Mathf.Clamp01((now - f.t0) / streakDuration);
                if (t >= 1f)
                {
                    // Landed: hide the flyer now - the settled popup star takes over.
                    f.img.enabled = false;
                    continue;
                }
                allLanding = false;
                float e = t * t * t * (t * (6f * t - 15f) + 10f); // smootherstep - a lazy, unhurried arrival
                Vector2 p = Bezier(f.from, f.ctrl, f.to, e);
                f.img.rectTransform.anchoredPosition = p;
                f.img.transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.05f, Mathf.Sin(e * Mathf.PI));
            }

            // Stage 3: each landing star settles into place in order inside the popup.
            bool allDone = true;
            for (int i = 0; i < settleAt.Length; i++)
            {
                float s = Mathf.Clamp01((now - settleAt[i]) / 0.5f);
                Image img = settledStars != null && i < settledStars.Length ? settledStars[i] : null;
                if (img == null) continue;
                if (s < 1f)
                {
                    allDone = false;
                    float e = Mathf.Clamp01(s * 1.15f);            // rise gently into place...
                    float overshoot = Mathf.Sin(e * Mathf.PI) * 0.26f; // ...with a soft settle bob
                    img.transform.localScale = Vector3.one * (0.4f + 0.6f * e + overshoot);
                    Color c = img.color;
                    c.a = Mathf.Clamp01(e * 1.2f);
                    img.color = c;
                }
                else
                {
                    img.transform.localScale = Vector3.one;
                }
            }
            allSettled = allDone;
            if (allLanding && pop >= 1f && allSettled)
            {
                // All stars are home - reveal the unfilled slots as dim empties, then show OK.
                for (int i = flyers.Length; i < (settledStars != null ? settledStars.Length : 0); i++)
                {
                    Image img = settledStars[i];
                    if (img == null) continue;
                    Color c = img.color;
                    c.a = 0.6f;
                    img.color = c;
                }
                okImage.enabled = true;
                okText.enabled = true;
                done = true;
            }
        }

        private void ApplyPopup(float pop)
        {
            if (popupGroup == null) return;
            popupGroup.alpha = pop;
            popup.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, pop);
            // The box, caption and subtitle all arrive together - the stars follow after.
            if (caption != null) { Color c = caption.color; c.a = pop; caption.color = c; }
            if (subtitle != null) { Color c = subtitle.color; c.a = pop; subtitle.color = c; }
            if (okImage != null) okImage.enabled = pop > 0.5f && allSettled;
            if (okText != null) okText.enabled = pop > 0.5f && allSettled;
        }

        private static Vector2 Bezier(Vector2 a, Vector2 c, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }
    }
}