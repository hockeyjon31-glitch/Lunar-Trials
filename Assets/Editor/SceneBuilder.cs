using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-only helper that (re)creates the Menu and Leaderboard scenes and registers all
/// three scenes (Menu, Main, Leaderboard) in EditorBuildSettings so SceneManager.LoadScene
/// by name works in standalone builds. Run headless with:
///   Unity.exe -batchmode -nographics -quit -projectPath ... -executeMethod SceneBuilder.Rebuild
/// </summary>
public static class SceneBuilder
{
    private const string SceneDir = "Assets/Scenes";
    private const string MenuScene = SceneDir + "/Menu.unity";
    private const string MainScene = SceneDir + "/Main.unity";
    private const string LeaderboardScene = SceneDir + "/Leaderboard.unity";

    public static void Rebuild()
    {
        Debug.Log("SceneBuilder.Rebuild started.");

        Directory.CreateDirectory(SceneDir);

        BuildScreenScene(MenuScene, "Menu", typeof(MenuController));
        BuildScreenScene(LeaderboardScene, "Leaderboard", typeof(LeaderboardScreen));

        // Make sure Main.unity exists and is valid before it goes into the build list.
        if (!File.Exists(MainScene))
        {
            Debug.LogError("SceneBuilder: " + MainScene + " is missing; nothing can run.");
            EditorApplication.Exit(1);
            return;
        }

        var scenes = new[]
        {
            new EditorBuildSettingsScene(MenuScene, true),
            new EditorBuildSettingsScene(MainScene, true),
            new EditorBuildSettingsScene(LeaderboardScene, true),
        };
        EditorBuildSettings.scenes = scenes;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("SceneBuilder.Rebuild done. Build scenes: " + EditorBuildSettings.scenes.Length);
    }

    private static void BuildScreenScene(string path, string rootName, System.Type controllerType)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        Camera cam = cameraGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.02f, 0.05f, 1f);
        cameraGo.AddComponent<AudioListener>();

        GameObject root = new GameObject(rootName);
        root.AddComponent(controllerType);

        // EditorSceneManager.SaveScene overwrites the file at path; these scenes are
        // generated from scratch every run so there is nothing to preserve.
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log("SceneBuilder: wrote " + path);
    }
}