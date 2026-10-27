using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a terrain-CONFORMING, feathered, procedurally textured decal for hazard patches -
/// lava flows, methane lakes and Titan mud bogs. Instead of a flat cylinder that floats on
/// slopes with a hard rim, the decal is a height-sampled mesh that moulds into the ground,
/// with a soft noise-baked alpha edge so its rim melts into the surrounding terrain. A small
/// procedural texture is generated per patch so every hazard has organic blotches rather than
/// a textureless flat disc. The material is returned so callers can animate it (e.g. lava).
/// </summary>
public static class TerrainDecal
{
    public enum Style { Mud, Ice, Lava }

    public const int DefaultLift = 5; // 0.05 of a metre, matches the old cylinders

    /// <summary>Wholly static build; call at scene setup. Returns the decal GameObject and
    /// hands back the shared material via out for any runtime animation.</summary>
    public static GameObject Build(MoonTerrainGen terrain, Vector2 centerXZ, float halfX, float halfZ,
        float lift, Color tint, Color glow, Style style, bool round, out Material material,
        bool magma = false, bool crystal = false)
    {
        material = null;
        if (terrain == null) return null;

        // ---- height-conforming mesh -------------------------------------------------------
        float gridStep = 1.6f;
        int segX = Mathf.Clamp(Mathf.CeilToInt(halfX * 2f / gridStep), 2, 26);
        int segZ = Mathf.Clamp(Mathf.CeilToInt(halfZ * 2f / gridStep), 2, 26);
        int cols = segX + 1;
        int rows = segZ + 1;

        Vector3[] verts = new Vector3[cols * rows];
        Vector3[] normals = new Vector3[cols * rows];
        Vector2[] uv = new Vector2[cols * rows];
        Color[] vcol = new Color[cols * rows];
        int[] tris = new int[segX * segZ * 6];

        float minX = centerXZ.x - halfX;
        float maxX = centerXZ.x + halfX;
        float minZ = centerXZ.y - halfZ;
        float maxZ = centerXZ.y + halfZ;

        for (int r = 0; r < rows; r++)
        {
            float z = minZ + (maxZ - minZ) * r / segZ;
            for (int c = 0; c < cols; c++)
            {
                float x = minX + (maxX - minX) * c / segX;
                int i = r * cols + c;
                Vector3 p = new Vector3(x, 0f, z);
                float h = terrain.SampleHeight(p);
                verts[i] = new Vector3(x, h + lift, z);
                uv[i] = new Vector2(c / (float)segX, r / (float)segZ);

                // central-difference normal in local metres so the decal faces the slopes.
                float hX = terrain.SampleHeight(new Vector3(Mathf.Min(x + gridStep, maxX), 0f, z));
                float hZ = terrain.SampleHeight(new Vector3(x, 0f, Mathf.Min(z + gridStep, maxZ)));
                Vector3 east = new Vector3(x + gridStep, hX + lift, z) - verts[i];
                Vector3 north = new Vector3(x, hZ + lift, z + gridStep) - verts[i];
                normals[i] = Vector3.Cross(east, north).normalized;
                if (normals[i].y < 0f) normals[i] = -normals[i];

                float nz = ValueNoise(x * 0.28f, z * 0.28f, 6f);
                vcol[i] = Color.white * (style == Style.Lava ? 1f : 1f - 0.05f + nz * 0.1f);
            }
        }

        int t = 0;
        for (int r = 0; r < segZ; r++)
        {
            for (int c = 0; c < segX; c++)
            {
                int i0 = r * cols + c;
                int i1 = i0 + 1;
                int i2 = i0 + cols;
                int i3 = i2 + 1;
                tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
            }
        }

        GameObject go = new GameObject("HazardDecal");
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        Mesh mesh = new Mesh { name = "HazardPatch" };
        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.colors = vcol;
        mesh.normals = normals;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;
        mr.shadowCastingMode = ShadowCastingMode.Off;

        // ---- procedural texture with feathered rim + organic blotches ----------------------
        Texture2D tex = BuildTexture(style, tint, glow, round, magma, crystal);
        material = MakeMaterial(style, tint, glow, tex, magma, crystal);

        mr.sharedMaterial = material;
        return go;
    }

    private static Material MakeMaterial(Style style, Color tint, Color glow, Texture2D tex, bool magma, bool crystal)
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.SetTexture("_MainTex", tex);
        mat.color = new Color(tint.r, tint.g, tint.b, 1f);

