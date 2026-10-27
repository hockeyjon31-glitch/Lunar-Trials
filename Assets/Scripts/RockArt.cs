using UnityEngine;

/// <summary>
/// Shared procedural rock art, so the route hazards (GameBootstrap), the race hazards
/// (RaceHazardDirector) and the terrain scatter (MoonTerrainGen) use the same look: a
/// regolith-tinted mottled texture (one per planet, cached by planet name) plus a rock
/// shape builder.
///
/// Shape comes from two layers, best first:
///  - the imported Lunar Landscape rock meshes (Resources/Geometry/Rocks.fbx) when present,
///    so every world can bed its surface and hazards in the pack's real rock silhouettes;
///  - then RockProfiles - compact silhouettes distilled from the reference rock packs (see
///    RockProfiles.cs and RockExtractor). Each boulder gets one small generated lathe mesh
///    whose proportions, silhouette and lumpiness match a real rock, with a convex
///    MeshCollider;
///  - the old primitive-cluster builder is used only when both are missing, so the game
///    still works.
///
/// Pure decoration plus shape: the caller owns the colliders, so route rocks keep theirs
/// (they damage the hull) while the terrain scatter strips them as before.
/// </summary>
public static class RockArt
{
    private static string cachedPlanet = "";
    private static Texture2D cachedTex;

    /// <summary>The four Lunar Landscape rock meshes, loaded once. Empty when the FBX is absent.</summary>
    private static Mesh[] fbxRocks;
    private static bool fbxWarned;

    private static Mesh[] FbxRocks()
    {
        if (fbxRocks == null)
        {
            fbxRocks = Resources.LoadAll<Mesh>("Geometry/Rocks");
            if (fbxRocks == null) fbxRocks = new Mesh[0];
            if (fbxRocks.Length == 0 && !fbxWarned)
            {
                fbxWarned = true;
                Debug.LogWarning("RockArt: no meshes in Resources/Geometry/Rocks.fbx; "
                                 + "rocks fall back to the RockProfiles lathes.");
            }
        }
        return fbxRocks;
    }

    /// <summary>
    /// The mottled regolith texture for a planet, built once per <paramref name="planetKey"/>
    /// and cached. The surface is two octaves of value noise in the planet's regolith hue so
    /// the rocks sit naturally on the ground, with darker fracture veins breaking it up.
    /// </summary>
    public static Texture2D TextureFor(Color tint, string planetKey)
    {
        if (cachedTex != null && cachedPlanet == planetKey) return cachedTex;
        cachedPlanet = planetKey;
        cachedTex = BuildTexture(tint);
        return cachedTex;
    }

