using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runs one Lunar Trials challenge attempt: the elapsed-time clock, the instant-restart
/// loop (R key + an on-screen RESTART button), checkpoint storage, and Gold/Silver/Bronze
/// medal scoring on completion. Created by the ChallengeRunner (to come) when a Trials
/// challenge starts; never present in the regular game, and inert outside the
/// LUNAR_TRIALS edition.
///
/// Trials loop: fail fast, press R, retry instantly. R restarts the WHOLE attempt (buggy
/// back to the start, clock to zero) - the classic trials convention. Checkpoints are
/// recorded as they are crossed (TrialsCheckpoint) and will drive fail-respawns once the
/// challenge-fail hooks land (see LunarTrials/DESIGN.md).
/// </summary>
public class TrialsRunManager : MonoBehaviour
{
    /// <summary>True while a Trials challenge attempt is live (the edition is Trials AND a
    /// run manager exists with a started challenge).</summary>
    public static bool TrialsActive => GameEdition.IsTrials && Current != null && Current.running;

    /// <summary>The live attempt's manager, or null.</summary>
    public static TrialsRunManager Current { get; private set; }

    private ChallengeDef def;
    private BuggyController buggy;
    private Vector3 spawnPos;
    private Quaternion spawnRot = Quaternion.identity;
    private float elapsed;
    private bool running;

