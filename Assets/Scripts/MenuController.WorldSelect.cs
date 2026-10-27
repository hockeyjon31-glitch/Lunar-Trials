using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// World Select (partial of MenuController). One row per planet for whichever mode tab is open
/// (HARVESTER / PROSPECTOR / SURVIVAL). Every row shares one layout, filled from a small data struct,
/// so the three modes can no longer drift apart:
///   [#] NAME + stars | what + BEST | conditions | mini world sphere (the launch button)
/// The sphere replaces the old green RE-RUN / LAND / CANYON / RIDGE buttons: click the world to
/// land on it, with a short caption under it saying what will happen. Locked worlds show a
/// dimmed sphere and the unlock hint. Same green / yellow palette as the garage.
/// </summary>
public partial class MenuController
{
    private enum MapMode { Harvest, Prospector, Survival }

    private static MapMode mapMode = MapMode.Harvest;

    // Harvester rows show the drain you will actually get (planet drain x your O2 Cell tier),
    // not the planet's raw number. Set to false to show the raw planet value again.
    // NOTE: check this matches how OxygenManager applies UpgradeSystem.DrainFactor.
    private static readonly bool ShowUpgradedDrain = true;

    private const float MapRowHeight = 100f;
    private const float MapRowStep = 108f;
    private const float OrbSize = 76f;

    private struct WorldRowData
    {
        public string what;          // column 2, line 1
        public string best;          // column 2, line 2
        public Color bestColor;
        public string cond1;         // column 3, line 1
        public string cond2;         // column 3, line 2
        public string action;        // caption under the sphere
        public System.Action launch;
    }

    // ------------------------------------------------------------------ entry points
    // A failed run returns here with its mode; follow it so the player lands back on the
    // mode they were just playing.
    private void OpenPlanetMap()
    {
        if (GameFlow.IsRidgeSurvival) OpenPlanetMap(MapMode.Survival);
        else if (GameFlow.Mode == GameMode.Prospector) OpenPlanetMap(MapMode.Prospector);
        else OpenPlanetMap(MapMode.Harvest);
    }

    private void OpenPlanetMap(bool raceMode)
    {
        OpenPlanetMap(raceMode, false);
    }

    private void OpenPlanetMap(bool raceMode, bool survivalMode)
    {
        OpenPlanetMap(survivalMode ? MapMode.Survival : (raceMode ? MapMode.Prospector : MapMode.Harvest));
    }

    /// <summary>Open (or rebuild) the map in the given mode. The old canvas is retired first, so
    /// flipping a tab is a clean re-draw rather than a stacked UI.</summary>
    private void OpenPlanetMap(MapMode mode)
    {
        mapMode = mode;
        RetireCanvas(GameObject.Find("MapCanvas"));

        Canvas canvas = UiKit.CreateRootCanvas("MapCanvas", 150);
        // Lunar artwork backdrop, translucent veil, then the panel - same stack as the menu.
        UiKit.Background(canvas.transform, "LunarBackground", "LunarBackground");
        RectTransform dim = UiKit.Rect(canvas.transform, "Dim", Vector2.zero, Vector2.zero, Vector2.zero);
        UiKit.StretchToFull(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0.012f, 0.02f, 0.016f, 0.62f);

        RectTransform panel = UiKit.Panel(canvas.transform, "MapPanel",
            new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(1280f, 860f), GPanel);

        string header, blurb;
        switch (mode)
        {
            case MapMode.Survival:
                header = "CHOOSE YOUR RIDGE CREST";
                blurb = "Drive the ridge as far as you can. Can you be the ultimate survivor and beat Platinum?";
                break;
            case MapMode.Prospector:
                header = "CHOOSE YOUR PROSPECTING RUN";
                blurb = "Descend each world's canyon in the dark to mine ore before your oxygen runs out. Survive the run to unlock the next world.";
                break;
            default:
                header = "CHOOSE YOUR LANDING SITE";
                blurb = "Harvest ore before your oxygen runs out. Bank tokens to make upgrades to your Buggy.";
                break;
        }
        Txt(panel, "MapHeader", header, 32, GYellow, new Vector2(40f, -58f), TextAnchor.MiddleLeft, 600f, 46f, UiKit.UiFontKind.Display);
        Txt(panel, "MapBlurb", blurb, 19, GMuted, new Vector2(40f, -108f), TextAnchor.MiddleLeft, 1180f, 28f);

        // Mode tabs hang off the right, in the order they unlock. Each shows how many worlds
        // are open in that mode, so a locked tab is obvious before you click it. Ridge Survival
        // is a per-planet BONUS (unlocked by clearing a world's harvest grades), shown on every
        // build regardless of platform.
        BuildMapTab(panel, "HARVESTER", MapMode.Harvest, -420f);
        BuildMapTab(panel, "PROSPECTOR", MapMode.Prospector, -216f);
        BuildMapTab(panel, "SURVIVAL", MapMode.Survival, -12f);

        float y = -148f;
        for (int i = 0; i < LevelManager.PlanetCount; i++)
        {
            BuildWorldRow(panel, i, y);
            y -= MapRowStep;
        }

        UiKit.Button(panel, "B_BackFromMap", "BACK", 26, GClose, GText,
            new Vector2(0f, 26f), new Vector2(260f, 56f), () => ClosePlanetMap());

        // Top-left token badge: click it for the garage; it pulses while an upgrade is affordable.
        AttachWallet(canvas);
    }

