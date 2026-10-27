using UnityEngine;

/// <summary>
/// Procedural Perseverance-style Mars Rover, distilled from the user's Mars-Rover reference
/// (RoverModel.ts) down to what the current game actually shows. Purely AESTHETIC like
/// LunarRoverArt: every part is a decorative primitive with its collider removed, so the
/// physics (rigidbody, the same 4 WheelColliders) is untouched. The science arm, drill,
/// laser and calibration targets are dropped - they have no gameplay purpose here. What
/// remains reads instantly as a heavy six-wheeler prospector: the boxy avionics body with a
/// gold insulating underbelly, a remote-sensing mast with a camera head, dual hazard cameras
/// and searchlight headlights, the MMRTG generator at the tail, a high-gain antenna dish and
/// cleated machined-aluminum wheels. The wheel visuals sit on the same 4 axle positions as the
/// buggy so the existing physics and spin wiring is reused unchanged.
/// </summary>
public static class MarsRoverArt
{
    // ------------------------------------------------------------ skins
    /// <summary>White aerospace composite with gold thermal foil and NASA red markings.</summary>
    public static readonly LunarRoverArt.RoverSkin NasaClassic = new LunarRoverArt.RoverSkin()
    {
        id = "perseverance_classic",
        primary = new Color(0.902f, 0.902f, 0.914f),    // white composite body
        secondary = new Color(0.137f, 0.149f, 0.173f), // dark anodized fittings / RTG core
        foil = new Color(0.831f, 0.686f, 0.216f),      // gold Kapton underbelly + dish
        accent = new Color(0.839f, 0.157f, 0.157f),    // mission red lenses / hub caps
        wheel = new Color(0.169f, 0.180f, 0.208f),     // machined aluminum wheels
        wheelOverlay = new Color(0.620f, 0.650f, 0.700f),
        webbing = new Color(0.310f, 0.510f, 0.870f),   // solar-cell blue (deck skins)
        suit = new Color(0.900f, 0.900f, 0.900f),
        visor = new Color(0.902f, 0.722f, 0f),
        metalness = 0.75f,
        gloss = 0.6f,
    };

    /// <summary>Rust-red body caked in Martian regolith; the harvest-field alternative.</summary>
    public static readonly LunarRoverArt.RoverSkin MartianDust = new LunarRoverArt.RoverSkin()
    {
        id = "martian_dust",
        primary = new Color(0.635f, 0.325f, 0.224f),    // iron-oxide red body
        secondary = new Color(0.282f, 0.184f, 0.145f), // darkened fittings
        foil = new Color(0.541f, 0.353f, 0.212f),      // dust-choked gold foil
        accent = new Color(0.875f, 0.518f, 0.325f),    // weathered copper trim
        wheel = new Color(0.353f, 0.243f, 0.208f),     // muddy machined aluminum
        wheelOverlay = new Color(0.440f, 0.330f, 0.280f),
        webbing = new Color(0.310f, 0.510f, 0.870f),
        suit = new Color(0.900f, 0.900f, 0.900f),
        visor = new Color(0.902f, 0.722f, 0f),
        metalness = 0.5f,
        gloss = 0.3f,
    };

    /// <summary>Skin lookup by slot (0 = Nasa Classic, 1 = Martian Dust).</summary>
    public static LunarRoverArt.RoverSkin SkinOf(int index)
    {
        LunarRoverArt.RoverSkin[] skins = { NasaClassic, MartianDust };
        return skins[Mathf.Clamp(index, 0, skins.Length - 1)];
    }

    // ------------------------------------------------------------ materials
    /// <summary>Builds the model's material set. Lit = Standard (in-game), unlit = flat (Garage showroom).</summary>
    public static LunarRoverArt.RoverMats CreateMats(LunarRoverArt.RoverSkin skin, bool lit)
    {
        LunarRoverArt.RoverMats m = new LunarRoverArt.RoverMats();
        m.primary = lit ? Standard(skin, skin.primary, skin.metalness, skin.gloss) : Unlit(skin.primary);
        m.secondary = lit ? Standard(skin, skin.secondary, 0.6f, 0.5f) : Unlit(skin.secondary);
        m.foil = lit ? Standard(skin, skin.foil, 0.85f, 0.7f) : Unlit(skin.foil);
        m.accent = lit ? Standard(skin, skin.accent, 0.4f, 0.6f) : Unlit(skin.accent);
        m.wheel = lit ? Standard(skin, skin.wheel, 0.8f, 0.4f) : Unlit(skin.wheel);
        m.wheelOverlay = lit ? Standard(skin, skin.wheelOverlay, 0.9f, 0.3f) : Unlit(skin.wheelOverlay);
        m.webbing = lit ? Standard(skin, skin.webbing, 0.5f, 0.4f) : Unlit(skin.webbing);
        m.suit = lit ? Standard(skin, skin.suit, 0.1f, 0.35f) : Unlit(skin.suit);
        m.visor = lit ? Standard(skin, skin.visor, 0.95f, 0.9f) : Unlit(skin.visor);
        m.all.AddRange(new[] { m.primary, m.secondary, m.foil, m.accent, m.wheel, m.wheelOverlay, m.webbing, m.suit, m.visor });
        return m;
    }

