using UnityEditor;
using UnityEngine;

/// <summary>
/// Configures the Lunar Landscape 3D scenery textures copied into Resources the way the runtime
/// builders expect:
///  - the two lunar ground photographs (Ground 01 / Ground 02) are combined on the CPU for the
///    Moon's terrain and rocks, so their diffuses must be CPU-readable; their normals import as
///    NormalMaps for rock relief. The live set is the pack's low-weight JPG variants
///    (_d.jpg/_n.jpg); the original PNGs are parked alongside as *_orig.png backups.
///
/// Run from the editor menu "Lunar Buggy > Import Lunar Scenery Textures" or from a batch build
/// via -executeMethod LunarSceneryImporter.ImportAll. Safe to re-run; it just reapplies settings
/// and reimports the (already present) assets.
/// </summary>
public static class LunarSceneryImporter
{
    private static readonly string[] Diffuse =
    {
        "Assets/Resources/Textures/LunarGround/T_LunarGround_Ground01_d.jpg",
        "Assets/Resources/Textures/LunarGround/T_LunarGround_Ground02_d.jpg",
    };

    private static readonly string[] NormalMaps =
    {
        "Assets/Resources/Textures/LunarGround/T_LunarGround_Ground01_n.jpg",
    };

    [MenuItem("Lunar Buggy/Import Lunar Scenery Textures", false, 48)]
    public static void ImportAll()
    {
        foreach (string path in Diffuse)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("LunarSceneryImporter: diffuse not found at " + path); continue; }
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.isReadable = true;              // combined on the CPU at world build
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.SaveAndReimport();
            Debug.Log("LunarSceneryImporter: diffuse " + path + " ready.");
        }

        foreach (string path in NormalMaps)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("LunarSceneryImporter: normal not found at " + path); continue; }
            ti.textureType = TextureImporterType.NormalMap;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.SaveAndReimport();
            Debug.Log("LunarSceneryImporter: normal " + path + " ready.");
        }
    }
}