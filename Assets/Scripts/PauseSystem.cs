using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-play pause + mute. The pause button, P and (on WebGL) a hidden browser tab all
/// route through here: the run freezes on Time.timeScale = 0 while an overlay offers
/// RESUME, SOUND and QUIT TO MENU. Physical time is untouched so the overlay's own input
/// still works. Pausing is blocked while a level intro/outro cinematic owns the camera, so
/// the deploy hand-off can never see a half-locked buggy.
///
/// CrazyGames wants a game to stop when it is not visible, so losing the browser tab also
/// pauses the run - and the SDK lifecycle signal goes out with it.
/// </summary>
public class PauseSystem : MonoBehaviour
{
    private bool paused;
    private float prevTimeScale = 1f;
    private BuggyController buggy;
    private RoverSounds buggyAudio;
    private bool buggyWasEnabled;

    private GameObject menuRoot;
    private Text soundLabel;
    private Text soundLabel2;
    private float hiddenPollAt;

    private void Start()
    {
        buggy = FindAnyObjectByType<BuggyController>();
        buggyAudio = buggy != null ? buggy.GetComponent<RoverSounds>() : null;

        // The play scene ships without an EventSystem, so the buttons below would be dead.
        UiKit.EnsureEventSystem();

        BuildPauseButton();
        BuildOverlay();
    }

    // Above the HUD (100) so the buttons stay clickable and clear of the boost bar; the
    // pause overlay sits one step above its own buttons.
    private const int CanvasOrder = 500;

    private void OnDestroy()
    {
        // A scene change while paused must not leak a frozen world into the next scene.
        if (paused) Time.timeScale = 1f;
    }

    private void Update()
    {
        // WebGL does not reliably raise OnApplicationFocus on a tab switch, so poll the
        // page's visibility flag (the .jslib reads document.hidden).
        if (Time.unscaledTime - hiddenPollAt > 0.25f)
        {
            hiddenPollAt = Time.unscaledTime;
            if (!paused && CrazyGamesBridge.TabHidden && !CinematicActive()) Pause();
        }

        // P only: CrazyGames lists Escape as a restricted key because it drops the player
        // out of fullscreen, so pausing on it ate the fullscreen state.
        if (Input.GetKeyDown(KeyCode.P) && (paused || !CinematicActive()))
        {
            Toggle();
        }
    }

    /// <summary>Belt and braces for "player walked away": the poll above catches a hidden
    /// browser tab, these catch what the browser reports through Unity instead - switching
    /// apps on a phone, or losing window focus on desktop. This never auto-resumes; the
    /// player returns to the pause menu.</summary>
    private void OnApplicationFocus(bool hasFocus)
    {
#if !UNITY_EDITOR
        if (!hasFocus && !paused && !CinematicActive()) Pause();
#endif
    }

    private void OnApplicationPause(bool pauseStatus)
    {
#if !UNITY_EDITOR
        if (pauseStatus && !paused && !CinematicActive()) Pause();
#endif
    }

    /// <summary>The level's intro/outro sequence owns the camera and the input lock while it
    /// runs, so pause stays out of its way. Both sequences outlive their own cinematic (the
    /// end sequence is built at level start, the intro object can linger), so this checks
    /// whether they are actually playing - not merely present.</summary>
    private static bool CinematicActive()
    {
        LevelIntroSequence intro = FindAnyObjectByType<LevelIntroSequence>();
        if (intro != null && intro.IsRunning) return true;
        LevelEndSequence end = FindAnyObjectByType<LevelEndSequence>();
        if (end != null && end.IsRunning) return true;
        return false;
    }

    public void Toggle()
    {
        if (paused) Resume();
        else Pause();
    }

