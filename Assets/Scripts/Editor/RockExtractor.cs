using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time editor tool: distills the reference rock packs (Free_Rocks, PolyOne
/// "Rocks Stylized", Rocks and Boulders 2) into compact numeric profiles and writes
/// them to Assets/Scripts/RockProfiles.cs.
///
/// How it stays "minimal":
///  - The FBX files are copied into a transient folder under Assets, imported, sampled,
///    then the folder is DELETED again. Nothing from the packs is kept in the project.
///  - Only 20 floats per rock are stored (proportions + 8 silhouette values + lumpiness).
///    No mesh, no texture, no material is shipped; the runtime builder recreates a small
///    lathe rock from these numbers at level build (a few hundred vertices per boulder).
///  - The "shading and texture" of the originals come back for free via the per-boulder
///    ellipse footprint, faceted normals and the existing per-planet regolith material.
///
/// Run from the editor menu "Lunar Buggy > Extract Rock Profiles" or from a batch
/// build via -executeMethod RockExtractor.ExtractRockProfiles.
/// </summary>
public static class RockExtractor
{
    private const string Staging = "Assets/Editor/DontShip_RockSrc";
    private const string OutputPath = "Assets/Scripts/RockProfiles.cs";

    private static readonly string[] SourceDirs =
    {
        @"C:\Users\hocke\Documents\Lunar Buggy Test\Free_Rocks\_models",
        @"C:\Users\hocke\Documents\Lunar Buggy Test\PolyOne\Rocks Stylized\Model",
        @"C:\Users\hocke\Documents\Lunar Buggy Test\Rocks and Boulders 2\Rocks\Source\Models",
    };

    [MenuItem("Lunar Buggy/Extract Rock Profiles", false, 45)]
    public static void ExtractRockProfiles()
    {
        List<string> skipped = Run();
        if (!Application.isBatchMode)
        {
            UnityEditor.EditorUtility.DisplayDialog(
                "Lunar Buggy",
                "Rock profiles written to " + OutputPath + (skipped.Count > 0
                    ? "\n\nSkipped (see console):\n" + string.Join("\n", skipped)
                    : string.Empty),
                "OK");
        }
    }

    // Public static entry, callable from batch: -executeMethod RockExtractor.ExtractRockProfiles
    public static List<string> Run()
    {
        List<string> problems = new List<string>();

        // 1) Gather candidate FBX files, skipping colliders/cliffs/standard-asset junk.
        List<(string label, string src, string clean)> sources =
            new List<(string label, string src, string clean)>();
        foreach (string dir in SourceDirs)
        {
            if (!Directory.Exists(dir))
            {
                Debug.LogWarning("RockExtractor: source folder missing - " + dir);
                continue;
            }

            string pack = dir.Contains("Free_Rocks") ? "Free_Rocks"
                        : dir.Contains("PolyOne")    ? "PolyOne"
                        : dir.Contains("Rocks and Boulders 2") ? "RB2" : "pack";

            foreach (string f in Directory.GetFiles(dir, "*.fbx", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (name.Contains("collider") || name.Contains("cliff")) continue;
                string label = (pack == "RB2" ? name : name) + " (" + pack + ")";
                string clean = BuildFileName(pack, Path.GetFileName(f));
                sources.Add((label, f, clean));
            }
        }

        // 2) Stage copies under Assets so Unity's importer can read them.
        if (sources.Count == 0)
        {
            Debug.LogError("RockExtractor: no source FBX files found. Nothing generated.");
            return problems;
        }

        try
        {
            Directory.CreateDirectory(Staging);
            List<(string label, string asset, string name)> staged =
                new List<(string label, string asset, string name)>();
            foreach (var s in sources)
            {
                string dest = Staging + "/" + s.clean;
                File.Copy(s.src, dest, true);
                staged.Add((s.label, dest, s.clean));
            }

            AssetDatabase.Refresh();

            // 3) Force readable imports (guarantees mesh.vertices works even for FBX
            //    whose read/write flag was off).
            foreach (var st in staged)
            {
                ModelImporter mi = AssetImporter.GetAtPath(st.asset) as ModelImporter;
                if (mi == null) continue;
                if (!mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); }
            }

            // 4) Sample every mesh and emit its profile line.
            StringBuilder sb = new StringBuilder();
            sb.Append(HeaderDoc);
            sb.Append("    public static readonly RockProfile[] Profiles = new RockProfile[]\n    {\n");

            foreach (var st in staged)
            {
                if (!System.IO.File.Exists(st.asset)) continue;
                Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(st.asset)
                    .OfType<Mesh>()
                    .Where(m => m.vertexCount > 0)
                    .ToArray();
                if (meshes.Length == 0)
                {
                    Debug.LogWarning("RockExtractor: no mesh in " + st.label);
                    problems.Add(st.label);
                    continue;
                }

                string line;
                if (!ComputeProfile(st.label, meshes, out line))
                {
                    Debug.LogWarning("RockExtractor: could not sample " + st.label);
                    problems.Add(st.label);
                    continue;
                }
                sb.Append(line);
            }

            sb.Append("    };\n}\n");

            // 5) Write the generated file, then drop the transient assets.
            File.WriteAllText(OutputPath, sb.ToString());
            AssetDatabase.Refresh();

            Debug.Log("RockExtractor: wrote " + OutputPath + " (" +
                      (sources.Count - problems.Count) + " profiles, " +
                      problems.Count + " skipped).");
        }
        finally
        {
            // Always purge the transient import folder so nothing from the packs ships.
            string meta = Staging + ".meta";
            if (Directory.Exists(Staging)) FileUtil.DeleteFileOrDirectory(Staging);
            if (File.Exists(meta)) File.Delete(meta);
            AssetDatabase.Refresh();
        }

        return problems;
    }

    private static string BuildFileName(string pack, string fileName)
    {
        string p = pack == "PolyOne" ? "P" : pack == "RB2" ? "R" : "F";
        return p + "_" + fileName;
    }

    // Samples a source mesh into a single generated C# profile initializer line.
    private static bool ComputeProfile(string label, Mesh[] meshes, out string line)
    {
        line = null;
        int total = 0;
        for (int i = 0; i < meshes.Length; i++) total += meshes[i].vertices.Length;
        if (total == 0) return false;

        // Concatenate every mesh in the FBX so multi-part rocks sample as one body.
        // FBX import bakes each sub-object's transform into its mesh, so all vertices
        // share the same root space.
        Vector3[] verts = new Vector3[total];
        int off = 0;
        for (int i = 0; i < meshes.Length; i++)
        {
            Vector3[] v = meshes[i].vertices;
            System.Array.Copy(v, 0, verts, off, v.Length);
            off += v.Length;
        }

        // Axis-aligned bounds in local space.
        Vector3 mn = verts[0], mx = verts[0];
        for (int i = 1; i < verts.Length; i++)
        {
            if (verts[i].x < mn.x) mn.x = verts[i].x;
            if (verts[i].y < mn.y) mn.y = verts[i].y;
            if (verts[i].z < mn.z) mn.z = verts[i].z;
            if (verts[i].x > mx.x) mx.x = verts[i].x;
            if (verts[i].y > mx.y) mx.y = verts[i].y;
            if (verts[i].z > mx.z) mx.z = verts[i].z;
        }
        if (mx.x - mn.x <= 0f || mx.y - mn.y <= 0f || mx.z - mn.z <= 0f) return false;

        float dx = mx.x - mn.x, dy = mx.y - mn.y, dz = mx.z - mn.z;
        float maxE = Mathf.Max(dx, Mathf.Max(dy, dz));
        float widthN = Mathf.Max(0.05f, dx / maxE);
        float heightN = Mathf.Max(0.05f, dy / maxE);
        float depthN = Mathf.Max(0.05f, dz / maxE);

        // 8 horizontal bands of equal height; each gets the RMS horizontal radius so a
        // stray outlier vertex cannot inflate a band the way a max-radius would.
        const int R = 8;                      // must match RockArt's row count
        float[] sum2 = new float[R];
        int[] cnt = new int[R];
        double lobN = 0.0, lobM = 0.0;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i];
            float t = (v.y - mn.y) / dy;      // 0..1 across the rock height
            int b = (int)(t * R);
            if (b >= R) b = R - 1;
            if (b < 0) b = 0;
            float rho = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            sum2[b] += rho * rho;
            cnt[b]++;

            // Lumpedness uses the middle 80% of the body (edges are sparse/noisy).
            if (t > 0.1f && t < 0.9f && rho > 0f)
            {
                lobM += rho; lobN++;
            }
        }

        float bodyMean = lobN > 0f ? (float)(lobM / lobN) : 0f;

        float[] rings = new float[R];
        float maxRing = 0f;
        for (int i = 0; i < R; i++)
        {
            rings[i] = cnt[i] > 0 ? Mathf.Sqrt(sum2[i] / cnt[i]) : 0f;
            if (rings[i] > maxRing) maxRing = rings[i];
        }
        if (maxRing <= 0f) return false;
        for (int i = 0; i < R; i++) rings[i] = rings[i] / maxRing;

        // Heal empty bands: sparse low-poly faces sometimes leave a height slice with no
        // vertex in it, which would pinch the runtime rock to nothing. Interpolate across
        // the gap instead - a real boulder silhouette is contiguous.
        for (int i = 0; i < R; i++)
        {
            if (rings[i] > 0.001f) continue;
            int lo = i - 1; while (lo >= 0 && rings[lo] <= 0.001f) lo--;
            int hi = i + 1; while (hi < R && rings[hi] <= 0.001f) hi++;
            if (lo < 0 && hi >= R) continue;          // whole body empty - skip rock
            if (lo < 0) rings[i] = rings[hi];
            else if (hi >= R) rings[i] = rings[lo];
            else rings[i] = (rings[lo] + rings[hi]) * 0.5f;
        }
        for (int i = 0; i < R; i++) if (rings[i] <= 0.001f) rings[i] = 0.1f;

        // Lumpedness = relative variation of horizontal radius in the body.
        double sd = 0.0;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i];
            float t = (v.y - mn.y) / dy;
            if (t > 0.1f && t < 0.9f)
            {
                float rho = Mathf.Sqrt(v.x * v.x + v.z * v.z);
                float d = rho - bodyMean;
                sd += d * d;
            }
        }
        float lobiness = 0f;
        if (lobN >= 8f && bodyMean > 1e-4f)
        {
            sd = Math.Sqrt(Math.Max(0.0, sd / lobN)) / bodyMean;
            lobiness = Mathf.Clamp((float)sd, 0f, 0.6f);
        }

        string wid = (widthN).ToString("0.###", CultureInfo.InvariantCulture);
        string hei = (heightN).ToString("0.###", CultureInfo.InvariantCulture);
        string dep = (depthN).ToString("0.###", CultureInfo.InvariantCulture);
        string lob = lobiness.ToString("0.###", CultureInfo.InvariantCulture);
        string ringVals = string.Join(", ", rings.Select(r =>
            r.ToString("0.###", CultureInfo.InvariantCulture) + "f"));

        line = string.Format(CultureInfo.InvariantCulture,
            "        // {0}\n" +
            "        new RockProfile {{ widthN = {1}f, heightN = {2}f, depthN = {3}f, " +
            "lobiness = {4}f, rings = new float[] {{ {5} }} }},\n",
            label, wid, hei, dep, lob, ringVals);
        return true;
    }

    private const string HeaderDoc =
        "// GENERATED by RockExtractor - do not hand-edit.\n" +
        "// Runs: Lunar Buggy > Extract Rock Profiles. Rewrites Assets/Scripts/RockProfiles.cs.\n" +
        "using UnityEngine;\n" +
        "\n" +
        "/// <summary>\n" +
        "/// Compact shape signatures distilled from the reference rock packs (Free_Rocks,\n" +
        "/// PolyOne \"Rocks Stylized\", Rocks and Boulders 2). See RockExtractor for how they\n" +
        "/// are produced and why nothing from the packs is imported into the build.\n" +
        "/// </summary>\n" +
        "public static class RockProfiles\n" +
        "{\n" +
        "    public struct RockProfile\n" +
        "    {\n" +
        "        public float widthN;\n" +
        "        public float heightN;\n" +
        "        public float depthN;\n" +
        "        public float lobiness;\n" +
        "        public float[] rings;\n" +
        "    }\n" +
        "\n" +
        "    public static bool HasProfiles => Profiles != null && Profiles.Length > 0;\n" +
        "\n" +
        "    /// <summary>Deterministic pick by boulder index.</summary>\n" +
        "    public static RockProfile Pick(int index)\n" +
        "    {\n" +
        "        if (!HasProfiles) return default(RockProfile);\n" +
        "        int i = index % Profiles.Length;\n" +
        "        if (i < 0) i += Profiles.Length;\n" +
        "        return Profiles[i];\n" +
        "    }\n" +
        "\n";
}