using System.Globalization;
using UnityEngine;

/// <summary>
/// CrazyGames global-leaderboard submit policy. On a CrazyGames build the game encrypts the
/// score with the official SDK's ScoreEncryption (AES-CTR + Unity marker) and posts it via
/// CrazySDK.User.SubmitScore; the board itself lives on CrazyGames (drawer, game-page widget,
/// player profile, weekly board) - there is no read-back API, so the in-game Leaderboard
/// screen stays a local top-10 (see Leaderboard.cs).
///
/// TO ENABLE (works on a Basic Launch game - leaderboards are not monetization):
///   1. Open https://developer.crazygames.com and select the game.
///   2. Leaderboards tab -> create the board: metric POINTS, sort DESC (the Harvest config
///      below). Only ONE metric/board can be active per game at the moment.
///   3. Generate the board's Encryption Key and paste the base64 string below.
///   4. Rebuild the CrazyGames WebGL build and upload. Scores submit automatically when a
///      run sets a new local #1 (Leaderboard.cs rank==1 path); until the key is pasted the
///      submissions silently skip (Enabled is false), with no errors anywhere.
///
/// If CrazyGames ever supports per-mode leaderboards (seasons), flip the race / survival
/// bools and configure a second board in the portal.
/// </summary>
public static class CrazyBoard
{
    // ------------------------------------------------------------- leaderboard config
    // Base64 encryption key from the Developer Portal (see the steps in the header above).
    // Compiles empty (scores silently skip) so the game ships before you set it up.
    public const string EncryptionKey = "";

    private const bool HarvestMetricEnabled = true;   // tokens (higher = better)
    private const bool RaceMetricEnabled = false;    // centiseconds (lower = better) - disabled while single-board
    private const bool SurvivalMetricEnabled = false; // metres (higher = better) - disabled while single-board

    /// <summary>True once the encryption key is configured and the SDK is ready.</summary>
    public static bool Enabled => !string.IsNullOrEmpty(EncryptionKey) && CrazyGamesBridge.Ready;

    // ---------------------------------------------------------------- submit points
    /// <summary>Call when a Harvest run sets a new WORLD best. The platform leaderboards
    /// are configured DESC (higher score wins) with metric POINTS.</summary>
    public static void SubmitHarvest(int world, int score)
    {
        if (!Enabled || !HarvestMetricEnabled || score <= 0) return;
        CrazyGamesBridge.SubmitScore(score, EncryptionKey);
        CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.HappyTime);
    }

    /// <summary>Call when a Lunar Race run sets a new WORLD best (fastest time). Only
    /// usable if a per-mode board is configured in the portal (ASC, MINUTES).</summary>
    public static void SubmitRace(int world, int centiseconds)
    {
        if (!Enabled || !RaceMetricEnabled || centiseconds <= 0) return;
        CrazyGamesBridge.SubmitScore(centiseconds, EncryptionKey);
        CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.HappyTime);
    }

    /// <summary>Call when a Ridge Survival run sets a new WORLD best (furthest distance).
    /// Only usable if a per-mode board is configured in the portal (DESC, POINTS).</summary>
    public static void SubmitSurvival(int world, float metres)
    {
        if (!Enabled || !SurvivalMetricEnabled || metres <= 0f) return;
        CrazyGamesBridge.SubmitScore(metres, EncryptionKey);
        CrazyGamesBridge.SendEvent(CrazyGamesBridge.SdkEvent.HappyTime);
    }
}