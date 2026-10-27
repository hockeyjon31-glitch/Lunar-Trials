using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Floating world-anchored progress ring shown over a nugget while it is being sampled.
/// Screen-space overlay whose position is refreshed from a world anchor every frame, so it
/// is visible with any camera and never sits in 3D space (no sorting headaches with terrain).
/// </summary>
public static class HarvestRingUI
{
    private static Canvas canvas;
    private static RectTransform root;
    private static Image targetImage;
    private static Image fillImage;
    private static Sprite ringSprite;

    private const int SortingOrder = 230; // above the HUD (100), below score popups (250)

    private static void Ensure()
    {
        if (canvas != null) return;

        GameObject canvasGo = new GameObject("HarvestRingCanvas", typeof(Canvas));
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        root = new GameObject("HarvestRing", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvas.transform, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.zero;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(96f, 96f);

        // Static target ring (the "pad" around the nugget), one shade darker.
        targetImage = MakeImage(root, "Target", RingSprite(), new Color(0.9f, 0.95f, 1f, 0.55f));
        targetImage.raycastTarget = false;

        // Radial progress arc drawn over the same ring shape.
        fillImage = MakeImage(root, "Fill", RingSprite(), Color.white);
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Radial360;
        fillImage.fillOrigin = (int)Image.Origin360.Top;
        fillImage.raycastTarget = false;

        root.gameObject.SetActive(false);
    }

    private static Image MakeImage(RectTransform parent, string name, Sprite sprite, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        return img;
    }

    private static Sprite RingSprite()
    {
        if (ringSprite != null) return ringSprite;
        const int size = 128;
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        float outer = c * c;                    // rim outer edge
        float rimOut = (c * 0.86f) * (c * 0.86f);
        float rimIn = (c * 0.70f) * (c * 0.70f);
        float disc = (c * 0.6f) * (c * 0.6f);   // faint inner disc (the "being sampled" area)
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d2 = dx * dx + dy * dy;
                byte a = 0;
                if (d2 <= disc)
                {
                    a = 26; // faint fill so the wedge reads even at low progress
                }
                else if (d2 <= rimOut)
                {
                    // Soft anti-aliased rim.
                    float inT = 1f - Mathf.Clamp01((d2 - rimIn) / ((rimOut - rimIn) * 0.5f));
                    float outT = Mathf.Clamp01((outer - d2) / (outer - rimOut));
                    a = (byte)(255f * Mathf.Min(inT, outT));
                }
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        t.SetPixels32(px);
        t.Apply();
        ringSprite = Sprite.Create(t, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return ringSprite;
    }

    /// <summary>Repositions the ring over a world anchor and updates its progress arc.</summary>
    public static void UpdateState(Vector3 worldAnchor, float progress, Color tint)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(worldAnchor);
        if (screen.z < 0f) { Hide(); return; }

        Ensure();
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        root.anchoredPosition = new Vector2(screen.x, screen.y);
        fillImage.color = new Color(tint.r, tint.g, tint.b, 1f);
        fillImage.fillAmount = Mathf.Clamp01(progress);
    }

    public static void Hide()
    {
        if (root != null) root.gameObject.SetActive(false);
    }
}