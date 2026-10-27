using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The Shop screen (partial of MenuController): the garage's vehicle bays. Where the garage
/// tunes the parts on your CURRENT rover, the shop sells you entirely different rovers to drive -
/// each a different set of stat dials + the multipliers gameplay reads (see VehicleSystem).
/// Two full-size variant cards: the LUNAR BUGGY (around average in most stats - the all-rounder
/// you already own) and the MARS ROVER (slower off the line and down the straights, weaker boost
/// and hop, but a rugged hull, a wide gather blade and an oxygen drain it barely notices -
/// a harvester's machine). Tap BUY to spend tokens (with the same rewarded-ad top-up the garage
/// offers when short), then EQUIP whichever you own from either card. The wallet badge stays
/// top-left: it opens the garage as usual. No showroom yet - the cards' stat bars ARE the pitch.
/// </summary>
public partial class MenuController
{
    private Canvas shopCanvas;
    private WalletBadge shopBadge;
    private readonly List<ShopCard> shopCards = new List<ShopCard>();

    private class ShopCard
    {
        public VehicleSystem.Vehicle vehicle;
        public Image bg;
        public Image accent;            // yellow edge strip: shown on the armed card
        public Text name, status, blurb;
        public Image[] segs;            // 6 stats x 10 segments
        public Text hint;
        public Button action;
        public Image actionBg;
        public Text actionLabel;
    }

    // Five shop dials. A vehicle's stat list is an ordered six whose index 4 is SCOOP, but the
    // gather blade is not a rating the shop sells the vehicle on, so it is dropped from the
    // readout here. ShopStatIds maps each remaining dial row to its Vehicle.Stat index, and both
    // the card builder and RefreshShop size themselves off ShopStatNames so the labels and the
    // segment grid stay in lockstep.
    private static readonly string[] ShopStatNames = { "SPEED", "BOOST", "JUMP", "OXYGEN", "HULL" };
    private static readonly int[] ShopStatIds = { 0, 1, 2, 3, 5 };   // 4 = SCOOP, omitted

    public void OpenShop()
    {
        if (shopCanvas != null) return;
        shopCards.Clear();

        Canvas canvas = UiKit.CreateRootCanvas("ShopCanvas", 160);   // above the menu/map; the garage (170) opens on top when its badge is clicked
        shopCanvas = canvas;

        UiKit.Background(canvas.transform, "LunarBackground", "LunarBackground");
        RectTransform dim = UiKit.Rect(canvas.transform, "Dim", Vector2.zero, Vector2.zero, Vector2.zero);
        UiKit.StretchToFull(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0.012f, 0.02f, 0.016f, 0.62f);

        // The bay is a 2 x 2 grid sized to fill the 1920x1080 reference screen rather than a
        // single row, so four vehicles do not shrink the cards to unreadable. Both dimensions
        // are derived from the vehicle count (columns fixed at 2, rows from ceil(n/2)), so the
        // shop keeps working as vehicles are added. Each card gets its own render layer so its
        // showroom camera never catches another card's model.
        VehicleSystem.Vehicle[] bay = VehicleSystem.All;
        const int cols = 2;
        int rows = Mathf.CeilToInt(bay.Length / (float)cols);
        const float cardW = 905f, cardH = 430f, colGap = 30f, rowGap = 24f;
        const float headerH = 70f, bottomPad = 108f, sidePad = 40f;

        float panelW = cardW * cols + colGap * (cols - 1) + sidePad * 2f;
        float panelH = headerH + cardH * rows + rowGap * (rows - 1) + bottomPad;

        RectTransform panel = UiKit.Panel(canvas.transform, "ShopPanel",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(panelW, panelH), GPanel);

        Txt(panel, "ShopHeader", "SHOP", 34, GYellow, new Vector2(0f, -22f), TextAnchor.MiddleCenter, 1200f, 46f, UiKit.UiFontKind.Display);
        Text tag = Txt(panel, "ShopTag", "VEHICLE BAYS", 18, GMuted, new Vector2(0f, -52f), TextAnchor.MiddleCenter, 800f, 24f);
        tag.raycastTarget = false;

        // CLOSE sits in its own band UNDER the cards, matching the garage's bottom-centre
        // placement, with bottomPad reserving the height so no card - or the vehicle preview
        // inside it - reaches the button. (It used to anchor top-LEFT off panelW * 0.5, which
        // put it on the panel's horizontal CENTRE - right on top of the SHOP title.)
        UiKit.Button(panel, "B_CloseShop", "CLOSE", 24, GClose, GText,
            new Vector2(0f, 26f), new Vector2(240f, 44f), () => CloseShop());

        float colX0 = -((cardW + colGap) * (cols - 1)) * 0.5f;
        for (int i = 0; i < bay.Length; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float x = colX0 + (cardW + colGap) * col;
            float y = -(headerH + (cardH + rowGap) * row);
            shopCards.Add(BuildShopCard(panel, bay[i], x, y, 9 + i, cardW, cardH));
        }

        shopBadge = AttachWallet(canvas);
        RefreshShop();
    }

