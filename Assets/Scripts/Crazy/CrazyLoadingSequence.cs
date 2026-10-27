using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if !NO_CRAZY_SDK
using CrazyGames;
#endif

/// <summary>
/// Boot loader for the CrazyGames SDK rule that the SDK must initialize before the game's own
/// menu runs. This component lives in the FIRST scene of the build (index 0, see the editor
/// build helper) and holds a splash screen until CrazySDK.Init has completed, then hands over
/// to the game's real menu scene. On any host where the SDK is absent (whitelisted partners like
/// itch.io, offline loads) it skips straight to the menu, so there is no load-time cost outside
/// the portal. A hard timeout guarantees the game can never strand itself on the splash.
/// </summary>
public class CrazyLoadingSequence : MonoBehaviour
{
    [Tooltip("Scene to transition to once the SDK is ready (or immediately when no SDK).")]
    public string nextSceneName = "Menu";

    [Tooltip("Portal brand cap: never hold the splash past this many seconds on the real "
            + "CrazyGames portal, even if the SDK is slow to initialize.")]
    public float maxWaitSeconds = 6f;

    [Tooltip("How long the splash holds on hosts that are NOT the portal (localhost, itch.io): "
            + "a fixed short brand beat. The SDK's own script loads from the CrazyGames CDN at "
            + "runtime, so on a blocked or offline connection init can never complete - the menu "
            + "must not wait on it. The bridge picks up readiness in the background instead.")]
    public float localBeatSeconds = 0.9f;

    private bool proceeded;

    private void Start()
    {
        BuildSplash();

#if NO_CRAZY_SDK
        // Foreign-host build (itch.io): the SDK is not compiled in at all, because its
        // sitelock would freeze the game before this scene ever loaded. Straight to the menu.
        Proceed();
#else
        if (CrazySDK.IsAvailable)
        {
            // Kick the SDK to life in the background either way.
            CrazySDK.Init(Proceed);

            if (IsPortalHost())
            {
                // Real CrazyGames portal / partner domain: the SDK script always loads here, so
                // hold the brand splash until initialization finishes (hard cap as a backstop).
                StartCoroutine(HoldTimeout());
            }
            else
            {
                // localhost / anything else: the SDK is best-effort (its script is fetched from
                // the CrazyGames CDN, so a blocked or offline connection stalls init forever).
                // Pass through after a fixed short beat and let the bridge reconcile readiness
                // in the background - never make the menu sit on a splash that cannot resolve.
                StartCoroutine(ProceedAfter(localBeatSeconds));
            }
        }
        else
        {
            Proceed();
        }
#endif
    }

    /// <summary>True when running on the actual CrazyGames portal (or their dev mirror), where
    /// the SDK script is guaranteed to load. Everywhere else (localhost, itch.io, plain hosts)
    /// the SDK is treated as best-effort and must not gate the boot splash.</summary>
    private static bool IsPortalHost()
    {
        string url = Application.absoluteURL;
        if (string.IsNullOrEmpty(url)) return false;
        try
        {
            string host = new System.Uri(url).Host;
            return host.EndsWith("crazygames.com") || host.EndsWith("dev-crazygames.be");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Safety net (portal path only): if the SDK never reports ready, proceed to the
    /// menu rather than sitting on the splash forever.</summary>
    private IEnumerator HoldTimeout()
    {
        yield return new WaitForSecondsRealtime(maxWaitSeconds);
        Proceed();
    }

    private IEnumerator ProceedAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        Proceed();
    }

    private void Proceed()
    {
        if (proceeded) return;
        proceeded = true;
        SceneManager.LoadScene(nextSceneName);
    }

    /// <summary>Minimal splash matching the menu's art direction, built in code so the loading
    /// scene carries zero asset references.</summary>
    private void BuildSplash()
    {
        var canvasGo = new GameObject("LoadingCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGo.AddComponent<GraphicRaycaster>();

        var bg = new GameObject("Background");
        bg.transform.SetParent(canvasGo.transform, false);
        var img = bg.AddComponent<Image>();
        img.color = new Color(0.03f, 0.05f, 0.12f, 1f);
        var rect = img.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        AddLabel(canvasGo.transform, "Title", "LUNAR HARVEST", 64,
            new Color(1f, 0.84f, 0.25f, 1f), new Vector2(0f, 60f), UiKit.UiFontKind.Display);
        AddLabel(canvasGo.transform, "Loading", "LOADING", 28,
            new Color(1f, 1f, 1f, 0.9f), new Vector2(0f, -10f), UiKit.UiFontKind.Display);
        AddLabel(canvasGo.transform, "Foot", "PREPARING THE ROVER", 16,
            new Color(1f, 1f, 1f, 0.35f), new Vector2(0f, -70f), UiKit.UiFontKind.Body);
    }

    private static void AddLabel(Transform parent, string name, string text, int size, Color color, Vector2 anchoredPos,
        UiKit.UiFontKind fontKind = UiKit.UiFontKind.Body)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<Text>();
        txt.text = text;
        txt.fontSize = size;
        txt.color = color;
        // Fake-bold is only applied to the Body face - the Display face (Russo One) is
        // already heavy, and synthesizing bold on it muddies the letterforms.
        txt.fontStyle = fontKind == UiKit.UiFontKind.Body ? FontStyle.Bold : FontStyle.Normal;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = UiKit.ResolveFont(fontKind);
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        var rt = txt.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1400f, 100f);
        rt.anchoredPosition = anchoredPos;
    }
}