    public void Pause()
    {
        if (paused) return;
        paused = true;
        prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        if (buggy != null)
        {
            buggyWasEnabled = buggy.inputEnabled;
            buggy.inputEnabled = false;
        }
        if (buggyAudio != null) buggyAudio.SetDriveSuspended(true);
        if (menuRoot != null) menuRoot.SetActive(true);
        RefreshLabels();
        CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.GameplayStop);
    }

    public void Resume()
    {
        if (!paused) return;
        paused = false;
        Time.timeScale = prevTimeScale;
        if (buggy != null) buggy.inputEnabled = buggyWasEnabled;
        if (buggyAudio != null) buggyAudio.SetDriveSuspended(false);
        if (menuRoot != null) menuRoot.SetActive(false);
        CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.GameplayStart);
    }

    public void QuitToMenu()
    {
        paused = false;
        Time.timeScale = 1f;
        GameFlow.GoToMenu();
    }

    private void ToggleSound()
    {
        GameSettings.ToggleMute();
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        if (soundLabel != null) soundLabel.text = GameSettings.Label;
        if (soundLabel2 != null) soundLabel2.text = GameSettings.Label;
    }

    // ------------------------------------------------------------- always-on buttons
    // Its own canvas above the HUD so the buttons stay clickable and sit clear of the
    // boost bar (top-right, below them).
    private void BuildPauseButton()
    {
        Canvas canvas = UiKit.CreateRootCanvas("SystemCanvas", CanvasOrder);
        canvas.transform.SetParent(transform, false);

        Button sound = MakeSmallButton(canvas.transform, "SoundButton", GameSettings.Label, new Vector2(-40f, -2f), () => ToggleSound());
        soundLabel2 = sound.GetComponentInChildren<Text>();
        MakeSmallButton(canvas.transform, "PauseButton", "PAUSE", new Vector2(-168f, -2f), Toggle);
    }

    private Button MakeSmallButton(Transform parent, string name, string label, Vector2 anchoredPos, System.Action onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(120f, 36f);
        Image img = go.GetComponent<Image>();
        img.color = new Color(0.06f, 0.08f, 0.1f, 0.75f);

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(go.transform, false);
        RectTransform lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        Text t = labelGo.GetComponent<Text>();
        t.font = UiKit.ResolveFont(UiKit.UiFontKind.Display);
        t.fontSize = 20;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(0.85f, 0.95f, 1f, 1f);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.text = label;

        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(() => { if (onClick != null) onClick(); });
        return b;
    }

    // ------------------------------------------------------------- pause overlay
    private void BuildOverlay()
    {
        Canvas canvas = UiKit.CreateRootCanvas("PauseCanvas", CanvasOrder + 1);
        canvas.transform.SetParent(transform, false);

        // menuRoot must be created first: the dim screen is its first child so it renders
        // behind the buttons AND disappears with them (a sibling dim stayed visible and
        // darkened the whole game while playing).
        menuRoot = new GameObject("PauseMenu", typeof(RectTransform));
        menuRoot.transform.SetParent(canvas.transform, false);
        UiKit.StretchToFull(menuRoot.GetComponent<RectTransform>());

        GameObject dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(menuRoot.transform, false);
        RectTransform dimRt = dim.GetComponent<RectTransform>();
        UiKit.StretchToFull(dimRt);
        Image dimImg = dim.GetComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.72f);

        MakeLabel(menuRoot.transform, "Title", "PAUSED", 72, new Vector2(0f, 170f), new Color(0.9f, 1f, 0.95f, 1f),
            UiKit.UiFontKind.Display);

        float y = 300f;
        MakeButton(menuRoot.transform, "ResumeButton", "RESUME", y, new Vector2(520f, 84f),
            new Color(0.12f, 0.4f, 0.24f, 1f), Resume);
        y -= 110f;

        Button sound = MakeButton(menuRoot.transform, "SoundButton", GameSettings.Label, y, new Vector2(520f, 78f),
            new Color(0.14f, 0.2f, 0.34f, 1f), ToggleSound);
        soundLabel = sound.GetComponentInChildren<Text>();
        y -= 95f;

        MakeButton(menuRoot.transform, "QuitButton", "QUIT TO MENU", y, new Vector2(520f, 78f),
            new Color(0.42f, 0.16f, 0.14f, 1f), QuitToMenu);

        menuRoot.SetActive(false);
    }

    private void MakeLabel(Transform parent, string name, string text, int fontSize, Vector2 anchoredPos, Color color,
        UiKit.UiFontKind fontKind = UiKit.UiFontKind.Body)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(900f, fontSize * 1.4f);
        Text t = go.GetComponent<Text>();
        t.font = UiKit.ResolveFont(fontKind);
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.text = text;
    }

    private Button MakeButton(Transform parent, string name, string label, float y, Vector2 size, Color bg, System.Action onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y - 300f);
        rt.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.color = bg;

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(go.transform, false);
        RectTransform lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        Text t = labelGo.GetComponent<Text>();
        t.font = UiKit.ResolveFont(UiKit.UiFontKind.Display);
        t.fontSize = 34;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(1f, 1f, 1f, 0.95f);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.text = label;

        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(() => { if (onClick != null) onClick(); });
        return b;
    }
}
