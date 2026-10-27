using UnityEngine;

/// <summary>
/// Asks for the world's misty cloud sky. Safe to call before the camera exists: the request is
/// parked on a <see cref="MistySkyDome"/> that builds itself on the following frame, so it does
/// not matter whether the sky or the camera is built first.
/// </summary>
public static class MistySky
{
    /// <summary>Parks a cloud dome for <paramref name="def"/>. No-op unless the world asks for one.</summary>
    public static void Build(LevelDefinition def)
    {
        if (def == null || !def.mistySky) return;
        if (GameObject.Find("MistySkyDome") != null) return; // already built for this scene

        GameObject go = new GameObject("MistySkyDome");
        go.AddComponent<MistySkyDome>().Configure(def);
    }
}

/// <summary>
/// Misty cloud sky for the worlds that wear a thick atmosphere instead of a black night sky.
/// Generates a soft value-noise cloud at run time and drapes it over an inverted sphere parented
/// to the camera, so the sky always sits behind the terrain however far the rover drives. The dome
/// is Unlit, so the level's distance fog cannot wash it out; its lower edge is blended into the
/// planet's own fog colour so the terrain's haze dissolves into the cloud rather than ending on a
/// hard horizon line.
/// </summary>
public class MistySkyDome : MonoBehaviour
{
    // Dome diameter in world units. Comfortably inside the camera's 6500m far plane and far wider
    // than any terrain the rover can reach, so the horizon is always cloud, never geometry.
    private const float DomeDiameter = 4000f;
    private const int TextureSize = 512;

    private LevelDefinition def;

    /// <summary>Hands over the world whose atmosphere this dome is dressing.</summary>
    public void Configure(LevelDefinition d)
    {
        def = d;
    }

    private void Start()
    {
        // Deferred to here on purpose: BuildSkyAndLighting runs before BuildCamera in the Survival
        // path, so a camera looked up on the previous line can still be null.
        Camera cam = Camera.main;
        if (def == null || cam == null)
        {
            Debug.LogWarning("MistySkyDome: no level data or no MainCamera; sky left as-is. def="
                             + (def == null ? "null" : def.planetName) + " cam=" + (cam == null ? "null" : cam.name));
            Destroy(gameObject);
            return;
        }

        // Floor under the dome: paint the camera's own clear colour pale too, so a failure to draw
        // the dome can never leave the player staring at a black sky.
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = def.cloudColor;

        Texture2D cloud = GenerateCloud(def.cloudColor, def.fogColor, def.cloudHazeFalloff,
                                        def.cloudDensity, LevelManager.CurrentLevelIndex);

        // Mirrored on X so the sphere's inward-facing side is the side that renders: a negative
        // scale flips the triangle winding outright, which is more dependable than relying on a
        // built-in shader exposing _Cull.
        transform.SetParent(cam.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = new Vector3(-DomeDiameter, DomeDiameter, DomeDiameter);

        MeshFilter mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildSphereMesh();

        MeshRenderer mr = gameObject.AddComponent<MeshRenderer>();
        // Unlit/Texture is force-included in the WebGL build for the horizon backdrops, so the
        // dome needs no build-script change to survive shader stripping.
        Material mat = new Material(Shader.Find("Unlit/Texture"));
        mat.mainTexture = cloud;
        // Belt and braces with the mirrored scale: if the shader does expose _Cull, draw both
        // faces so the dome cannot vanish behind a winding assumption.
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // One line of self-diagnosis: if the sky is still black, this says which piece is at fault.
        Debug.Log("MistySkyDome: " + def.planetName
                  + " cam=" + cam.name + " far=" + cam.farClipPlane
                  + " shader=" + (mat.shader != null ? mat.shader.name : "NULL")
                  + " tex=" + cloud.width + "x" + cloud.height
                  + " topPixel=" + cloud.GetPixel(cloud.width / 2, cloud.height - 2)
                  + " boundsR=" + mr.bounds.extents.magnitude
                  + " inFrustum=" + GeometryUtility.TestPlanesAABB(
                      GeometryUtility.CalculateFrustumPlanes(cam), mr.bounds));
    }

    /// <summary>
    /// Unity's built-in sphere primitive, built here rather than via CreatePrimitive so the dome
    /// carries no collider and no default Standard material to clean up.
    /// </summary>
    private static Mesh BuildSphereMesh()
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temp);
        return mesh;
    }

    /// <summary>
    /// Builds the cloud texture: four octaves of tiling value noise, lifted off the planet's haze
    /// colour towards the top of the dome and shaded into billows by the noise itself.
    /// </summary>
    private static Texture2D GenerateCloud(Color cloud, Color haze, float falloff, float density, int seed)
    {
        Texture2D tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.name = "MistyCloud";

        // Each octave keeps its own random lattice sized to its period, so every layer wraps on its
        // own and the finished texture tiles seamlessly around the dome's longitude.
        int[] periods = { 4, 8, 16, 32 };
        float[] amps = { 1f, 0.5f, 0.25f, 0.125f };
        float[][] grids = new float[periods.Length][];
        float ampSum = 0f;
        System.Random rng = new System.Random(1000 + seed);
        for (int o = 0; o < periods.Length; o++)
        {
            int p = periods[o];
            float[] g = new float[p * p];
            for (int i = 0; i < g.Length; i++) g[i] = (float)rng.NextDouble();
            grids[o] = g;
            ampSum += amps[o];
        }

        Color[] px = new Color[TextureSize * TextureSize];
        // Cloud density fades the shading out entirely, leaving a flat haze at 0.
        float shadeLow = 1f - 0.22f * density;
        float shadeHigh = 1f + 0.18f * density;

        for (int y = 0; y < TextureSize; y++)
        {
            // v = 0 is the dome's bottom pole, i.e. the horizon the terrain fades into.
            float v = (y + 0.5f) / TextureSize;
            float lift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(v / Mathf.Max(0.01f, falloff)));
            Color baseCol = Color.Lerp(haze, cloud, lift);

            for (int x = 0; x < TextureSize; x++)
            {
                float u = (x + 0.5f) / TextureSize;
                float n = 0f;
                for (int o = 0; o < periods.Length; o++)
                {
                    n += amps[o] * SampleLattice(grids[o], periods[o], u, v);
                }
                n = Mathf.Clamp01(n / ampSum);

                float shade = Mathf.Lerp(shadeLow, shadeHigh, Mathf.SmoothStep(0f, 1f, n));
                px[y * TextureSize + x] = new Color(baseCol.r * shade, baseCol.g * shade, baseCol.b * shade, 1f);
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>Bilinearly samples a periodic lattice, smoothing the interpolants so the grid never shows as blocks.</summary>
    private static float SampleLattice(float[] grid, int p, float u, float v)
    {
        float fx = u * p, fy = v * p;
        int x0 = Mathf.FloorToInt(fx) % p, y0 = Mathf.FloorToInt(fy) % p;
        int x1 = (x0 + 1) % p, y1 = (y0 + 1) % p;
        float tx = fx - Mathf.Floor(fx), ty = fy - Mathf.Floor(fy);
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);

        float top = Mathf.Lerp(grid[y0 * p + x0], grid[y0 * p + x1], tx);
        float bottom = Mathf.Lerp(grid[y1 * p + x0], grid[y1 * p + x1], tx);
        return Mathf.Lerp(top, bottom, ty);
    }
}
