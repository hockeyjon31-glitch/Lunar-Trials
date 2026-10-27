using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The top-left "BANKED TOKENS" wallet badge, shown on the menu screens and the end-of-run
/// boards. It displays the garage WALLET balance ("TOKENS  n") so the player always sees
/// what has been banked. A win board can PLAY A TRANSFER: gold coins peel off a harvested
/// pile at the screen, arc into the badge one by one, each landing with a token clink, while
/// the counter counts the balance up to its new total. Baking the coin disc here keeps the
/// effect completely asset-free (matching the runtime-built UI everywhere else).
///
/// MENU MODE (liveWallet + SetClickHandler): the badge becomes a garage shortcut. It follows the
/// saved wallet, pulses while at least one upgrade is affordable, and a click runs the handler.
/// The end-of-run boards use the same mode (their token badge jumps to the garage), and it costs
/// the transfer animation nothing: the landing nudge owns the number's scale while it plays, and
/// the wallet poll only resyncs once the count has settled.
/// </summary>
public class WalletBadge : MonoBehaviour
{
    public Text amount;
    public Image coin;

    private int shown;
    private int target;
    private float tickEvery;
    private float tickAccum;

    private static Sprite coinSprite;

    private const float CapX = 40f;         // left edge for the badge content
    private const float CapY = -14f;        // (legacy cap slot, retained for layout symmetry)
    private const float AmountY = -40f;     // "n" value hangs below the cap line
    private const float AmountW = 320f;
    private const float AmountH = 42f;
    private const float AmountCenterY = -61f; // vertical middle of the amount line

    // ------------------------------------------------------------------ garage shortcut + attention
    private static readonly Color AmountBright = new Color(1f, 0.96f, 0.62f, 1f);

    /// <summary>Menu screens turn this on: the number follows the saved wallet (so a purchase made
    /// in the garage shows up when you return) and the badge pulses while any upgrade is affordable.</summary>
    public bool liveWallet;

    /// <summary>A coin animation owns the number right now: it resolved its start and end when it
    /// was requested, and the live-wallet poll must not drag the badge to the saved total until it
    /// has finished (see ClaimNumber).</summary>
    private bool animating;

    private System.Action onClick;
    private Image hitArea;
    private Text hintText;
    private bool hovered;
    private bool attention;
    private bool wasActive;
    private bool pulseRunning;      // the coin-landing nudge owns the number's scale while true
    private float attentionPhase;
    private float pollAccum;

    /// <summary>Builds the badge pinned to the canvas top-left. Set to the current balance.
    /// Shows a baked coin disc beside the plain number - no "BANKED" cap, no "TOKENS  n"
    /// wording cluttering the corner.</summary>
    public static WalletBadge AttachTopLeft(Canvas canvas, int initial)
    {
        WalletBadge badge = canvas.gameObject.AddComponent<WalletBadge>();

        // A small coin disc sitting just left of the number (sprite-only, so it survives the
        // WebGL build), vertically centred on the amount line.
        Image coin = NewCoinDisc(canvas.transform, new Vector2(CapX + 18f, AmountCenterY), 36f);

        Text amt = UiKit.Label(canvas.transform, "WalletAmount", Format(initial), 28,
            UpgradeSystem.TokenColor, new Vector2(CapX + 44f, AmountY),
            TextAnchor.MiddleLeft, AmountW, AmountH);
        badge.coin = coin;
        badge.amount = amt;
        amt.raycastTarget = false;   // the (optional) hit area owns clicks
        badge.InstantSet(initial);
        return badge;
    }

    /// <summary>Snaps the displayed balance to a value (menu draws, garage purchases).</summary>
    public WalletBadge InstantSet(int value)
    {
        shown = target = value;
        if (amount != null) amount.text = Format(value);
        return this;
    }

    /// <summary>Counts the displayed balance up to <paramref name="final"/> over <paramref name="seconds"/>.</summary>
    public void CountTo(int final, float seconds)
    {
        target = final;
        int steps = Mathf.Max(1, Mathf.Abs(final - shown));
        tickEvery = Mathf.Max(0.025f, seconds / steps);
        tickAccum = 0f;
    }

    /// <summary>Claims the number for a coin animation: the badge resolves where the count
    /// starts and ends NOW, and the live-wallet poll leaves it alone until the coins land.
    ///
    /// Both matter, and both were bugs. The poll only stands aside while a count is actually
    /// running (<c>shown == target</c>), which is trivially true during an animation's opening
    /// beat - so the poll would drag the badge to the saved total before the count began, and
    /// the animation would then start from the wrong base and land on a doubled figure before
    /// snapping back. Claiming the number up front fixes the base, and holding the poll off for
    /// the animation's whole life keeps the count visible.</summary>
    private int ClaimNumber(int delta)
    {
        animating = true;
        return shown + delta;
    }

