using UnityEngine;

/// <summary>
/// Blocks all audio until the player's first real input (click / tap / key). Browsers - the
/// CrazyGames WebGL build included - refuse to start an AudioContext before a user gesture, so
/// the menu music MenuMusic fires at boot would be stopped at the page level: WebGL logs the
/// "AudioContext was not allowed to start" warning and any pre-gesture sound is simply lost.
///
/// This gate holds UnityEngine's own master pause until that first gesture, keeping the boot
/// silent on every platform (standalone included, where it also lets the menu music start under
/// the player's fingers). Once unlocked it never re-arms - the mixer is handed back entirely to
/// GameSettings.volume, which only deals in loudness, so the player's SOUND toggle keeps working
/// exactly as before. Safe to call from any boot path; idempotent.
/// </summary>
public static class AudioInputGate
{
    private static bool ensured;
    private static bool unlocked;

    public static bool Unlocked => unlocked;

    /// <summary>Creates the one persistent gate. The carrier GameObject is DontDestroyOnLoad so
    /// it keeps polling the input across scene switches (menu -> level -> win board -> menu).</summary>
    public static void Ensure()
    {
        if (ensured) return;
        ensured = true;
        AudioListener.pause = true;   // silent from the very first rendered frame
        GameObject go = new GameObject("AudioInputGate");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<AudioInputGateBehaviour>();
    }

    private sealed class AudioInputGateBehaviour : MonoBehaviour
    {
        private void Update()
        {
            if (unlocked) return;
            bool gesture = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)
                || Input.GetMouseButtonDown(2)
                || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
                || Input.anyKeyDown;
            if (!gesture) return;

            unlocked = true;
            AudioListener.pause = false;
        }
    }
}