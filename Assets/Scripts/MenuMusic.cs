using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu-screen music that survives scene switches so the track never restarts when moving
/// between the main menu, garage, level selector and leaderboard. One persistent GameObject
/// (DontDestroyOnLoad) holds the looping AudioSource; scene loads decide whether it is audible.
/// The game scene ("Main") and any gameplay route silence it entirely - the level intro and
/// the run are the ones with their own audio.
/// </summary>
public class MenuMusic : MonoBehaviour
{
    public static MenuMusic Instance { get; private set; }

    private AudioSource src;
    private AudioClip clip;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        clip = Resources.Load<AudioClip>("Menu_Music");
        src = gameObject.AddComponent<AudioSource>();
        src.spatialBlend = 0f;
        src.loop = true;
        src.playOnAwake = false;
        src.volume = 0.75f;
        src.clip = clip;
        ApplyForActiveScene();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>Ensures the persistent menu-music carrier exists (called by menu scenes).</summary>
    public static void Ensure()
    {
        if (Instance != null) return;
        new GameObject("MenuMusic").AddComponent<MenuMusic>();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyForActiveScene();
    }

    private void ApplyForActiveScene()
    {
        if (clip == null || src == null) return;
        string name = SceneManager.GetActiveScene().name;
        bool menuWorld = name == "Menu" || name == "Leaderboard";
        if (menuWorld && !src.isPlaying)
        {
            src.Play();
        }
        else if (!menuWorld)
        {
            src.Stop();
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }
}