    private static void ClosePlanetMap()
    {
        RetireCanvas(GameObject.Find("MapCanvas"));
    }

    /// <summary>Hides a canvas at once and destroys it a moment later, after the click handler
    /// that triggered this has fully returned (a same-frame Destroy of the pressed button is what
    /// wedged the EventSystem on WebGL). The name changes so GameObject.Find never returns it.</summary>
    private static void RetireCanvas(GameObject go)
    {
        if (go == null) return;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        go.name = go.name + "_retired";
        go.SetActive(false);
        Destroy(go, 0.1f);
    }

    // ------------------------------------------------------------------ helpers
    private static bool WorldUnlocked(MapMode mode, int index)
    {
        switch (mode)
        {
            case MapMode.Prospector: return LevelManager.IsModeUnlocked(index, GameMode.Prospector);
            case MapMode.Survival: return LevelManager.IsModeUnlocked(index, GameMode.Survival);
            default: return LevelManager.IsUnlocked(index);
        }
    }

    private static GameMode GameModeOf(MapMode mode)
    {
        switch (mode)
        {
            case MapMode.Prospector: return GameMode.Prospector;
            case MapMode.Survival: return GameMode.Survival;
            default: return GameMode.Harvester;
        }
    }

    private void BuildMapTab(RectTransform panel, string label, MapMode mode, float x)
    {
        bool on = mapMode == mode;
        int open = 0;
        for (int i = 0; i < LevelManager.PlanetCount; i++)
            if (WorldUnlocked(mode, i)) open++;

        Color onBg = new Color(0.18f, 0.45f, 0.3f, 1f);
        Color offBg = new Color(0.1f, 0.14f, 0.12f, 1f);
        Color onTxt = new Color(0.9f, 1f, 0.92f, 1f);
        Color offTxt = open > 0 ? new Color(0.55f, 0.64f, 0.58f, 1f) : new Color(0.38f, 0.44f, 0.4f, 1f);

        UiKit.Button(panel, "B_Map" + label, label + "  " + open + "/" + LevelManager.PlanetCount, 20,
            on ? onBg : offBg, on ? onTxt : offTxt,
            new Vector2(x, -58f), new Vector2(190f, 46f), () => OpenPlanetMap(mode));
    }

