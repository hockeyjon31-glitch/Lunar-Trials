using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CrazyGames-only first-run tutorial coach (the Unity editor counts as "on", so the flow can
/// be tested there; localhost SDK-demo sessions pass too). Kongregate, itch.io and desktop
/// builds never see any of this - their runs stay exactly as they were.
///
/// Two jobs, both CrazyGames Full-Launch onboarding:
///   1. FAST LAUNCH  (see MenuController): the CG build lands straight in the first available
///      harvest run instead of the menu.
///   2. TUTORIALS: during each mode's FIRST run, a modal pop-up pauses the game and explains
///      one thing at a time, with a BIG FLASHING ARROW beside the HUD element being talked
///      about. The world stays frozen until the player clicks the big tick (or presses
///      Enter/Space), then play resumes exactly where it left off.
///        Harvester (first level only): the oxygen route + compass, what meteors do to the
///        hull, the green-lit final canister + pickup rocket, and what ore is worth;
///        Prospector (first run): dark navigation + hazards, the >=50% clear quota, and the
///        flashing sonar blips on the compass that point at nearby ore;
///        Ridge Survival (first run): landslides, and the chasm gaps (J / JUMP to hop).
///
/// Every tutorial is shown at most once per player (persisted through SaveBridge, so
/// cloud-saved like the rest of the progress). The modal's dim veil eats every click so the
/// paused run cannot be steered or paused through it, and the freeze uses a captured
/// timeScale (the same pattern as AdCoordinator) so nothing can drain, crash or advance
/// while the player reads.
/// </summary>
public class TutorialCoach : MonoBehaviour
{
    /// <summary>True where the onboarding flow applies: the CrazyGames portal (or its localhost
    /// demo) and the editor. Everything else - other portals, plain web uploads, desktop -
    /// is a no-op.</summary>
    public static bool Enabled => CrazyGamesBridge.PortalLive || Application.isEditor;

    // ------------------------------------------------------------- persisted per-tutorial flags
    private const string KeyHarvestIntro = "lunar.tut.harvest.intro";
    private const string KeyHarvestHull = "lunar.tut.harvest.hull";
    private const string KeyHarvestPod = "lunar.tut.harvest.pod";
    private const string KeyHarvestOre = "lunar.tut.harvest.ore";
    private const string KeyProspectorIntro = "lunar.tut.prospector.intro";
    private const string KeyProspectorOre = "lunar.tut.prospector.ore";
    private const string KeyRidgeIntro = "lunar.tut.ridge.intro";
    private const string KeyRidgeGap = "lunar.tut.ridge.gap";

    /// <summary>Which HUD element the big flashing arrow should point at (screen-space HUD
    /// anchors matching HUD.cs: oxygen bar / resource tally / integrity bar top-left,
    /// compass dial bottom-left).</summary>
    public enum PointTarget { None, Compass, OxygenBar, HullMeter, TokenTally }

    private class Tutorial
    {
        public string title;
        public string body;
        public PointTarget target;
        public float delay;
    }

    private static TutorialCoach instance;
    private readonly Queue<Tutorial> tutorialQueue = new Queue<Tutorial>();
    private bool showing;
    private bool acknowledged;

    private RectTransform dim;
    private RectTransform card;
    private Text cardTitle;
    private Text cardBody;
    private RectTransform arrow;
    private Vector2 arrowBasePos;

    // Freeze bookkeeping (same shape as AdCoordinator's consent freeze).
    private static float savedTimeScale = 1f;
    private static int freezeDepth;

    // ------------------------------------------------------------------ entry points
    /// <summary>Called from LevelIntroSequence.HandOff - the moment control reaches the player
    /// (or the race countdown takes over). Posts the mode's first tutorial.</summary>
    public static void OnHandOff()
    {
        if (!Enabled) return;

        if (GameFlow.Mode == GameMode.Harvester && LevelManager.CurrentLevelIndex == 0)
        {
            Post(KeyHarvestIntro, "FOLLOW THE COMPASS",
                "Your oxygen drains constantly - drive to the glowing OXYGEN CANISTERS to refill it.\n" +
                "The compass dial (bottom left) ALWAYS points at your next canister, and a marker " +
                "on the screen edge shows the way when it is off-screen.",
                PointTarget.Compass);
        }
        else if (GameFlow.Mode == GameMode.Prospector)
        {
            orePingSeen = false;
            Post(KeyProspectorIntro, "PROSPECTOR RUN",
                "This canyon runs DARK - trust the compass and stay on the route. " +
                "Watch for rock falls and collapsing ground.",
                PointTarget.Compass);
            // The "find the ore" tutorial is NOT on a timer: it fires from HUD.Update the first
            // time the compass actually blinks at nearby ore (NotifyOrePing) - so the arrow and
            // the words land together with the thing they describe.
        }
        else if (GameFlow.IsRidgeSurvival)
        {
            Post(KeyRidgeIntro, "RIDE THE RIDGE",
                "Survive the crest for distance. Watch for LANDSLIDES - red hazard discs mark " +
                "collapsing ground ahead. Keep moving and never stop on a marked flank.",
                PointTarget.None);
            Post(KeyRidgeGap, "MIND THE GAPS",
                "Chasms break the crest. Build speed and tap J (the JUMP button on touch) to " +
                "hop them - a long fall ends the run.",
                PointTarget.None, 1.2f);
        }
    }

    /// <summary>First hull damage of the run (meteor blasts and boulder impacts both funnel
    /// through BuggyHealth.TakeDamage). Harvester's first level only, per the onboarding scope.
    /// Delayed slightly: TakeDamage fires mid-collision, so pausing that instant would freeze
    /// the rebound and camera shake mid-motion (a wildly shaking screen behind the box). A
    /// short beat of live play lets the hit land, then the modal explains it.</summary>
    public static void NotifyHullDamaged()
    {
        if (!OnFirstHarvestLevel()) return;
        Post(KeyHarvestHull, "HULL DAMAGE",
            "Meteor blasts - and boulder impacts - damage the buggy. Watch the red INTEGRITY " +
            "bar (top left): at 0% the run ends.",
            PointTarget.HullMeter, 1.2f);
    }

    /// <summary>An oxygen canister was collected. After the FIRST one: the refilled tank, the
    /// next canister on the compass, and the green-lit final one that calls the pickup rocket.</summary>
    public static void NotifyPodCollected(int podsCollected, bool isFinalPod)
    {
        if (!OnFirstHarvestLevel()) return;
        if (podsCollected != 1 || isFinalPod) return;
        Post(KeyHarvestPod, "TANK REFILLED",
            "The blue OXYGEN bar (top left) is full again - keep an eye on it as it drains.\n" +
            "The compass points at the next canister, and the GREEN-LIT canister is the last " +
            "one: collecting it calls the pickup rocket down to you.",
            PointTarget.OxygenBar);
    }

    /// <summary>First ore nugget of the run: what it is worth and where it actually banks.</summary>
    public static void NotifyOreCollected(int value)
    {
        if (!OnFirstHarvestLevel()) return;
        Post(KeyHarvestOre, "ORE IS TOKENS",
            "Every nugget you drive over banks TOKENS - watch the tally (top left) climb.\n" +
            "Load up, then reach the PICKUP ROCKET to keep them: lose the run and the lot is gone.",
            PointTarget.TokenTally);
    }

    private static bool OnFirstHarvestLevel()
    {
        return Enabled && GameFlow.Mode == GameMode.Harvester
            && LevelManager.CurrentLevelIndex == 0;
    }

    private static bool orePingSeen;

    /// <summary>The compass's proximity ping just lit up at nearby ore (HUD.Update drives this
    /// every frame the sonar is in range). Prospector runs only: this is the cue for the
    /// ore tutorial, so the message arrives together with the blips it describes. The latch
    /// keeps the per-frame caller from hammering the save store; Post's persisted flag makes
    /// it once-per-player.</summary>
    public static void NotifyOrePing()
    {
        if (!Enabled) return;
        if (GameFlow.Mode != GameMode.Prospector) return;
        if (orePingSeen) return;
        orePingSeen = true;
        // A short beat of live play so the player actually SEES the compass blinking before
        // the modal freezes the world to explain it.
        Post(KeyProspectorOre, "FIND THE ORE",
            "Those flashing blips on the compass point at nearby ore. Collect at least HALF " +
            "the field to clear the site - the tally shows your live percentage.",
            PointTarget.Compass, 1f);
    }

    /// <summary>Persists the shown-flag immediately and queues the modal. Marking at post time
    /// (not display time) keeps a quit-and-retry run from re-nagging a seen tutorial.</summary>
    private static void Post(string key, string title, string body, PointTarget target, float delay = 0f)
    {
        if (!Enabled) return;
        if (SaveBridge.GetInt(key, 0) != 0) return;
        SaveBridge.SetInt(key, 1);
        SaveBridge.Save();

        TutorialCoach coach = Ensure();
        coach.tutorialQueue.Enqueue(new Tutorial { title = title, body = body, target = target, delay = delay });
        if (!coach.showing) coach.StartCoroutine(coach.RunQueue());
    }

    private static TutorialCoach Ensure()
    {
        if (instance == null)
        {
            instance = new GameObject("TutorialCoach").AddComponent<TutorialCoach>();
        }
        return instance;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            // Never leak a frozen world into the next scene (quit-to-menu mid-tutorial etc.).
            while (freezeDepth > 0) PopFreeze();
        }
    }

    // ------------------------------------------------------------------ presentation
    private IEnumerator RunQueue()
    {
        showing = true;
        while (tutorialQueue.Count > 0)
        {
            Tutorial tut = tutorialQueue.Dequeue();
            if (tut.delay > 0f)
            {
                float waited = 0f;
                while (waited < tut.delay)
                {
                    // Live gameplay only: a pause or ad freeze holds the countdown.
                    if (Time.timeScale == 0f) { yield return null; continue; }
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            // Never open the modal over a frozen world, and NEVER mid camera shake: the shake
            // decays on scaled time, so freezing now would strand the view mid-jitter. Wait
            // for live, calm gameplay - however long that takes.
            while (Time.timeScale == 0f || BuggyCamera.AnyShakeActive) yield return null;
            yield return ShowTutorial(tut);
        }
        showing = false;
    }

    private IEnumerator ShowTutorial(Tutorial tut)
    {
        BuildUi();

        cardTitle.text = tut.title;
        cardBody.text = tut.body;
        LayoutArrow(tut.target);
        acknowledged = false;

        // Freeze the world for the read (captured timeScale, like AdCoordinator's consent).
        PushFreeze();

        // Flash in.
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.3f)
        {
            SetAlpha(Mathf.Clamp01(t));
            yield return null;
        }
        SetAlpha(1f);

        // Hold until the tick is clicked (or Enter/Space). A short guard keeps a key that is
        // already held down (Space = boost) from instantly dismissing the box unread.
        float shown = 0f;
        while (!acknowledged)
        {
            shown += Time.unscaledDeltaTime;
            if (shown > 0.5f && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space)))
            {
                acknowledged = true;
                break;
            }
            yield return null;
        }

        // Flash out, then hand the world back.
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.3f)
        {
            SetAlpha(Mathf.Clamp01(t));
            yield return null;
        }
        SetAlpha(0f);
        PopFreeze();
    }

    private void Update()
    {
        // The arrow flashes the whole time the modal is up (unscaled: the world is frozen).
        if (arrow != null && arrow.gameObject.activeSelf && cardGroup != null && cardGroup.alpha > 0.9f)
        {
            float flash = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f));
            arrowGroup.alpha = flash;
            // A gentle bob ALONG the arrow's pointing axis, toward the element it names.
            arrow.anchoredPosition = arrowBasePos + arrowBobDir
                * (Mathf.Sin(Time.unscaledTime * 5f) * 12f);
        }
    }

    private void SetAlpha(float a)
    {
        cardGroup.alpha = a;
        if (dimGroup != null) dimGroup.alpha = a;
        if (arrowGroup != null) arrowGroup.alpha = a * (arrow.gameObject.activeSelf ? 1f : 0f);

        // CRITICAL: raycast-blocking is tied to visibility. A CanvasGroup with alpha 0 still
        // raycasts by default, so a "hidden" dim veil would keep silently eating every click
        // beneath it (the star-ceremony OK button was the casualty) for the rest of the run.
        bool live = a > 0.01f;
        cardGroup.blocksRaycasts = live;
        if (dimGroup != null) dimGroup.blocksRaycasts = live;
    }

    private CanvasGroup cardGroup;
    private CanvasGroup dimGroup;
    private CanvasGroup arrowGroup;
    private Vector2 arrowBobDir = Vector2.left;

    private void BuildUi()
    {
        if (card != null) return;

        UiKit.EnsureEventSystem();
        Canvas canvas = UiKit.CreateRootCanvas("TutorialCanvas", 480);
        canvas.transform.SetParent(transform, false);

        // Full-screen dim veil: eats every click so the frozen run cannot be driven or paused
        // through while the tutorial is up. Faded by its own CanvasGroup (raycast stays on).
        dim = UiKit.Panel(canvas.transform, "TutorialDim", Vector2.zero, Vector2.zero, Vector2.zero,
            new Color(0.01f, 0.02f, 0.05f, 0.66f));
        UiKit.StretchToFull(dim);
        dimGroup = dim.gameObject.AddComponent<CanvasGroup>();

        // The modal card: big, centred, big text.
        card = UiKit.Panel(canvas.transform, "TutorialCard",
            new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(1240f, 470f),
            new Color(0.03f, 0.06f, 0.10f, 0.98f));
        cardGroup = card.gameObject.AddComponent<CanvasGroup>();

        cardTitle = UiKit.Label(card, "TutorialTitle", "", 44,
            new Color(1f, 0.88f, 0.5f, 1f), new Vector2(0f, -76f), TextAnchor.MiddleCenter, 1150f, 64f,
            UiKit.UiFontKind.Display);
        cardTitle.raycastTarget = false;

        cardBody = UiKit.Label(card, "TutorialBody", "", 27,
            new Color(0.87f, 0.96f, 0.92f, 1f), new Vector2(0f, -212f), TextAnchor.MiddleCenter, 1150f, 190f);
        cardBody.raycastTarget = false;
        cardBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        cardBody.verticalOverflow = VerticalWrapMode.Overflow;

        // The big tick button: green, with a baked white check mark and a GOT IT label.
        // (The panel's own Image is the button graphic - adding a second Image to the same
        // GameObject would leave the button's click target undefined.)
        RectTransform tick = UiKit.Panel(card, "TickButton",
            new Vector2(0.5f, 1f), new Vector2(0f, -400f), new Vector2(330f, 88f),
            new Color(0.14f, 0.48f, 0.29f, 1f));
        Image tickImg = tick.GetComponent<Image>();

        RectTransform tickIcon = UiKit.Rect(tick, "TickIcon", new Vector2(0f, 0.5f), new Vector2(62f, 0f), new Vector2(52f, 42f));
        Image iconImg = tickIcon.gameObject.AddComponent<Image>();
        iconImg.sprite = CheckSprite();
        iconImg.raycastTarget = false;

        Text tickLabel = UiKit.Label(tick, "TickLabel", "GOT IT", 34,
            new Color(0.88f, 1f, 0.92f, 1f), new Vector2(60f, 14f), TextAnchor.MiddleCenter, 230f, 60f,
            UiKit.UiFontKind.Display);
        tickLabel.raycastTarget = false;

        Button tickButton = tick.gameObject.AddComponent<Button>();
        tickButton.targetGraphic = tickImg;
        ColorBlock colors = tickButton.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.22f);
        colors.pressedColor = new Color(0.5f, 1f, 0.75f, 1f);
        colors.selectedColor = Color.white;
        tickButton.colors = colors;
        tickButton.onClick.AddListener(delegate { acknowledged = true; });

        // The big flashing arrow (sits OUTSIDE the card, beside the HUD element in question).
        // Long "=====>" shaft-and-head sprite; the pivot stays the rect CENTRE so the
        // 90/180-degree rotations never swing the arrow off its anchor.
        arrow = UiKit.Rect(canvas.transform, "TutorialArrow",
            new Vector2(0f, 0f), new Vector2(430f, 115f), new Vector2(190f, 60f));
        arrow.pivot = new Vector2(0.5f, 0.5f);
        Image arrowImg = arrow.gameObject.AddComponent<Image>();
        arrowImg.sprite = ArrowSprite();
        arrowImg.raycastTarget = false;
        arrowGroup = arrow.gameObject.AddComponent<CanvasGroup>();
        arrowGroup.interactable = false;
        arrowGroup.blocksRaycasts = false;

        SetAlpha(0f);
    }

    /// <summary>Parks the big flashing arrow beside the HUD element the tutorial talks about.
    /// Coordinates mirror HUD.cs, and the arrow's ANCHOR must match the element's corner too.
    /// NOTE the top-left corner: anchored rects measure +y UP, so anything hanging below the
    /// top edge needs NEGATIVE y (this is exactly why the first arrow pass sat off-screen).
    /// The sprite points RIGHT by default; a z-rotation aims it at the element.</summary>
    private void LayoutArrow(PointTarget target)
    {
        Vector2 anchor = new Vector2(0f, 0f);
        arrowBobDir = Vector2.left;

        switch (target)
        {
            case PointTarget.Compass:
                // Bottom-left dial: arrow to its RIGHT, pointing LEFT at it.
                anchor = new Vector2(0f, 0f);
                arrowBasePos = new Vector2(430f, 115f);
                arrowBobDir = Vector2.left;
                arrow.localRotation = Quaternion.Euler(0f, 0f, 180f);
                break;
            case PointTarget.OxygenBar:
                // Blue oxygen bar (top-left, x 40..460, y 40..74 from top): arrow to its
                // RIGHT, vertically centred on the bar, pointing LEFT at it.
                anchor = new Vector2(0f, 1f);
                arrowBasePos = new Vector2(560f, -57f);
                arrowBobDir = Vector2.left;
                arrow.localRotation = Quaternion.Euler(0f, 0f, 180f);
                break;
            case PointTarget.HullMeter:
                // Red integrity bar (top-left, x 40..400, y 232..254 from top): arrow to its
                // RIGHT, vertically centred on the bar, pointing LEFT at it.
                anchor = new Vector2(0f, 1f);
                arrowBasePos = new Vector2(500f, -243f);
                arrowBobDir = Vector2.left;
                arrow.localRotation = Quaternion.Euler(0f, 0f, 180f);
                break;
            case PointTarget.TokenTally:
                // The ore tally is the TOP-LEFT resource panel (40..400 x, y 161..203 from
                // top): arrow to its RIGHT, vertically centred on it, pointing LEFT at it.
                anchor = new Vector2(0f, 1f);
                arrowBasePos = new Vector2(495f, -182f);
                arrowBobDir = Vector2.left;
                arrow.localRotation = Quaternion.Euler(0f, 0f, 180f);
                break;
            default:
                anchor = new Vector2(0f, 0f);
                arrowBasePos = new Vector2(430f, 115f);
                arrowBobDir = Vector2.left;
                arrow.localRotation = Quaternion.identity;
                break;
        }

        // The arrow is built once with a bottom-left anchor - every other corner needs the
        // anchor moved to match, or the position is measured from the wrong corner.
        arrow.anchorMin = anchor;
        arrow.anchorMax = anchor;
        arrow.pivot = new Vector2(0.5f, 0.5f);

        bool show = target != PointTarget.None;
        arrow.gameObject.SetActive(show);
        if (arrow.gameObject.activeSelf)
        {
            arrow.anchoredPosition = arrowBasePos;
        }
    }

    // ------------------------------------------------------------------ the freeze
    private static void PushFreeze()
    {
        if (freezeDepth == 0)
        {
            savedTimeScale = Time.timeScale;
            AudioListener.pause = true;
        }
        freezeDepth++;
        Time.timeScale = 0f;
    }

    private static void PopFreeze()
    {
        if (freezeDepth <= 0) return;
        freezeDepth--;
        if (freezeDepth == 0)
        {
            Time.timeScale = savedTimeScale;
            AudioListener.pause = false;
        }
    }

    // ------------------------------------------------------------------ baked sprites
    // Drawn geometry, same WebGL rationale as the baked stars: glyph arrows / check marks are
    // stripped from baked web fonts, textures never are.

    private static Sprite arrowSprite;

    /// <summary>Classic long arrow pointing RIGHT ("=====>" with a solid triangular head):
    /// a thick horizontal shaft with a broad head. Rotations aim it up/left/down; the
    /// RectTransform's centre pivot keeps every rotation in place.</summary>
    private static Sprite ArrowSprite()
    {
        if (arrowSprite != null) return arrowSprite;
        const int w = 190;
        const int h = 60;
        const int headStart = 110;   // x where the triangular head begins
        const int shaftHalf = 11;    // shaft half-thickness (22px shaft)
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[w * h];
        float cy = (h - 1) * 0.5f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool inShaft = x < headStart + 6 && Mathf.Abs(y - cy) <= shaftHalf;
                // Head: full height where it meets the shaft, tapering to a TIP at the far
                // right edge - so the point aims the way the arrow travels.
                bool inHead = false;
                if (x >= headStart)
                {
                    float frac = (x - headStart) / (float)(w - 1 - headStart);   // 0..1 along the head
                    float halfH = (1f - frac) * (h * 0.5f);
                    inHead = Mathf.Abs(y - cy) <= halfH;
                }

                if (!inShaft && !inHead)
                {
                    px[y * w + x] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }
                // Brighter toward the upper edge, like the baked stars and coin.
                float shine = Mathf.Clamp01(1f - (y - cy) / (h * 0.9f) - x / (w * 2.2f));
                Color col = Color.Lerp(new Color(1f, 0.72f, 0.18f, 1f), new Color(1f, 0.96f, 0.62f, 1f), shine);
                px[y * w + x] = col;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        arrowSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        arrowSprite.name = "TutorialArrow";
        return arrowSprite;
    }

    private static Sprite checkSprite;

    /// <summary>Thick white check mark drawn as two rounded strokes (for the tick button).</summary>
    private static Sprite CheckSprite()
    {
        if (checkSprite != null) return checkSprite;
        const int s = 96;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[s * s];
        Vector2 a = new Vector2(20f, 50f);
        Vector2 b = new Vector2(40f, 30f);
        Vector2 c = new Vector2(78f, 70f);
        const float stroke = 8.5f;
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float d1 = DistanceToSegment(new Vector2(x, y), a, b);
                float d2 = DistanceToSegment(new Vector2(x, y), b, c);
                float d = Mathf.Min(d1, d2);
                float alpha = Mathf.Clamp01(stroke - d + 1f);
                px[y * s + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        checkSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        checkSprite.name = "TutorialCheck";
        return checkSprite;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        float t = lenSq > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }
}
