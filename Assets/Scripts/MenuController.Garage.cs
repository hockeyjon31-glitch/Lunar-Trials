using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The Garage screen (partial of MenuController). Master-detail layout:
///   left   - the five upgrades in two groups (DRIVE / SURVIVAL). Each row shows the
///            name, tier bar, "now > next" value and the price of the next tier.
///   right  - the revolving showroom on top, and below it a detail card for the SELECTED upgrade:
///            description, NOW vs NEXT TIER, a footnote, and the one UPGRADE button.
/// Tapping/clicking a row selects it (works on touch); hovering only previews its part on the
/// buggy and snaps back to the selection when the pointer leaves. Rows are built once and
/// refreshed in place; nothing is destroyed inside a click handler.
/// Colours stay in the menu's green / yellow palette.
/// </summary>
public partial class MenuController
{
    // ------------------------------------------------------------------ garage palette
    private static readonly Color GPanel = new Color(0.055f, 0.075f, 0.065f, 1f);
    private static readonly Color GRow = new Color(0.08f, 0.12f, 0.10f, 0.95f);
    private static readonly Color GRowHover = new Color(0.11f, 0.17f, 0.13f, 1f);
    private static readonly Color GRowSel = new Color(0.13f, 0.24f, 0.17f, 1f);
    private static readonly Color GBox = new Color(0.05f, 0.08f, 0.065f, 1f);
    private static readonly Color GYellow = new Color(238f / 255f, 221f / 255f, 130f / 255f, 1f); // menu title yellow
    private static readonly Color GGoldDim = new Color(0.62f, 0.55f, 0.25f, 1f);   // token gold, unaffordable
    private static readonly Color GLeaf = new Color(0.75f, 0.85f, 0.6f, 1f);       // effect text green
    private static readonly Color GMint = new Color(0.7f, 1f, 0.8f, 1f);           // tier pips / "now" value
    private static readonly Color GText = new Color(0.92f, 0.96f, 0.9f, 1f);
    private static readonly Color GMuted = new Color(0.55f, 0.64f, 0.58f, 1f);
    private static readonly Color GBuyOn = new Color(0.14f, 0.45f, 0.28f, 1f);     // the existing buy green
    private static readonly Color GBuyOff = new Color(0.14f, 0.18f, 0.16f, 1f);
    private static readonly Color GSegOff = new Color(0.2f, 0.26f, 0.22f, 1f);
    private static readonly Color GClose = new Color(0.14f, 0.2f, 0.16f, 1f);

    // ------------------------------------------------------------------ garage state
    private class GarageRow
    {
        public string id;
        public Image bg;
        public Image accent;
        public Text levelText;
        public Text effectText;
        public Text costText;
        public Image[] segs;
    }

    private class GarageDetail
    {
        public Text name, level, desc, nowValue, nowUnit, nextValue, nextUnit, note, buyLabel;
        public Image buyBg;
        public Button buy;
    }

    private Canvas garageCanvas;
    private GaragePreview garagePreview;
    private RawImage garagePreviewImage;
    private WalletBadge garageBadge;
    private Text garageBuildText;
    private GarageDetail garageDetail;
    private readonly List<GarageRow> garageRows = new List<GarageRow>();
    private string garageSelected = "engine";
    private string garageHover = "";
    private bool garageSpendLock;   // true while the coin-spend animation plays (blocks a double buy)

    private Text buildTagLabel;

    // Escape closes whichever overlay is open (garage first, then the planet map).
    private void Update()
    {
        // Keep the CrazyGames bridge + ad coordinator ticking while the menu is up (the run
        // scene's GameBootstrap.Update covers a level; the menu needs its own tick so the
        // garage's rewarded top-up can complete its ad). The bridge poll is internally
        // rate-limited, so double-driving it from two scenes is safe.
        CrazyGamesBridge.Poll();

        RefreshBuildTag();

        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        // Close the topmost screen first: the garage stacks above the shop, so Esc backs out
        // over it before reaching the shop underneath.
        if (garageCanvas != null) CloseGarage();
        else if (shopCanvas != null) CloseShop();
        else if (GameObject.Find("MapCanvas") != null) ClosePlanetMap();
    }

