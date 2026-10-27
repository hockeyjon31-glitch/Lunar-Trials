using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns two jobs on Newgrounds only: ticking <see cref="NewgroundsBridge"/>, and offering the
/// platform's own sign-in so a guest's medals and scores have an account to land on.
///
/// The whole component is created only when <see cref="NewgroundsBridge.Enabled"/> is true after
/// Init. On itch.io, CrazyGames, Kongregate or in the editor <see cref="Ensure"/> returns before
/// any GameObject exists, so foreign builds pay nothing: no canvas, no polling, no network. The
/// gate is purely runtime, so one binary is correct on every portal - the same pattern as
/// <see cref="KongregateSession"/>.
///
/// Deliberately minimal chrome: a single top-left button, shown only while the API is connected
/// and nobody is logged in. This corner is free (FullscreenButton is bottom-right, Kongregate's
/// register control is top-right), and signing in is optional - the game is fully playable as a
/// guest, it just banks no medals.
/// </summary>
public class NewgroundsSession : MonoBehaviour
{
    private static readonly Color ButtonBg = new Color(0.05f, 0.08f, 0.16f, 0.62f);
    private static readonly Color ButtonText = new Color(0.85f, 0.92f, 1f, 1f);

    private CanvasGroup loginGroup;
    private bool showing;

    /// <summary>Creates the driver once per session, above every scene's own UI.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Ensure()
    {
        if (FindAnyObjectByType<NewgroundsSession>() != null) return;

        // Host detection happens here rather than behind a compile-time define, so one binary is
        // correct everywhere and there is no prepare/finish step that a future build could forget.
        NewgroundsBridge.Init();
        if (!NewgroundsBridge.Enabled) return;

        GameObject go = new GameObject("NewgroundsSession");
        DontDestroyOnLoad(go);
        go.AddComponent<NewgroundsSession>();
    }

    private void Awake()
    {
        NewgroundsBridge.Attach(this);
    }

    private void Start()
    {
        UiKit.EnsureEventSystem();

        Canvas canvas = UiKit.CreateRootCanvas("NewgroundsCanvas", 33000);   // above every menu/gameplay canvas
        canvas.transform.SetParent(transform, false);

        Button button = UiKit.Button(canvas.transform, "LoginButton", "LOG IN WITH NEWGROUNDS", 22,
            ButtonBg, ButtonText, new Vector2(22f, -22f), new Vector2(320f, 56f), OnLoginClicked);
        loginGroup = button.gameObject.AddComponent<CanvasGroup>();
        SetVisible(false);
    }

    private void Update()
    {
        // Tick is self-rate-limited by the library (status fires on change) and returns
        // immediately off Newgrounds.
        NewgroundsBridge.Tick();
        Refresh();
    }

    /// <summary>Offer sign-in only while connected and genuinely not logged in; hide it the
    /// moment a user exists or the login page is open in another tab.</summary>
    private void Refresh()
    {
        bool want = NewgroundsBridge.Ready && !NewgroundsBridge.HasUser && !NewgroundsBridge.LoginOpen;
        if (want == showing) return;
        showing = want;
        SetVisible(want);
    }

    private void SetVisible(bool shown)
    {
        if (loginGroup == null) return;
        loginGroup.alpha = shown ? 1f : 0f;
        loginGroup.interactable = shown;
        loginGroup.blocksRaycasts = shown;
    }

    private void OnLoginClicked()
    {
        // Opens the Newgrounds login page in a new tab. Once the visitor finishes, the next
        // Refresh sees the new user, hides this button, and NewgroundsBridge back-fills the
        // medal ladder and any secret awards earned as a guest.
        NGIO.OpenLoginPage();
    }
}