    private void Update()
    {
        UpdateLive();
        if (shown == target) return;
        tickAccum += Time.deltaTime;
        if (tickAccum < tickEvery) return;
        tickAccum = 0f;
        shown += target > shown ? 1 : -1;
        if (amount != null) amount.text = Format(shown);
    }

    // ------------------------------------------------------------------ click + attention
    /// <summary>Makes the badge clickable (null removes it). A transparent hit area sits over the
    /// coin and number; hovering brightens them and shows a small "OPEN GARAGE" hint.</summary>
    public WalletBadge SetClickHandler(System.Action handler)
    {
        onClick = handler;
        if (handler != null && hitArea == null) BuildHitArea();
        if (hitArea != null) hitArea.raycastTarget = handler != null;
        if (handler == null) hovered = false;
        return this;
    }

    private void BuildHitArea()
    {
        hintText = UiKit.Label(transform, "WalletHint", "", 32, new Color(0.93f, 0.87f, 0.51f, 1f),
            new Vector2(CapX + 44f, -84f), TextAnchor.MiddleLeft, 560f, 76f);
        hintText.raycastTarget = false;
        hintText.enabled = false;

        GameObject go = new GameObject("WalletHit", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(CapX - 8f, -32f);
        rt.sizeDelta = new Vector2(250f, 58f);

        hitArea = go.GetComponent<Image>();
        hitArea.color = new Color(0f, 0f, 0f, 0.003f);   // invisible, but still catches the pointer

        Button b = go.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.targetGraphic = hitArea;
        b.onClick.AddListener(() => { if (onClick != null) onClick(); });

        EventTrigger trigger = go.AddComponent<EventTrigger>();
        EventTrigger.Entry enter = new EventTrigger.Entry();
        enter.eventID = EventTriggerType.PointerEnter;
        enter.callback.AddListener(delegate { hovered = true; });
        trigger.triggers.Add(enter);
        EventTrigger.Entry exit = new EventTrigger.Entry();
        exit.eventID = EventTriggerType.PointerExit;
        exit.callback.AddListener(delegate { hovered = false; });
        trigger.triggers.Add(exit);
    }

    /// <summary>Menu-mode housekeeping, four times a second at most: follow the saved wallet, decide
    /// whether an upgrade is affordable, and drive the pulse / hover look every frame.</summary>
    private void UpdateLive()
    {
        if (liveWallet)
        {
            pollAccum += Time.unscaledDeltaTime;
            if (pollAccum >= 0.25f)
            {
                pollAccum = 0f;
                attention = UpgradeSystem.CanAffordAny();
                // Keep the number honest after a purchase made elsewhere (e.g. in the garage),
                // but never mid-animation: shown == target is also true during an animation's
                // opening beat, and yanking the number there would restart the count from the
                // wrong base (and land it on a doubled figure).
                if (shown == target && !animating)
                {
                    int wallet = UpgradeSystem.GetTokens();
                    if (wallet != shown) InstantSet(wallet);
                }
            }
        }
        else
        {
            attention = false;
        }

        bool hoverOn = hovered && onClick != null;
        bool active = attention || hoverOn;
        Color baseColor = UpgradeSystem.TokenColor;

        if (active)
        {
            float w = 0f;
            if (attention)
            {
                attentionPhase += Time.unscaledDeltaTime * (Mathf.PI * 2f / 1.2f);   // one breath per 1.2 s
                w = 0.5f + 0.5f * Mathf.Sin(attentionPhase);
            }
            float swell = 1f + 0.18f * w + (hoverOn ? 0.05f : 0f);
            ApplyLook(swell, Color.Lerp(baseColor, AmountBright, hoverOn ? 1f : w));
        }
        else if (wasActive)
        {
            attentionPhase = 0f;
            ApplyLook(1f, baseColor);   // settle back to rest
        }
        wasActive = active;

        if (hintText != null)
        {
            if (hintText.enabled != hoverOn) hintText.enabled = hoverOn;
            if (hoverOn) hintText.text = attention ? "UPGRADES READY!\nOPEN GARAGE" : "OPEN GARAGE";
        }
    }

    private void ApplyLook(float swell, Color tint)
    {
        Vector3 scale = new Vector3(swell, swell, 1f);
        if (amount != null)
        {
            amount.color = tint;
            if (!pulseRunning) amount.rectTransform.localScale = scale;   // the coin-landing nudge has priority
        }
        if (coin != null) coin.rectTransform.localScale = scale;
    }

    private static string Format(int value)
    {
        return value.ToString("N0");
    }

    /// <summary>Plays the harvest-to-bank transfer: the <paramref name="gain"/> counts up onto the
    /// badge while gold coins arc out of a pile at <paramref name="pileNormal"/> (0..1 screen
    /// position, from bottom-left) into the number, a clink on every landing.</summary>
    public WalletBadge PlayTransfer(int gain, Vector2 pileNormal)
    {
        if (gain <= 0 || amount == null) return this;
        // Claimed out here, before the routine's opening beat: shown must be read while it still
        // shows the pre-run balance, not after the poll or a later credit has moved it.
        int end = ClaimNumber(gain);
        StartCoroutine(TransferRoutine(gain, end, pileNormal));
        return this;
    }

    /// <summary>Plays a garage spend: the <paramref name="cost"/> counts DOWN off the badge
    /// while the same gold coins fly OUT of the badge over to an upgrade row at
    /// <paramref name="target"/> (a button under the same canvas), a clink on every landing,
    /// then <paramref name="onDone"/> fires once the transfer lands.</summary>
    public WalletBadge PlaySpend(int cost, RectTransform target, System.Action onDone = null)
    {
        if (cost <= 0 || amount == null || target == null) return this;
        // Claimed before the opening beat for the same reason as PlayTransfer: a purchase made
        // elsewhere lowers the saved wallet, and the poll would otherwise move the base and the
        // count would deduct the cost a second time from an already-reduced number.
        int end = ClaimNumber(-cost);
        StartCoroutine(SpendRoutine(cost, end, target, onDone));
        return this;
    }

    private IEnumerator SpendRoutine(int cost, int end, RectTransform target, System.Action onDone)
    {
        const float duration = 1.6f;
        const float startDelay = 0.35f;
        int coins = Mathf.Clamp(Mathf.CeilToInt(cost / 8f), 6, 14);
        float inline = duration * 0.6f / coins;

        // Coins launch out of the badge's own number (the "bank")...
        Rect r = amount.rectTransform.rect;
        Vector2 from = amount.rectTransform.anchoredPosition
            + new Vector2(r.width / 2f, -r.height / 2f);

        // ...and arc over to the upgrade row's button, measured in the same canvas space.
        Vector2 land = CanvasPointOf(target);

        yield return new WaitForSeconds(startDelay);
        CountTo(end, duration);
        RoverSounds.PlayTokenClink(0.65f);

        for (int i = 0; i < coins; i++)
        {
            Image coin = NewCoin();
            // Anchor the coin around the badge: coins that fly toward the row start tight
            // around the number (left of / below it), not from its exact centre.
            Vector2 launch = from + new Vector2(-30f + Random.value * 60f, 6f + Random.value * 30f);
            PlaceCoin(coin, launch, 42f);
            StartCoroutine(FlyCoin(coin, land));
            yield return new WaitForSeconds(inline);
        }
        yield return new WaitForSeconds(0.5f);
        RoverSounds.PlayTokenClink(0.5f);
        Pulse();

        if (onDone != null) onDone();
        animating = false;
    }

    /// <summary>Converts a widget's world position to the badge's canvas top-left space (the
    /// same anchored space PlaceCoin uses for flying coins). Both the badge and the upgrade
    /// rows live under the same canvas, so no camera is needed.</summary>
    private static Vector2 CanvasPointOf(RectTransform target)
    {
        Canvas canvas = target.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) return Vector2.zero;
        RectTransform canvasRt = (RectTransform)canvas.transform;
        Vector2 local = canvasRt.InverseTransformPoint(target.position);

        // Local (0,0) is the canvas centre with +y up, but coins are placed from the canvas
        // top-left corner with +y down (the same space as the badge and PlaceCoin).
        Rect c = canvasRt.rect;
        return new Vector2(local.x + c.width * 0.5f, c.height * 0.5f - local.y);
    }

