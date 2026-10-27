using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns two jobs on Kongregate only: ticking <see cref="KongregateBridge"/>, and offering the
/// platform's own sign-up overlay to visitors who are not signed in.
///
/// Kongregate forbids account systems of its own, so a player who arrives as a guest has their
/// progress sitting on an anonymous profile that will not follow them anywhere. Their fix is the
/// client API's registration box, which raises in-page with no reload - we just have to give the
/// player a reason to press it, hence the button.
///
/// The whole component is created only when <c>LunarKongOnHost()</c> says the game is really
/// running on kongregate.com. On itch.io or CrazyGames <see cref="Ensure"/> returns before any
/// GameObject exists, so foreign builds pay nothing: no canvas, no polling, no network.
///
/// Built to match <see cref="FullscreenButton"/>'s shape (ScreenSpaceOverlay canvas above every
/// scene's own UI, 1920x1080 reference at match 0.5, UiKit helpers). The two cannot collide:
/// FullscreenButton stands down whenever the page is framed, and Kongregate always frames us.
/// This control sits top-right rather than bottom-right because that corner belongs to the
/// host's own chrome.
/// </summary>
public class KongregateSession : MonoBehaviour
{
    private CanvasGroup registerGroup;
    private bool showing;

    /// <summary>Creates the driver once per session, above every scene's own UI.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Ensure()
    {
        if (FindAnyObjectByType<KongregateSession>() != null) return;

        // Host detection happens here rather than behind a compile-time define, so one binary
        // is correct everywhere and there is no prepare/finish step that a future build could
        // forget to run (the mistake that shipped CrazySDK's SiteLock to a host it rejects).
        KongregateBridge.Init();
        if (!KongregateBridge.Enabled) return;

        GameObject go = new GameObject("KongregateSession");
        DontDestroyOnLoad(go);
        go.AddComponent<KongregateSession>();
    }

    private void Start()
    {
        UiKit.EnsureEventSystem();

        GameObject canvasGo = new GameObject("KongregateCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 33000;   // above every menu and gameplay canvas in the project
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.transform.SetParent(transform, false);

        Vector2 anchor = new Vector2(1f, 1f);
        Vector2 cornerPos = new Vector2(-22f, -22f);
        Vector2 size = new Vector2(300f, 56f);

        RectTransform rt = UiKit.Panel(transform, "RegisterButton", anchor, cornerPos, size,
            new Color(0.05f, 0.08f, 0.16f, 0.62f));
        Text label = UiKit.Label(rt, "Label", "REGISTER TO SAVE", 22,
            new Color(0.85f, 0.92f, 1f, 1f), Vector2.zero, TextAnchor.MiddleCenter,
            size.x, size.y);
        UiKit.StretchToFull(label.rectTransform);

        Button b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = rt.GetComponent<Image>();
        ColorBlock colors = b.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.3f);
        colors.pressedColor = new Color(0.4f, 0.9f, 0.7f, 1f);
        colors.selectedColor = Color.white;
        b.colors = colors;
        b.onClick.AddListener(OnRegisterClicked);

        registerGroup = rt.gameObject.AddComponent<CanvasGroup>();
        SetVisible(false);
    }

    private void Update()
    {
        // Poll is self rate-limited to 0.5s and returns immediately on a foreign host, so
        // calling it per-frame costs one float compare outside Kongregate.
        KongregateBridge.Poll();
        Refresh();
    }

    /// <summary>Only offer sign-up while the API is up AND the visitor is genuinely a guest.
    /// Either condition unmet and the control hides rather than presenting a button that would
    /// do nothing.</summary>
    private void Refresh()
    {
        bool want = KongregateBridge.Ready && KongregateBridge.IsGuest;
        if (want == showing) return;
        showing = want;
        SetVisible(want);
    }

    private void SetVisible(bool shown)
    {
        if (registerGroup == null) return;
        registerGroup.alpha = shown ? 1f : 0f;
        registerGroup.interactable = shown;
        registerGroup.blocksRaycasts = shown;
    }

    private void OnRegisterClicked()
    {
        // Raises Kongregate's own in-page sign-up form. Once the visitor finishes it, the
        // next Poll sees the new identity, adopts the platform name as the callsign and
        // re-submits the stat set so their records follow the fresh account.
        KongregateBridge.ShowRegistrationBox();
    }
}