    /// <summary>Starts (or restarts fresh) the attempt manager for a challenge. Returns null
    /// outside the Trials edition so callers can simply no-op.</summary>
    public static TrialsRunManager StartChallenge(ChallengeDef def, BuggyController buggy)
    {
        if (!GameEdition.IsTrials || def == null || buggy == null) return null;

        if (Current != null) Destroy(Current.gameObject);
        TrialsRunManager mgr = new GameObject("TrialsRunManager").AddComponent<TrialsRunManager>();
        mgr.def = def;
        mgr.buggy = buggy;
        mgr.spawnPos = buggy.SpawnPosition;
        mgr.spawnRot = buggy.transform.rotation;
        mgr.ResetAttemptState();
        Current = mgr;
        return mgr;
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
            // A retry reloads the scene straight from the fail panel - never leak the frozen
            // world into the fresh attempt.
            while (freezeDepth > 0) PopFreeze();
        }
    }

    private void ResetAttemptState()
    {
        elapsed = 0f;
        running = true;
    }

    /// <summary>A checkpoint gate was crossed. Respawn-point storage returns with the
    /// checkpoint-respawn fail loop planned in LunarTrials/DESIGN.md; today the trials loop
    /// restarts challenges fresh, so this is a documented no-op.</summary>
    public void SetCheckpoint(Vector3 position, Quaternion rotation)
    {
    }

    /// <summary>Elapsed seconds of the live (or just-completed) attempt.</summary>
    public float Elapsed => elapsed;

    // ------------------------------------------------------------------ the fail panel
    private bool failPanelUp;
    private RectTransform failCard;
    private CanvasGroup failGroup;
    private Text failTitle;
    private Text failHint;
    private Coroutine failRoutine;
    private static float savedTimeScale;
    private static int freezeDepth;

    /// <summary>The trials fail loop WITH a press to retry: a challenge death (O2 out, hull
    /// destroyed, fall) freezes the world and shows the reason with a RETRY button (Enter or
    /// Space work too). RETRY reloads the challenge completely fresh - pods, nuggets, oxygen,
    /// hull and the clock all back to zero. Returns true when the fail was consumed (the
    /// caller skips its own game-over flow).</summary>
    public bool TryFailPanel(string reason)
    {
        if (failPanelUp) return false;
        failPanelUp = true;
        PushFreeze();
        BuildFailPanel(reason);
        failRoutine = StartCoroutine(FailFlow());
        return true;
    }

    private IEnumerator FailFlow()
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.3f)
        {
            failGroup.alpha = Mathf.Clamp01(t);
            yield return null;
        }
        failGroup.alpha = 1f;

        while (true)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space))
                break;
            yield return null;
        }

        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.25f)
        {
            failGroup.alpha = Mathf.Clamp01(t);
            yield return null;
        }

        ChallengeRunner.RestartActive();   // scene reload - OnDestroy releases the freeze
    }

    private void BuildFailPanel(string reason)
    {
        if (failCard != null) return;
        UiKit.EnsureEventSystem();
        Canvas canvas = UiKit.CreateRootCanvas("TrialsFailCanvas", 490);
        canvas.transform.SetParent(transform, false);

        RectTransform dim = UiKit.Panel(canvas.transform, "FailDim", Vector2.zero, Vector2.zero,
            Vector2.zero, new Color(0.02f, 0.03f, 0.06f, 0.6f));
        UiKit.StretchToFull(dim);
        dim.gameObject.AddComponent<CanvasGroup>();

        failCard = UiKit.Panel(canvas.transform, "FailCard",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 360f),
            new Color(0.05f, 0.07f, 0.1f, 0.98f));
        failGroup = failCard.gameObject.AddComponent<CanvasGroup>();

        failTitle = UiKit.Label(failCard, "FailTitle", reason, 44,
            new Color(1f, 0.45f, 0.35f, 1f), new Vector2(0f, -80f), TextAnchor.MiddleCenter, 920f, 64f,
            UiKit.UiFontKind.Display);
        failTitle.raycastTarget = false;

        failHint = UiKit.Label(failCard, "FailHint", "PRESS RETRY TO GO AGAIN", 24,
            new Color(0.85f, 0.93f, 1f, 1f), new Vector2(0f, -150f), TextAnchor.MiddleCenter, 900f, 40f);
        failHint.raycastTarget = false;

        RectTransform retry = UiKit.Panel(failCard, "RetryButton",
            new Vector2(0.5f, 1f), new Vector2(0f, -255f), new Vector2(320f, 80f),
            new Color(0.14f, 0.48f, 0.29f, 1f));
        Text retryLabel = UiKit.Label(retry, "RetryLabel", "RETRY", 34,
            new Color(0.88f, 1f, 0.92f, 1f), Vector2.zero, TextAnchor.MiddleCenter, 300f, 70f,
            UiKit.UiFontKind.Display);
        retryLabel.raycastTarget = false;

        Button retryBtn = retry.gameObject.AddComponent<Button>();
        retryBtn.targetGraphic = retry.GetComponent<Image>();
        ColorBlock colors = retryBtn.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.2f);
        colors.pressedColor = new Color(0.5f, 1f, 0.75f, 1f);
        colors.selectedColor = Color.white;
        retryBtn.colors = colors;
        retryBtn.onClick.AddListener(delegate
        {
            if (failRoutine != null) StopCoroutine(failRoutine);
            failRoutine = null;
            ChallengeRunner.RestartActive();   // scene reload - OnDestroy releases the freeze
        });

        failGroup.alpha = 0f;
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

    private void Update()
    {
        if (!running) return;
        elapsed += Time.deltaTime;

        // Instant restart: R on desktop; the on-screen RESTART button covers touch.
        if (Input.GetKeyDown(KeyCode.R)) RestartAttempt();
    }

    /// <summary>Instant full restart: reloads the challenge fresh (scene re-launch through the
    /// ChallengeRunner), so pods, ore, oxygen, hull, timer and every run counter start truly
    /// from zero - a surgical reset would leave collected canisters/nuggets behind.</summary>
    public void RestartAttempt()
    {
        if (def == null) return;
        ChallengeRunner.RestartActive();
    }

    /// <summary>The buggy reached the challenge goal: stops the clock and reports the medal.
    /// (Token payout + completion save + next-challenge unlock land with the ChallengeRunner.)</summary>
    public string Complete()
    {
        if (!running) return "";
        running = false;
        string medal = def != null ? def.MedalFor(elapsed) : "";
        Debug.Log("[Trials] " + (def != null ? def.id : "?") + " complete in "
            + elapsed.ToString("F1") + "s - medal: " + (string.IsNullOrEmpty(medal) ? "none" : medal));
        return medal;
    }

    private HUD hud;

    private void Start()
    {
        hud = FindAnyObjectByType<HUD>();
        // Mid-run restart is the R key (desktop). Touch players get retries from the fail
        // panel and the win board - a separate always-on button duplicated those.
    }
}

