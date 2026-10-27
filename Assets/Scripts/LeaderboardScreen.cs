using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The leaderboard screen: browseable top-score boards for ALL 15 levels (5 worlds x
/// Survey / Race Canyon / Ridge Survival). Left/right toggle buttons flip through the boards,
/// with a "BOARD n OF 15" counter so the player can find any world's scores without ever
/// returning to the menus. The run that just finished opens on its own board, highlighted.
/// All UI is built from code.
/// </summary>
public class LeaderboardScreen : MonoBehaviour
{
    private RectTransform listPanel;
    private Text titleText;
    private Text counterText;
    private Text runnerText;
    private Text prevText;
    private Text nextText;
    private int browseIndex;

    /// <summary>Board slots per world, in display order: Survey, Race Canyon, Ridge Survival.</summary>
    private const int BoardsPerWorld = 3;
    private static readonly string[] SlotNames = { "SURVEY", "RACE - CANYON", "RIDGE - SURVIVAL" };

    private void Awake()
    {
        GameFlow.ResetSaveIfVersionChanged();
        UiKit.EnsureEventSystem();
        EnsureCamera();
        MenuMusic.Ensure();
        browseIndex = ArrivalBoardIndex();
        BuildUI();
        ShowBoard();
    }

    private void EnsureCamera()
    {
        Camera cam = Camera.main;
        if (cam != null) return;
        GameObject go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.02f, 0.05f, 1f);
        go.AddComponent<AudioListener>();
    }

    /// <summary>Board slot that should open first: the run that just finished (if any), else
    /// the last world/mode selection. Wraps safely for any GameFlow state.</summary>
    private static int ArrivalBoardIndex()
    {
        int world = Mathf.Clamp(LevelManager.CurrentLevelIndex, 0, LevelManager.PlanetCount - 1);
        int slot = 0; // Survey
        if (GameFlow.IsRidgeSurvival) slot = 2;
        else if (GameFlow.Mode == GameMode.Race)
            slot = GameFlow.CurrentCourse == RaceCourse.Ridge ? 2 : 1;
        return ClampBoard(world * BoardsPerWorld + slot);
    }

    private static int BoardCount() => LevelManager.PlanetCount * BoardsPerWorld;
    private static int ClampBoard(int i) => ((i % BoardCount()) + BoardCount()) % BoardCount();

    private void MoveBoard(int delta)
    {
        browseIndex = ClampBoard(browseIndex + delta);
        ShowBoard();
    }

    private void BuildUI()
    {
        Canvas canvas = UiKit.CreateRootCanvas("LeaderboardCanvas", 100);

        // Lunar artwork backdrop, plus a translucent veil to keep the board readable. The same
        // image the main menu uses, so both screens share one visual identity.
        UiKit.Background(canvas.transform, "LunarBackground", "LunarBackground");
        RectTransform dim = UiKit.Rect(canvas.transform, "Dim", Vector2.zero, Vector2.zero, Vector2.zero);
        UiKit.StretchToFull(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0.015f, 0.015f, 0.035f, 0.35f);

        titleText = UiKit.Label(canvas.transform, "Title", "", 40,
            new Color(0.5f, 1f, 0.85f, 1f), new Vector2(0f, -70f), TextAnchor.MiddleCenter, 1400f, 66f,
            UiKit.UiFontKind.Display);

        counterText = UiKit.Label(canvas.transform, "Counter", "", 22,
            new Color(0.55f, 0.62f, 0.68f, 1f), new Vector2(0f, -120f), TextAnchor.MiddleCenter, 500f, 30f);

        runnerText = UiKit.Label(canvas.transform, "ResultLine", "", 30,
            new Color(1f, 0.92f, 0.55f, 1f), new Vector2(0f, -156f), TextAnchor.MiddleCenter, 1400f, 44f);

        // Rows are built into this panel (cleared on each pass). The panel is sat below the
        // result line so the top-10 rows stay clear of it.
        listPanel = UiKit.Panel(canvas.transform, "List", new Vector2(0.5f, 0.5f),
            new Vector2(0f, -80f), new Vector2(1060f, 640f), new Color(0.05f, 0.05f, 0.07f, 0.55f));

        // Board navigation: small HORIZONTAL SLITS tucked in at the very top of the board,
        // either side of the "BOARD n OF 15" counter - thin pills that flip through the
        // boards without hogging the screen like the old full-height side columns.
        // NOTE: UiKit anchors negative-x to the right edge and positive-x to the left edge,
        // so PREV lives on the LEFT (+x) and NEXT on the RIGHT (-x).
        Color navBg = new Color(0.10f, 0.24f, 0.26f, 1f);
        Color navFg = new Color(0.7f, 1f, 0.9f, 1f);
        prevText = UiKit.Button(canvas.transform, "B_Prev", "\u25C0  PREV", 16,
            navBg, navFg, new Vector2(330f, -120f), new Vector2(130f, 30f), () => MoveBoard(-1))
            .GetComponentInChildren<Text>();
        nextText = UiKit.Button(canvas.transform, "B_Next", "NEXT  \u25B6", 16,
            navBg, navFg, new Vector2(-330f, -120f), new Vector2(130f, 30f), () => MoveBoard(+1))
            .GetComponentInChildren<Text>();

        UiKit.Button(canvas.transform, "B_BackToMenu", "BACK TO MENU", 30,
            new Color(0.12f, 0.32f, 0.26f, 1f), new Color(0.8f, 1f, 0.9f, 1f),
            new Vector2(0f, 60f), new Vector2(420f, 66f), () => GameFlow.GoToMenu());

        // Tiny footer so a stale/cached build is easy to spot.
        UiKit.Label(canvas.transform, "BuildTag", "BUILD " + GameFlow.BuildTag, 16,
            new Color(0.4f, 0.45f, 0.5f, 1f), new Vector2(0f, 8f), TextAnchor.MiddleCenter, 300f, 24f);

        // Top-left banked-token badge (the garage wallet), consistent across the menu screens.
        WalletBadge.AttachTopLeft(canvas, UpgradeSystem.GetTokens());
    }

    /// <summary>Refreshes the title, counter and top-10 rows for the currently browsed board.</summary>
    private void ShowBoard()
    {
        int world = browseIndex / BoardsPerWorld;
        int slot = browseIndex % BoardsPerWorld;
        bool isRace = slot == 1;      // RACE - CANYON: the fair stock time board
        bool isSurvival = slot == 2;  // RIDGE - SURVIVAL: best distance survived board

        LevelDefinition def = LevelManager.Planets[world];
        string planet = def != null ? def.planetName : "WORLD " + (world + 1);

        titleText.text = "LEADERBOARD  -  " + planet + "  -  " + SlotNames[slot];
        counterText.text = "BOARD " + (browseIndex + 1) + " OF " + BoardCount();

        // Nav buttons always exist; hide a toggle's label at the end of its travel so the
        // player can see at a glance there is nowhere left to go that way.
        prevText.text = browseIndex > 0 ? "\u25C0  PREV" : "";
        nextText.text = browseIndex < BoardCount() - 1 ? "NEXT  \u25B6" : "";

        // Runner line: only meaningful on the arrival board (the run this screen opened for).
        bool arrival = browseIndex == ArrivalBoardIndex();
        if (arrival && GameFlow.FinalScore > 0)
        {
            if (isSurvival)
            {
                runnerText.text = "RUNNER  " + GameFlow.DisplayName + "   -   SURVIVED "
                    + OxygenManager.FormatKm(GameFlow.FinalScore)
                    + (GameFlow.RaceNewBest ? "   -   NEW BEST" : "");
            }
            else if (isRace)
            {
                runnerText.text = "RUNNER  " + GameFlow.DisplayName + "   -   RACED "
                    + OxygenManager.FormatRaceTime(GameFlow.FinalScore)
                    + (GameFlow.RaceNewBest ? "   -   NEW BEST" : "");
            }
            else
            {
                runnerText.text = "RUNNER  " + GameFlow.DisplayName + "   -   SURVEY "
                    + GameFlow.FinalScore + " TOKENS";
            }
        }
        else
        {
            runnerText.text = "";
        }

        foreach (Transform child in listPanel)
        {
            Destroy(child.gameObject);
        }

        List<LeaderboardEntry> entries = isSurvival
            ? Leaderboard.LoadSurvivalWorld(world)
            : isRace ? Leaderboard.LoadRaceWorld(world, "canyon") : Leaderboard.LoadWorld(world);

        if (entries.Count == 0)
        {
            UiKit.Label(listPanel, "Empty", isSurvival ? "NO DISTANCES YET" : "NO TIMES YET", 30,
                new Color(0.5f, 0.55f, 0.62f, 1f), new Vector2(0f, 0f), TextAnchor.MiddleCenter, 700f, 48f);
            return;
        }

        float rowY = 340f;
        float rowGap = 62f;

        Color hdr = new Color(0.55f, 0.62f, 0.68f, 1f);
        RectTransform hdrRow = UiKit.Rect(listPanel, "HdrRow", new Vector2(0.5f, 0.5f),
            new Vector2(0f, rowY + 30f), new Vector2(1020f, 26f));
        RowText(hdrRow, "HdrRank", "RANK", 20, hdr, new Vector2(0f, 0.5f), 30f, 90f, TextAnchor.MiddleCenter);
        RowText(hdrRow, "HdrName", "RUNNER", 20, hdr, new Vector2(0f, 0.5f), 150f, 430f, TextAnchor.MiddleLeft);
        RowText(hdrRow, "HdrValue", isSurvival ? "DISTANCE" : (isRace ? "TIME" : "TOKENS"), 20, hdr,
            new Vector2(0f, 0.5f), 620f, 380f, TextAnchor.MiddleRight);

        int justRunScore = arrival && GameFlow.FinalScore > 0 ? GameFlow.FinalScore : -1;
        for (int i = 0; i < entries.Count && i < 10; i++)
        {
            LeaderboardEntry e = entries[i];
            bool isNew = arrival && e.name == GameFlow.DisplayName && e.score == justRunScore;

            RectTransform row = UiKit.Rect(listPanel, "Row" + i, new Vector2(0.5f, 0.5f),
                new Vector2(0f, rowY), new Vector2(1020f, 52f));
            if (isNew)
            {
                Image bg = row.gameObject.AddComponent<Image>();
                bg.color = new Color(0.16f, 0.34f, 0.26f, 0.9f);
            }

            Color nameColor = isNew ? new Color(1f, 0.9f, 0.5f, 1f) : Color.white;
            string value = isSurvival ? OxygenManager.FormatKm(e.score)
                : isRace ? OxygenManager.FormatRaceTime(e.score) : e.score.ToString("N0");
            RowText(row, "Rank", (i + 1).ToString(), 26, new Color(0.7f, 0.75f, 0.8f, 1f),
                new Vector2(0f, 0.5f), 30f, 90f, TextAnchor.MiddleCenter);
            RowText(row, "Name", e.name, 28, nameColor,
                new Vector2(0f, 0.5f), 150f, 430f, TextAnchor.MiddleLeft, clip: true);
            RowText(row, "Value", value, 28,
                new Color(0.5f, 1f, 0.8f, 1f), new Vector2(0f, 0.5f), 620f, 380f, TextAnchor.MiddleRight);

            rowY -= rowGap;
        }
    }

    /// <summary>
    /// Adds one leaderboard row cell: a rect anchored/ pivoted on the left edge, filled
    /// with clipped text so the columns (rank / runner / value) can never overlap.
    /// </summary>
    private static void RowText(RectTransform parent, string name, string text, int fontSize, Color color,
        Vector2 anchor, float x, float width, TextAnchor alignment, bool clip = false)
    {
        RectTransform rt = UiKit.Rect(parent, name, anchor, new Vector2(x, 0f), new Vector2(width, 46f));
        if (clip) rt.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        Text t = rt.gameObject.AddComponent<Text>();
        t.font = UiKit.ResolveDefaultFont();
        t.fontSize = fontSize;
        t.alignment = alignment;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = text;
        UiKit.StretchToFull(t.rectTransform);
    }
}