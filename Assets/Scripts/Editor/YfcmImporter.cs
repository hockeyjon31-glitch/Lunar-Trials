using UnityEditor;
using UnityEngine;

/// <summary>
/// Configures the imported Yughues Free Concrete texture swatches (Resources/Textures/YFCM)
/// the way the runtime material builders expect: each diffuse as a repeating, CPU-readable sRGB
/// texture (the surface/rock builder combines two swatches and the terrain decal bakes one into
/// its feathered map, both of which sample on the CPU), and each normal as a NormalMap so the
/// Standard shader shows real relief.
///
/// Run from the editor menu "Lunar Buggy > Import YFCM Textures" or from a batch build via
/// -executeMethod YfcmImporter.ImportAll. Safe to re-run any time; it just re-applies settings
/// and reimports the (already present) textures.
/// </summary>
public static class YfcmImporter
{
    private static readonly string[] Diffuse =
    {
        "Assets/Resources/Textures/YFCM/T_YFCM_Honeycombing_d.tga",
        "Assets/Resources/Textures/YFCM/T_YFCM_Moldy_d.tga",
        "Assets/Resources/Textures/YFCM/T_YFCM_ColoredSand_d.tga",
        "Assets/Resources/Textures/YFCM/T_YFCM_Crystallized_d.tga",
        "Assets/Resources/Textures/YFCM/T_YFCM_Fibers_d.tga",
    };

    private static readonly string[] NormalMaps =
    {
        "Assets/Resources/Textures/YFCM/T_YFCM_Honeycombing_n.tga",
        "Assets/Resources/Textures/YFCM/T_YFCM_ColoredSand_n.tga",
        "Assets/Resources/Textures/YFCM/T_YFCM_Crystallized_n.tga",
    };

    [MenuItem("Lunar Buggy/Import YFCM Textures", false, 46)]
    public static void ImportAll()
    {
        foreach (string path in Diffuse)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("YfcmImporter: diffuse not found at " + path); continue; }
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.isReadable = true;              // combined / baked on the CPU at world build
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.SaveAndReimport();
            Debug.Log("YfcmImporter: diffuse " + path + " ready.");
        }

        foreach (string path in NormalMaps)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("YfcmImporter: normal not found at " + path); continue; }
            ti.textureType = TextureImporterType.NormalMap;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.SaveAndReimport();
            Debug.Log("YfcmImporter: normal " + path + " ready.");
        }
    }
}