    private IEnumerator TransferRoutine(int gain, int end, Vector2 pileNormal)
    {
        const float duration = 1.6f;
        const float startDelay = 0.45f;
        int coins = Mathf.Clamp(gain / 8, 6, 14);
        float inline = duration * 0.6f / coins;   // stagger between launches

        RectTransform canvasRt = (RectTransform)transform;
        Vector2 pile = new Vector2(
            pileNormal.x * canvasRt.rect.width,
            canvasRt.rect.height * (1f - pileNormal.y));

        Rect r = amount.rectTransform.rect;
        Vector2 land = amount.rectTransform.anchoredPosition
            + new Vector2(r.width / 2f, -r.height / 2f);

        // The harvested pile itself: a fanned cluster of resting coin discs where the flyers
        // launch from, so the coins visibly "come out of the pile" onto the bank display.
        SpawnHeap(pile);

        yield return new WaitForSeconds(startDelay);
        CountTo(end, duration);
        RoverSounds.PlayTokenClink(0.65f);

        for (int i = 0; i < coins; i++)
        {
            Image coin = NewCoin();
            PlaceCoin(coin, pile, 42f);
            StartCoroutine(FlyCoin(coin, land));
            yield return new WaitForSeconds(inline);
        }
        yield return new WaitForSeconds(0.35f);
        RoverSounds.PlayTokenClink(0.5f);
        Pulse();
        animating = false;
    }