        if (style == Style.Lava)
        {
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Glossiness", 0.25f);
        }
        else if (style == Style.Ice)
        {
            mat.SetFloat("_Metallic", 0.05f);
            mat.SetFloat("_Glossiness", 0.92f);
            if (crystal)
            {
                // Frozen slick patch: the crystallised concrete swatch's relief, while the
                // feathered albedo/alpha (built with the swatch baked in - see BuildTexture)
                // keeps the patch melting into the ground.
                Texture2D n = Yfcm.Normal("T_YFCM_Crystallized");
                if (n != null)
                {
                    mat.EnableKeyword("_NORMALMAP");
                    mat.SetTexture("_BumpMap", n);
                    mat.SetTextureScale("_BumpMap", Yfcm.DecalTiling);
                }
            }
        }
        else // Mud: flat, matte, dry-crumbly.
        {
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Glossiness", 0.06f);
        }

        if (style != Style.Mud)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(glow.r, glow.g, glow.b, 1f));
            if (style == Style.Lava)
            {
                // Hot streaks ride a SEPARATE repeat tile (scrolls cleanly without dragging the
                // feathered rim around); the frozen lakes keep the static blotched emission.
                if (magma && LavaArt.Available)
                {
                    // Real magma swatch: the emissive seam map drives the glow and the normal
                    // adds crust relief, while the decal keeps its own feathered albedo/alpha
                    // (built with the swatch's colour baked in - see BuildTexture).
                    LavaArt.Dress(mat, LavaArt.DecalAlbedoTiling, new Color(1.4f, 1f, 0.55f));
                }
                else
                {
                    mat.SetTexture("_EmissionMap", BuildLavaGlowTile());
                }
            }
            else
            {
                mat.SetTexture("_EmissionMap", tex);
            }
        }

        // Transparent so the feathered rim shows terrain under the soft edge.
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = 3000;
        return mat;
    }

    private static Texture2D BuildTexture(Style style, Color tint, Color glow, bool round, bool magma, bool crystal)
    {
        const int TEX = 256;
        Color[] px = new Color[TEX * TEX];

        for (int y = 0; y < TEX; y++)
        {
            for (int x = 0; x < TEX; x++)
            {
                float u = (x + 0.5f) / TEX;
                float v = (y + 0.5f) / TEX;

                float alpha = EdgeAlpha(u, v, round);

                if (style == Style.Lava)
                {
                    // Elongated flow streaks: noise squeezed along the band + a wandering lava tongue.
                    float flow = ValueNoise(u * 14f, v * 3.2f, 4f);
                    float tongue = ValueNoise(u * 5f + Mathf.Sin(v * 3.1f) * 0.35f, v * 4f, 3f);
                    float bright = Mathf.Clamp01(0.35f + 0.9f * Mathf.Pow(flow, 3f) + 0.35f * Mathf.SmoothStep(0.55f, 1f, tongue));
                    Color baseC = Color.Lerp(new Color(0.42f, 0.09f, 0.03f), new Color(0.95f, 0.28f, 0.06f), bright);
                    if (magma)
                    {
                        // Bake the real magma albedo (dark crust, faint hot seams) into the
                        // feathered patch, eased toward the hot procedural streaks so the flow
                        // still reads as moving lava rather than a static photo tile.
                        Color s = LavaArt.AlbedoAt(u, v, baseC);
                        baseC = Color.Lerp(s, baseC, 0.25f);
                    }
                    px[y * TEX + x] = new Color(baseC.r, baseC.g, baseC.b, alpha);
                }
                else if (style == Style.Ice)
                {
                    float n = ValueNoise(u * 10f, v * 10f, 5f);
                    float crack = ValueNoise(u * 22f, v * 22f, 3f);
                    float a = 0.55f + 0.45f * n;
                    float r = Mathf.Lerp(0.34f, 0.58f, a) - 0.1f * Mathf.SmoothStep(0.78f, 1f, crack);
                    float g = Mathf.Lerp(0.55f, 0.76f, a) - 0.05f * Mathf.SmoothStep(0.78f, 1f, crack);
                    float bl = Mathf.Lerp(0.72f, 0.9f, a) + 0.08f * Mathf.SmoothStep(0.78f, 1f, crack);
                    if (crystal)
                    {
                        // Bake the crystallised concrete swatch under the icy sheen so the frozen
                        // patch reads as fracturing mineral rather than flat glass (the swatch
                        // normal rides on top - see MakeMaterial).
                        Color proc = new Color(r, g, bl);
                        Color s = Yfcm.SampleDiffuse("T_YFCM_Crystallized", u, v, proc);
                        r = Mathf.Lerp(s.r, r, 0.6f);
                        g = Mathf.Lerp(s.g, g, 0.6f);
                        bl = Mathf.Lerp(s.b, bl, 0.6f);
                    }
                    px[y * TEX + x] = new Color(r, g, bl, alpha);
                }
                else // Mud: a smudged, low-frequency darker-brown slurry.
                {
                    // The texture carries ONLY a soft luminance modulation - no baked colour - so
                    // the caller's tint sets the brown outright, instead of the two multiplying
                    // down to a near-black blob (which is why the flats vanished into shadow).
                    // Both octaves are LOW frequency, so the blotches read as smeared slurry
                    // rather than the old sharp speckle.
                    float broad = ValueNoise(u, v, 2.2f);
                    float mid = ValueNoise(u, v, 5.0f);
                    float shade = Mathf.Lerp(0.60f, 1f, broad * 0.65f + mid * 0.35f);
                    shade *= 1f - 0.08f * Mathf.SmoothStep(0.55f, 1f, mid); // faint damp smears
                    // A whisper of warmth so it stays brown even under a near-white tint.
                    px[y * TEX + x] = new Color(shade, shade * 0.97f, shade * 0.92f, alpha);
                }
            }
        }

        Texture2D tex = new Texture2D(TEX, TEX, TextureFormat.RGBA32, false);
        tex.name = style.ToString() + "Decal";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.anisoLevel = 1;
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>Repeatable emissive streaks for the lava flow, tiled so the director can scroll
    /// them along the band; the alpha/rim feathering stays on the main texture.</summary>
    private static Texture2D BuildLavaGlowTile()
    {
        const int TEX = 128;
        Color[] px = new Color[TEX * TEX];
        for (int y = 0; y < TEX; y++)
        {
            float v = y / (float)TEX;
            for (int x = 0; x < TEX; x++)
            {
                float u = x / (float)TEX;
                float flow = ValueNoise(u * 18f, v * 3.4f, 4f);
                float tongue = ValueNoise(u * 6f + Mathf.Sin(v * 3.1f) * 0.4f, v * 4f, 3f);
                float bright = Mathf.Clamp01(0.18f + 1.5f * Mathf.Pow(flow, 4f) + 0.6f * Mathf.SmoothStep(0.6f, 1f, tongue));
                px[y * TEX + x] = new Color(bright, bright * 0.72f, bright * 0.25f, 1f);
            }
        }
        Texture2D tex = new Texture2D(TEX, TEX, TextureFormat.RGBA32, false);
        tex.name = "LavaGlowStreaks";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.anisoLevel = 4;
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>Soft 1->0 falloff towards the rim: 0.8 of the map is solid, the outer band
    /// feathers smoothly out so the patch melts into the ground. Round patches get a disc
    /// mask, lanes a rounded-rectangle (bands stay solid across the course for the full width).</summary>
    private static float EdgeAlpha(float u, float v, bool round)
    {
        float du = Mathf.Abs(u - 0.5f) * 2f;
        float dv = Mathf.Abs(v - 0.5f) * 2f;
        float edge = round
            ? Mathf.Sqrt(du * du + dv * dv)
            : Mathf.Max(du, dv);
        // 0.8 solid -> 1.0 gone: an 0.22-wide feather band on each rim (local edge ~2-3m).
        float f = Mathf.SmoothStep(0.8f, 1.0f, edge);
        return 1f - Mathf.Clamp01(f);
    }

    /// <summary>Cheap grid value-noise (blotches), smooth via bilinear interpolation between
    /// random cells so patches look organic rather than pixelated.</summary>
    private static float ValueNoise(float x, float y, float scale)
    {
        float fx = x * scale;
        float fy = y * scale;
        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        float tx = fx - x0;
        float ty = fy - y0;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);

        float v00 = Hash(x0, y0);
        float v10 = Hash(x0 + 1, y0);
        float v01 = Hash(x0, y0 + 1);
        float v11 = Hash(x0 + 1, y0 + 1);
        float v0 = Mathf.Lerp(v00, v10, tx);
        float v1 = Mathf.Lerp(v01, v11, tx);
        return Mathf.Lerp(v0, v1, ty);
    }

    private static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263) ^ 0x9E3779B9u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h = h ^ (h >> 16);
        return (h & 0xFFFF) / 65535f;
    }
}