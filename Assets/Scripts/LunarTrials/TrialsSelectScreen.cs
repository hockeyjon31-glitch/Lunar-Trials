using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Lunar Trials edition's main screen: the 12-challenge ladder as a click-to-play grid.
/// Replaces the regular menu entirely inside the LUNAR_TRIALS build (MenuController routes
/// here); cards show each challenge's title, objective, best medal, and locked state.
/// Built entirely from code through UiKit, like every other screen.
/// </summary>
public static class TrialsSelectScreen
{
    private static bool gateHintShown;

    public static void Open()
    {
        Canvas canvas = UiKit.CreateRootCanvas("TrialsSelect", 110);
        UiKit.EnsureEventSystem();

        // Backdrop + dim, matching the main menu's art direction.
        UiKit.Background(canvas.transform, "LunarBackground", "LunarBackground");
        RectTransform dim = UiKit.Rect(canvas.transform, "Dim", Vector2.zero, Vector2.zero, Vector2.zero);
        UiKit.StretchToFull(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0.015f, 0.015f, 0.035f, 0.6f);

        UiKit.Label(canvas.transform, "Title", "LUNAR TRIALS", 64,
            new Color(238f / 255f, 221f / 255f, 130f / 255f, 1f), new Vector2(0f, -40f),
            TextAnchor.MiddleCenter, 1200f, 84f, UiKit.UiFontKind.Display);
        UiKit.Label(canvas.transform, "Subtitle", "MOON CHALLENGE LADDER", 24,
            new Color(0.6f, 0.68f, 0.8f, 1f), new Vector2(0f, -104f),
            TextAnchor.MiddleCenter, 1000f, 36f, UiKit.UiFontKind.Display);

        // Shop access (top-right): vehicles are bought and equipped here, exactly like the
        // campaign menu's shop - the shop opens as an overlay and returns to the ladder.
        UiKit.Button(canvas.transform, "B_TrialsShop", "SHOP", 24,
            new Color(0.08f, 0.1f, 0.13f, 0.9f), new Color(0.85f, 0.93f, 1f, 1f),
            new Vector2(-110f, -55f), new Vector2(190f, 54f),
            () => MenuController.Instance.OpenShop());

        // 3-column grid of challenge cards (4 rows). Cards are anchored to the screen CENTRE,
        // so the grid is laid out symmetrically around the middle: 4 rows of 112 + gaps span
        // roughly +250..-270 in reference space - balanced between the title above and the
        // controls hint below, and fully on-screen at every supported window size.
        float cardW = 400f, cardH = 112f, gapX = 30f, gapY = 24f;
        float firstRowCentreY = 195f - cardH * 0.5f;
        for (int i = 0; i < ChallengePack.All.Count; i++)
        {
            ChallengeDef def = ChallengePack.All[i];
            int col = i % 3;
            int row = i / 3;
            float x = (col - 1) * (cardW + gapX);
            float y = firstRowCentreY - row * (cardH + gapY);
            BuildCard(canvas.transform, def, new Vector2(x, y), new Vector2(cardW, cardH));
        }

        UiKit.Label(canvas.transform, "ControlsHint",
            "WASD DRIVE   ·   SPACE BOOST   ·   J HOP   ·   R RESTART", 20,
            new Color(0.55f, 0.62f, 0.72f, 1f), new Vector2(0f, 46f), TextAnchor.MiddleCenter, 1200f, 30f);
    }

    private static void BuildCard(Transform parent, ChallengeDef def, Vector2 centre, Vector2 size)
    {
        bool cleared = ChallengeRunner.IsCleared(def);
        bool unlocked = ChallengePack.IsUnlocked(def, ClearedIds());

        RectTransform card = UiKit.Panel(parent, "Card_" + def.id,
            new Vector2(0.5f, 0.5f), centre, size,
            unlocked ? new Color(0.07f, 0.11f, 0.15f, 0.94f) : new Color(0.05f, 0.05f, 0.07f, 0.9f));

        Text title = UiKit.Label(card, "Title",
            unlocked ? def.title : "[LOCKED] " + def.title, 24,
            cleared ? new Color(1f, 0.88f, 0.4f, 1f)
                : unlocked ? new Color(0.85f, 0.94f, 1f, 1f) : new Color(0.45f, 0.48f, 0.55f, 1f),
            new Vector2(0f, -26f), TextAnchor.MiddleCenter, size.x - 24f, 34f, UiKit.UiFontKind.Display);
        title.raycastTarget = false;

        Text brief = UiKit.Label(card, "Brief", unlocked ? def.brief : "CLEAR THE PREVIOUS CHALLENGE", 17,
            unlocked ? new Color(0.75f, 0.83f, 0.88f, 1f) : new Color(0.4f, 0.43f, 0.5f, 1f),
            new Vector2(0f, -58f), TextAnchor.MiddleCenter, size.x - 28f, 34f);
        brief.raycastTarget = false;
        brief.horizontalOverflow = HorizontalWrapMode.Wrap;

        string status = !unlocked ? ""
            : cleared ? "MEDAL: " + (string.IsNullOrEmpty(ChallengeRunner.MedalOf(def)) ? "CLEAR" : ChallengeRunner.MedalOf(def))
            : "NEW";
        Text statusText = UiKit.Label(card, "Status", status, 16,
            new Color(0.5f, 1f, 0.7f, 0.9f), new Vector2(0f, -90f), TextAnchor.MiddleCenter, size.x - 24f, 24f);
        statusText.raycastTarget = false;

        if (unlocked)
        {
            Button cardBtn = card.gameObject.AddComponent<Button>();
            cardBtn.targetGraphic = card.GetComponent<Image>();
            ColorBlock colors = cardBtn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.16f);
            colors.pressedColor = new Color(0.45f, 0.9f, 0.7f, 1f);
            colors.selectedColor = Color.white;
            cardBtn.colors = colors;
            ChallengeDef clicked = def;
            cardBtn.onClick.AddListener(delegate
            {
                Debug.Log("[Trials] card clicked: " + clicked.id);
                ChallengeRunner.StartChallenge(clicked);
            });
        }
    }

    private static System.Collections.Generic.HashSet<string> ClearedIds()
    {
        HashSet<string> ids = new HashSet<string>();
        for (int i = 0; i < ChallengePack.All.Count; i++)
        {
            if (ChallengeRunner.IsCleared(ChallengePack.All[i])) ids.Add(ChallengePack.All[i].id);
        }
        return ids;
    }
}
