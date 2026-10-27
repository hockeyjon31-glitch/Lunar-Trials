using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One saved run on a leaderboard.</summary>
[Serializable]
public class LeaderboardEntry
{
    public string name;
    public int score;
    public string detail;
    /// <summary>Sum of the player's upgrade levels (0..25) at the time the run was made.</summary>
    public int buildLevel;
}

/// <summary>
/// Persistent top-10 leaderboards stored with PlayerPrefs (C# JSON), so saves survive
/// restarts without any files to manage. Harvester keeps a separate per-world board (the
/// run's tokens, which are its score, reset every level), which the planet map reads to show the player's own best on each
/// world. Lunar Race keeps its own per-world time board in centiseconds (smallest = fastest)
/// for Canyon runs only. Ridge Survival keeps its own per-world DISTANCE board in metres
/// (largest = best), for the survival play-variant on the Ridge crest.
/// </summary>
public static class Leaderboard
{
    private const string HarvestWorldKeyPrefix = "lunar.leaderboard.harvest.w";
    private const string RaceWorldKeyPrefix = "lunar.leaderboard.race.w";
    private const string RidgeSurvivalKeyPrefix = "lunar.leaderboard.ridge.w";
    private const int MaxEntries = 10;

    [Serializable]
    private class EntryList
    {
        public List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
    }

    // ------------------------------------------------------- per-world Harvester boards

    private static string WorldKey(int worldIndex) => HarvestWorldKeyPrefix + worldIndex + ".v2";

    /// <summary>Loads one world's Harvester board, best first (never returns null).</summary>
    public static List<LeaderboardEntry> LoadWorld(int worldIndex)
    {
        return LoadKey(WorldKey(worldIndex));
    }

    /// <summary>The player's own best score on a world (0 if none yet).</summary>
    public static int BestForWorld(int worldIndex)
    {
        return BestKey(WorldKey(worldIndex));
    }

    public static bool IsEmptyWorld(int worldIndex)
    {
        return LoadKey(WorldKey(worldIndex)).Count == 0;
    }

    /// <summary>Whether a score would place on a given world's board.</summary>
    public static bool WouldQualifyOnWorld(int worldIndex, int score)
    {
        return WouldQualifyKey(WorldKey(worldIndex), score);
    }

    /// <summary>Records a Harvester run on its world's board; returns the 1-based rank.</summary>
    public static int AddWorld(int worldIndex, string name, int score, string detail, int buildLevel = 0)
    {
        int rank = AddKey(WorldKey(worldIndex), name, score, detail, buildLevel);
        if (rank == 1) CrazyBoard.SubmitHarvest(worldIndex, score);
        if (rank == 1) NewgroundsBridge.SubmitHarvest(worldIndex, score);
        return rank;
    }

    public static void ClearWorld(int worldIndex)
    {
        ClearKey(WorldKey(worldIndex));
    }

    // ------------------------------------------------------- per-world Lunar Race boards
    // Race times are centiseconds, so the board sorts ASCENDING (fastest first) unlike the
    // Harvester token boards (descending). Each world now has separate boards per course
    // variant (Canyon / Ridge).

    private static string RaceWorldKey(int worldIndex) => RaceWorldKeyPrefix + worldIndex + ".v1";
    private static string RaceCourseKey(int worldIndex, string courseKey) =>
        RaceWorldKeyPrefix + worldIndex + "." + courseKey + ".v2";

    /// <summary>Loads one world's Lunar Race board for a specific course, fastest first.</summary>
    public static List<LeaderboardEntry> LoadRaceWorld(int worldIndex, string courseKey)
    {
        return LoadKey(RaceCourseKey(worldIndex, courseKey));
    }

    /// <summary>The best (fastest) race time on a world/course in centiseconds (0 if none yet).</summary>
    public static int BestRaceWorld(int worldIndex, string courseKey)
    {
        return BestAscendingKey(RaceCourseKey(worldIndex, courseKey));
    }

    public static bool IsEmptyRaceWorld(int worldIndex, string courseKey)
    {
        return LoadKey(RaceCourseKey(worldIndex, courseKey)).Count == 0;
    }

    /// <summary>Records a Lunar Race run on its world/course board; returns the 1-based rank
    /// (1 = fastest). Only completed runs should be recorded.</summary>
    public static int AddRaceWorld(int worldIndex, string name, int centiseconds, string detail,
        string courseKey, int buildLevel = 0)
    {
        int rank = AddKeyAscending(RaceCourseKey(worldIndex, courseKey), name, centiseconds, detail, buildLevel);
        if (rank == 1)
        {
            CrazyBoard.SubmitRace(worldIndex, centiseconds);
            KongregateBridge.OnProgressChanged();   // new course best -> race-time stat now
        }
        return rank;
    }

    public static void ClearRaceWorld(int worldIndex, string courseKey)
    {
        ClearKey(RaceCourseKey(worldIndex, courseKey));
    }

    // -------------------------------------------------- per-world Ridge Survival boards
    // Ridge Survival banks DISTANCE in METRES (largest = best, so the board sorts descending
    // like the Harvester token boards, unlike the ascending race-time boards). The Ridge no
    // longer has a race-time board of its own - it is a Survival mode now.