    private static Material Standard(LunarRoverArt.RoverSkin skin, Color c, float metalness, float gloss)
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = c;
        mat.SetFloat("_Metallic", metalness);
        mat.SetFloat("_Glossiness", gloss);
        return mat;
    }

    private static Material Unlit(Color c)
    {
        Material mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = c;
        return mat;
    }

    // ------------------------------------------------------------ primitives
    private static Transform Part(PrimitiveType type, Transform parent, string name, Material mat, Vector3 scale, Vector3 pos, Vector3 rot)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        go.transform.localPosition = pos;
        if (rot != Vector3.zero) go.transform.localEulerAngles = rot;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        Collider col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        return go.transform;
    }

    private static Transform Box(Transform parent, string name, Material mat, Vector3 scale, Vector3 pos, Vector3 rot = default(Vector3))
        => Part(PrimitiveType.Cube, parent, name, mat, scale, pos, rot);

    // Unity cylinder: axis +Y, height 2, radius 0.5. Rotated to a chosen axis for structural tubes.
    private static Transform Cyl(Transform parent, string name, Material mat, float length, float radius, Vector3 pos, Vector3 rot)
        => Part(PrimitiveType.Cylinder, parent, name, mat, new Vector3(radius * 2f, length / 2f, radius * 2f), pos, rot);

    private static Transform Sphere(Transform parent, string name, Material mat, float r, Vector3 pos)
        => Part(PrimitiveType.Sphere, parent, name, mat, new Vector3(r * 2f, r * 2f, r * 2f), pos, Vector3.zero);

    private static Transform Cone(Transform parent, string name, Material mat, float r, float h, Vector3 pos, Vector3 rot)
        => Part(PrimitiveType.Cylinder, parent, name, mat, new Vector3(r * 2f, h / 2f, r * 2f), pos, rot);

    // ------------------------------------------------------------ static chassis / body
    /// <summary>
    /// Builds the boxy avionics body (warm electronics box with a gold insulating underbelly and
    /// a dark equipment deck), the remote-sensing mast + camera head, hazard camera lenses and
    /// dual searchlight headlights on the nose, the aft MMRTG generator with heat fins, the
    /// high-gain antenna dish and the low-gain UHF wand, plus the differential bar over the deck.
    /// Everything is a static child of <paramref name="parent"/>; wheel visuals are separate so
    /// the physics can spin them. Returns the chassis root handing off to the caller.
    /// </summary>
    public static Transform BuildChassis(Transform parent, LunarRoverArt.RoverMats m)
    {
        GameObject root = new GameObject("Rover_Chassis");
        root.transform.SetParent(parent, false);
        Transform chassis = root.transform;

        // Warm electronics box: the boxy white body that sits over (and clear of) the wheels.
        Box(chassis, "MRO_Body", m.primary, new Vector3(2.0f, 0.5f, 3.4f), new Vector3(0f, 0.92f, 0f));
        // Gold multi-layer insulation blanket on the underbelly.
        Box(chassis, "MRO_Foil", m.foil, new Vector3(1.95f, 0.14f, 3.3f), new Vector3(0f, 0.72f, 0f));
        // Dark top equipment deck.
        Box(chassis, "MRO_Deck", m.secondary, new Vector3(1.95f, 0.05f, 3.3f), new Vector3(0f, 1.17f, 0f));

        // Differential cross-bar over the deck linking the two rocker halves.
        Cyl(chassis, "MRO_DiffBar", m.secondary, 2.1f, 0.03f, new Vector3(0f, 1.28f, 0.35f), new Vector3(0f, 0f, 90f));

        BuildMast(chassis, m);
        BuildHeadlights(chassis, m);
        BuildRTG(chassis, m);
        BuildAntenna(chassis, m);

        return chassis;
    }

    // Remote-sensing mast: vertical column with a camera head (lens peering forward).
    private static void BuildMast(Transform chassis, LunarRoverArt.RoverMats m)
    {
        Transform mast = new GameObject("MRO_Mast").transform;
        mast.SetParent(chassis, false);
        mast.localPosition = new Vector3(0.55f, 1.17f, 0.95f);

        Cyl(mast, "MRO_MastColumn", m.primary, 0.85f, 0.05f, new Vector3(0f, 0.42f, 0f), Vector3.zero);
        Box(mast, "MRO_MastHead", m.primary, new Vector3(0.36f, 0.18f, 0.28f), new Vector3(0f, 0.87f, 0f));
        Cyl(mast, "MRO_MastLens", m.accent, 0.05f, 0.05f, new Vector3(0f, 0.84f, 0.17f), new Vector3(90f, 0f, 0f));
    }

    // Dual high-intensity searchlights + the stereo hazard-camera lenses on the nose.
    private static void BuildHeadlights(Transform chassis, LunarRoverArt.RoverMats m)
    {
        foreach (float lampX in new[] { -0.5f, 0.5f })
        {
            Box(chassis, "MRO_LampHousing", m.secondary, new Vector3(0.16f, 0.16f, 0.14f), new Vector3(lampX, 0.82f, 1.64f));
            Cyl(chassis, "MRO_LampBulb", m.accent, 0.05f, 0.065f, new Vector3(lampX, 0.82f, 1.72f), new Vector3(90f, 0f, 0f));
        }

        // Stereo hazard-camera eyes, low on the nose between the lamps.
        Cyl(chassis, "MRO_Hazcam", m.accent, 0.03f, 0.035f, new Vector3(-0.09f, 0.66f, 1.75f), new Vector3(90f, 0f, 0f));
        Cyl(chassis, "MRO_Hazcam", m.accent, 0.03f, 0.035f, new Vector3(0.09f, 0.66f, 1.75f), new Vector3(90f, 0f, 0f));
    }

    // MMRTG generator slung off the tail: core cylinder + four radial heat-dissipation fins.
    private static void BuildRTG(Transform chassis, LunarRoverArt.RoverMats m)
    {
        Transform rtg = new GameObject("MRO_RTG").transform;
        rtg.SetParent(chassis, false);
        rtg.localPosition = new Vector3(0f, 0.55f, -1.95f);

        Cyl(rtg, "MRO_RTG_Core", m.secondary, 1.1f, 0.22f, Vector3.zero, new Vector3(90f, 0f, 0f));
        for (int i = 0; i < 4; i++)
        {
            Box(rtg, "MRO_RTG_Fin", m.wheel, new Vector3(0.02f, 0.46f, 0.4f), Vector3.zero, new Vector3(0f, 0f, i * 45f));
        }
    }

    // High-gain parabolic dish on a gimbal base + the low-gain UHF whip on the far corner.
    private static void BuildAntenna(Transform chassis, LunarRoverArt.RoverMats m)
    {
        Transform dish = new GameObject("MRO_Dish").transform;
        dish.SetParent(chassis, false);
        dish.localPosition = new Vector3(0.9f, 1.22f, -0.45f);

        Cyl(dish, "MRO_DishBase", m.secondary, 0.1f, 0.055f, new Vector3(0f, 0.05f, 0f), Vector3.zero);
        Cone(dish, "MRO_Dish", m.foil, 0.26f, 0.16f, new Vector3(0f, 0.34f, 0f), new Vector3(-24f, 22f, 0f));
        Cyl(dish, "MRO_FeedHorn", m.accent, 0.28f, 0.025f, new Vector3(0f, 0.30f, 0.18f), new Vector3(90f, 0f, 0f));

        Cyl(chassis, "MRO_UHF", m.primary, 0.55f, 0.03f, new Vector3(-1.0f, 1.22f, -0.45f), Vector3.zero);
        Sphere(chassis, "MRO_UHF_Tip", m.accent, 0.04f, new Vector3(-1.0f, 1.5f, -0.45f));
    }

    // ------------------------------------------------------------ spinning wheel
    /// <summary>
    /// Builds one Mars Rover wheel visual: a machined-aluminum barrel with 8 grouser cleats
    /// around the rim, a hub, the inboard traction-drive motor and an outboard hub cap.
    /// Axle = X so BuggyController's spin (rotation about local X) rolls it correctly.
    /// Returns the transform that should be handed to the buggy's wheel-visual references.
    /// </summary>
    public static Transform BuildWheelVisual(Transform parent, Vector3 localPos, LunarRoverArt.RoverMats m, float radius, float width)
    {
        GameObject go = new GameObject("RoverWheelVisual");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        // Machined aluminum barrel (axle along X).
        Cyl(go.transform, "MRO_Wheel", m.wheel, width, radius, Vector3.zero, new Vector3(0f, 0f, 90f));
        // Cleat bars around the rim, each pointing outward along the wheel's radius.
        const int cleatCount = 8;
        for (int i = 0; i < cleatCount; i++)
        {
            float a = (i / (float)cleatCount) * 2f * Mathf.PI;
            float r = radius + 0.02f;
            Box(go.transform, "MRO_Cleat", m.wheel, new Vector3(width * 0.6f, 0.05f, 0.055f),
                new Vector3(0f, r * Mathf.Cos(a), r * Mathf.Sin(a)),
                new Vector3(a * Mathf.Rad2Deg, 0f, 0f));
        }

        // Inner hub, inboard traction drive motor (the cleats read over it) and outboard cap.
        Cyl(go.transform, "MRO_Hub", m.primary, width * 0.55f, radius * 0.4f, Vector3.zero, new Vector3(0f, 0f, 90f));
        Cyl(go.transform, "MRO_Motor", m.foil, 0.14f, 0.13f, new Vector3(-0.16f, 0f, 0f), new Vector3(0f, 0f, 90f));
        Cyl(go.transform, "MRO_HubCap", m.accent, 0.05f, radius * 0.16f, new Vector3(width * 0.5f + 0.03f, 0f, 0f), new Vector3(0f, 0f, 90f));

        return go.transform;
    }
}