    /// <summary>
    /// One vehicle card. The card is a wide rectangle filling half the screen, so it is split:
    /// the live showroom render on the left, the name / status / blurb / stat dials / price on
    /// the right. <paramref name="cardW"/> and <paramref name="cardH"/> drive every internal
    /// offset, so the grid can be retuned in one place.
    /// </summary>
    private ShopCard BuildShopCard(RectTransform panel, VehicleSystem.Vehicle v, float x, float y, int layer,
        float cardW, float cardH)
    {
        RectTransform root = UiKit.Panel(panel, "Card_" + v.id, new Vector2(0.5f, 1f),
            new Vector2(x, y), new Vector2(cardW, cardH), GRow);

        ShopCard c = new ShopCard { vehicle = v };
        c.bg = root.GetComponent<Image>();

        // Yellow wasp strip on the left edge: lit on the vehicle that's currently armed.
        RectTransform accent = UiKit.Panel(root, "Accent", new Vector2(0f, 0f), Vector2.zero, new Vector2(6f, cardH), GYellow);
        c.accent = accent.GetComponent<Image>();
        c.accent.raycastTarget = false;

        // Live 3D model of THE vehicle on this card (same showroom as the garage, rendered into
        // its own RawImage). Each card uses its own render layer (9 / 10 / 11 / 12) so its camera
        // never catches another card's model, and the menu camera never catches any of them.
        // Sized down to 65% of the old 400x300 so it no longer crowds the card, and re-centred
        // on the old footprint (centre 222,-170) to keep the left column balanced. The camera is
        // pulled back to 7.0 (was 5.6) so the buggy has margin on every side of the black canvas:
        // at 5.6 the model's long axis clipped the frame corners as the showroom spun it around.
        GameObject modelGo = new GameObject("Model_" + v.id);
        modelGo.transform.SetParent(root, false);
        GaragePreview preview = modelGo.AddComponent<GaragePreview>();
        preview.Build(root, new Vector2(0f, 1f), new Vector2(92f, -72.5f), new Vector2(260f, 195f),
            v, layer, 7.0f);

        // Right-hand spec column.
        const float specX = 444f;
        float specW = cardW - specX - 24f;

        c.name = Txt(root, "Name", v.displayName, 28, GYellow, new Vector2(specX, -30f), TextAnchor.MiddleLeft, specW, 32f);
        c.name.fontStyle = FontStyle.Bold;
        c.status = Txt(root, "Status", "", 19, GMint, new Vector2(specX, -64f), TextAnchor.MiddleLeft, specW, 22f);
        c.status.fontStyle = FontStyle.Bold;

        c.blurb = Txt(root, "Blurb", v.blurb, 15, GText, new Vector2(specX, -90f), TextAnchor.UpperLeft, specW, 64f);
        c.blurb.horizontalOverflow = HorizontalWrapMode.Wrap;
        c.blurb.verticalOverflow = VerticalWrapMode.Truncate;

        // Five stat dials - label left, ten baked segments right (no text glyphs for WebGL).
        // The label and the ten segments share the spec column so the bar always ends inside
        // the card: 130px label, 10px gap, then 10 segments at a 26px pitch.
        const float labelW = 130f, segX = 140f, segPitch = 26f;
        c.segs = new Image[ShopStatNames.Length * UpgradeSystem.MaxLevel];
        for (int r = 0; r < ShopStatNames.Length; r++)
        {
            float rowY = -178f - 32f * r;
            Text label = Txt(root, "Stat_" + r, ShopStatNames[r], 15, GMuted,
                new Vector2(specX, rowY), TextAnchor.MiddleLeft, labelW, 26f);
            label.raycastTarget = false;
            for (int i = 0; i < UpgradeSystem.MaxLevel; i++)
            {
                RectTransform seg = UiKit.Panel(root, "Seg_" + r + "_" + i, new Vector2(0f, 1f),
                    new Vector2(specX + segX + i * segPitch, rowY - 13f), new Vector2(22f, 14f), GSegOff);
                c.segs[r * UpgradeSystem.MaxLevel + i] = seg.GetComponent<Image>();
                seg.GetComponent<Image>().raycastTarget = false;
            }
        }

        // Bottom band: hint (price / lock reason) left, action button right. The hint is capped
        // so its right edge stays clear of the action button.
        c.hint = Txt(root, "Hint", "", 15, GMuted, new Vector2(specX, -380f), TextAnchor.MiddleLeft, specW - 274f, 46f);
        c.hint.horizontalOverflow = HorizontalWrapMode.Wrap;

        c.action = UiKit.Button(root, "B_Action", "", 20, GBuyOn, GText,
            new Vector2(-20f, -372f), new Vector2(250f, 50f), () => ShopAction(c));
        c.actionBg = c.action.targetGraphic as Image;
        c.actionLabel = c.action.transform.Find("Label").GetComponent<Text>();
        return c;
    }