    // ------------------------------------------------------------------ one world row
    /// <summary>Builds one world's map row. Unlocked rows carry a star ladder and a hover that
    /// flashes the row's FRONTIER star (the one you're on now) - locked rows show the gate hint
    /// and never flash.</summary>
    private void BuildWorldRow(RectTransform panel, int index, float y)
    {
        LevelDefinition def = LevelManager.Planets[index];
        bool unlocked = WorldUnlocked(mapMode, index);

        RectTransform row = UiKit.Panel(panel, "Row_" + index, new Vector2(0f, 1f),
            new Vector2(60f, y), new Vector2(1160f, MapRowHeight),
            unlocked ? new Color(0.08f, 0.13f, 0.10f, 0.95f) : new Color(0.07f, 0.09f, 0.08f, 0.9f));

        // Column 1: number, name and the progress stars underneath (harvest / race / ridge grade stars).
        RowTxt(row, "Num", (index + 1).ToString(), 24, GMuted, new Vector2(16f, -16f), TextAnchor.MiddleLeft, 50f, 34f);
        RowTxt(row, "Name", def.planetName, 28, unlocked ? GText : GMuted, new Vector2(64f, -14f), TextAnchor.MiddleLeft, 300f, 36f);

        // Baked sprite stars (never text glyphs - WebGL strips them from the font atlas).
        Image[] ladder = null;
        if (mapMode == MapMode.Harvest)
        {
            ladder = UiKit.StarLadder(row, "Stars", LevelManager.GetHarvestSitesPassed(index),
                LevelManager.MaxHarvestGrade, new Vector2(0f, 1f), new Vector2(76f, -72f), 24f, 8f);
        }
        else if (mapMode == MapMode.Prospector)
        {
            // Prospector has its own grade progression and five-star ladder, independent of Harvest.
            ladder = UiKit.StarLadder(row, "Stars", LevelManager.GetProspectorSitesPassed(index),
                LevelManager.MaxProspectorGrade, new Vector2(0f, 1f), new Vector2(76f, -72f), 24f, 8f);
        }
        else if (mapMode == MapMode.Survival)
        {
            // Four grade stars, one per ridge grade (Bronze / Silver / Gold / Platinum): each
            // glows in its grade colour once reached, empty (dim) until then - like harvest stars.
            ladder = UiKit.StarLadder(row, "Stars", GameFlow.RidgeGradeNames.Length, new Vector2(0f, 1f),
                new Vector2(76f, -72f), 24f, 8f, GameFlow.RidgeGradeColors,
                Mathf.Max(0, LevelManager.GetRidgeBestGrade(index) + 1));
        }

        // Columns 2 and 3 (or the lock hint).
        if (unlocked)
        {
            WorldRowData d = RowDataFor(mapMode, index, def);
            RowTxt(row, "What", d.what, 20, GLeaf, new Vector2(380f, -20f), TextAnchor.MiddleLeft, 300f, 28f);
            RowTxt(row, "Best", d.best, 18, d.bestColor, new Vector2(380f, -54f), TextAnchor.MiddleLeft, 320f, 26f);
            RowTxt(row, "Cond1", d.cond1, 19, new Color(0.85f, 0.76f, 0.45f, 1f), new Vector2(720f, -24f), TextAnchor.MiddleLeft, 300f, 28f);
            RowTxt(row, "Cond2", d.cond2, 19, new Color(0.6f, 0.8f, 0.7f, 1f), new Vector2(720f, -58f), TextAnchor.MiddleLeft, 300f, 28f);

            RectTransform orb = WorldOrb.Create(row, index, def, new Vector2(1f, 1f), new Vector2(-24f, -6f), OrbSize, true);
            RowTxt(row, "Action", d.action, 15, GYellow, new Vector2(-24f, -82f), TextAnchor.MiddleCenter, OrbSize, 16f);
            AttachOrbButton(orb, d.launch);
            AttachRowLaunch(row, orb, ladder, index, d.launch);
        }
        else
        {
            // The hint names the gate: for a harvest world it is the previous world's RACE
            // bronze; for a race world it is surviving this world's harvest; for survival it
            // is clearing all of this world's harvest grades.
            string hint = LevelManager.UnlockGateText(index, GameModeOf(mapMode));
            RowTxt(row, "Locked", "LOCKED", 24, new Color(0.45f, 0.5f, 0.46f, 1f), new Vector2(380f, -18f), TextAnchor.MiddleLeft, 300f, 30f);
            Text hintText = RowTxt(row, "LockHint", hint, 17, new Color(0.4f, 0.46f, 0.42f, 1f),
                new Vector2(380f, -52f), TextAnchor.UpperLeft, 620f, 40f);
            hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hintText.verticalOverflow = VerticalWrapMode.Truncate;

            WorldOrb.Create(row, index, def, new Vector2(1f, 1f), new Vector2(-24f, -6f), OrbSize, false);
            RowTxt(row, "Action", "LOCKED", 15, GMuted, new Vector2(-24f, -82f), TextAnchor.MiddleCenter, OrbSize, 16f);
        }
    }

