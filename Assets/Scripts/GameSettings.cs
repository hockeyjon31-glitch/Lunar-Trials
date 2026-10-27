using UnityEngine;

/// <summary>
/// Player settings that are not progress - currently just the master mute - stored through
/// SaveBridge so the choice follows the player like the rest of the save (cloud on
/// CrazyGames, PlayerPrefs elsewhere).
///
/// AudioListener.volume is a single global multiplier applied to every AudioSource in the
/// game (menu music, engine, music, one-shots), so one knob mutes the whole experience and
/// it survives scene changes automatically.
/// </summary>
public static class GameSettings
{
    private const string MutedKey = "lunar.muted";
    private static bool loaded;

    public static bool Muted { get; private set; }

    /// <summary>CrazyGames' own mute (site header / the video player). While this is true
    /// the player's SOUND toggle cannot bring audio back - the platform setting must win,
    /// per the SDK game-settings requirement.</summary>
    public static bool SdkMuted { get; private set; }

    /// <summary>Called by CrazyGamesBridge when the platform mute changes.</summary>
    public static void SetSdkMuted(bool muted)
    {
        if (SdkMuted == muted) return;
        SdkMuted = muted;
        Apply();
    }

    /// <summary>Reads the stored setting and applies it. Safe to call repeatedly.</summary>
    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        Muted = SaveBridge.GetString(MutedKey, "0") == "1";
        Apply();
    }

    /// <summary>Re-reads the stored value; used once the CrazyGames cloud store comes online
    /// (the first read at boot may have hit local PlayerPrefs before the SDK was ready).</summary>
    public static void Reload()
    {
        loaded = false;
        Load();
    }

    public static void SetMuted(bool muted)
    {
        loaded = true;
        Muted = muted;
        SaveBridge.SetString(MutedKey, muted ? "1" : "0");
        SaveBridge.Save();
        Apply();
    }

    public static void ToggleMute()
    {
        SetMuted(!Muted);
    }

    /// <summary>Short label for toggle buttons: the CURRENT state. Shows OFF whenever the
    /// platform mute is on, because toggling cannot override it.</summary>
    public static string Label => (Muted || SdkMuted) ? "SOUND: OFF" : "SOUND: ON";

    private static void Apply()
    {
        AudioListener.volume = (Muted || SdkMuted) ? 0f : 1f;
    }
}
