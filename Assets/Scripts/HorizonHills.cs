using UnityEngine;

/// <summary>
/// A low-detail ring of distant hills wrapped around the terrain edge, so the horizon reads
/// as a rolling range instead of a flat line against the sky dome. Built only from code; the
/// mesh is a polar band from just outside the playfield rim out past the camera's reach, with
/// slope shading baked into vertex colours and a per-world regolith tint. Uses the fog-immune
/// Hidden/HorizonHills shader - the playfield's exponential fog would swallow the ring whole,
/// so the ring fades to the world's haze colour in its own distance term instead. Purely
/// cosmetic: no colliders, and every vertex sits below the terrain's inner edge so nothing
/// ever clips through the playable surface.
/// </summary>
public static class HorizonHills
{
    public static void Build(float extent, LevelDefinition def)
    {
        if (def == null) return;
        if (GameObject.Find("HorizonHills") != null) return;

        float inner = extent + 30f;
        float outer = extent + 1350f;
        int around = 220;
        int rows = 12;
        int cols = around + 1;

        float amp = Mathf.Clamp(def.hillAmplitude * 5f, 45f, 110f);
        float baseY = -300f;

        Vector3[] verts = new Vector3[rows * cols];
        Color[] tints = new Color[rows * cols];
        Color rock = def.regolithColor;
        Color haze = def.fogColor;
        if (haze.a <= 0.001f) haze = new Color(0.04f, 0.04f, 0.06f, 1f);

        // Lay down the polar grid.
        for (int i = 0; i < rows; i++)
        {
            float tr = i / (float)(rows - 1);
            float r = Mathf.Lerp(inner, outer, tr);
            for (int j = 0; j < cols; j++)
            {
                float ang = (j % around) / (float)around * Mathf.PI * 2f;
                float wx = Mathf.Cos(ang) * r;
                float wz = Mathf.Sin(ang) * r;
                verts[i * cols + j] = new Vector3(wx, HeightAt(wx, wz, tr, amp, baseY), wz);
            }
        }

        // Index the quads (column wraps seamlessly onto its duplicate).
        int[] tris = new int[(rows - 1) * around * 6];
        int t = 0;
        for (int i = 0; i < rows - 1; i++)
        {
            for (int j = 0; j < around; j++)
            {
                int a = i * cols + j;
                int b = a + 1;
                int c = a + cols;
                int d = c + 1;
                tris[t++] = a;
                tris[t++] = c;
                tris[t++] = b;
                tris[t++] = b;
                tris[t++] = c;
                tris[t++] = d;
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "HorizonHillsMesh";
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Bake slope shading + a height blend toward the haze tint.
        Vector3[] normals = mesh.normals;
        Vector3[] positions = mesh.vertices;
        Vector3 lightDir = new Vector3(0.35f, 0.86f, 0.36f).normalized;
        for (int k = 0; k < positions.Length; k++)
        {
            float shade = 0.45f + 0.55f * Mathf.Clamp01(Vector3.Dot(normals[k], lightDir));
            float crest = Mathf.Clamp01((positions[k].y - baseY) / (amp * 2.2f));
            tints[k] = Color.Lerp(haze, rock * shade, 0.55f + 0.45f * crest);
        }
        mesh.colors = tints;

        Material mat = new Material(Shader.Find("Hidden/HorizonHills"));
        mat.name = "HorizonHillsMat";
        mat.SetColor("_HazeColor", haze);
        mat.SetFloat("_HazeStart", extent - 200f);
        mat.SetFloat("_HazeEnd", outer);

        GameObject go = new GameObject("HorizonHills");
        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    private static float HeightAt(float wx, float wz, float tr, float amp, float baseY)
    {
        // Hill profile: rise out of the terrain rim over the first stretch, then roll gently.
        float rise = Mathf.Clamp01(tr / 0.30f);
        rise = rise * rise * (3f - 2f * rise);

        float n1 = Mathf.PerlinNoise(wx * 0.00045f + 11f, wz * 0.00045f + 7f) * 2f - 1f;
        float n2 = Mathf.PerlinNoise(wx * 0.0014f + 3f, wz * 0.0014f + 19f) * 2f - 1f;
        float ridge = Mathf.Clamp01(Mathf.PerlinNoise(wx * 0.0009f + 31f, wz * 0.0009f + 41f) * 1.6f - 0.4f);

        float roll = 0.65f * n1 + 0.35f * n2 + 0.6f * (ridge - 0.5f);
        float h = 30f + amp * (0.45f + 0.55f * (0.5f + 0.5f * Mathf.Clamp(roll, -1f, 1f)));
        return Mathf.Lerp(baseY, h, Mathf.Clamp01(rise + 0.10f));
    }
}