    private IEnumerator FlyCoin(Image coin, Vector2 land)
    {
        RectTransform rt = (RectTransform)coin.transform;
        Vector2 start = rt.anchoredPosition;
        float flight = 0.5f + Random.value * 0.15f;
        float arc = 60f + Random.value * 60f;   // upward crest (smaller y = higher)
        Vector2 mid = (start + land) / 2f + new Vector2(0f, -arc);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / flight;
            float k = Mathf.Clamp01(t);
            float s = k * k * (3f - 2f * k);    // smoothstep
            Vector2 pos = Vector2.Lerp(start, land, s)
                + Vector2.LerpUnclamped(start, mid, Mathf.Abs(2f * k - 1f)) * (1f - Mathf.Abs(2f * k - 1f));
            rt.anchoredPosition = pos;
            rt.localEulerAngles = new Vector3(0f, 0f, 400f * k);
            yield return null;
        }
        rt.anchoredPosition = land;
        Destroy(coin.gameObject);
        RoverSounds.PlayTokenClink(0.55f);
        Pulse();
    }

    /// <summary>A quick elastic scale nudge when a coin lands. Only this pulse coroutine is
    /// stopped - never the transfer or coin coroutines, or the transfer would die mid-air.</summary>
    private Coroutine pulse;

    private void Pulse()
    {
        if (amount == null) return;
        if (pulse != null) StopCoroutine(pulse);
        pulse = StartCoroutine(PulseRoutine());
    }

    private IEnumerator PulseRoutine()
    {
        pulseRunning = true;
        float t = 0f;
        while (t < 0.18f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / 0.18f);
            float s = 1.14f - 0.14f * Mathf.Sin(k * Mathf.PI);
            amount.rectTransform.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        amount.rectTransform.localScale = Vector3.one;
        pulseRunning = false;
    }

    private void SpawnHeap(Vector2 pile)
    {
        for (int i = 0; i < 5; i++)
        {
            Image coin = NewCoin();
            PlaceCoin(coin, pile + new Vector2((i - 2) * 26f, -12f + Random.value * 14f),
                38f - Random.value * 8f);
            coin.color = new Color(0.9f, 0.78f, 0.35f, 0.65f);
            ((RectTransform)coin.transform).localEulerAngles =
                new Vector3(0f, 0f, (i - 2) * 18f + (Random.value - 0.5f) * 10f);
        }
    }

    private Image NewCoin()
    {
        GameObject go = new GameObject("Coin", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        go.GetComponent<Image>().raycastTarget = false;
        return go.GetComponent<Image>();
    }

    /// <summary>A static coin disc pinned to a canvas corner at <paramref name="center"/>
    /// (centre of the disc), the same sprite the flying coins use.</summary>
    private static Image NewCoinDisc(Transform parent, Vector2 center, float size)
    {
        GameObject go = new GameObject("WalletCoin", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        Vector2 a = UiKit.AnchorFor(center);
        rt.anchorMin = a;
        rt.anchorMax = a;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center;
        rt.sizeDelta = new Vector2(size, size);
        Image img = go.GetComponent<Image>();
        img.sprite = CoinSprite();
        img.raycastTarget = false;
        return img;
    }

    private static void PlaceCoin(Image coin, Vector2 at, float size)
    {
        RectTransform rt = (RectTransform)coin.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = at;
        rt.sizeDelta = new Vector2(size, size);
        coin.sprite = CoinSprite();
    }

    /// <summary>Bakes a gold coin disc (rim + sheen) once and caches the sprite.</summary>
    private static Sprite CoinSprite()
    {
        if (coinSprite != null) return coinSprite;
        const int S = 64;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        float R = S * 0.49f;
        Color baseGold = new Color(1f, 0.84f, 0.25f, 1f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                if (d >= R) { px[y * S + x] = new Color(0f, 0f, 0f, 0f); continue; }
                // Rim: darker ring near the edge; face brightens toward the upper-left sheen.
                float rim = d > R * 0.72f ? 1f - (d / R - 0.72f) / 0.28f : 1f;
                float shine = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), new Vector2(S * 0.34f, S * 0.34f)) / R);
                Color col = baseGold * (0.62f + 0.3f * rim + 0.12f * shine);
                col.a = 1f;
                px[y * S + x] = col;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        coinSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        coinSprite.name = "BakedCoin";
        return coinSprite;
    }
}