    // ------------------------------------------------------------------ build
    // Release footer: just the build tag. The SDK/ad debug state (READY / ADS LIVE /
    // PORTAL ON / last ad error) that used to hang here is deliberately gone from the
    // submitted build - it is developer telemetry, not player-facing.
    private void RefreshBuildTag()
    {
        if (buildTagLabel == null) return;
        string s = "BUILD " + GameFlow.BuildTag;
        if (buildTagLabel.text != s) buildTagLabel.text = s;
    }

    /// <summary>The top-left token badge for the menu screens: click it to open the garage, and it
    /// pulses while at least one upgrade is affordable. (The garage's own badge is attached plain.)</summary>
    private WalletBadge AttachWallet(Canvas canvas)
    {
        WalletBadge badge = WalletBadge.AttachTopLeft(canvas, UpgradeSystem.GetTokens());
        badge.liveWallet = true;
        badge.SetClickHandler(() => OpenGarage());
        return badge;
    }

    public void OpenGarage()
    {
        if (garageCanvas != null) return;   // already open (e.g. a double click on the badge)
        garageSpendLock = false;
        garageHover = "";
        garageRows.Clear();

        Canvas canvas = UiKit.CreateRootCanvas("GarageCanvas", 170);   // above World Select (150) and the Shop (160), so opening it from their badges stacks it on top; closing returns you there
        garageCanvas = canvas;

        // Lunar artwork backdrop with a translucent veil, then the panel - same stack as the menu,
        // so the garage reads as part of the same screen family (the showroom viewport
        // still renders from the layer-8 camera into its own texture, unaffected).
        UiKit.Background(canvas.transform, "LunarBackground", "LunarBackground");
        RectTransform dim = UiKit.Rect(canvas.transform, "Dim", Vector2.zero, Vector2.zero, Vector2.zero);
        UiKit.StretchToFull(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0.012f, 0.02f, 0.016f, 0.62f);

        RectTransform panel = UiKit.Panel(canvas.transform, "GaragePanel",
            new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1500f, 980f), GPanel);

        Txt(panel, "UpgradeHeader", "PERFORMANCE UPGRADES", 30, GYellow,
            new Vector2(0f, -28f), TextAnchor.MiddleCenter, 1400f, 44f, UiKit.UiFontKind.Display);
        garageBuildText = Txt(panel, "GarageBuild", "BUILD LEVEL  " + BuildLevelText(), 22, GMint,
            new Vector2(-40f, -40f), TextAnchor.MiddleRight, 400f, 30f);

        // Right column: showroom on top, detail card underneath. (The token balance is the
        // top-left badge only; the panel no longer repeats it.)
        garagePreview = new GameObject("GaragePreview").AddComponent<GaragePreview>();
        garagePreviewImage = garagePreview.Build(panel, new Vector2(1f, 1f), new Vector2(-40f, -100f), new Vector2(700f, 380f));
        BuildGarageDetail(panel);

        // Left column: grouped upgrade rows.
        float y = -100f;
        y = BuildGarageGroup(panel, "DRIVE", new[] { "engine", "boost", "jump" }, y);
        BuildGarageGroup(panel, "SURVIVAL", new[] { "oxygen", "hull" }, y);

        UiKit.Button(panel, "B_CloseGarage", "CLOSE", 26, GClose, GText,
            new Vector2(0f, 40f), new Vector2(260f, 56f), () => CloseGarage());

        garageBadge = WalletBadge.AttachTopLeft(canvas, UpgradeSystem.GetTokens());

