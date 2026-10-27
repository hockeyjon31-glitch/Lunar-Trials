using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Bottom-right fullscreen control for embed hosts that give the game no chrome of their own
/// (itch.io, and a plain browser tab).
///
/// Unity's own fullscreen button is deliberately stripped from index.html on every build we
/// ship - the CrazyGames portal frames the game fullscreen itself and owns that decision - so
/// without this control a CrazyGames-free build has no way to go fullscreen at all. This adds
/// it back in-engine, driving the browser Fullscreen API through LunarExtras.jslib.
///
/// Shape of the control:
///   - windowed: a dim "FULLSCREEN" button sits in the bottom-right corner.
///   - fullscreen: that button is HIDDEN. An invisible hit area covering the same corner is all
///     that is left, and moving the pointer into it fades in an "EXIT FULLSCREEN" button, so the
///     way out is deliberately a little hidden rather than a second permanent control.
///
/// State is POLLED (LunarIsFullscreen) instead of pushed from JS, because the player can leave
/// fullscreen with Esc or F11 and the browser handles those without Unity ever seeing an event.
///
/// Not created on the CrazyGames portal: the platform owns fullscreen there, and
/// LunarOnCrazyGamesHost is checked once at startup so the control stays off it in every build,
/// SDK or not.
///
/// Also not created when the page is running inside an iframe (itch.io's embed, the CrazyGames
/// portal). A framed document can only reach real fullscreen if the host's iframe policy allows
/// it - which is what the "FULLSCREEN BLOCKED BY THIS HOST" notice is for - while the host
/// already provides a fullscreen control in this same corner. Those hosts resize the iframe and
/// the canvas now fills it (see the index.html patch in LunarBuggyMenu), so their button is the
/// right one to use, and standing down leaves it unblocked.
public class FullscreenButton : MonoBehaviour
{
    // ------------------------------------------------------------------ native
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void LunarRequestFullscreen();
    [DllImport("__Internal")] private static extern void LunarExitFullscreen();
    [DllImport("__Internal")] private static extern int LunarIsFullscreen();
    [DllImport("__Internal")] private static extern int LunarOnCrazyGamesHost();
    [DllImport("__Internal")] private static extern int LunarIsFramed();
#else
    // Editor and native players: there is no browser to ask, and Unity's own fullscreen
    // handling is already correct, so the control stays out of the way.
    private static void LunarRequestFullscreen() { }
    private static void LunarExitFullscreen() { }
    private static int LunarIsFullscreen() { return 0; }
    private static int LunarOnCrazyGamesHost() { return 0; }
    private static int LunarIsFramed() { return 0; }
#endif

    private const float PollInterval = 0.2f;
    private const float FadeSpeed = 6f;
    // requestFullscreen resolves asynchronously, so a refusal can only be called a moment
    // after the click - long enough for a real request to have taken effect.
    private const float RefusalGrace = 0.6f;

    [Header("Placement")]
    [Tooltip("Inset from the bottom-right corner, in reference-resolution pixels.")]
    public Vector2 margin = new Vector2(22f, 22f);
    [Tooltip("Size of the visible FULLSCREEN / EXIT FULLSCREEN buttons.")]
    public Vector2 buttonSize = new Vector2(232f, 62f);
    [Tooltip("Size of the invisible corner hit area that reveals EXIT FULLSCREEN. It overhangs "
            + "the button by half the difference on each axis, so a slightly larger area is a "
            + "little easier to find without changing where the button appears.")]
    public Vector2 hoverZoneSize = new Vector2(300f, 130f);

    private CanvasGroup enterGroup;   // the always-there "FULLSCREEN" button
    private CanvasGroup exitGroup;    // the hover-revealed "EXIT FULLSCREEN" button
    private Text notice;              // transient "host refused" message
    private bool fullscreen;
    private float nextPollAt;
    private float noticeUntil;
    private float refusalCheckAt;     // > 0 while a request is still being given time to land

    /// <summary>Creates the control once per session, above every scene's own UI.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Ensure()
    {
        if (FindAnyObjectByType<FullscreenButton>() != null) return;
        if (LunarOnCrazyGamesHost() != 0) return;   // the portal frames the game itself

        // Inside an iframe, a framed document cannot be promoted to real fullscreen unless the
        // host's iframe policy allows it, and the host already ships its own fullscreen button
        // in this same bottom-right corner. Standing down here leaves that working control
        // visible and avoids two buttons fighting over the corner.
        if (LunarIsFramed() != 0) return;

        GameObject go = new GameObject("FullscreenButton");
        DontDestroyOnLoad(go);
        go.AddComponent<FullscreenButton>();
    }