    private void RefreshShop()
    {
        if (shopBadge != null && !garageSpendLock) shopBadge.InstantSet(UpgradeSystem.GetTokens());

        int tokens = UpgradeSystem.GetTokens();
        foreach (ShopCard c in shopCards)
        {
            if (c == null) continue;
            VehicleSystem.Vehicle v = c.vehicle;
            bool owned = VehicleSystem.IsOwned(v);
            bool equipped = VehicleSystem.IsEquipped(v);
            bool unlocked = VehicleSystem.IsUnlocked(v);

            c.accent.enabled = equipped;
            c.segs = c.segs ?? new Image[0];
            for (int r = 0; r < ShopStatNames.Length; r++)
            {
                int lit = Mathf.Clamp(v.Stat(ShopStatIds[r]), 0, UpgradeSystem.MaxLevel);
                for (int i = 0; i < UpgradeSystem.MaxLevel; i++)
                {
                    int idx = r * UpgradeSystem.MaxLevel + i;
                    if (c.segs[idx] == null) continue;
                    c.segs[idx].color = i < lit ? GMint : GSegOff;
                }
            }

            if (!unlocked)
            {
                c.status.text = "LOCKED";
                c.status.color = GMuted;
                c.hint.text = v.unlockOnRidgeGold
                    ? "GOLD ON ANY RIDGE"
                    : v.unlockWorld >= 0
                        ? "REACH " + LevelManager.Planets[v.unlockWorld].planetName
                        : "";
                SetShopButton(c, "LOCKED", GBuyOff, false, false);
            }
            else if (!owned)
            {
                c.status.text = "NEW";
                c.status.color = GYellow;
                bool afford = tokens >= v.cost;
                c.hint.text = v.cost.ToString("N0") + " TOKENS";
                if (afford) SetShopButton(c, "BUY   " + v.cost.ToString("N0") + " TOKENS", GBuyOn, true, true);
                else if (AdCoordinator.TopUpEnabled)
                    SetShopButton(c, "WATCH AD  +" + AdCoordinator.GarageTopUpTokens.ToString("N0") + " TOKENS", GBuyOff, true, true);
                else SetShopButton(c, "NEED " + (v.cost - tokens).ToString("N0") + " MORE", GBuyOff, false, false);
            }
            else
            {
                c.status.text = equipped ? "EQUIPPED" : "OWNED";
                c.status.color = equipped ? GMint : GMuted;
                c.hint.text = equipped ? "ON THE FIELD" : "IN THE BAY";
                if (equipped) SetShopButton(c, "EQUIPPED", GBuyOff, false, false);
                else SetShopButton(c, "EQUIP", GBuyOn, true, true);
            }
        }
    }

    private static void SetShopButton(ShopCard c, string label, Color bg, bool enabled, bool hot)
    {
        if (c.actionLabel != null) c.actionLabel.text = label;
        if (c.actionBg != null) c.actionBg.color = bg;
        c.actionLabel.color = hot ? new Color(0.85f, 1f, 0.9f, 1f) : GMuted;
        if (c.action != null) c.action.interactable = enabled;
    }

    private void ShopAction(ShopCard c)
    {
        VehicleSystem.Vehicle v = c.vehicle;
        if (VehicleSystem.IsEquipped(v)) return;

        if (!VehicleSystem.IsOwned(v))
        {
            if (!VehicleSystem.IsUnlocked(v)) return;
            // REWARDED TOP-UP (same pattern as the garage): the tap landed while the card is
            // short on tokens, so play one ad worth GarageTopUpTokens and re-read the card.
            if (UpgradeSystem.GetTokens() < v.cost)
            {
                AdCoordinator.TryClaimTopUp(() =>
                {
                    UpgradeSystem.AddTokens(AdCoordinator.GarageTopUpTokens);
                    RefreshShop();
                });
                return;
            }
            int cost = VehicleSystem.TryBuy(v);
            if (cost <= 0) return;
            // Automatic arm: buying a vehicle fields it immediately.
            VehicleSystem.Select(v.id);
            if (shopBadge != null)
            {
                garageSpendLock = true;
                shopBadge.PlaySpend(cost, (RectTransform)c.action.transform, () =>
                {
                    garageSpendLock = false;
                    RefreshShop();
                });
            }
            else RefreshShop();
            return;
        }

        // Owning, not equipped: equip it.
        VehicleSystem.Select(v.id);
        RefreshShop();
    }

    internal void CloseShop()
    {
        if (shopCanvas == null) return;
        GameObject go = shopCanvas.gameObject;
        shopCanvas = null;
        shopCards.Clear();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        go.SetActive(false);
        Destroy(go, 0.1f);
    }
}