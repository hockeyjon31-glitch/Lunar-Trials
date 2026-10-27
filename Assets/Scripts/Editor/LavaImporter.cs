using UnityEditor;
using UnityEngine;

/// <summary>
/// Configures the imported lava ("Magma") swatch textures (Resources/Textures/Lava) the way the
/// runtime material builder expects: the base as a repeating, CPU-readable sRGB texture (the
/// terrain decal bakes its albedo per patch, which needs sampling), the emissive as a repeating
/// sRGB glow tile, and the normal as a NormalMap for real crust relief.
///
/// Run from the editor menu "Lunar Buggy > Import Lava Textures" or from a batch build via
/// -executeMethod LavaImporter.ImportAll. Safe to re-run any time.
/// </summary>
public static class LavaImporter
{
    private static readonly string[] BaseTextures =
    {
        "Assets/Resources/Textures/Lava/T_Lava_Magma_d.png",
    };

    private static readonly string[] EmissiveTextures =
    {
        "Assets/Resources/Textures/Lava/T_Lava_Magma_e.png",
    };

    private static readonly string[] NormalMaps =
    {
        "Assets/Resources/Textures/Lava/T_Lava_Magma_n.png",
    };

    [MenuItem("Lunar Buggy/Import Lava Textures", false, 47)]
    public static void ImportAll()
    {
        foreach (string path in BaseTextures)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("LavaImporter: base not found at " + path); continue; }
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.isReadable = true;              // the decal bakes this albedo into its feathered map
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.SaveAndReimport();
            Debug.Log("LavaImporter: base " + path + " ready.");
        }

        foreach (string path in EmissiveTextures)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("LavaImporter: emissive not found at " + path); continue; }
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.SaveAndReimport();
            Debug.Log("LavaImporter: emissive " + path + " ready.");
        }

        foreach (string path in NormalMaps)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("LavaImporter: normal not found at " + path); continue; }
            ti.textureType = TextureImporterType.NormalMap;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.SaveAndReimport();
            Debug.Log("LavaImporter: normal " + path + " ready.");
        }
    }
}