    private static Texture2D BuildTexture(Color tint)
    {
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
        tex.name = "RockRegolith";
        tex.wrapMode = TextureWrapMode.Repeat;

        Color dark = Color.Lerp(tint, Color.black, 0.40f);
        Color light = Color.Lerp(tint, Color.white, 0.30f);
        Color vein = Color.Lerp(tint, Color.black, 0.62f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Two-octave value noise gives the mottled rock surface.
                float n1 = Mathf.PerlinNoise(x * 0.10f, y * 0.10f);
                float n2 = Mathf.PerlinNoise(x * 0.32f + 7.3f, y * 0.32f + 3.1f);
                float v = n1 * 0.72f + n2 * 0.28f;
                // Sparse dark fracture veins on a slow third octave.
                float f = Mathf.PerlinNoise(x * 0.05f + 41f, y * 0.05f + 67f);
                Color c = Color.Lerp(dark, light, v);
                if (f > 0.68f) c = Color.Lerp(c, vein, (f - 0.68f) / 0.32f);
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply(true, false);
        return tex;
    }

    /// <summary>
    /// Builds a rock on <paramref name="root"/>. Prefers a real silhouette from the imported
    /// rock meshes; falls back to a RockProfiles lathe (one small generated mesh + convex
    /// collider), then to overlapping primitives. Shape is stable per <paramref name="index"/>.
    /// </summary>
    public static void BuildRockShape(Transform root, float scale, Material mat, int index)
    {
        if (TryBuildImportedRock(root, scale, mat, index)) return;
        if (RockProfiles.HasProfiles)
        {
            BuildProfileMesh(root, scale, mat, index, RockProfiles.Pick(index));
            return;
        }
        BuildPrimitiveCluster(root, scale, mat, index);
    }

    // The real rock meshes from the Lunar Landscape pack, sized to the caller's scale and
    // recentred on the root so a rock behaves exactly like the generated shapes before it.
    private static bool TryBuildImportedRock(Transform root, float scale, Material mat, int index)
    {
        Mesh[] meshes = FbxRocks();
        if (meshes == null || meshes.Length == 0) return false;

        Mesh mesh = meshes[Mathf.Abs(index) % meshes.Length];
        Bounds b = mesh.bounds;
        float maxD = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (maxD <= 0f) return false;

        float k = scale / maxD;
        root.localScale = Vector3.one * k;

        GameObject go = new GameObject("RockMesh");
        go.transform.SetParent(root, false);
        go.transform.localPosition = -b.center;   // recentre the maverick import pivot

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        MeshCollider mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;
        return true;
    }

    // Generates a single lathe mesh whose ellipsoid footprint, silhouette and lumpiness
    // come from the tested profile. Cheap: 8 rows x 16 azimuths ~= 288 triangles.
    private static void BuildProfileMesh(Transform root, float scale, Material mat,
                                         int index, RockProfiles.RockProfile p)
    {
        const int Rows = 8;          // must match RockExtractor's ring count
        const int Az = 16;

        float maxE = Mathf.Max(p.widthN, Mathf.Max(p.heightN, p.depthN));
        if (maxE <= 0f) return;
        float rx = p.widthN / maxE * scale * 0.5f;
        float rz = p.depthN / maxE * scale * 0.5f;
        float height = p.heightN / maxE * scale;

        float[] rings = p.rings;
        if (rings == null || rings.Length == 0) { BuildPrimitiveCluster(root, scale, mat, index); return; }

        int V = Rows * Az + 2;             // +2 cap poles so the lathe is a closed solid
        int botPole = Rows * Az;
        int topPole = Rows * Az + 1;
        Vector3[] verts = new Vector3[V];
        Vector2[] uvs = new Vector2[V];
        for (int i = 0; i < Rows; i++)
        {
            int ri = Mathf.Clamp(Mathf.RoundToInt((float)i / (Rows - 1) * (rings.Length - 1)),
                                 0, rings.Length - 1);
            float n = rings[ri];
            float t = (i + 0.5f) / Rows;
            float y = -height * 0.5f + height * t;
            for (int a = 0; a < Az; a++)
            {
                float theta = 2f * Mathf.PI * a / Az;
                Vector3 dir = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
                // Ellipse footprint shrinks/grows to the ring silhouette.
                Vector3 fp = new Vector3(dir.x * rx, 0f, dir.z * rz);
                float baseR = fp.magnitude;
                // Lumpy surface: per-vertex radial wobble seeded by position, deterministic.
                float wob = 1f + p.lobiness * (Hash01(index * 7919 + i * 313 + a * 97) * 2f - 1f);
                if (wob < 0.55f) wob = 0.55f;
                float radius = baseR * n * wob;
                int vi = i * Az + a;
                verts[vi] = new Vector3(dir.x * radius, y, dir.z * radius);
                uvs[vi] = new Vector2((float)a / Az, t);
            }
        }
        verts[botPole] = new Vector3(0f, -height * 0.5f, 0f);
        uvs[botPole] = new Vector2(0.5f, 0f);
        verts[topPole] = new Vector3(0f, +height * 0.5f, 0f);
        uvs[topPole] = new Vector2(0.5f, 1f);

        // Body rows plus two fans that seal the openings - without them the rock is an
        // open tube and reads as hollow from above, below and grazing angles.
        int[] tris = new int[6 * Rows * Az];
        int ti = 0;
        for (int i = 0; i < Rows - 1; i++)
        {
            for (int a = 0; a < Az; a++)
            {
                int a2 = (a + 1) % Az;
                int v00 = i * Az + a;
                int v01 = i * Az + a2;
                int v10 = (i + 1) * Az + a;
                int v11 = (i + 1) * Az + a2;
                tris[ti++] = v00; tris[ti++] = v10; tris[ti++] = v01;
                tris[ti++] = v01; tris[ti++] = v10; tris[ti++] = v11;
            }
        }
        // Bottom cap: fans the base ring down to the centre point (outward = -Y).
        for (int a = 0; a < Az; a++)
        {
            int a2 = (a + 1) % Az;
            tris[ti++] = botPole;
            tris[ti++] = a;
            tris[ti++] = a2;
        }
        // Top cap: fans the top ring up to the centre point (outward = +Y).
        int lastRow = (Rows - 1) * Az;
        for (int a = 0; a < Az; a++)
        {
            int a2 = (a + 1) % Az;
            tris[ti++] = topPole;
            tris[ti++] = lastRow + a2;
            tris[ti++] = lastRow + a;
        }

        Mesh mesh = new Mesh();
        mesh.name = "RockProfile_" + index;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter mf = root.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        MeshRenderer mr = root.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        MeshCollider mc = root.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;
    }

    private static float Hash01(int s)
    {
        uint h = (uint)s;
        h ^= h >> 16; h *= 0x7feb352d;
        h ^= h >> 15; h *= 0x846ca68b;
        h ^= h >> 16;
        return (h & 0xffff) / 65535f;
    }

    /// <summary>Fallback cluster of overlapping primitives (kept as solid parts - their
    /// colliders stay on the runtime hazard path). Shape is stable per <paramref name="index"/>.</summary>
    private static void BuildPrimitiveCluster(Transform root, float scale, Material mat, int index)
    {
        System.Random rng = new System.Random(index * 7919 + 31);
        int style = rng.Next(4);
        float h = 0.62f + (float)rng.NextDouble() * 0.30f; // height squash
        float w = 0.80f + (float)rng.NextDouble() * 0.45f; // width stretch
        float s = scale;

        switch (style)
        {
            case 0: // rounded blob
                Part(root, index, 0, mat, PrimitiveType.Sphere,
                    new Vector3(s * w, s * h, s * w), Vector3.zero, RandEuler(rng));
                break;

            case 1: // fused cluster: a big lobe with two smaller buddies tucked in
                Part(root, index, 0, mat, PrimitiveType.Sphere,
                    new Vector3(s * w, s * h, s * w), Vector3.zero, RandEuler(rng));
                Part(root, index, 1, mat, PrimitiveType.Sphere,
                    new Vector3(s * 0.5f * w, s * 0.5f * h, s * 0.5f * w),
                    new Vector3(s * 0.62f, -s * 0.08f, s * 0.4f), RandEuler(rng));
                Part(root, index, 2, mat, PrimitiveType.Sphere,
                    new Vector3(s * 0.42f * w, s * 0.42f * h, s * 0.42f * w),
                    new Vector3(-s * 0.55f, s * 0.04f, -s * 0.48f), RandEuler(rng));
                break;

            case 2: // angular fragment: a chunky cube with an offset broken cap
                Part(root, index, 0, mat, PrimitiveType.Cube,
                    new Vector3(s * w * 1.35f, s * h * 0.9f, s * w * 1.05f), Vector3.zero, RandEuler(rng));
                Part(root, index, 1, mat, PrimitiveType.Cube,
                    new Vector3(s * 0.62f * w, s * 0.55f * h, s * 0.55f * w),
                    new Vector3(s * 0.42f, s * 0.5f, s * 0.2f), RandEuler(rng));
                break;

            default: // squat slab, half buried in the regolith
                Part(root, index, 0, mat, PrimitiveType.Cube,
                    new Vector3(s * w * 1.7f, s * h * 0.55f, s * w * 1.2f), Vector3.zero, RandEuler(rng));
                Part(root, index, 1, mat, PrimitiveType.Sphere,
                    new Vector3(s * 0.5f * w, s * 0.3f * h, s * 0.4f * w),
                    new Vector3(s * 0.5f, s * 0.28f, -s * 0.4f), RandEuler(rng));
                break;
        }
    }

    private static void Part(Transform parent, int index, int k, Material mat, PrimitiveType type,
                             Vector3 scale, Vector3 pos, Vector3 rot)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = "R" + index + "_" + k;
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        go.transform.localPosition = pos;
        go.transform.localEulerAngles = rot;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = mat;
    }

    private static Vector3 RandEuler(System.Random rng)
    {
        return new Vector3((float)(rng.NextDouble() * 360f),
                           (float)(rng.NextDouble() * 360f),
                           (float)(rng.NextDouble() * 360f));
    }
}