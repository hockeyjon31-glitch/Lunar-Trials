using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The web builds dress the terrain and props with tiling material swatches (concrete, metal,
/// lunar ground, lava). Their 1024 normal/spec maps are ~1.3 MB each and, being already
/// block-compressed, Brotli cannot shrink them - so they dominate the download. These surfaces are
/// only seen at range, so the whole set is capped at <see cref="MaxSize"/> (a ~4x cut on each map,
/// ~16 MB off the build) with no visible difference. The unique full-screen backdrop art (sky,
/// planets, Milky Way) is deliberately left at full resolution.
///
/// Called automatically at the top of every web build (LunarBuggyMenu.RunBuild); run manually from
/// "Lunar Buggy > Cap Web Texture Sizes". Originals are unaffected; revert by re-running the
/// per-pack importers (YfcmImporter, LunarSceneryImporter, LavaImporter) or reimporting by hand.
/// </summary>
public static class WebTextureBudget
{
    public const int MaxSize = 512;

    private static readonly string[] Folders =
    {
        "Assets/Resources/Textures/YFCM",
        "Assets/Resources/Textures/YFMetal",
        "Assets/Resources/Textures/LunarGround",
        "Assets/Resources/Textures/Lava",
    };

    [MenuItem("Lunar Buggy/Cap Web Texture Sizes", false, 47)]
    public static void Apply()
    {
        int capped = 0;
        foreach (string folder in Folders)
        {
            if (!Directory.Exists(folder))
            {
                Debug.LogWarning("WebTextureBudget: folder missing: " + folder);
                continue;
            }

            foreach (string file in Directory.GetFiles(folder))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".tga" && ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;

                string path = file.Replace('\\', '/');
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                if (ti.maxTextureSize <= MaxSize) continue;

                ti.maxTextureSize = MaxSize;
                ti.SaveAndReimport();
                capped++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"WebTextureBudget: capped {capped} texture(s) to {MaxSize}px for the web build.");
    }
}
