using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Lunar Trials congratulations screen: challenge name, completion time, medal, the
/// trials token payout (medal-scaled, plus a no-damage bonus), and a left-to-right nav row -
/// MAIN MENU (back to the ladder) / RETRY (same challenge, chase a faster time) / NEXT LEVEL.
/// Replaces the campaign win board entirely whenever a Trials challenge is active, so none
/// of the campaign's grade/prospector/token-split elements can leak into this edition.
/// </summary>
public static class TrialsWinBoard
{
    [Tooltip("Medal-base payouts; a clean (no-damage) run adds the no-damage bonus. A good " +
            "go therefore lands ~120-150 tokens, per the Trials economy.")]
    private const int TokensGold = 150;
    private const int TokensSilver = 135;
    private const int TokensBronze = 120;
    private const int TokensNoMedal = 100;
    private const int TokensNoDamageBonus = 25;

    public static void Show(ChallengeDef def)
    {
        TrialsRunManager mgr = TrialsRunManager.Current;
        float elapsed = mgr != null ? mgr.Elapsed : 0f;
        string medal = def.MedalFor(elapsed);

        RoverSounds.PlayCelebration();

        // Trials payout: medal-scaled, plus a clean-run bonus. Paid to the wallet every clear
        // (the Trials shop spends from the same wallet).
        int tokens = medal == "GOLD" ? TokensGold
            : medal == "SILVER" ? TokensSilver
            : medal == "BRONZE" ? TokensBronze
            : TokensNoMedal;
        BuggyHealth health = Object.FindAnyObjectByType<BuggyHealth>();
        bool noDamage = health != null && !health.IsDestroyed
            && health.CurrentHealth >= health.maxHealth - 0.5f;
        if (noDamage) tokens += TokensNoDamageBonus;
        UpgradeSystem.AddTokens(tokens);

        Canvas canvas = UiKit.CreateRootCanvas("WinCanvas", 200);
        UiKit.EnsureEventSystem();
        UiKit.Background(canvas.transform, "LunarBackground", "LunarBackground");
        RectTransform dim = UiKit.Panel(canvas.transform, "WinDim", Vector2.zero, Vector2.zero,
            new Vector2(1f, 1f), new Color(0.01f, 0.02f, 0.06f, 0.62f));
        UiKit.StretchToFull(dim);

        Color gold = new Color(1f, 0.84f, 0.25f, 1f);
        UiKit.Label(canvas.transform, "WinTitle", "CONGRATULATIONS", 56, gold,
            new Vector2(0f, -95f), TextAnchor.MiddleCenter, 1400f, 90f, UiKit.UiFontKind.Display);

        UiKit.Label(canvas.transform, "WinChallenge",
            def.title + " CLEARED   ·   TIME  " + elapsed.ToString("F1") + "s   ·   MEDAL  "
                + (string.IsNullOrEmpty(medal) ? "NONE" : medal),
            30, new Color(0.85f, 0.94f, 1f, 1f), new Vector2(0f, -175f), TextAnchor.MiddleCenter,
            1500f, 48f);

        UiKit.Label(canvas.transform, "WinTokens",
            "+" + tokens.ToString("N0") + " TOKENS TO WALLET" + (noDamage ? "  ·  NO-DAMAGE BONUS" : ""),
            32, gold, new Vector2(0f, -250f), TextAnchor.MiddleCenter, 1400f, 48f);

        // Nav row, left to right: ladder / retry / next. Square icon buttons a bit below the
        // centre; the label explaining each ("RETURN" / "TRY AGAIN" / "NEXT LEVEL") appears
        // ABOVE the row only while hovering that button - the trials-genre hover-tip idiom.
        Text hoverTip = UiKit.Label(canvas.transform, "NavHoverTip", "", 30,
            new Color(1f, 0.9f, 0.6f, 1f), new Vector2(0f, -90f), TextAnchor.MiddleCenter, 900f, 44f,
            UiKit.UiFontKind.Display);
        hoverTip.gameObject.SetActive(false);

        BuildNav(canvas, "NavMenu", "End", "RETURN", new Vector2(-220f, 0f),
            hoverTip, () => GameFlow.GoToMenu());
        BuildNav(canvas, "NavRetry", "Retry_Button", "TRY AGAIN", new Vector2(0f, 0f),
            hoverTip, () => ChallengeRunner.RestartActive());
        BuildNav(canvas, "NavNext", "Next_Button", "NEXT LEVEL", new Vector2(220f, 0f),
            hoverTip, () => ChallengeRunner.StartNext());
    }

    private static void BuildNav(Canvas canvas, string name, string textureKey, string tip,
        Vector2 pos, Text hoverTip, System.Action onClick)
    {
        Button nav = UiKit.ImageButton(canvas.transform, "B_Trials_" + name, textureKey,
            new Vector2(0.5f, 0.5f), pos, new Vector2(130f, 130f), onClick, 1f);
        UiKit.WireHover(nav, hoverTip, tip);
    }
}
