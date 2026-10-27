using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using System.IO.Compression;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor menu helpers that set up the Lunar Buggy level with one click, so you don't
/// need to hand-place anything in the scene.
///
/// Menu: Lunar Buggy > Setup Level from Empty Scene
/// </summary>
public static class LunarBuggyMenu
{
    private const string MenuRoot = "Lunar Buggy";
    private const string CrazyLoadingScenePath = "Assets/Scenes/CrazyLoading.unity";

    [MenuItem(MenuRoot + "/Setup Level from Empty Scene", false, 0)]
    public static void SetupLevel()
    {
        // Remove any existing bootstrap to avoid duplicates.
        var existing = Object.FindAnyObjectByType<GameBootstrap>();
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        // Create the main camera first (used by GameBootstrap).
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }

        // Create the bootstrap object that builds everything at play time.
        GameObject bootGo = new GameObject("Game");
        bootGo.AddComponent<GameBootstrap>();

        // Close any lightmapping / other stray setup is not needed.

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Lunar Buggy level ready. Press Play to drive!");
    }

    [MenuItem(MenuRoot + "/Clear Level Setup", false, 20)]
    public static void ClearLevel()
    {
        var existing = Object.FindAnyObjectByType<GameBootstrap>();
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("Lunar Buggy bootstrap removed.");
        }
    }

    // One-click in-editor playtest: authorise the launch and enter Play mode in the Main
    // scene, so tuning changes (audio levels, physics, harvest tuning) can be previewed in
    // seconds from the Inspector instead of shipping a WebGL build every iteration.
    [MenuItem(MenuRoot + "/Play Test Level (Main.unity)", false, 25)]
    public static void PlayTestLevel()
    {
        GameFlow.SetEditorPlaytest(0);
        string path = "Assets/Scenes/Main.unity";
        UnityEngine.Object scene = UnityEditor.AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        if (scene != null)
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        else
        {
            Debug.LogError("Play Test Level: " + path + " not found.");
        }
    }

    [MenuItem(MenuRoot + "/Play Test Trials Challenge (FIRST TRAX)", false, 27)]
    public static void PlayTestTrialsChallenge()
    {
        // Stages the challenge id in SessionState (survives the play-mode domain reload);
        // GameBootstrap.RestoreTrialsChallenge picks it up and activates the run.
        UnityEditor.SessionState.SetString("trials.challenge.id", ChallengePack.All[0].id);
        GameBootstrap.TrialsChallenge = ChallengePack.All[0];
        string path = "Assets/Scenes/Main.unity";
        UnityEngine.Object scene = UnityEditor.AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        if (scene != null)
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        else
        {
            Debug.LogError("Play Test Trials: " + path + " not found.");
        }
    }

    // Creates (or recreates) the standalone airtime/physics playground in its own scene:
    // run-up, 10/15/20/25 degree ramps, 3/5/8/12 m gap jumps, a bump field, up/down slopes
    // and a 5 m drop-off. Everything is built from code at Play; the scene only holds the
    // PhysicsTestBed object and a camera.
    [MenuItem(MenuRoot + "/Physics Test Bed", false, 28)]
    public static void CreatePhysicsTestBed()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject bedGo = new GameObject("Physics Test Bed");
        bedGo.AddComponent<PhysicsTestBed>();

        // A camera in the scene so the Game view has a target before Play; PhysicsTestBed
        // reuses it (and attaches BuggyCamera) at runtime.
        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();

        Directory.CreateDirectory("Assets/Scenes");
        string path = "Assets/Scenes/PhysicsTestBed.unity";
        EditorSceneManager.SaveScene(scene, path);
        AssetDatabase.Refresh();
        Debug.Log("Physics test bed created at " + path + ". Press Play to drive and tune.");
    }

    // One-click jump straight into TITAN's HARVEST run so the new intro video + Saturn
    // backdrop art can be previewed without clicking through the chain unlock to reach it.
    [MenuItem(MenuRoot + "/Play Test Titan Harvest", false, 26)]
    public static void PlayTestTitanHarvest()
    {
        GameFlow.SetEditorPlaytest(4); // TITAN
        string path = "Assets/Scenes/Main.unity";
        UnityEngine.Object scene = UnityEditor.AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        if (scene != null)
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        else
        {
            Debug.LogError("Play Test Titan Harvest: " + path + " not found.");
        }
    }

    // Batch-mode entry point: `-executeMethod LunarBuggyMenu.BuildWebGL`.
    // Builds a Brotli-compressed WebGL player from the enabled scenes in Build Settings.
    [MenuItem(MenuRoot + "/Build WebGL (Unity Play v1.2)", false, 33)]
    public static void BuildUnityPlay12()
    {
        // Ensure Brotli compression is set before building.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;

        string webglDir = Path.Combine(Application.dataPath, "..", "Builds", "WebGL");
        string buildDir = Path.Combine(webglDir, "Build");
        string tempDir = Path.Combine(Path.GetTempPath(), "UnityPlay12");
        string outZip = Path.Combine(Path.Combine(Application.dataPath, "..", "Builds"), "Unity_play1.2.zip");

        // Clean temp
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        // Copy root files from WebGL dir
        foreach (string f in Directory.GetFiles(webglDir))
            File.Copy(f, Path.Combine(tempDir, Path.GetFileName(f)), true);
        foreach (string d in Directory.GetDirectories(Path.Combine(webglDir, "StreamingAssets")))
            DirectoryCopy(Path.Combine(webglDir, "StreamingAssets"), Path.Combine(tempDir, "StreamingAssets"));
        foreach (string d in Directory.GetDirectories(Path.Combine(webglDir, "TemplateData")))
            DirectoryCopy(Path.Combine(webglDir, "TemplateData"), Path.Combine(tempDir, "TemplateData"));

        // Create Build subdir and decompress .br -> .unityweb
        string buildTargetDir = Path.Combine(tempDir, "Build");
        Directory.CreateDirectory(buildTargetDir);

        var brotliFiles = new[] { "WebGL.data.br", "WebGL.framework.js.br", "WebGL.wasm.br" };
        foreach (string br in brotliFiles)
        {
            string src = Path.Combine(buildDir, br);
            string outName = br.Replace(".br", ".unityweb");
            string dst = Path.Combine(buildTargetDir, outName);
            byte[] compressed = File.ReadAllBytes(src);
            using (var msIn = new MemoryStream(compressed))
            using (var brStream = new BrotliStream(msIn, CompressionMode.Decompress))
            using (var msOut = new MemoryStream())
            {
                brStream.CopyTo(msOut);
                File.WriteAllBytes(dst, msOut.ToArray());
                Debug.Log($"Decompressed {br} -> {outName} ({msOut.Length / 1024 / 1024.0:F1} MB)");
            }
        }

        // Copy loader.js unchanged
        File.Copy(Path.Combine(buildDir, "WebGL.loader.js"), Path.Combine(buildTargetDir, "WebGL.loader.js"), true);

        // Create zip
        if (File.Exists(outZip)) File.Delete(outZip);
        ZipFile.CreateFromDirectory(tempDir, outZip, System.IO.Compression.CompressionLevel.Optimal, false);

        long size = new FileInfo(outZip).Length;
        Debug.Log($"Created: {outZip} ({size / 1024 / 1024.0:F1} MB)");

        // Cleanup
        Directory.Delete(tempDir, true);
        Debug.Log("Build Unity_play1.2.zip done.");
    }

    private static void DirectoryCopy(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string f in Directory.GetFiles(sourceDir))
            File.Copy(f, Path.Combine(destinationDir, Path.GetFileName(f)), true);
        foreach (string d in Directory.GetDirectories(sourceDir))
            DirectoryCopy(Path.Combine(sourceDir, d), Path.Combine(destinationDir, d));
    }

    [MenuItem(MenuRoot + "/Build WebGL (Brotli)", false, 30)]
    public static void BuildWebGL()
    {
        RunBuild(WebGLCompressionFormat.Brotli);
    }

    // Brotli build that embeds the JS decompressor (Decompression Fallback), so the .unityweb
    // output plays on ANY static host (Unity Play, itch.io, a folder server) even when the
    // server sends no Content-Encoding header. Slightly larger loader, universally compatible.
    [MenuItem(MenuRoot + "/Build WebGL (Brotli, Unity Play)", false, 32)]
    public static void BuildWebGLBrotliPortable()
    {
        PlayerSettings.WebGL.decompressionFallback = true;
        RunBuild(WebGLCompressionFormat.Brotli);
    }

    // Batch-mode entry point: `-executeMethod LunarBuggyMenu.BuildWebGLNoCompression`.
    // Uncompressed WebGL build: loads over plain HTTP with any static server (no brotli
    // Content-Encoding needed). Use for local playtesting; keep Brotli for real hosting.
    [MenuItem(MenuRoot + "/Build WebGL (No Compression)", false, 31)]
    public static void BuildWebGLNoCompression()
    {
        RunBuild(WebGLCompressionFormat.Disabled);
    }

    // CrazyGames submission build: the same no-compression WebGL build, then index.html
    // gets the CrazyGames "common fixes" style/script patch (scroll + context menu + touch
    // select suppression). The SDK itself is the OFFICIAL CrazyGames Unity SDK (Assets/
    // CrazySDK + Plugins/crazySDK.jslib): it loads and initializes the platform SDK at
    // runtime, so no SDK script is injected into the page - which is what the preview/QA
    // tool needs to recognize an SDK environment.
    [MenuItem(MenuRoot + "/Build WebGL (CrazyGames)", false, 32)]
    public static void BuildWebGLCrazy()
    {
        CleanOutputDir();
        RunBuild(WebGLCompressionFormat.Disabled);
        PatchCrazyIndexHead();
    }

    // The build to actually submit. CrazyGames asks for Brotli-compressed files (their upload
    // check reports "compression could not be detected" otherwise) and serves them with
    // Content-Encoding: br. Brotli also drops the download to roughly a third of the
    // uncompressed size, which is what gets us under the 20MB mobile-homepage threshold.
    // Decompression fallback stays OFF: that is the variant the platform detects and serves.
    [MenuItem(MenuRoot + "/Build WebGL (CrazyGames, Brotli)", false, 34)]
    public static void BuildWebGLCrazyBrotli()
    {
        CleanOutputDir();
        PlayerSettings.WebGL.decompressionFallback = false;
        RunBuild(WebGLCompressionFormat.Brotli);
        PatchCrazyIndexHead();
    }

    // ------------------------------------------------------------ itch.io build
    // Two things make the CrazyGames submission build unplayable on itch.io, and neither is
    // about the game's own code:
    //
    //  1. COMPRESSION. CrazyGames serves .br files with Content-Encoding: br. itch.io serves
    //     uploaded files as-is, so a Brotli build hands the browser raw Brotli bytes where it
    //     expects JS/wasm and the loader dies on a blank screen. This build is therefore
    //     UNCOMPRESSED (.data / .wasm), which any static host serves correctly.
    //
    //  2. THE SDK SITELOCK, which is the one that actually eats the upload. The official
    //     CrazyGames SDK runs SiteLock.Check() from [RuntimeInitializeOnLoadMethod] BEFORE the
    //     first scene loads (CrazySDK.cs) and calls SiteLockFreezeGame on any host that is not
    //     a CrazyGames/regional domain or an explicitly whitelisted one. So it freezes the game
    //     on itch.io regardless of what the bridge does. There is no supported flag to turn it
    //     off, so the only honest fix is to not ship the SDK at all: NO_CRAZY_SDK compiles out
    //     every CrazySDK call in CrazyGamesBridge and CrazyLoadingSequence, and the caller
    //     moves Assets/CrazySDK out of the project for the duration of the build.
    //
    // Result: an itch build with no SDK, no sitelock, no ads, no leaderboard, saving to
    // PlayerPrefs exactly as a plain web upload is meant to.
    private const string NoSdkDefine = "NO_CRAZY_SDK";
    // The two steps run as separate Unity invocations (arm, then build), so the pre-build
    // define set has to be kept somewhere that outlives a domain reload. EditorPrefs does;
    // a static field would not.
    private const string SavedDefinesKey = "LunarBuggy.ItchBuild.SavedDefines";

    //     NO_CRAZY_SDK ALONE IS NOT ENOUGH, and the two halves fail independently:
    //
    //       1. The define only compiles out OUR calls (CrazyGamesBridge, CrazyLoadingSequence).
    //          Assets/CrazySDK/Scripts/CrazySDK.cs has an UNGUARDED
    //          [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] that calls
    //          SiteLock.Check() on every WebGL player, before the first scene loads. Nothing in the
    //          SDK respects the define.
    //       2. Managed stripping cannot be relied on to drop it either. Unity deliberately
    //          PRESERVES every method carrying RuntimeInitializeOnLoadMethod, so High stripping
    //          still keeps SiteLock.Check() - this project sets managedStrippingLevel: WebGL: 3
    //          (High) in ProjectSettings.asset, and the method is linked in regardless.
    //
    //     So the SDK folder itself has to leave the project for the duration of the build. That
    //     used to be a manual step ("the caller moves Assets/CrazySDK out"), and a build that
    //     forgot it shipped SiteLock to itch: the upload logged "SiteLock v1.9.0 activated ...
    //     Code 3" and froze before the menu. Same itch settings, same byte-identical index.html,
    //     but only that build would not load. Stashing it here makes the step impossible to skip.
    //
    //     The stash is a project-root folder (never imported by Unity, which only reads Assets/
    //     and Packages/) and each item's .meta travels with it so the GUID survives the round
    //     trip. The EditorPrefs flag is what lets a LATER Unity invocation - the one that runs
    //     the build - find a stash it did not create.
    private const string SdkStashedKey = "LunarBuggy.ItchBuild.SdkStashed";
    private const string SdkStashFolder = "CrazySDK_itch_build_stash";
    private static readonly string[] SdkAssetPaths =
    {
        "Assets/CrazySDK",
        "Assets/Plugins/crazySDK.jslib",
    };

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

    /// <summary>Builds the absolute paths to move for one project-relative entry: the asset
    /// itself plus the sibling .meta that holds its GUID.</summary>
    private static string[] SdkMoveSet(string root, string rel)
    {
        string src = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        return new[] { src, src + ".meta" };
    }

    /// <summary>Hard stop in front of every SDK-free build. The itch.io regression proved the two
    /// halves of SDK exclusion - the compile-time define and the folder move - can drift out of
    /// sync, and when they do the build still SUCCEEDS: it just links Assets/CrazySDK and then
    /// SiteLock.Check() freezes the game on that host before the first scene. A green build told
    /// us nothing, so this converts a silent, invisible failure into a loud abort at step one,
    /// before CleanOutputDir throws away the last known-good output.
    ///
    /// Interactive menu clicks show the exception in the console; a batch invocation throws out
    /// of -executeMethod and Unity exits non-zero, so a scripted pipeline cannot ignore it.</summary>
    private static void AssertSdkExcluded(string buildName)
    {
        bool sdkPresent = Directory.Exists(Path.Combine(ProjectRoot, "Assets", "CrazySDK"));
        bool defineArmed = PlayerSettings
            .GetScriptingDefineSymbols(NamedBuildTarget.WebGL).Contains(NoSdkDefine);

        if (!sdkPresent && defineArmed) return;

        throw new System.InvalidOperationException(
            $"Build WebGL ({buildName}, no SDK) ABORTED: the CrazyGames SDK is not fully excluded. " +
            $"Assets/CrazySDK present = {sdkPresent}, {NoSdkDefine} armed = {defineArmed}. " +
            "Either would ship SiteLock.Check(), which runs from RuntimeInitializeOnLoadMethod " +
            "BEFORE the first scene and freezes this host. Run the matching 'Prepare ... build " +
            "(SDK excluded)' step first, then rerun the build.");
    }

    /// <summary>Takes the CrazyGames SDK out of the project so it cannot be compiled or linked
    /// into an itch build. Idempotent: an entry that is already in the stash is left alone, so
    /// re-running prep after an aborted build cannot clobber the stashed copy.</summary>
    private static void StashSdkAssets()
    {
        string root = ProjectRoot;
        string stash = Path.Combine(root, SdkStashFolder);
        int moved = 0;

        foreach (string rel in SdkAssetPaths)
        {
            foreach (string src in SdkMoveSet(root, rel))
            {
                if (!File.Exists(src) && !Directory.Exists(src)) continue;
                Directory.CreateDirectory(stash);
                string dest = Path.Combine(stash, Path.GetFileName(src));
                if (File.Exists(dest) || Directory.Exists(dest)) continue;
                if (Directory.Exists(src)) Directory.Move(src, dest);
                else File.Move(src, dest);
                moved++;
            }
        }

        EditorPrefs.SetString(SdkStashedKey, "1");
        AssetDatabase.Refresh();
        Debug.Log(moved > 0
            ? $"Itch build prep: stashed {moved} SDK item(s) into {SdkStashFolder}/ (define + folder both required)"
            : $"Itch build prep: SDK already out of the project (nothing to stash in {SdkStashFolder}/)");
    }

    /// <summary>Puts the SDK back exactly where it came from, so the project is left in its normal
    /// CrazyGames state. Safe to call when nothing was ever stashed.</summary>
    private static void RestoreSdkAssets()
    {
        string root = ProjectRoot;
        string stash = Path.Combine(root, SdkStashFolder);
        if (!Directory.Exists(stash))
        {
            EditorPrefs.DeleteKey(SdkStashedKey);
            return;
        }

        int restored = 0;
        foreach (string rel in SdkAssetPaths)
        {
            foreach (string dest in SdkMoveSet(root, rel))
            {
                string src = Path.Combine(stash, Path.GetFileName(dest));
                if (!File.Exists(src) && !Directory.Exists(src)) continue;
                if (File.Exists(dest) || Directory.Exists(dest)) continue;   // already back home
                string parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                if (Directory.Exists(src)) Directory.Move(src, dest);
                else File.Move(src, dest);
                restored++;
            }
        }

        if (Directory.GetFileSystemEntries(stash).Length == 0) Directory.Delete(stash);
        EditorPrefs.DeleteKey(SdkStashedKey);
        AssetDatabase.Refresh();
        Debug.Log($"Itch build: restored {restored} SDK item(s) from {SdkStashFolder}/");
    }

    /// <summary>Batch step 1: arm the NO_CRAZY_SDK define and exit. Separate from the build
    /// because flipping a scripting define makes Unity recompile, and a build requested in the
    /// same invocation would race that compile.</summary>
    [MenuItem(MenuRoot + "/Prepare itch.io build (SDK excluded)", false, 36)]
    public static void PrepareItchBuild()
    {
        string current = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
        if (!current.Contains(NoSdkDefine))
        {
            EditorPrefs.SetString(SavedDefinesKey, current);
        }
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL,NoSdkDefine);
        StashSdkAssets();
        AssetDatabase.SaveAssets();
        Debug.Log($"Itch build prep: {BuildTargetGroup.WebGL} defines = {NoSdkDefine} (saved '{current}')");
    }

    /// <summary>Puts the WebGL defines back the way PrepareItchBuild found them, so the project
    /// is left in its normal SDK state and the next CrazyGames build needs no extra steps.
    /// Also the recovery path if a build was interrupted: it brings a stashed Assets/CrazySDK
    /// back from CrazySDK_itch_build_stash/, which is the one piece of state that would
    /// otherwise leave the project without its SDK.</summary>
    [MenuItem(MenuRoot + "/Finish itch.io build (restore defines)", false, 37)]
    public static void FinishItchBuild()
    {
        RestoreSavedDefines();
    }

    private static void RestoreSavedDefines()
    {
        // Folder first, then defines: putting the SDK back triggers a reimport, and doing that
        // before the define is cleared avoids a second compile.
        RestoreSdkAssets();
        string saved = EditorPrefs.HasKey(SavedDefinesKey) ? EditorPrefs.GetString(SavedDefinesKey) : "";
        EditorPrefs.DeleteKey(SavedDefinesKey);
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL,saved);
        AssetDatabase.SaveAssets();
        Debug.Log($"Itch build: restored {BuildTargetGroup.WebGL} defines = '{saved}'");
    }

    /// <summary>Batch step 2: uncompressed WebGL build with the SDK compiled out, then the
    /// same index.html style patch (no SDK script is injected - that was never in the patch).</summary>
    [MenuItem(MenuRoot + "/Build WebGL (itch.io, no SDK)", false, 35)]
    public static void BuildWebGLItch()
    {
        AssertSdkExcluded("itch.io");
        try
        {
            CleanOutputDir();
            RunBuild(WebGLCompressionFormat.Disabled);
            PatchCrazyIndexHead();
        }
        finally
        {
            // Put the project back the way we found it, so the next CrazyGames build is a
            // normal SDK build without the caller having to remember anything.
            RestoreSavedDefines();
        }
    }

    /// <summary>BROTLI, with the decompressor embedded (Decompression Fallback), is the itch.io
    /// upload. It is kept only because it matches the 30/09/26 build that is known to work on
    /// itch: that build's loader is byte-for-byte the same size as this one's (118,133 bytes),
    /// which means the same Brotli decoder is embedded and the .unityweb payloads are framed
    /// the same way. Decompression Fallback matters here because it lets Unity ship only the
    /// compressed .unityweb files and decode them itself, so itch never has to send the right
    /// Content-Encoding header.
    ///
    /// DO NOT "fix" a white screen or a failed load by switching this to Gzip. That was the
    /// wrong diagnosis once already: the itch upload that appeared to be a compression failure
    /// was really a ZIP packaging failure - ZipFile.CreateFromDirectory on .NET Framework wrote
    /// backslash entry names, itch's Linux host treated them as literal filenames, and every
    /// asset 404'd. The zip MUST be built with Tools/New-ItchZip.ps1, which normalises the
    /// separators to forward slashes. Compression format was never the variable.</summary>
    [MenuItem(MenuRoot + "/Build WebGL (itch.io, no SDK, compressed)", false, 38)]
    public static void BuildWebGLItchCompressed()
    {
        AssertSdkExcluded("itch.io");
        // The fallback flag is a project setting that outlives the build, so put it back or the
        // next CrazyGames build silently inherits it.
        bool savedFallback = PlayerSettings.WebGL.decompressionFallback;
        try
        {
            CleanOutputDir();
            PlayerSettings.WebGL.decompressionFallback = true;
            RunBuild(WebGLCompressionFormat.Brotli);
            PatchCrazyIndexHead();
        }
        finally
        {
            PlayerSettings.WebGL.decompressionFallback = savedFallback;
            RestoreSavedDefines();
            Debug.Log($"Itch build: restored decompressionFallback = {savedFallback}");
        }
    }

    // --------------------------------------------------------------- Lunar Trials
    // The CrazyGames-only spin-off edition (see LunarTrials/DESIGN.md). Same prepare/build/
    // restore shape as itch/mobile-light: arm the LUNAR_TRIALS define and stash the art the
    // edition never loads (other planets' backdrops and their ground/lava texture sets),
    // build Brotli to Builds/LunarTrials, then restore everything.
    private const string TrialsDefine = "LUNAR_TRIALS";
    private const string TrialsSavedDefinesKey = "LunarBuggy.TrialsSavedDefines";
    private const string TrialsStashFolder = "LunarTrials_art_stash";

    // Art the Trials edition never loads (Moon-only game). File names are looked up anywhere
    // under Assets/Resources so the stash survives folder reorganisation. The Moon's own
    // ground (Ground01), the landing-pad/lander metal swatches (YFMeM 23/37), Earth/Sun sky
    // cards (used by Ridge challenges) and all audio stay in.
    private static readonly string[] TrialsStashFileNames =
    {
        "io.jpg", "mars.jpg", "venus.jpg",
        "Jupiter.jpg", "Saturn.png",
        "T_Lava_Magma_d.png", "T_Lava_Magma_e.png", "T_Lava_Magma_n.png",
        "T_LunarGround_Ground02_d.jpg",
        "T_YFCM_ColoredSand_d.tga", "T_YFCM_ColoredSand_n.tga",
        "T_YFCM_Crystallized_d.tga", "T_YFCM_Crystallized_n.tga",
        "T_YFCM_Fibers_d.tga",
        "T_YFCM_Honeycombing_d.tga", "T_YFCM_Honeycombing_n.tga",
        "T_YFCM_Moldy_d.tga",
    };

    /// <summary>Editor testing toggle: arms/disarms the LUNAR_TRIALS define so the Trials
    /// select screen and challenge systems can be played in-editor without a build.</summary>
    [MenuItem(MenuRoot + "/Toggle Lunar Trials edition (editor testing)", false, 48)]
    public static void ToggleTrialsEdition()
    {
        string current = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
        bool armed = current.Contains(TrialsDefine);
        string next = armed
            ? current.Replace(TrialsDefine, "").Replace(";;", ";").TrimEnd(';')
            : string.IsNullOrEmpty(current) ? TrialsDefine : current + ";" + TrialsDefine;
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, next);
        Debug.Log($"Lunar Trials edition {(armed ? "DISABLED" : "ENABLED")} - recompiling. " +
                  "Enter Play Mode to test the challenge ladder (tutorials/fast-launch still " +
                  "follow their own CrazyGames gating).");
    }

    [MenuItem(MenuRoot + "/Prepare Lunar Trials build (define + art stash)", false, 50)]
    public static void PrepareTrialsBuild()
    {
        string current = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
        if (!current.Contains(TrialsDefine))
        {
            EditorPrefs.SetString(TrialsSavedDefinesKey, current);
        }
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, TrialsDefine);
        StashTrialsArt();
        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        Debug.Log("Lunar Trials prep: define armed, non-Moon art stashed.");
    }

    private static void StashTrialsArt()
    {
        string resources = Path.Combine(ProjectRoot, "Assets", "Resources");
        string stash = Path.Combine(ProjectRoot, TrialsStashFolder);
        Directory.CreateDirectory(stash);
        var manifest = new System.Text.StringBuilder();
        int moved = 0;
        foreach (string name in TrialsStashFileNames)
        {
            string[] found = Directory.GetFiles(resources, name, SearchOption.AllDirectories);
            foreach (string src in found)
            {
                string dest = Path.Combine(stash, name);
                if (File.Exists(dest)) continue;
                string rel = src.Substring(ProjectRoot.Length + 1).Replace('\\', '/');
                manifest.AppendLine(rel);
                File.Move(src, dest);
                string meta = src + ".meta";
                if (File.Exists(meta)) File.Move(meta, dest + ".meta");
                moved++;
            }
        }
        File.WriteAllText(Path.Combine(stash, "manifest.txt"), manifest.ToString());
        Debug.Log($"Lunar Trials prep: stashed {moved} art file(s) into {TrialsStashFolder}/");
    }

    [MenuItem(MenuRoot + "/Build WebGL (Lunar Trials, Brotli)", false, 51)]
    public static void BuildWebGLTrials()
    {
        if (!PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL).Contains(TrialsDefine))
        {
            throw new System.InvalidOperationException(
                "Build WebGL (Lunar Trials) ABORTED: the LUNAR_TRIALS define is not armed. " +
                "Run 'Prepare Lunar Trials build (define + art stash)' first, then rerun.");
        }
        try
        {
            CleanOutputDir("LunarTrials");
            RunBuild(WebGLCompressionFormat.Brotli, "LunarTrials");
            PatchCrazyIndexHead(false, "LunarTrials");
        }
        finally
        {
            FinishTrialsBuild();
        }
    }

    [MenuItem(MenuRoot + "/Finish Lunar Trials build (restore)", false, 52)]
    public static void FinishTrialsBuild()
    {
        RestoreTrialsArt();
        string saved = EditorPrefs.HasKey(TrialsSavedDefinesKey)
            ? EditorPrefs.GetString(TrialsSavedDefinesKey) : "";
        EditorPrefs.DeleteKey(TrialsSavedDefinesKey);
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, saved);
        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        Debug.Log($"Lunar Trials build: restored defines = '{saved}' and stashed art.");
    }

    private static void RestoreTrialsArt()
    {
        string root = ProjectRoot;
        string stash = Path.Combine(root, TrialsStashFolder);
        string manifestPath = Path.Combine(stash, "manifest.txt");
        if (!Directory.Exists(stash) || !File.Exists(manifestPath)) return;

        int restored = 0;
        foreach (string rel in File.ReadAllLines(manifestPath))
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            string src = Path.Combine(stash, Path.GetFileName(rel));
            string dest = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(src)) continue;
            if (File.Exists(dest)) File.Delete(dest);
            string parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.Move(src, dest);
            string meta = src + ".meta";
            if (File.Exists(meta)) File.Move(meta, dest + ".meta");
            restored++;
        }
        Directory.Delete(stash, true);
        Debug.Log($"Lunar Trials build: restored {restored} art file(s) from {TrialsStashFolder}/");
    }

    // --------------------------------------------------------------- Kongregate
    // Same two-step shape as the itch build, because the reason is the same: Assets/CrazySDK is
    // not a Kongregate domain, so its SiteLock would freeze the game there too, and
    // NO_CRAZY_SDK alone does not compile the SDK out. Kongregate API support itself is NOT
    // conditional - it is detected at runtime by LunarKongOnHost(), so no define or stash is
    // needed for it and a build stays correct on every host it is never uploaded to.

    /// <summary>Batch step 1 for the Kongregate upload. The operation is byte-for-byte the itch
    /// one (same define, same folder move), so this delegates rather than duplicating it - the
    /// separate menu entry exists only so the prepare/build pairing is unambiguous when reading
    /// the menu, instead of an itch step quietly feeding a Kongregate build.</summary>
    [MenuItem(MenuRoot + "/Prepare Kongregate build (SDK excluded)", false, 40)]
    public static void PrepareKongregateBuild()
    {
        PrepareItchBuild();
    }

    /// <summary>Brotli WITH the decompressor embedded (Decompression Fallback ON) - the same
    /// configuration as the itch.io compressed build. This is a correction: the first Kongregate
    /// build shipped with fallback OFF on the theory that Kongregate's host sends the right
    /// Content-Encoding header for .br. It does not (at least not on the preview CDN): the
    /// preview failed with "Unable to parse Build/WebGL.framework.js.br", which is the browser
    /// being handed raw brotli bytes and told to run them as JavaScript. With fallback ON the
    /// loader decodes the .unityweb payloads itself and no server header is needed, so the game
    /// cold-loads on a dumb static server exactly like the itch build does. The cost is the
    /// embedded decoder (~91 KB of loader) - a worthwhile trade for a host we cannot rely on.</summary>
    [MenuItem(MenuRoot + "/Build WebGL (Kongregate, no SDK)", false, 41)]
    public static void BuildWebGLKongregate()
    {
        AssertSdkExcluded("Kongregate");
        bool savedFallback = PlayerSettings.WebGL.decompressionFallback;
        try
        {
            CleanOutputDir();
            PlayerSettings.WebGL.decompressionFallback = true;
            RunBuild(WebGLCompressionFormat.Brotli);
            PatchCrazyIndexHead();
        }
        finally
        {
            PlayerSettings.WebGL.decompressionFallback = savedFallback;
            RestoreSavedDefines();
            Debug.Log($"Kongregate build: restored decompressionFallback = {savedFallback}");
        }
    }

    // --------------------------------------------------------------- Newgrounds
    // Same two-step prepare/build shape as itch/Kongregate, for the same reason: Assets/CrazySDK
    // is not a Newgrounds domain, so its SiteLock would freeze the game there. Newgrounds serves
    // uploads as a plain static host, so this build uses Brotli WITH the embedded decompressor
    // (Decompression Fallback ON) - the same self-contained config that cold-loads on Kongregate
    // - and the index.html style patch so the canvas fills the portal's iframe. The Newgrounds.io
    // medal/scoreboard API is integrated through NewgroundsBridge/NewgroundsSession plus the
    // embedded io.newgrounds.unity package; it is gated at RUNTIME on the host, so this exact
    // binary is safe everywhere else (the API is a permanent no-op off newgrounds.com).
    [MenuItem(MenuRoot + "/Prepare Newgrounds build (SDK excluded)", false, 45)]
    public static void PrepareNewgroundsBuild()
    {
        PrepareItchBuild();
    }

    [MenuItem(MenuRoot + "/Build WebGL (Newgrounds, no SDK)", false, 46)]
    public static void BuildWebGLNewgrounds()
    {
        AssertSdkExcluded("Newgrounds");
        bool savedFallback = PlayerSettings.WebGL.decompressionFallback;
        try
        {
            CleanOutputDir();
            // Newgrounds' static host mishandles the pre-compressed .unityweb (fallback) files -
            // the loader receives altered bytes and dies with "UnityFramework is not defined" /
            // "Unable to parse the build". Ship UNCOMPRESSED files instead: no Content-Encoding
            // header is required, so no server config can break them. Newgrounds allows large
            // uploads, and its own edge compression (if any) is transparent to the browser.
            PlayerSettings.WebGL.decompressionFallback = false;
            RunBuild(WebGLCompressionFormat.Disabled);
            PatchCrazyIndexHead(showFullscreen: true);
        }
        finally
        {
            PlayerSettings.WebGL.decompressionFallback = savedFallback;
            RestoreSavedDefines();
            Debug.Log($"Newgrounds build: restored decompressionFallback = {savedFallback}");
        }
    }

    // --------------------------------------------------------------- mobile-light build
    // The CrazyGames MOBILE wrapper wants a smaller download than the web build. The one Moon
    // texture trimmed there is the second lunar ground photo (Ground02): mobile plays the Moon
    // on the single Ground01 blend and claws back the whole file's compressed weight. Web keeps
    // the full Ground01+02 blend (see "#if MOBILE_LITE" in Yfcm.ComboFor). Same two-step
    // prepare/restore contract as the itch SDK exclusion, because the reason is the same: a
    // scripting define alone does not pick the file out of the build - Unity includes every
    // asset under Assets/Resources whether any code references it or not - so Ground02 itself
    // has to leave the folder for the duration of the build.
    private const string MobileLiteDefine = "MOBILE_LITE";
    // The two steps run as separate Unity invocations (arm, then build), so the pre-build
    // define set has to be kept somewhere that outlives a domain reload. EditorPrefs does.
    private const string MobileLiteSavedDefinesKey = "LunarBuggy.MobileLite.SavedDefines";
    private const string MobileLiteGround02StashKey = "LunarBuggy.MobileLite.Ground02Stashed";
    private const string MobileLiteStashFolder = "LunarGround02_mobile_lite_stash";
    private static readonly string[] MobileLiteGround02Files =
    {
        "Assets/Resources/Textures/LunarGround/T_LunarGround_Ground02_d.png",
        "Assets/Resources/Textures/LunarGround/T_LunarGround_Ground02_n.png",
    };

    private static string[] Ground02MoveSet(string root, string rel)
    {
        string src = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        return new[] { src, src + ".meta" };
    }

    /// <summary>Batch step 1: arm the MOBILE_LITE define and move the Ground02 textures out of
    /// Assets/Resources so they cannot be linked into the mobile build. Idempotent: an item
    /// already in the stash is left alone.</summary>
    [MenuItem(MenuRoot + "/Prepare mobile-light build (Ground02 out)", false, 42)]
    public static void PrepareMobileLiteBuild()
    {
        string current = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
        if (!current.Contains(MobileLiteDefine))
        {
            EditorPrefs.SetString(MobileLiteSavedDefinesKey, current);
        }
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL,MobileLiteDefine);

        string root = ProjectRoot;
        string stash = Path.Combine(root, MobileLiteStashFolder);
        int moved = 0;
        foreach (string rel in MobileLiteGround02Files)
        {
            foreach (string src in Ground02MoveSet(root, rel))
            {
                if (!File.Exists(src) && !Directory.Exists(src)) continue;
                Directory.CreateDirectory(stash);
                string dest = Path.Combine(stash, Path.GetFileName(src));
                if (File.Exists(dest) || Directory.Exists(dest)) continue;
                if (Directory.Exists(src)) Directory.Move(src, dest);
                else File.Move(src, dest);
                moved++;
            }
        }
        EditorPrefs.SetString(MobileLiteGround02StashKey, "1");
        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        Debug.Log($"Mobile-light prep: {BuildTargetGroup.WebGL} defines = {MobileLiteDefine}" +
                  $" (saved '{current}'), stashed {moved} Ground02 item(s) into {MobileLiteStashFolder}/");
    }

    /// <summary>Hard stop in front of every mobile-light build, the mirror of the itch SDK
    /// guard: either half of the exclusion (the define or the folder move) drifting out of sync
    /// would ship Ground02 and quietly defeat the whole size saving.</summary>
    private static void AssertMobileLiteExcluded(string buildName)
    {
        bool ground02Present = File.Exists(Path.Combine(ProjectRoot, "Assets", "Resources",
            "Textures", "LunarGround", "T_LunarGround_Ground02_d.png"));
        bool defineArmed = PlayerSettings
            .GetScriptingDefineSymbols(NamedBuildTarget.WebGL).Contains(MobileLiteDefine);

        if (!ground02Present && defineArmed) return;

        throw new System.InvalidOperationException(
            $"Build WebGL ({buildName}, mobile-light) ABORTED: the Moon's Ground02 blend is not fully excluded. " +
            $"Ground02 present = {ground02Present}, {MobileLiteDefine} armed = {defineArmed}. " +
            "Either would ship the second lunar ground texture and defeat the size saving. Run the " +
            "matching 'Prepare mobile-light build (Ground02 out)' step first, then rerun the build.");
    }

    /// <summary>Puts the Ground02 textures and WebGL defines back the way the prepare step found
    /// them, so the next regular CrazyGames build keeps the full Moon blend with no extra steps.
    /// Also the recovery path if a build was interrupted.</summary>
    private static void RestoreMobileLiteState()
    {
        // Files first, then defines: restoring the textures triggers a reimport, and doing that
        // before the define is cleared avoids a second compile (same order as the itch restore).
        string root = ProjectRoot;
        string stash = Path.Combine(root, MobileLiteStashFolder);
        if (!Directory.Exists(stash))
        {
            EditorPrefs.DeleteKey(MobileLiteGround02StashKey);
        }
        else
        {
            int restored = 0;
            foreach (string rel in MobileLiteGround02Files)
            {
                foreach (string dest in Ground02MoveSet(root, rel))
                {
                    string src = Path.Combine(stash, Path.GetFileName(dest));
                    if (!File.Exists(src) && !Directory.Exists(src)) continue;
                    if (File.Exists(dest) || Directory.Exists(dest)) continue;   // already back home
                    string parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    if (Directory.Exists(src)) Directory.Move(src, dest);
                    else File.Move(src, dest);
                    restored++;
                }
            }
            if (Directory.GetFileSystemEntries(stash).Length == 0) Directory.Delete(stash);
            EditorPrefs.DeleteKey(MobileLiteGround02StashKey);
            Debug.Log($"Mobile-light build: restored {restored} Ground02 item(s) from {MobileLiteStashFolder}/");
        }

        string saved = EditorPrefs.HasKey(MobileLiteSavedDefinesKey)
            ? EditorPrefs.GetString(MobileLiteSavedDefinesKey) : "";
        EditorPrefs.DeleteKey(MobileLiteSavedDefinesKey);
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL,saved);
        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        Debug.Log($"Mobile-light build: restored {BuildTargetGroup.WebGL} defines = '{saved}'");
    }

    /// <summary>Batch step 2: Brotli CrazyGames build with the Moon on a single ground texture.
    /// After the build the project is put back to its full-blend state, so the next web build
    /// needs no extra steps. Also the recovery path if a build was interrupted: it brings the
    /// stashed Ground02 back from <see cref="MobileLiteStashFolder"/> and clears the define.</summary>
    [MenuItem(MenuRoot + "/Finish mobile-light build (restore defines)", false, 44)]
    public static void FinishMobileLiteBuild()
    {
        RestoreMobileLiteState();
    }

    /// <summary>Batch step 2: Brotli CrazyGames build (the mobile submission variant) with the
    /// Moon dressed on the single Ground01 texture. If the batch exits before this runs, run
    /// FinishMobileLiteBuild to put the project back.</summary>
    [MenuItem(MenuRoot + "/Build WebGL (CrazyGames, Brotli, mobile-light)", false, 43)]
    public static void BuildWebGLCrazyBrotliMobileLite()
    {
        AssertMobileLiteExcluded("CrazyGames mobile-light");
        // The fallback flag is a project setting that outlives the build, so put it back or the
        // next CrazyGames build silently inherits it (same guard as the itch/Kongregate builds).
        bool savedFallback = PlayerSettings.WebGL.decompressionFallback;
        try
        {
            CleanOutputDir();
            PlayerSettings.WebGL.decompressionFallback = false;
            RunBuild(WebGLCompressionFormat.Brotli);
            PatchCrazyIndexHead();
        }
        finally
        {
            PlayerSettings.WebGL.decompressionFallback = savedFallback;
            RestoreMobileLiteState();
            Debug.Log($"Mobile-light build: restored decompressionFallback = {savedFallback}");
        }
    }


        // Submission builds are uploaded file-by-file, so the folder must not contain leftovers
    // from an earlier build in a different compression format - mixed .br and plain files
    // make the portal's compression detection ambiguous.
    private static void CleanOutputDir(string folder = "WebGL")
    {
        string outDir = Path.Combine(Application.dataPath, "..", "Builds", folder);
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
    }

    // Re-applies the CrazyGames "common fixes" style patch to an already-built
    // Builds/WebGL/index.html (handy when nothing in the C# side changed).
    [MenuItem(MenuRoot + "/Patch CrazyGames index.html (style fixes)", false, 33)]
    public static void PatchCrazyIndexHeadMenu()
    {
        PatchCrazyIndexHead();
    }

    // Patches the freshly generated index.html in place. Idempotent: skips when already
    // patched. The CrazyGames SDK itself is NOT injected here - the official Unity SDK
    // (Assets/CrazySDK/Plugins/crazySDK.jslib) loads and initializes the platform SDK at
    // runtime, exactly what the preview/QA tool expects. This only adds the platform's
    // recommended common fixes:
    //   - the Unity template's own fullscreen button: hidden on CrazyGames/itch.io/Kongregate
    //     (the portal owns fullscreen and an in-game button fought it), but KEPT on Newgrounds
    //     (showFullscreen: true) with the surrounding Unity logo + build title hidden,
    //   - stop touch-select / double-tap magnification on mobile,
    //   - block page scroll + the browser context menu over the canvas.
    private static void PatchCrazyIndexHead(bool showFullscreen = false, string folder = "WebGL")
    {
        string index = Path.Combine(Application.dataPath, "..", "Builds", folder, "index.html");
        if (!File.Exists(index))
        {
            Debug.LogError("Patch CrazyGames index: " + index + " not found - build WebGL first.");
            return;
        }
        string html = File.ReadAllText(index);
        const string marker = "user-select: none";
        if (html.Contains(marker))
        {
            Debug.Log("Patch CrazyGames index: already patched, skipping.");
            return;
        }

        string headSnippet =
            "    <style>\n" +
            // The Unity template's footer carries its own fullscreen button. CrazyGames, itch.io
            // and Kongregate hide the WHOLE footer (the portal owns fullscreen there, and an
            // in-game button fought it - see FullscreenButton). Newgrounds keeps the button
            // (showFullscreen) but drops the Unity logo/watermark and the build title around it,
            // so only the fullscreen control remains in the corner.
            (showFullscreen
                ? "      #unity-logo-title-footer { display: none !important; }\n" +
                  "      #unity-build-title { display: none !important; }\n" +
                  // The template's own style.css hides the whole footer on mobile
                  // (.unity-mobile #unity-footer { display: none }); override it so mobile
                  // players keep the fullscreen control too.
                  "      .unity-mobile #unity-footer { display: block !important; }\n" +
                  // The template lays the footer OUT OF FLOW below the canvas. With our
                  // full-bleed canvas (100% height) that pushes the footer past the viewport,
                  // where html,body{overflow:hidden} clips it - which is why the fullscreen
                  // button never appeared. Float the footer back over the bottom edge of the
                  // canvas instead, and let clicks fall through the footer except on the
                  // button itself, so it never steals input from the game.
                  "      #unity-footer { position: absolute !important; left: 0 !important; right: 0 !important; bottom: 0 !important; margin: 0 !important; height: 38px !important; z-index: 10 !important; pointer-events: none !important; }\n" +
                  "      #unity-fullscreen-button { pointer-events: auto !important; cursor: pointer !important; }\n"
                : "      #unity-footer { display: none !important; }\n") +
                  // CrazyGames mobile requirement: stop double-tap/hold from selecting the
                  // whole page or raising the magnification menu on tablets.
                  "      body { -webkit-user-select: none; -moz-user-select: none; -ms-user-select: none; user-select: none; }\n" +
                  // Unity's default WebGL template pins the desktop canvas to a fixed box by writing
                  // inline styles from its own script (canvas.style.width = "960px"; height = "600px"),
                  // and its CSS only ever sizes the canvas to 100% on the MOBILE path. So on any
                  // desktop the game stays a 960x600 letterbox in the middle of the page forever -
                  // which is why itch.io's fullscreen button appeared to do nothing: the host was
                  // correctly growing the iframe, but the canvas ignored it. Forcing the document,
                  // container and canvas to 100% with !important overrides the template's inline
                  // styles, so the canvas tracks the iframe and fills whatever the host gives it.
                  // This is what the itch.io docs ask for: "a template or markup that allows for
                  // your game's canvas to adjust to the size of the window it is placed in".
                  //
                  // EVERY property here must be !important, including the positioning ones and not
                  // just the sizes. This block is injected at the top of <head>, so TemplateData/
                  // style.css is parsed AFTER it, and an equally specific later rule wins on
                  // source order. style.css sets #unity-container { position: absolute } and
                  // #unity-container.unity-desktop { left: 50%; top: 50%; transform: translate(-50%,-50%) }.
                  // Overriding only the size and the transform therefore produced a full-viewport
                  // box still anchored at the centre with its centring correction cancelled, which
                  // pushed the game half a screen down and right and left only its top-left corner
                  // on screen. The whole rule has to win outright, not just the size half of it.
                  "      html, body { width: 100% !important; height: 100% !important; margin: 0 !important; padding: 0 !important; overflow: hidden !important; background: #1F1F20; }\n" +
                  "      #unity-container.unity-desktop { position: fixed !important; left: 0 !important; top: 0 !important; right: auto !important; bottom: auto !important; width: 100% !important; height: 100% !important; transform: none !important; }\n" +
                  "      #unity-container.unity-desktop #unity-canvas { width: 100% !important; height: 100% !important; display: block !important; }\n" +
                  "    </style>\n" +
            "    <script>\n" +
            // CrazyGames "common fixes": stop the page scrolling under the canvas and the
            // browser context menu opening on top of it.
            "    (function () {\n" +
            "      window.addEventListener('wheel', function (e) { e.preventDefault(); }, { passive: false });\n" +
            "      window.addEventListener('keydown', function (e) {\n" +
            "        if (e.key === 'ArrowUp' || e.key === 'ArrowDown' || e.key === ' ' || e.code === 'Space') e.preventDefault();\n" +
            "      });\n" +
            "      document.addEventListener('contextmenu', function (e) { e.preventDefault(); });\n" +
            "    })();\n" +
            "    </script>\n";

        string marker2 = "<head>";
        if (html.IndexOf(marker2, System.StringComparison.OrdinalIgnoreCase) < 0)
        {
            Debug.LogError("Patch CrazyGames index: no <head> tag found in index.html.");
            return;
        }
        int at = html.IndexOf(marker2, System.StringComparison.OrdinalIgnoreCase) + marker2.Length;
        html = html.Substring(0, at) + "\n" + headSnippet + html.Substring(at);

        File.WriteAllText(index, html);
        Debug.Log("Patch CrazyGames index: patched " + index);
    }

    private static void RunBuild(WebGLCompressionFormat format, string outputFolder = "WebGL")
    {
        // CrazyGames build rule: the SDK loading scene must be scene index 0 so the platform
        // SDK initializes before the game's own menu runs. Ensure (create on demand) and pin
        // it first in Build Settings - every build variant then boots through it, and hosts
        // without the SDK skip it instantly (see CrazyLoadingSequence).
        EnsureCrazyLoadingScene();

        string[] scenes =
            EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("Build WebGL: no enabled scenes in Build Settings.");
            EditorApplication.Exit(1);
            return;
        }

        string outDir = Path.Combine(Application.dataPath, "..", "Builds", outputFolder);
        Directory.CreateDirectory(outDir);

        PlayerSettings.WebGL.compressionFormat = format;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);

        // Tiling material swatches dominate the download once block-compressed; cap them so every
        // web build ships the lightweight set (see WebTextureBudget).
        WebTextureBudget.Apply();

        // The whole world is built at runtime with Shader.Find("Standard"). Because no scene
        // references those materials at build time, shader stripping removes them and runtime
        // materials come out magenta/purple. Pin every shader we construct by name.
        AddToAlwaysIncludedShaders("Standard");
        AddToAlwaysIncludedShaders("UI/Default");
        AddToAlwaysIncludedShaders("Sprites/Default");
        AddToAlwaysIncludedShaders("Unlit/Color"); // horizon backdrops (ignores fog)
        AddToAlwaysIncludedShaders("Unlit/Texture"); // textured backdrops (e.g. Earth map)
        AddToAlwaysIncludedShaders("Hidden/ColorKey"); // flat sky cards: punches the art's black
                                                       // surround out so it has no visible edge
        AddToAlwaysIncludedShaders("Hidden/HorizonHills"); // distant hill ring (its own fog term)
        AddToAlwaysIncludedShaders("Legacy Shaders/Particles/Alpha Blended"); // WheelDust soft dots

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildResult result = report != null ? report.summary.result : BuildResult.Failed;
        Debug.Log($"Build WebGL finished with result: {result}");
        if (result != BuildResult.Succeeded)
        {
            Debug.LogError($"Build WebGL failed. Total errors: {(report != null ? report.summary.totalErrors : -1)}");
            EditorApplication.Exit(1);
        }
        else
        {
            double mb = report.summary.totalSize / (1024.0 * 1024.0);
            Debug.Log($"Build WebGL succeeded at {outDir} ({mb:F1} MB total).");
        }
    }

    // Build-Settings rule from the CrazyGames SDK docs: the loading scene MUST be scene index 0
    // so the platform SDK initializes before the game's own scenes run. Idempotent: creates the
    // scene once if missing, then always pins it first in Build Settings. Non-Crazy hosts boot
    // through it and are handed straight to the menu by CrazyLoadingSequence in the same frame.
    private static void EnsureCrazyLoadingScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(CrazyLoadingScenePath) == null)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.05f, 0.12f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = 5f;

            var loaderGo = new GameObject("CrazyLoadingSequence");
            var loader = loaderGo.AddComponent<CrazyLoadingSequence>();
            loader.nextSceneName = "Menu";

            EditorSceneManager.SaveScene(scene, CrazyLoadingScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Created CrazyGames loading scene at " + CrazyLoadingScenePath);
        }

        var list = EditorBuildSettings.scenes.Where(s => s.path != CrazyLoadingScenePath).ToList();
        list.Insert(0, new EditorBuildSettingsScene(CrazyLoadingScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // Adds a shader to Graphics Settings > Always Included Shaders so it survives
    // stripping even when nothing references it at build time.
    private static void AddToAlwaysIncludedShaders(string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogWarning($"Always-included shader '{shaderName}' not found; skipping.");
            return;
        }

        var graphicsSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (graphicsSettings == null || graphicsSettings.Length == 0) return;

        SerializedObject so = new SerializedObject(graphicsSettings[0]);
        try
        {
            SerializedProperty sp = so.FindProperty("m_AlwaysIncludedShaders");
            if (sp == null) return;

            for (int i = 0; i < sp.arraySize; i++)
            {
                if (sp.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    Debug.Log($"'{shaderName}' already in Always Included Shaders.");
                    return;
                }
            }

            sp.InsertArrayElementAtIndex(sp.arraySize);
            sp.GetArrayElementAtIndex(sp.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"Added '{shaderName}' to Always Included Shaders.");
        }
        finally
        {
            so.Dispose();
        }
    }
}