    private static string RidgeSurvivalWorldKey(int worldIndex) => RidgeSurvivalKeyPrefix + worldIndex + ".v1";

    /// <summary>Loads one world's Ridge Survival board, best distance first.</summary>
    public static List<LeaderboardEntry> LoadSurvivalWorld(int worldIndex)
    {
        return LoadKey(RidgeSurvivalWorldKey(worldIndex));
    }

    /// <summary>The best (largest) distance survived on a world in metres (0 if none yet).</summary>
    public static int BestSurvivalWorld(int worldIndex)
    {
        return BestKey(RidgeSurvivalWorldKey(worldIndex));
    }

    public static bool IsEmptySurvivalWorld(int worldIndex)
    {
        return LoadKey(RidgeSurvivalWorldKey(worldIndex)).Count == 0;
    }

    /// <summary>Records a Ridge Survival run on its world's distance board; returns the 1-based rank.</summary>
    public static int AddSurvivalWorld(int worldIndex, string name, int meters, string detail, int buildLevel = 0)
    {
        int rank = AddKey(RidgeSurvivalWorldKey(worldIndex), name, meters, detail, buildLevel);
        if (rank == 1) CrazyBoard.SubmitSurvival(worldIndex, meters);
        if (rank == 1) NewgroundsBridge.SubmitSurvival(worldIndex, meters);
        return rank;
    }

    public static void ClearSurvivalWorld(int worldIndex)
    {
        ClearKey(RidgeSurvivalWorldKey(worldIndex));
    }

    // Legacy overloads (no course key) — used by the old finish screen; treat as Canyon.
    public static List<LeaderboardEntry> LoadRaceWorld(int worldIndex) =>
        LoadRaceWorld(worldIndex, "canyon");
    public static int BestRaceWorld(int worldIndex) =>
        BestRaceWorld(worldIndex, "canyon");
    public static bool IsEmptyRaceWorld(int worldIndex) =>
        IsEmptyRaceWorld(worldIndex, "canyon");
    public static int AddRaceWorld(int worldIndex, string name, int centiseconds, string detail, int buildLevel = 0) =>
        AddRaceWorld(worldIndex, name, centiseconds, detail, "canyon", buildLevel);
    public static void ClearRaceWorld(int worldIndex) =>
        ClearRaceWorld(worldIndex, "canyon");

    // ------------------------------------------------------- shared keyed internals

    private static List<LeaderboardEntry> LoadKey(string key)
    {
        try
        {
            string json = SaveBridge.GetString(key, "");
            if (string.IsNullOrEmpty(json)) return new List<LeaderboardEntry>();
            EntryList data = JsonUtility.FromJson<EntryList>(json);
            if (data == null || data.entries == null) return new List<LeaderboardEntry>();
            return data.entries;
        }
        catch (Exception)
        {
            return new List<LeaderboardEntry>();
        }
    }

    private static int BestKey(string key)
    {
        List<LeaderboardEntry> entries = LoadKey(key);
        return entries.Count > 0 ? entries[0].score : 0;
    }

    private static int BestAscendingKey(string key)
    {
        List<LeaderboardEntry> entries = LoadKey(key);
        return entries.Count > 0 ? entries[0].score : 0;
    }

    private static bool WouldQualifyKey(string key, int score)
    {
        if (score <= 0) return false;
        List<LeaderboardEntry> entries = LoadKey(key);
        if (entries.Count < MaxEntries) return true;
        return score > entries[entries.Count - 1].score;
    }

    private static int AddKey(string key, string name, int score, string detail, int buildLevel)
    {
        List<LeaderboardEntry> entries = LoadKey(key);
        string clean = SanitizeName(name);
        entries.Add(new LeaderboardEntry { name = clean, score = score, detail = detail, buildLevel = buildLevel });
        entries.Sort((a, b) => b.score.CompareTo(a.score));
        if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);

        EntryList data = new EntryList { entries = entries };
        SaveBridge.SetString(key, JsonUtility.ToJson(data));
        SaveBridge.Save();

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].score == score && entries[i].name == clean) return i + 1;
        }
        return -1;
    }

    /// <summary>Adds an entry to an ascending (smallest score first) board, keeps the best 10.</summary>
    private static int AddKeyAscending(string key, string name, int score, string detail, int buildLevel)
    {
        List<LeaderboardEntry> entries = LoadKey(key);
        string clean = SanitizeName(name);
        entries.Add(new LeaderboardEntry { name = clean, score = score, detail = detail, buildLevel = buildLevel });
        entries.Sort((a, b) => a.score.CompareTo(b.score));
        if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);

        EntryList data = new EntryList { entries = entries };
        SaveBridge.SetString(key, JsonUtility.ToJson(data));
        SaveBridge.Save();

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].score == score && entries[i].name == clean) return i + 1;
        }
        return -1;
    }

    private static void ClearKey(string key)
    {
        SaveBridge.DeleteKey(key);
        SaveBridge.Save();
    }

    /// <summary>Reusable player name for display/search; falls back to 'username'.</summary>
    public static string SanitizeName(string raw)
    {
        return GameFlow.Sanitize(raw).Length == 0 ? "username" : GameFlow.Sanitize(raw);
    }
}