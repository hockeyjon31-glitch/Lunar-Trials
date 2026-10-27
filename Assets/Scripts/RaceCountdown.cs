using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lunar Race start ceremony. On race load the canyon first appears through a black
/// fade-in, a big "3 - 2 - 1 - GO!" countdown plays over the buggy, and the camera zooms
/// from a wide reveal down into the normal chase perspective. The buggy is locked (no
/// steering), the race clock is paused and every rival is held at the line until "GO" hands
/// the player control. GameBootstrap creates one of these for each race load.
/// </summary>
public class RaceCountdown : MonoBehaviour
{
    [Header("References")]
    public BuggyController buggy;
    public RivalRacer[] rivals;
    public OxygenManager oxygen;

    [Header("Timing (seconds)")]
    [Tooltip("Black fade-in that reveals the canyon at the very start.")]
    public float fadeInDuration = 0.9f;
    [Tooltip("When the first countdown number ('3') appears.")]
    public float firstNumberTime = 0.35f;
    [Tooltip("Seconds each countdown number stays on screen.")]
    public float stepDuration = 1.0f;
    [Tooltip("Seconds the GO! banner holds before the ceremony ends.")]
    public float goHoldTime = 1.3f;
    [Tooltip("How far (as a multiplier) the camera starts pulled back before zooming to the chase shot.")]
    public float cameraStartScale = 1.7f;
    [Tooltip("True when the level-intro already revealed the world (its planet card + dolly). "
            + "The countdown then skips its own black fade-in and keeps the current camera scale "
            + "instead of re-pulling wide, so it drops straight into 3-2-1-GO.")]
    public bool skipFadeIn = false;

    private static readonly string[] Numbers = { "3", "2", "1" };

    private float timer;
    private bool released;
    private bool countdownAudioPlayed;
    private Image fade;
    private Text numberText;
    private BuggyCamera followCam;

    private void Start()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (oxygen == null) oxygen = OxygenManager.Instance;
        if (rivals == null || rivals.Length == 0) rivals = RivalRacer.All();

        followCam = FindAnyObjectByType<BuggyCamera>();

        // Lock everything until GO (defensive; the bootstrap already did this). Rivals stay
        // ENABLED so their bodies and name tags remain visible on the grid through the 3-2-1;
        // the hold flag keeps them planted until GO.
        if (buggy != null) buggy.inputEnabled = false;
        if (oxygen != null) oxygen.clockPaused = true;
        for (int i = 0; i < rivals.Length; i++)
        {
            if (rivals[i] != null)
            {
                rivals[i].Initialize();
                rivals[i].SetHeld(true);
            }
        }

        // Pull the chase camera back for the reveal; it glides to 1.0x through the countdown.
        // When the level intro already handled the reveal (skipFadeIn), keep the camera where
        // the intro handed off and let the countdown's own zoom finish the push-in.
        if (followCam != null && !skipFadeIn) followCam.offsetScale = cameraStartScale;

        Canvas canvas = UiKit.CreateRootCanvas("RaceCountdownCanvas", 260);
        canvas.transform.SetParent(transform, false);

        // Full-screen black veil that fades out to reveal the course (already clear when the
        // level-intro handled the reveal: skipFadeIn starts fully transparent).
        GameObject fadeGo = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        fadeGo.transform.SetParent(canvas.transform, false);
        fade = fadeGo.GetComponent<Image>();
        UiKit.StretchToFull(fade.rectTransform);
        fade.color = skipFadeIn ? new Color(0f, 0f, 0f, 0f) : Color.black;

        // Big centred countdown text.
        GameObject numGo = new GameObject("CountNumber", typeof(RectTransform), typeof(Text));
        numGo.transform.SetParent(canvas.transform, false);
        RectTransform nrt = numGo.GetComponent<RectTransform>();
        nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0.5f);
        nrt.pivot = new Vector2(0.5f, 0.5f);
        nrt.anchoredPosition = Vector2.zero;
        nrt.sizeDelta = new Vector2(1100f, 420f);
        numberText = numGo.GetComponent<Text>();
        numberText.font = UiKit.ResolveFont(UiKit.UiFontKind.Display);
        numberText.fontSize = 200;
        numberText.alignment = TextAnchor.MiddleCenter;
        numberText.horizontalOverflow = HorizontalWrapMode.Overflow;
        numberText.color = Color.white;
        numberText.text = "";
    }

    private void Update()
    {
        if (fade == null || numberText == null) return;
        timer += Time.deltaTime;

        // Fade the black veil out to reveal the canyon.
        float fadeA = 1f - Mathf.Clamp01(timer / fadeInDuration);
        fade.color = new Color(0f, 0f, 0f, fadeA);

        // Countdown numbers, then GO.
        float goStart = firstNumberTime + 3 * stepDuration;
        if (timer < goStart)
        {
            // Robotic countdown audio plays once, in sync with the visual countdown ("3").
            if (!countdownAudioPlayed && timer >= firstNumberTime)
            {
                countdownAudioPlayed = true;
                AudioClip clip = Resources.Load<AudioClip>("Robotic_Countdown");
                if (clip != null) RoverSounds.PlayCountdownVoice(clip);
            }
            int i = Mathf.Clamp((int)((timer - firstNumberTime) / stepDuration), 0, 2);
            float local = timer - (firstNumberTime + i * stepDuration);
            ShowNumber(Numbers[i], local, stepDuration - 0.35f, Color.white);
        }
        else
        {
            float local = timer - goStart;
            ShowNumber("GO!", local, goHoldTime - 0.55f, new Color(0.45f, 1f, 0.55f, 1f));
        }

        // Camera zoom: wide reveal over the countdown, chase shot by GO.
        if (followCam != null)
        {
            float zoomT = Mathf.Clamp01((timer - 0.4f) / 3.0f);
            float eased = zoomT * zoomT * (3f - 2f * zoomT);
            followCam.offsetScale = Mathf.Lerp(cameraStartScale, 1f, eased);
        }

        // GO! - hand the player control, start the race clock, release every rival.
        if (!released && timer >= goStart)
        {
            released = true;
            if (buggy != null) buggy.inputEnabled = true;
            if (oxygen != null) oxygen.clockPaused = false;
            for (int i = 0; i < rivals.Length; i++)
            {
                if (rivals[i] != null) rivals[i].SetHeld(false);
            }
        }

        // Ceremony ends shortly after GO; the countdown canvas dies with it.
        if (timer >= goStart + goHoldTime)
        {
            Destroy(gameObject);
        }
    }

    private void ShowNumber(string value, float local, float outStart, Color baseColor)
    {
        float inA = Mathf.Clamp01(local / 0.16f);
        float outA = 1f - Mathf.Clamp01((local - outStart) / 0.35f);
        numberText.text = value;
        numberText.rectTransform.localScale = Vector3.one * (1.8f - 0.8f * inA);
        numberText.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Min(inA, outA));
    }
}