        RefreshGarageUI();
        garagePreview.SelectUpgrade(garageSelected);
    }

    private void BuildGarageDetail(RectTransform panel)
    {
        GarageDetail d = new GarageDetail();
        RectTransform root = UiKit.Panel(panel, "Detail", new Vector2(1f, 1f),
            new Vector2(-40f, -496f), new Vector2(700f, 360f), GRow);

        d.name = Txt(root, "Name", "", 34, GYellow, new Vector2(20f, -14f), TextAnchor.MiddleLeft, 480f, 44f);
        d.name.fontStyle = FontStyle.Bold;
        d.level = Txt(root, "Level", "", 22, GMuted, new Vector2(-20f, -22f), TextAnchor.MiddleRight, 180f, 30f);

        d.desc = Txt(root, "Desc", "", 20, GText, new Vector2(20f, -58f), TextAnchor.UpperLeft, 660f, 60f);
        d.desc.horizontalOverflow = HorizontalWrapMode.Wrap;
        d.desc.verticalOverflow = VerticalWrapMode.Truncate;

        // NOW box (left) and NEXT TIER box (right).
        RectTransform nowBox = UiKit.Panel(root, "NowBox", new Vector2(0f, 1f), new Vector2(20f, -124f), new Vector2(320f, 96f), GBox);
        Txt(nowBox, "L", "NOW", 16, GMuted, new Vector2(14f, -8f), TextAnchor.MiddleLeft, 200f, 22f);
        d.nowValue = Txt(nowBox, "V", "", 32, GMint, new Vector2(14f, -30f), TextAnchor.MiddleLeft, 300f, 40f);
        d.nowValue.fontStyle = FontStyle.Bold;
        d.nowUnit = Txt(nowBox, "U", "", 16, GMuted, new Vector2(14f, -70f), TextAnchor.MiddleLeft, 300f, 22f);

        RectTransform nextBox = UiKit.Panel(root, "NextBox", new Vector2(0f, 1f), new Vector2(360f, -124f), new Vector2(320f, 96f), GBox);
        Txt(nextBox, "L", "NEXT TIER", 16, GMuted, new Vector2(14f, -8f), TextAnchor.MiddleLeft, 200f, 22f);
        d.nextValue = Txt(nextBox, "V", "", 32, UpgradeSystem.TokenColor, new Vector2(14f, -30f), TextAnchor.MiddleLeft, 300f, 40f);
        d.nextValue.fontStyle = FontStyle.Bold;
        d.nextUnit = Txt(nextBox, "U", "", 16, GMuted, new Vector2(14f, -70f), TextAnchor.MiddleLeft, 300f, 22f);

        d.note = Txt(root, "Note", "", 17, GMuted, new Vector2(20f, -232f), TextAnchor.UpperLeft, 660f, 44f);
        d.note.horizontalOverflow = HorizontalWrapMode.Wrap;
        d.note.verticalOverflow = VerticalWrapMode.Truncate;

        // The one buy button. Its label carries the price (or the shortfall) so the action is
        // obvious; it stays in place and only recolours/relabels on purchase.
        d.buy = UiKit.Button(root, "B_Buy", "UPGRADE", 28, GBuyOn, GText,
            new Vector2(20f, 16f), new Vector2(660f, 60f), () => BuyUpgrade(garageSelected));
        d.buyBg = d.buy.targetGraphic as Image;
        d.buyLabel = d.buy.transform.Find("Label").GetComponent<Text>();

        garageDetail = d;
    }

    private float BuildGarageGroup(RectTransform panel, string title, string[] ids, float y)
    {
        Text header = Txt(panel, "Group_" + title, title, 18, GMuted, new Vector2(44f, y), TextAnchor.MiddleLeft, 300f, 26f,
            UiKit.UiFontKind.Display);
        header.raycastTarget = false;
        y -= 30f;
        for (int i = 0; i < ids.Length; i++)
        {
            garageRows.Add(BuildGarageRow(panel, ids[i], y));
            y -= 94f;
        }
        return y - 6f;
    }

    private static Text RowTxt(RectTransform row, string name, string text, int size, Color color,
        Vector2 pos, TextAnchor align, float width, float height)
    {
        Text t = Txt(row, name, text, size, color, pos, align, width, height);
        t.raycastTarget = false;   // the row itself owns clicks and hover
        return t;
    }

    private GarageRow BuildGarageRow(RectTransform panel, string id, float y)
    {
        UpgradeSystem.UpgradeInfo info = UpgradeSystem.InfoOf(id);
        RectTransform row = UiKit.Panel(panel, "Row_" + id, new Vector2(0f, 1f),
            new Vector2(40f, y), new Vector2(660f, 86f), GRow);

        GarageRow w = new GarageRow();
        w.id = id;
        w.bg = row.GetComponent<Image>();

        // Yellow edge strip: only visible on the selected row.
        RectTransform accent = UiKit.Panel(row, "Accent", new Vector2(0f, 1f), Vector2.zero, new Vector2(6f, 86f), GYellow);
        w.accent = accent.GetComponent<Image>();
        w.accent.raycastTarget = false;

        RowTxt(row, "Name", info.name, 26, GText, new Vector2(20f, -12f), TextAnchor.MiddleLeft, 400f, 32f);
        w.levelText = RowTxt(row, "Level", "", 20, GMuted, new Vector2(-16f, -14f), TextAnchor.MiddleRight, 200f, 28f);
        w.effectText = RowTxt(row, "Effect", "", 19, GLeaf, new Vector2(20f, -44f), TextAnchor.MiddleLeft, 440f, 26f);
        w.costText = RowTxt(row, "Cost", "", 22, UpgradeSystem.TokenColor, new Vector2(-16f, -44f), TextAnchor.MiddleRight, 200f, 28f);

        // Tier bar: ten small baked segments (no text glyphs, so it renders the same on WebGL).
        w.segs = new Image[UpgradeSystem.MaxLevel];
        for (int i = 0; i < w.segs.Length; i++)
        {
            RectTransform s = UiKit.Panel(row, "Seg" + i, new Vector2(0f, 1f),
                new Vector2(20f + i * 24f, -74f), new Vector2(20f, 6f), GSegOff);
            w.segs[i] = s.GetComponent<Image>();
            w.segs[i].raycastTarget = false;
        }

        // Click = select (touch friendly).
        Button rb = row.gameObject.AddComponent<Button>();
        rb.targetGraphic = w.bg;
        rb.transition = Selectable.Transition.None;
        rb.onClick.AddListener(() => SelectGarageRow(id));

        // Hover = preview the part; leaving snaps the showroom back to the selected upgrade.
        EventTrigger hover = row.gameObject.AddComponent<EventTrigger>();
        EventTrigger.Entry enter = new EventTrigger.Entry();
        enter.eventID = EventTriggerType.PointerEnter;
        enter.callback.AddListener(delegate
        {
            garageHover = id;
            RefreshGarageRowColors();
            if (garagePreview != null) garagePreview.SelectUpgrade(id);
        });
        hover.triggers.Add(enter);
        EventTrigger.Entry exit = new EventTrigger.Entry();
        exit.eventID = EventTriggerType.PointerExit;
        exit.callback.AddListener(delegate
        {
            garageHover = "";
            RefreshGarageRowColors();
            if (garagePreview != null) garagePreview.SelectUpgrade(garageSelected);
        });
        hover.triggers.Add(exit);

        return w;
    }

    // ------------------------------------------------------------------ refresh
    private static string BuildLevelText()
    {
        return UpgradeSystem.BuildLevel + " / " + UpgradeSystem.MaxLevel * UpgradeSystem.UpgradeCount;
    }

    private void SelectGarageRow(string id)
    {
        garageSelected = id;
        RefreshGarageUI();
        if (garagePreview != null) garagePreview.SelectUpgrade(id);
    }

    /// <summary>Re-reads wallet / levels and updates every label in place (nothing is rebuilt).</summary>
    private void RefreshGarageUI()
    {
        if (garageBuildText != null) garageBuildText.text = "BUILD LEVEL  " + BuildLevelText();
        // Don't snap the badge while its coin-spend animation is still counting down.
        if (garageBadge != null && !garageSpendLock) garageBadge.InstantSet(UpgradeSystem.GetTokens());

        int tokens = UpgradeSystem.GetTokens();
        foreach (GarageRow w in garageRows)
        {
            if (w == null) continue;
            UpgradeSystem.UpgradeInfo info = UpgradeSystem.InfoOf(w.id);
            bool afford = !info.maxed && tokens >= info.cost;

            w.levelText.text = "LV " + info.level + " / " + info.maxLevel;
            w.effectText.text = info.effect;
            w.costText.text = info.maxed ? "MAXED" : info.cost.ToString("N0") + " TOKENS";
            w.costText.color = info.maxed ? GMuted : (afford ? UpgradeSystem.TokenColor : GGoldDim);
            for (int i = 0; i < w.segs.Length; i++)
                w.segs[i].color = i < info.level ? GMint : GSegOff;
        }
        RefreshGarageRowColors();
        RefreshGarageDetail();
    }

    private void RefreshGarageRowColors()
    {
        foreach (GarageRow w in garageRows)
        {
            if (w == null || w.bg == null) continue;
            bool sel = w.id == garageSelected;
            bool hov = w.id == garageHover;
            w.bg.color = sel ? GRowSel : (hov ? GRowHover : GRow);
            w.accent.enabled = sel;
        }
    }

    private void RefreshGarageDetail()
    {
        GarageDetail d = garageDetail;
        if (d == null) return;

        UpgradeSystem.UpgradeInfo info = UpgradeSystem.InfoOf(garageSelected);
        int tokens = UpgradeSystem.GetTokens();
        bool afford = !info.maxed && tokens >= info.cost;

        d.name.text = info.name;
        d.level.text = "LEVEL " + info.level + " / " + info.maxLevel;
        d.desc.text = info.desc;

        d.nowValue.text = info.nowValue;
        d.nowValue.color = info.level == 0 ? GMuted : GMint;
        d.nowUnit.text = info.unit;

        d.nextValue.text = info.maxed ? "MAX" : info.nextValue;
        d.nextValue.color = info.maxed ? GMuted : UpgradeSystem.TokenColor;
        d.nextUnit.text = info.maxed ? "fully upgraded" : info.unit;

        // Race runs the fair stock arcade (GameFlow), so no upgrade applies there.
        d.note.text = (string.IsNullOrEmpty(info.note) ? "" : info.note + "  ") + "Not used in Race (stock arcade).";

        if (info.maxed) d.buyLabel.text = "MAXED OUT";
        else if (afford) d.buyLabel.text = "UPGRADE   " + info.cost.ToString("N0") + " TOKENS";
        // REWARDED TOP-UP (dormant until AdCoordinator.AdsEnabled): when the player cannot
        // afford the selected upgrade, the shortfall becomes a player-initiated ad offer worth
        // ONE upgrade (GarageTopUpTokens = 100 - never a big windfall). CrazyGames' policy
        // allows rewarded ads only on an explicit tap, never as an interstitial here.
        else if (AdCoordinator.TopUpEnabled) d.buyLabel.text = "WATCH AD  +" + AdCoordinator.GarageTopUpTokens.ToString("N0") + " TOKENS";
        else d.buyLabel.text = "NEED " + (info.cost - tokens).ToString("N0") + " MORE TOKENS";

        if (d.buyBg != null) d.buyBg.color = afford ? GBuyOn : GBuyOff;
        d.buyLabel.color = afford ? new Color(0.85f, 1f, 0.9f, 1f) : GMuted;
    }

    // ------------------------------------------------------------------ buying
    private void BuyUpgrade(string id)
    {
        if (garageSpendLock) return;
        // REWARDED TOP-UP (player-initiated, dormant): the tap landed while short on tokens,
        // so the offer plays one rewarded ad worth GarageTopUpTokens (100 = ONE upgrade's
        // worth) and tops the balance up. The purchase stays a manual second UPGRADE tap, so
        // the ad only ever makes ONE upgrade affordable - it never hands over a spendable
        // windfall. Cooldowns, rate limits and the gameplayStop-first rule all live inside
        // AdCoordinator; with ads dormant this silently falls through to the old no-op return.
        if (!UpgradeSystem.CanAfford(id))
        {
            AdCoordinator.TryClaimTopUp(() =>
            {
                UpgradeSystem.AddTokens(AdCoordinator.GarageTopUpTokens);
                RefreshGarageUI();
                if (garagePreview != null)
                {
                    garagePreview.Refresh();
                    garagePreview.SelectUpgrade(garageSelected);
                }
            });
            return;
        }
        int cost = UpgradeSystem.CostOfNext(id);
        if (!UpgradeSystem.TryUpgrade(id)) return;

        // Spend flow: token clinks + coins fly from the bank to the buy button, the balance
        // counts down under them; once the last coin lands the tool-clank rings and an
        // "UPGRADE INSTALLED" banner pops over the showroom.
        if (garageBadge != null && garageDetail != null && garageDetail.buy != null)
        {
            garageSpendLock = true;
            garageBadge.PlaySpend(cost, (RectTransform)garageDetail.buy.transform,
                () => StartCoroutine(UpgradeInstalledFlow()));
        }
        else
        {
            RefreshGarageUI();
            if (garagePreview != null) { garagePreview.Refresh(); garagePreview.SelectUpgrade(garageSelected); }
        }
    }

    private IEnumerator UpgradeInstalledFlow()
    {
        RoverSounds.PlayToolsClank();
        yield return new WaitForSeconds(0.15f);
        garageSpendLock = false;
        RefreshGarageUI();
        if (garagePreview != null)
        {
            garagePreview.Refresh();                       // tarnish, plow width and thrusters update
            garagePreview.SelectUpgrade(garageSelected);
        }
        ShowUpgradeInstalledPopup();
    }

    /// <summary>Short "UPGRADE INSTALLED" banner over the showroom: swells in, then fades.</summary>
    private void ShowUpgradeInstalledPopup()
    {
        if (garagePreviewImage == null) return;
        Text popup = Txt(garagePreviewImage.rectTransform, "UpgradeInstalled", "UPGRADE INSTALLED", 36, GMint,
            Vector2.zero, TextAnchor.MiddleCenter, 700f, 60f);
        popup.fontStyle = FontStyle.Bold;
        popup.raycastTarget = false;
        RectTransform pro = popup.rectTransform;
        pro.anchorMin = Vector2.zero;
        pro.anchorMax = Vector2.one;
        pro.offsetMin = Vector2.zero;
        pro.offsetMax = Vector2.zero;
        StartCoroutine(UpgradeInstalledPopupLife(popup));
    }

    private IEnumerator UpgradeInstalledPopupLife(Text popup)
    {
        RectTransform rt = popup.rectTransform;
        float age = 0f;
        const float life = 1.6f;
        const float popIn = 0.15f;
        while (rt != null && age < life)
        {
            age += Time.deltaTime;
            float k = age / popIn;
            if (k < 1f)
            {
                float e = 1f - (1f - k) * (1f - k);
                rt.localScale = Vector3.one * (0.6f + 0.4f * e + 0.15f * Mathf.Sin(e * Mathf.PI));
            }
            Color c = popup.color;
            c.a = Mathf.Clamp01(1f - (age - 0.7f) / 0.9f);
            popup.color = c;
            yield return null;
        }
        if (popup != null) Destroy(popup.gameObject);
    }

    // ------------------------------------------------------------------ close
    internal void CloseGarage()
    {
        if (garageCanvas == null) return;
        GameObject go = garageCanvas.gameObject;
        garageCanvas = null;
        garageDetail = null;
        garageRows.Clear();

        // Clear the EventSystem selection first, hide the canvas at once, and destroy it a
        // moment later - after this click handler has fully returned (a same-frame Destroy of
        // the pressed button is what wedged the EventSystem on WebGL before).
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        go.SetActive(false);
        if (garagePreview != null)
        {
            Destroy(garagePreview.gameObject);
            garagePreview = null;
        }
        Destroy(go, 0.1f);
    }
}