    private WorldRowData RowDataFor(MapMode mode, int index, LevelDefinition def)
    {
        WorldRowData d = new WorldRowData();
        Color has = new Color(1f, 0.85f, 0.3f, 1f);
        Color none = new Color(0.4f, 0.46f, 0.42f, 1f);

        if (mode == MapMode.Prospector)
        {
            bool cleared = LevelManager.IsProspectorCleared(index);
            int best = LevelManager.GetProspectorBest(index);
            d.what = "PROSPECTOR RUN";
            d.best = best > 0 ? "BEST  " + best.ToString("N0") + " TOKENS"
                : (cleared ? "CLEARED - NO TOKENS SCORED" : "NOT PLAYED YET");
            d.bestColor = best > 0 || cleared ? has : none;
            d.cond1 = "DROP  " + def.raceDrop.ToString("0") + " m";
            d.action = cleared ? "RE-RUN" : "DESCEND";
            int i = index;
            d.launch = () => GameFlow.StartProspectorAt(i);
        }
        else if (mode == MapMode.Survival)
        {
            int best = Leaderboard.BestSurvivalWorld(index);
            d.what = "RIDGE CREST";
            d.best = best > 0 ? "BEST  " + OxygenManager.FormatKm(best) : "NO DISTANCE YET";
            d.bestColor = best > 0 ? has : none;
            d.cond1 = "DROP  " + def.raceDrop.ToString("0") + " m";
            d.action = "SURVIVE";
            int i = index;
            d.launch = () => GameFlow.StartSurvivalAt(i, RaceCourse.Ridge);
        }
        else
        {
            int best = Leaderboard.BestForWorld(index);
            float drain = def.drainPerSecond * (ShowUpgradedDrain ? UpgradeSystem.DrainFactor : 1f) * VehicleSystem.DrainMul;
            d.what = def.harvestableName;
            d.best = best > 0 ? "BEST  " + best.ToString("N0") + " TOKENS" : "NOT PLAYED YET";
            d.bestColor = best > 0 ? has : none;
            d.cond1 = "O2 DRAIN  " + drain.ToString("0.0") + "/s";
            d.action = LevelManager.IsCleared(index) ? "RE-RUN" : "LAND";
            int i = index;
            d.launch = () => GameFlow.StartHarvesterAt(i);
        }
        d.cond2 = "GRIP  " + def.gripMultiplier.ToString("0.00");
        return d;
    }

    /// <summary>Makes the whole world row clickable (not just the orb), so one tap on the row
    /// launches instantly. Rows that have no orb still add no button - locked worlds can't launch.
    /// Hover also flashes the row's FRONTIER star (the one you're on now) until you hover away:
    /// never on locked rows, never on rows you aren't actually pointing at.</summary>
    private static void AttachRowLaunch(RectTransform row, RectTransform orb, Image[] ladder, int index, System.Action launch)
    {
        Button b = row.gameObject.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.targetGraphic = row.GetComponent<Image>();
        b.onClick.AddListener(() => { if (launch != null) launch(); });

        // What to flash while hovered: the star right after the ones already passed, if any.
        int frontier = mapMode == MapMode.Prospector
            ? LevelManager.GetProspectorSitesPassed(index)
            : mapMode == MapMode.Harvest ? LevelManager.GetHarvestSitesPassed(index)
            : mapMode == MapMode.Survival ? LevelManager.GetRidgeBestGrade(index) + 1
            : LevelManager.GetHarvestSitesPassed(index);
        int cap = mapMode == MapMode.Prospector ? LevelManager.MaxProspectorGrade
            : mapMode == MapMode.Harvest ? LevelManager.MaxHarvestGrade
            : mapMode == MapMode.Survival ? GameFlow.RidgeGradeNames.Length
            : LevelManager.MaxHarvestGrade;
        bool flashable = ladder != null && frontier >= 0 && frontier < cap && frontier < ladder.Length;

        // Hover lifts the row tint, swells the orb, and sets the world revolving exactly like
        // the orb's own hover, so the whole row reads as one big target instead of a text panel
        // with a tiny button. (The old yellow highlight ring is gone - the lit row is enough.)
        Transform art = orb != null ? orb.Find("Art") : null;
        Image rowImg = row.GetComponent<Image>();
        Color baseColor = rowImg.color;
        EventTrigger trigger = row.gameObject.AddComponent<EventTrigger>();
        EventTrigger.Entry enter = new EventTrigger.Entry();
        enter.eventID = EventTriggerType.PointerEnter;
        enter.callback.AddListener(delegate
        {
            rowImg.color = Color.Lerp(baseColor, new Color(0.13f, 0.22f, 0.17f, 1f), 0.55f);
            if (art != null) art.localScale = Vector3.one * 1.06f;
            OrbRevolve.Start(art);
            if (flashable && row.GetComponent<StarBurst>() == null)
            {
                StarBurst p = row.gameObject.AddComponent<StarBurst>();
                p.Begin(ladder, frontier, frontier + 1);
            }
        });
        trigger.triggers.Add(enter);
        EventTrigger.Entry exit = new EventTrigger.Entry();
        exit.eventID = EventTriggerType.PointerExit;
        exit.callback.AddListener(delegate
        {
            rowImg.color = baseColor;
            if (art != null) art.localScale = Vector3.one;
            OrbRevolve.Stop(art);
            StarBurst p = row.GetComponent<StarBurst>();
            if (p != null) Object.Destroy(p);
        });
        trigger.triggers.Add(exit);
    }