    private void Start()
    {
        UiKit.EnsureEventSystem();

        GameObject canvasGo = new GameObject("FullscreenCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;   // above every menu and gameplay canvas in the project
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.transform.SetParent(transform, false);

        // Bottom-right anchor, shared by both controls so one coordinate places either.
        Vector2 anchor = new Vector2(1f, 0f);
        Vector2 cornerPos = new Vector2(-margin.x, margin.y);

        // ---- the always-visible FULLSCREEN button
        enterGroup = BuildButton(transform, "EnterFullscreen", "FULLSCREEN", anchor, cornerPos,
            buttonSize, new Color(0.05f, 0.08f, 0.16f, 0.62f), OnEnterClicked);
        SetVisible(enterGroup, true, true);

        // ---- the invisible corner hover area. A sibling, NOT a child of the enter button: it
        //      has to keep receiving hover while that button is hidden.
        RectTransform zone = UiKit.Rect(transform, "ExitHoverZone", anchor, cornerPos, hoverZoneSize);
        Image hit = zone.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0.004f);   // invisible, but still a raycast target
        hit.raycastTarget = true;

        // The revealed control lives INSIDE the hover area: PointerExit only fires when the
        // pointer leaves the whole zone, so the button stays reachable the whole time the
        // cursor is inside it. Half the zone/button difference centres it in the zone, which
        // lands it on the same corner the enter button occupies.
        Vector2 overhang = (hoverZoneSize - buttonSize) * 0.5f;
        exitGroup = BuildButton(zone, "ExitFullscreen", "EXIT FULLSCREEN", new Vector2(1f, 0f),
            overhang, buttonSize, new Color(0.05f, 0.08f, 0.16f, 0.62f), OnExitClicked);
        SetVisible(exitGroup, false, false);

        EventTrigger trigger = zone.gameObject.AddComponent<EventTrigger>();
        EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(delegate { SetVisible(exitGroup, true, true); });
        trigger.triggers.Add(enter);
        EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(delegate { SetVisible(exitGroup, false, false); });
        trigger.triggers.Add(exit);

        // ---- transient message, just above the corner
        notice = UiKit.Label(transform, "FullscreenNotice", "", 22,
            new Color(1f, 0.86f, 0.35f, 1f), Vector2.zero, TextAnchor.LowerRight, 820f, 40f);
        RectTransform nrt = notice.rectTransform;
        nrt.anchorMin = anchor; nrt.anchorMax = anchor; nrt.pivot = anchor;
        nrt.anchoredPosition = new Vector2(-margin.x, margin.y + buttonSize.y + 14f);
        notice.gameObject.SetActive(false);
    }

    /// <summary>One control as a CanvasGroup-wrapped button, so it can fade without losing its
    /// raycasts. The group drives alpha AND interactivity, which an image colour cannot.</summary>
    private CanvasGroup BuildButton(Transform parent, string name, string label, Vector2 anchor,
        Vector2 anchoredPos, Vector2 size, Color background, System.Action onClick)
    {
        RectTransform rt = UiKit.Panel(parent, name, anchor, anchoredPos, size, background);
        Text text = UiKit.Label(rt, "Label", label, 22, new Color(0.85f, 0.92f, 1f, 1f),
            Vector2.zero, TextAnchor.MiddleCenter, size.x, size.y);
        UiKit.StretchToFull(text.rectTransform);
        Button b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = rt.GetComponent<Image>();
        ColorBlock colors = b.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.3f);
        colors.pressedColor = new Color(0.4f, 0.9f, 0.7f, 1f);
        colors.selectedColor = Color.white;
        b.colors = colors;
        b.onClick.AddListener(() => { if (onClick != null) onClick(); });
        return rt.gameObject.AddComponent<CanvasGroup>();
    }

    /// <summary>Alpha is animated separately by <see cref="Fade"/>, so this only sets whether
    /// the control is live. An invisible control must also be unclickable, or a fully faded
    /// button still swallows clicks in the corner.</summary>
    private static void SetVisible(CanvasGroup group, bool shown, bool live)
    {
        if (group == null) return;
        group.interactable = live;
        group.blocksRaycasts = live;
        if (shown) group.alpha = 1f;
    }

    private void OnEnterClicked()
    {
        LunarRequestFullscreen();
        // Give the async request a moment to land before assuming the host refused it.
        refusalCheckAt = Time.unscaledTime + RefusalGrace;
        Refresh();
    }

    private void OnExitClicked()
    {
        LunarExitFullscreen();
        refusalCheckAt = 0f;
        SetVisible(exitGroup, false, false);
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextPollAt)
        {
            nextPollAt = Time.unscaledTime + PollInterval;
            Refresh();
        }
        if (refusalCheckAt > 0f && Time.unscaledTime >= refusalCheckAt)
        {
            refusalCheckAt = 0f;
            if (!fullscreen) ShowNotice("FULLSCREEN BLOCKED BY THIS HOST");
        }
        Fade();
    }

    private void Refresh()
    {
        bool now = LunarIsFullscreen() != 0;
        if (now == fullscreen) return;
        fullscreen = now;

        // Windowed: show the way in, hide the way out. Fullscreen: the reverse, leaving only
        // the invisible corner the player has to find to get back out.
        SetVisible(enterGroup, !fullscreen, !fullscreen);
        if (fullscreen) SetVisible(exitGroup, false, false);
    }

    private void Fade()
    {
        if (enterGroup != null)
        {
            float target = fullscreen ? 0f : 1f;
            enterGroup.alpha = Mathf.MoveTowards(enterGroup.alpha, target, Time.unscaledDeltaTime * FadeSpeed);
        }
        if (exitGroup != null)
        {
            // A revealed control fades in; leaving fullscreen hides it without waiting for
            // the pointer to leave the zone, so the corner does not keep a live hit area.
            float target = exitGroup.interactable ? 1f : 0f;
            exitGroup.alpha = Mathf.MoveTowards(exitGroup.alpha, target, Time.unscaledDeltaTime * FadeSpeed);
        }
        if (notice != null && notice.gameObject.activeSelf)
        {
            if (Time.unscaledTime >= noticeUntil) notice.gameObject.SetActive(false);
            else
            {
                Color c = notice.color;
                c.a = Mathf.Clamp01((noticeUntil - Time.unscaledTime) / 0.6f);
                notice.color = c;
            }
        }
    }

    private void ShowNotice(string message)
    {
        if (notice == null || string.IsNullOrEmpty(message)) return;
        notice.text = message;
        Color c = notice.color; c.a = 1f; notice.color = c;
        notice.gameObject.SetActive(true);
        noticeUntil = Time.unscaledTime + 2.5f;
    }
}
