using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Static spawner for the "+N" token flash that appears when a nugget is harvested.
/// It lives on its own screen-space overlay canvas (no scaler, so canvas units are pixels
/// and world-to-screen mapping is exact), pops in big, floats upward and fades out.
/// </summary>
public static class ScorePopup
{
    private static Canvas canvas;

    private static void EnsureCanvas()
    {
        if (canvas != null) return;
        GameObject canvasGo = new GameObject("ScorePopupCanvas", typeof(Canvas));
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250; // above the HUD (100) and end screen (200)
    }

    /// <summary>Flashes a bonus like "+10" rising from a world-space point on screen.</summary>
    public static void Spawn(Vector3 worldPos, string text, Color color, int fontSize = 76)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(worldPos);
        if (screen.z < 0f) return; // behind the camera

        EnsureCanvas();

        GameObject go = new GameObject("ScorePopup", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(canvas.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(screen.x, screen.y);
        rt.sizeDelta = new Vector2(640f, 140f);
        rt.localScale = Vector3.one * 0.4f;

        Text t = go.GetComponent<Text>();
        t.font = HUD.ResolveDefaultFont();
        t.fontSize = fontSize;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.color = color;
        t.text = text;

        go.AddComponent<ScorePopupAnim>();
    }
}

/// <summary>Rises, pops in, then fades a score popup over its lifetime.</summary>
public class ScorePopupAnim : MonoBehaviour
{
    private const float RiseSpeed = 160f;
    private const float Life = 1.15f;
    private const float PopIn = 0.16f;
    private const float Overshoot = 1.12f;

    private Text text;
    private RectTransform rt;
    private Vector2 startPos;
    private float age;

    private void Awake()
    {
        text = GetComponent<Text>();
        rt = GetComponent<RectTransform>();
        startPos = rt.anchoredPosition;
    }

    private void Update()
    {
        age += Time.deltaTime;

        if (age < PopIn)
        {
            float k = age / PopIn;
            rt.localScale = Vector3.one * Mathf.Lerp(0.4f, Overshoot, k);
        }
        else
        {
            float k = Mathf.Clamp01((age - PopIn) / 0.18f);
            rt.localScale = Vector3.one * Mathf.Lerp(Overshoot, 1f, k);
        }

        rt.anchoredPosition = startPos + new Vector2(0f, RiseSpeed * age);

        if (text != null)
        {
            Color c = text.color;
            c.a = Mathf.Clamp01(1f - age / Life);
            text.color = c;
        }

        if (age >= Life) Destroy(gameObject);
    }
}