    /// <summary>Makes the orb clickable: hover swells the sphere and sets it revolving (the
    /// whole row does the same, so the target reads as one lit block, no yellow ring needed).</summary>
    private static void AttachOrbButton(RectTransform orb, System.Action launch)
    {
        Button b = orb.gameObject.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.targetGraphic = orb.GetComponent<Image>();
        b.onClick.AddListener(() => { if (launch != null) launch(); });

        Transform art = orb.Find("Art");
        EventTrigger trigger = orb.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry enter = new EventTrigger.Entry();
        enter.eventID = EventTriggerType.PointerEnter;
        enter.callback.AddListener(delegate
        {
            if (art != null) art.localScale = Vector3.one * 1.08f;
            OrbRevolve.Start(art);
        });
        trigger.triggers.Add(enter);

        EventTrigger.Entry exit = new EventTrigger.Entry();
        exit.eventID = EventTriggerType.PointerExit;
        exit.callback.AddListener(delegate
        {
            if (art != null) art.localScale = Vector3.one;
            OrbRevolve.Stop(art);
        });
        trigger.triggers.Add(exit);
    }

    /// <summary>Turns the world's surface left-to-right while hovered: slides the orb's Skim
    /// uvRect so the mottled pattern drifts across the fixed shaded ball - a globe seen from
    /// the side, turning on its axis, rather than the old in-plane spin (which read as if you
    /// were looking down on it).</summary>
    private sealed class OrbRevolve : MonoBehaviour
    {
        public RawImage skim;
        [Tooltip("Whole turns per second (1.0 = one full revolution).")]
        public float turnsPerSecond = 0.12f;
        public float direction = 1f;

        private void Update()
        {
            if (skim == null || !isActiveAndEnabled) return;
            Rect r = skim.uvRect;
            r.x -= direction * turnsPerSecond * Time.deltaTime;
            skim.uvRect = r;
        }

        public static void Start(Transform artGo)
        {
            if (artGo == null) return;
            Transform skimGo = artGo.Find("Skim");
            if (skimGo == null) return;
            RawImage skimImg = skimGo.GetComponent<RawImage>();
            if (skimImg == null) return;
            OrbRevolve spin = artGo.GetComponent<OrbRevolve>();
            if (spin == null) spin = artGo.gameObject.AddComponent<OrbRevolve>();
            spin.skim = skimImg;
            WorldOrb.OrbSpin tag = skimGo.GetComponent<WorldOrb.OrbSpin>();
            spin.direction = tag != null ? tag.direction : 1f;
            spin.enabled = true;
        }

        public static void Stop(Transform artGo)
        {
            if (artGo == null) return;
            OrbRevolve spin = artGo.GetComponent<OrbRevolve>();
            if (spin != null) spin.enabled = false;
        }
    }
}
