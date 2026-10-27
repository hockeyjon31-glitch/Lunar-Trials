using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural Apollo Lunar Roving Vehicle, built from primitives at runtime to match the
/// NASA reference imagery: lattice chassis with a wire-mesh floor, four wire-mesh wheels
/// with chevron tread and hubcaps, burnt-orange fiberglass fenders with mud flaps, tan
/// fabric seats, the gold high-gain umbrella antenna with ribbed underside, the low-gain
/// antenna, TV camera, tool palette with geology brushes and gold thermal foil bays.
/// Purely AESTHETIC: every part is a decorative primitive with its collider removed, so
/// the physics (rigidbody, WheelColliders, pickup trigger) is untouched. The same builder
/// feeds the in-game buggy (Standard materials, lit by the level) and the Garage showroom
/// (Unlit materials, lit by nothing).
///
/// The skins are kept as presets so the shop can swap the look without touching the model.
/// </summary>
public static class LunarRoverArt
{
    // ------------------------------------------------------------ skins
    public struct RoverSkin
    {
        public string id;
        public Color primary;
        public Color secondary;
        public Color foil;
        public Color accent;
        public Color wheel;
        public Color wheelOverlay;
        public Color webbing;
        public Color suit;
        public Color visor;
        public Color fender;
        public float metalness;
        public float gloss;
    }

    public static readonly RoverSkin Historic = new RoverSkin()
    {
        id = "apollo_historic",
        primary = new Color(0.847f, 0.867f, 0.894f),    // spacecraft aluminum
        secondary = new Color(0.227f, 0.247f, 0.278f), // frame brackets / dark fittings
        foil = new Color(0.831f, 0.686f, 0.216f),      // gold Kapton / mylar
        accent = new Color(0.788f, 0.165f, 0.165f),    // NASA red
        wheel = new Color(0.604f, 0.635f, 0.675f),     // galvanized wire mesh
        wheelOverlay = new Color(0.710f, 0.737f, 0.776f),
        webbing = new Color(0.71f, 0.58f, 0.40f),      // beige LRV seat fabric
        suit = new Color(0.933f, 0.933f, 0.933f),
        visor = new Color(0.902f, 0.722f, 0f),
        fender = new Color(0.82f, 0.44f, 0.16f),       // burnt-orange fiberglass fenders
        metalness = 0.8f,
        gloss = 0.62f,
    };

    public static readonly RoverSkin ArtemisGold = new RoverSkin()
    {
        id = "artemis_gold",
        primary = new Color(1f, 1f, 1f),
        secondary = new Color(0.122f, 0.137f, 0.161f),
        foil = new Color(0.898f, 0.663f, 0.231f),
        accent = new Color(0.01f, 0.518f, 0.780f),
        wheel = new Color(0.631f, 0.659f, 0.710f),
        wheelOverlay = new Color(0.73f, 0.75f, 0.8f),
        webbing = new Color(0.71f, 0.58f, 0.40f),
        suit = new Color(0.933f, 0.933f, 0.933f),
        visor = new Color(0.902f, 0.722f, 0f),
        fender = new Color(0.82f, 0.44f, 0.16f),
        metalness = 0.85f,
        gloss = 0.75f,
    };

    public static readonly RoverSkin LunarCarbon = new RoverSkin()
    {
        id = "lunar_carbon",
        primary = new Color(0.102f, 0.110f, 0.125f),
        secondary = new Color(0.067f, 0.075f, 0.082f),
        foil = new Color(0.180f, 0.196f, 0.231f),
        accent = new Color(0.976f, 0.451f, 0.086f),
        wheel = new Color(0.278f, 0.298f, 0.333f),
        wheelOverlay = new Color(0.42f, 0.45f, 0.5f),
        webbing = new Color(0.16f, 0.20f, 0.24f),
        suit = new Color(0.933f, 0.933f, 0.933f),
        visor = new Color(0.902f, 0.722f, 0f),
        fender = new Color(0.15f, 0.16f, 0.18f),
        metalness = 0.75f,
        gloss = 0.5f,
    };

    /// <summary>The Mars Rover variant paint: rusted frame, olive radiator foil and hazard trim.
    /// A stand-in skin for the vehicle in shop + world until the variant earns its own mesh.</summary>
    public static readonly RoverSkin MarsRed = new RoverSkin()
    {
        id = "mars_rover",
        primary = new Color(0.72f, 0.33f, 0.18f),      // rusted Martian aluminum
        secondary = new Color(0.16f, 0.10f, 0.08f),    // burnt fittings
        foil = new Color(0.44f, 0.58f, 0.42f),         // olive radiator foil
        accent = new Color(0.95f, 0.72f, 0.30f),       // hazard yellow trim
        wheel = new Color(0.34f, 0.16f, 0.12f),        // baked-mud wire mesh
        wheelOverlay = new Color(0.45f, 0.22f, 0.16f),
        webbing = new Color(0.42f, 0.26f, 0.18f),
        suit = new Color(0.85f, 0.80f, 0.72f),
        visor = new Color(0.10f, 0.45f, 0.85f),
        fender = new Color(0.50f, 0.22f, 0.14f),
        metalness = 0.7f,
        gloss = 0.4f,
    };

    /// <summary>The Favonius's paint: bare titanium pressure hardware, matte carbon structure and a
    /// cold cyan instrument accent. Deliberately the least "painted" of the four - this is a
    /// machine that lives inside a furnace, so the finish is functional, not decorative.</summary>
    public static readonly RoverSkin FavoniusTitanium = new RoverSkin()
    {
        id = "favonius_titanium",
        primary = new Color(0.62f, 0.64f, 0.67f),        // Ti-6Al-4V, sandblasted
        secondary = new Color(0.13f, 0.14f, 0.16f),     // carbon-fibre chassis tube
        foil = new Color(0.78f, 0.80f, 0.83f),          // ceramic aerogel blanket, high-albedo
        accent = new Color(0.16f, 0.62f, 0.78f),         // cyan instrument + sail trim markings
        wheel = new Color(0.20f, 0.21f, 0.23f),         // spring-mesh, basalt-dusted
        wheelOverlay = new Color(0.34f, 0.36f, 0.40f),
        webbing = new Color(0.16f, 0.20f, 0.24f),
        suit = new Color(0.88f, 0.90f, 0.92f),
        visor = new Color(0.20f, 0.68f, 0.85f),         // sapphire spectrometer window
        fender = new Color(0.50f, 0.26f, 0.11f),
        metalness = 0.9f,
        gloss = 0.68f,
    };

    /// <summary>Skin lookup by slot (0 = Historic, 1 = Artemis, 2 = Carbon, 3 = Mars Red,
    /// 4 = Favonius titanium).</summary>
    public static RoverSkin SkinOf(int index)
    {
        RoverSkin[] skins = { Historic, ArtemisGold, LunarCarbon, MarsRed, FavoniusTitanium };
        return skins[Mathf.Clamp(index, 0, skins.Length - 1)];
    }

    // ------------------------------------------------------------ materials
    public class RoverMats
    {
        public Material primary;
        public Material secondary;
        public Material foil;
        public Material accent;
        public Material wheel;
        public Material wheelOverlay;
        public Material webbing;
        public Material suit;
        public Material visor;
        public Material fender;

        public List<Material> all = new List<Material>();
    }

    private static Material Standard(RoverSkin skin, Color c, float metalness, float gloss)
    {
        Material m = new Material(Shader.Find("Standard"));
        m.color = c;
        m.SetFloat("_Metallic", metalness);
        m.SetFloat("_Glossiness", gloss);
        return m;
    }

    private static Material Unlit(Color c)
    {
        Material m = new Material(Shader.Find("Unlit/Color"));
        m.color = c;
        return m;
    }

    /// <summary>Builds the model's material set. Lit = Standard (in-game), unlit = flat (Garage showroom).</summary>
    public static RoverMats CreateMats(RoverSkin skin, bool lit)
    {
        RoverMats m = new RoverMats();
        // In-game the hull and the structural tubes wear the Yughues metal 37 (weathered
        // steel) so the frame reads as real metal; the gold foil bay, accent stripe, wheels,
        // fenders and crew keep their skin colours. The flat Garage showroom keeps the
        // skin's own aluminium / accent colours (unlit) so the paint identifies each skin.
        m.primary = lit ? YFMetal.New(37) : Unlit(skin.primary);
        m.secondary = lit ? YFMetal.New(37) : Unlit(skin.secondary);
        m.foil = lit ? Standard(skin, skin.foil, 0.85f, 0.7f) : Unlit(skin.foil);
        m.accent = lit ? Standard(skin, skin.accent, 0.4f, 0.6f) : Unlit(skin.accent);
        m.wheel = lit ? Standard(skin, skin.wheel, 0.75f, 0.35f) : Unlit(skin.wheel);
        m.wheelOverlay = lit ? Standard(skin, skin.wheelOverlay, 0.9f, 0.25f) : Unlit(skin.wheelOverlay);
        m.webbing = lit ? Standard(skin, skin.webbing, 0.1f, 0.25f) : Unlit(skin.webbing);
        m.suit = lit ? Standard(skin, skin.suit, 0.1f, 0.35f) : Unlit(skin.suit);
        m.visor = lit ? Standard(skin, skin.visor, 0.95f, 0.9f) : Unlit(skin.visor);
        m.fender = lit ? Standard(skin, skin.fender, 0.55f, 0.42f) : Unlit(skin.fender);
        m.all.AddRange(new[] { m.primary, m.secondary, m.foil, m.accent, m.wheel, m.wheelOverlay, m.webbing, m.suit, m.visor, m.fender });
        return m;
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

    /// <summary>Deep child search by name.</summary>
    public static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name) return child;
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Seated rider figure (suited driver with gold-visor helmet): pelvis, torso
    /// leaning forward, PLSS backpack, arms reaching to the controls, legs either forward
    /// onto a footboard (buggy bucket) or astride the machine (bike). Attached relative to
    /// the given seat node; returns the rider root.</summary>
    public static Transform AttachRider(Transform seat, RoverMats m, Vector3 seatLocalPos,
        float leanForwardDeg, bool astride)
    {
        Transform rider = new GameObject("Rider").transform;
        rider.SetParent(seat, false);
        rider.localPosition = seatLocalPos;
        float lean = leanForwardDeg * Mathf.Deg2Rad;

        Box(rider, "Rider_Pelvis", m.suit, new Vector3(0.34f, 0.16f, 0.3f), new Vector3(0f, 0.06f, 0f));
        Transform torso = Box(rider, "Rider_Torso", m.suit, new Vector3(0.36f, 0.46f, 0.28f),
            new Vector3(0f, 0.34f, -0.04f), new Vector3(lean * Mathf.Rad2Deg, 0f, 0f));
        Box(rider, "Rider_PLSS", m.primary, new Vector3(0.3f, 0.4f, 0.16f),
            new Vector3(0f, 0.36f, -0.16f), new Vector3(lean * Mathf.Rad2Deg, 0f, 0f));

        Sphere(rider, "Rider_Helmet", m.suit, 0.15f, new Vector3(0f, 0.57f, lean * 0.25f - 0.02f));
        Sphere(rider, "Rider_Visor", m.visor, 0.11f, new Vector3(0f, 0.56f, lean * 0.25f + 0.07f));

        foreach (float sx in new[] { -0.2f, 0.2f })
        {
            Vector3 shoulder = new Vector3(sx, 0.46f, -0.04f);
            Vector3 hand = new Vector3(sx * 0.65f, 0.16f, 0.32f);
            Limb(rider, "Rider_Arm", m.suit, shoulder, hand, 0.09f);
        }

        foreach (float lx in new[] { -0.13f, 0.13f })
        {
            if (astride)
            {
                Vector3 hip = new Vector3(lx, -0.02f, 0.02f);
                Vector3 knee = new Vector3(lx * 1.7f, -0.14f, 0.26f);
                Vector3 foot = new Vector3(lx * 1.5f, -0.42f, 0.1f);
                Limb(rider, "Rider_Thigh", m.suit, hip, knee, 0.14f);
                Limb(rider, "Rider_Shin", m.suit, knee, foot, 0.12f);
            }
            else
            {
                Vector3 hip = new Vector3(lx, -0.02f, 0.04f);
                Vector3 knee = new Vector3(lx, 0.0f, 0.34f);
                Vector3 foot = new Vector3(lx, -0.34f, 0.3f);
                Limb(rider, "Rider_Thigh", m.suit, hip, knee, 0.15f);
                Limb(rider, "Rider_Shin", m.suit, knee, foot, 0.13f);
            }
        }
        return rider;
    }

    private static Transform Limb(Transform parent, string name, Material mat, Vector3 a, Vector3 b, float thickness)
    {
        Vector3 mid = (a + b) * 0.5f;
        Vector3 dir = b - a;
        float len = dir.magnitude;
        Transform t = Box(parent, name, mat, new Vector3(thickness, len, thickness), mid, Vector3.zero);
        if (len > 0.001f)
            t.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        return t;
    }

    // ------------------------------------------------------------ static chassis / body
    /// <summary>
    /// Builds everything that does not move (frame, gold bay, seats + crew, console,
    /// antennas, TV camera, headlight housings, fenders, science palette) as decorative
    /// children of <paramref name="parent"/>. Wheel visuals are separate so the physics can
    /// spin them. Returns the chassis root handing off to the caller.
    /// </summary>
    public static Transform BuildChassis(Transform parent, RoverMats m)
    {
        // LUNAR TRIALS: the edition's own trials buggy (tubular spaceframe, roll cage,
        // knobby tyres) replaces the historic LRV replica everywhere in this project.
        if (GameEdition.IsTrials) return BuildTrialsChassis(parent, m);

        GameObject root = new GameObject("LRV_Chassis");
        root.transform.SetParent(parent, false);
        Transform chassis = root.transform;

        // Main center tubular floor frame.
        Box(chassis, "LRV_Floor", m.primary, new Vector3(1.65f, 0.12f, 2.2f), new Vector3(0f, 0.42f, 0f));

        // Wire-mesh floor deck: the LRV's floor is open mesh, not plate. A thin panel with a
        // procedural crosshatch texture sits just above the solid frame and reads as lattice.
        BuildMeshFloor(chassis, m);

        // Gold thermal foil blanket over the forward battery / electronics bay.
        Box(chassis, "LRV_FoilBay", m.foil, new Vector3(1.4f, 0.45f, 0.85f), new Vector3(0f, 0.65f, 0.95f));

        // Forward bumper tube ("tow ring").
        Cyl(chassis, "LRV_Bumper", m.secondary, 1.6f, 0.08f, new Vector3(0f, 0.42f, 1.45f), new Vector3(0f, 0f, 90f));

        // Center chassis handrails / structural tubes running along the sides.
        Cyl(chassis, "LRV_Rail", m.secondary, 1.8f, 0.05f, new Vector3(-0.84f, 0.52f, 0f), new Vector3(90f, 0f, 0f));
        Cyl(chassis, "LRV_Rail", m.secondary, 1.8f, 0.05f, new Vector3(0.84f, 0.52f, 0f), new Vector3(90f, 0f, 0f));

        // Cross-bracing tubes under the floor: the LRV frame is an open lattice, and these
        // diagonals read through the mesh deck as real structure.
        Cyl(chassis, "LRV_CrossBrace", m.secondary, 1.7f, 0.04f, new Vector3(0f, 0.3f, 0.55f), new Vector3(90f, 0f, 32f));
        Cyl(chassis, "LRV_CrossBrace", m.secondary, 1.7f, 0.04f, new Vector3(0f, 0.3f, -0.55f), new Vector3(90f, 0f, -32f));

        BuildSeats(chassis, m);
        BuildConsole(chassis, m);
        BuildAntennaAndCamera(chassis, m);
        BuildLowGainAntenna(chassis, m);
        BuildHeadlights(chassis, m);
        BuildSciencePalette(chassis, m);
        BuildFenders(chassis, m);

        // LUNAR TRIALS: suited driver in the bucket, hands to the wheel.
        AttachRider(chassis, m, new Vector3(0f, 0.56f, -0.25f), 12f, false);

        return chassis;
    }

    /// <summary>
    /// The sprint buggy's body: the same family of tubular frame, re-proportioned for speed.
    /// Lower and wider than the LRV, a raked twin-cheek nose in place of the square bumper tube,
    /// side skirts, a rear diffuser, a low wraparound screen and a rear wing on twin pylons. The
    /// umbrella antenna and TV camera are deliberately dropped - those tall boxy parts are what
    /// make the historic rover read as a utility truck, and they are the first things a designer
    /// takes off a car built for speed.
    /// </summary>
    public static Transform BuildSprintChassis(Transform parent, RoverMats m)
    {
        GameObject root = new GameObject("Sprint_Chassis");
        root.transform.SetParent(parent, false);
        Transform chassis = root.transform;

        // Wide, low floor pan. The stance does most of the work before any detail is read.
        Box(chassis, "Sprint_Floor", m.primary, new Vector3(2.05f, 0.1f, 2.6f), new Vector3(0f, 0.3f, -0.1f));

        // Raked nose: two angled cheeks closing on a blunt centre tip.
        Box(chassis, "Sprint_NoseL", m.primary, new Vector3(0.9f, 0.14f, 0.9f), new Vector3(-0.42f, 0.33f, 1.28f), new Vector3(0f, 26f, 0f));
        Box(chassis, "Sprint_NoseR", m.primary, new Vector3(0.9f, 0.14f, 0.9f), new Vector3(0.42f, 0.33f, 1.28f), new Vector3(0f, -26f, 0f));
        Box(chassis, "Sprint_NoseTip", m.accent, new Vector3(0.5f, 0.1f, 0.42f), new Vector3(0f, 0.32f, 1.62f));

        // Splitter under the nose, and skirts that tie the wide stance together.
        Box(chassis, "Sprint_Splitter", m.secondary, new Vector3(1.95f, 0.05f, 0.5f), new Vector3(0f, 0.24f, 1.42f));
        Box(chassis, "Sprint_SkirtL", m.secondary, new Vector3(0.14f, 0.16f, 1.9f), new Vector3(-1f, 0.28f, -0.1f));
        Box(chassis, "Sprint_SkirtR", m.secondary, new Vector3(0.14f, 0.16f, 1.9f), new Vector3(1f, 0.28f, -0.1f));

        // Rear deck over the battery bay, plus diffuser strakes off the tail.
        Box(chassis, "Sprint_RearDeck", m.foil, new Vector3(1.7f, 0.12f, 0.8f), new Vector3(0f, 0.36f, -1f));
        Box(chassis, "Sprint_DiffuserL", m.secondary, new Vector3(0.12f, 0.18f, 0.6f), new Vector3(-0.45f, 0.28f, -1.45f));
        Box(chassis, "Sprint_DiffuserC", m.secondary, new Vector3(0.12f, 0.18f, 0.6f), new Vector3(0f, 0.28f, -1.45f));
        Box(chassis, "Sprint_DiffuserR", m.secondary, new Vector3(0.12f, 0.18f, 0.6f), new Vector3(0.45f, 0.28f, -1.45f));

        // Low cockpit tub with a raked screen - open topped, so the crew's heads sit above it.
        Box(chassis, "Sprint_Tub", m.primary, new Vector3(1.5f, 0.3f, 1.1f), new Vector3(0f, 0.42f, -0.1f));
        Box(chassis, "Sprint_Screen", m.visor, new Vector3(1.3f, 0.34f, 0.06f), new Vector3(0f, 0.68f, 0.42f), new Vector3(-38f, 0f, 0f));
        Box(chassis, "Sprint_RearBulk", m.secondary, new Vector3(1.5f, 0.42f, 0.14f), new Vector3(0f, 0.62f, -0.62f));

        // Rear wing on twin pylons: the single clearest read that this one is built for speed.
        Cyl(chassis, "Sprint_WingPylonL", m.secondary, 0.42f, 0.05f, new Vector3(-0.55f, 0.68f, -1.32f), Vector3.zero);
        Cyl(chassis, "Sprint_WingPylonR", m.secondary, 0.42f, 0.05f, new Vector3(0.55f, 0.68f, -1.32f), Vector3.zero);
        Box(chassis, "Sprint_Wing", m.accent, new Vector3(2f, 0.06f, 0.42f), new Vector3(0f, 0.9f, -1.38f), new Vector3(-14f, 0f, 0f));

        // Gold shoulder stripe, carrying the Artemis accent along the flank.
        Box(chassis, "Sprint_StripeL", m.accent, new Vector3(0.06f, 0.06f, 1.8f), new Vector3(-0.86f, 0.42f, -0.1f));
        Box(chassis, "Sprint_StripeR", m.accent, new Vector3(0.06f, 0.06f, 1.8f), new Vector3(0.86f, 0.42f, -0.1f));

        BuildSprintSeats(chassis, m);
        BuildHeadlights(chassis, m);

        return chassis;
    }

    // Low bucket seats in place of the LRV's upright lawn chairs, sat deeper in the tub.
    private static void BuildSprintSeats(Transform chassis, RoverMats m)
    {
        Transform seats = new GameObject("Sprint_Seats").transform;
        seats.SetParent(chassis, false);
        seats.localPosition = new Vector3(0f, 0.34f, -0.18f);

        for (int i = 0; i < 2; i++)
        {
            float seatX = i == 0 ? -0.34f : 0.34f;
            Transform seat = new GameObject("Sprint_Seat").transform;
            seat.SetParent(seats, false);
            seat.localPosition = new Vector3(seatX, 0f, 0f);

            Box(seat, "Sprint_SeatBottom", m.webbing, new Vector3(0.46f, 0.05f, 0.46f), new Vector3(0f, 0.12f, 0f));
            Box(seat, "Sprint_SeatBack", m.webbing, new Vector3(0.44f, 0.5f, 0.05f), new Vector3(0f, 0.34f, -0.2f), new Vector3(-26f, 0f, 0f));
            BuildAstronaut(seat, "Sprint_Crew_" + (i == 0 ? "L" : "R"), m);
        }
    }

    // Two folding lawn-chair crew seats with a stylized EVA astronaut in each. The LRV's
    // seats are beige fabric tubes on a slanted frame (see the NASA reference imagery).
    private static void BuildSeats(Transform chassis, RoverMats m)
    {
        Transform seats = new GameObject("LRV_Seats").transform;
        seats.SetParent(chassis, false);
        seats.localPosition = new Vector3(0f, 0.48f, -0.15f);

        for (int i = 0; i < 2; i++)
        {
            float seatX = i == 0 ? -0.38f : 0.38f;
            Transform seat = new GameObject("LRV_Seat").transform;
            seat.SetParent(seats, false);
            seat.localPosition = new Vector3(seatX, 0f, 0f);

            Box(seat, "LRV_SeatBottom", m.webbing, new Vector3(0.52f, 0.04f, 0.5f), new Vector3(0f, 0.15f, 0f));
            Box(seat, "LRV_SeatBack", m.webbing, new Vector3(0.52f, 0.65f, 0.04f), new Vector3(0f, 0.45f, -0.24f), new Vector3(-12.6f, 0f, 0f));
            Cyl(seat, "LRV_ChairLeg", m.secondary, 0.3f, 0.04f, new Vector3(-0.24f, 0.05f, 0.2f), Vector3.zero);
            Cyl(seat, "LRV_ChairLeg", m.secondary, 0.3f, 0.04f, new Vector3(0.24f, 0.05f, 0.2f), Vector3.zero);
            Box(seat, "LRV_Footrest", m.primary, new Vector3(0.48f, 0.03f, 0.35f), new Vector3(0f, 0.02f, 0.42f));
            // Fabric armrest tube along the outer edge of each seat.
            Cyl(seat, "LRV_Armrest", m.webbing, 0.42f, 0.035f, new Vector3(i == 0 ? -0.3f : 0.3f, 0.28f, -0.05f), new Vector3(90f, 0f, 0f));

            BuildAstronaut(seat, "LRV_Astronaut_" + (i == 0 ? "CDR" : "LMP"), m);
        }
    }

    private static void BuildAstronaut(Transform seat, string name, RoverMats m)
    {
        Transform astro = new GameObject(name).transform;
        astro.SetParent(seat, false);

        Box(astro, "LRV_Torso", m.suit, new Vector3(0.38f, 0.48f, 0.28f), new Vector3(0f, 0.46f, -0.1f), new Vector3(-8.6f, 0f, 0f));
        Box(astro, "LRV_PLSS", m.primary, new Vector3(0.34f, 0.48f, 0.2f), new Vector3(0f, 0.48f, -0.28f));
        Sphere(astro, "LRV_Helmet", m.suit, 0.16f, new Vector3(0f, 0.78f, -0.06f));
        Sphere(astro, "LRV_Visor", m.visor, 0.12f, new Vector3(0f, 0.78f, -0.01f)); // gold sun visor, slightly forward

        Box(astro, "LRV_Leg", m.suit, new Vector3(0.14f, 0.38f, 0.14f), new Vector3(-0.12f, 0.22f, 0.16f), new Vector3(31.5f, 0f, 0f));
        Box(astro, "LRV_Leg", m.suit, new Vector3(0.14f, 0.38f, 0.14f), new Vector3(0.12f, 0.22f, 0.16f), new Vector3(31.5f, 0f, 0f));
        Box(astro, "LRV_Arm", m.suit, new Vector3(0.11f, 0.32f, 0.11f), new Vector3(-0.24f, 0.44f, 0.05f), new Vector3(-34.4f, 0f, -11.5f));
        Box(astro, "LRV_Arm", m.suit, new Vector3(0.11f, 0.32f, 0.11f), new Vector3(0.24f, 0.44f, 0.05f), new Vector3(-34.4f, 0f, 11.5f));
    }

    // T-handle center control console (drive stick) + speedometer display.
    private static void BuildConsole(Transform chassis, RoverMats m)
    {
        Transform console = new GameObject("LRV_Console").transform;
        console.SetParent(chassis, false);
        console.localPosition = new Vector3(0f, 0.48f, 0.25f);

        Cyl(console, "LRV_StickPillar", m.secondary, 0.45f, 0.06f, new Vector3(0f, 0.22f, 0f), new Vector3(-11.5f, 0f, 0f));
        Cyl(console, "LRV_THandle", m.accent, 0.26f, 0.036f, new Vector3(0f, 0.44f, -0.04f), new Vector3(0f, 0f, 90f));
        Box(console, "LRV_Display", m.secondary, new Vector3(0.26f, 0.18f, 0.04f), new Vector3(0f, 0.38f, 0.06f), new Vector3(-28.6f, 0f, 0f));
    }

    // High-gain umbrella mesh antenna (gold, ribbed) on the front-left, color TV camera on
    // the front-right, and the small low-gain cone antenna beside the camera - the LRV's
    // full communications fit (see the NASA reference imagery).
    private static void BuildAntennaAndCamera(Transform chassis, RoverMats m)
    {
        Transform mast = new GameObject("LRV_Antenna").transform;
        mast.SetParent(chassis, false);
        mast.localPosition = new Vector3(-0.35f, 0.88f, 1.15f);
        Cyl(mast, "LRV_AntennaMast", m.secondary, 0.9f, 0.06f, new Vector3(0f, 0.45f, 0f), Vector3.zero);

        // The umbrella dish itself: a wide gold cone tilted toward the ground-plane feed horn.
        Transform dish = Cone(mast, "LRV_Umbrella", m.foil, 0.85f, 0.18f, new Vector3(0f, 0.92f, 0f), new Vector3(-22f, 17f, 0f));
        Cyl(mast, "LRV_FeedHorn", m.accent, 0.35f, 0.03f, new Vector3(0f, 0.92f, 0.5f), new Vector3(90f, 0f, 0f));

        // Rib detail: thin radial strakes under the dish skin, giving the umbrella its
        // segmented look from the reference imagery.
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(a), -0.12f, Mathf.Cos(a));
            Transform rib = Box(dish, "LRV_UmbrellaRib", m.secondary,
                new Vector3(0.035f, 0.09f, 0.5f),
                new Vector3(Mathf.Sin(a) * 0.32f, -0.1f, Mathf.Cos(a) * 0.32f),
                new Vector3(-24f, i * 45f, 0f));
            rib.localPosition = dish.localPosition + dir * 0.28f;
        }

        Transform tvCam = new GameObject("LRV_TVCamera").transform;
        tvCam.SetParent(chassis, false);
        tvCam.localPosition = new Vector3(0.35f, 0.88f, 1.15f);
        Cyl(tvCam, "LRV_CameraPole", m.secondary, 0.65f, 0.05f, new Vector3(0f, 0.32f, 0f), Vector3.zero);
        Box(tvCam, "LRV_CameraBox", m.primary, new Vector3(0.22f, 0.2f, 0.32f), new Vector3(0f, 0.65f, 0f));
        Cyl(tvCam, "LRV_CameraLens", m.secondary, 0.15f, 0.13f, new Vector3(0f, 0.65f, 0.22f), new Vector3(90f, 0f, 0f));
    }

    // The low-gain antenna: a small tilted cone on its own short mast, mounted beside the
    // TV camera on the real vehicle.
    private static void BuildLowGainAntenna(Transform chassis, RoverMats m)
    {
        Transform low = new GameObject("LRV_LowGainAntenna").transform;
        low.SetParent(chassis, false);
        low.localPosition = new Vector3(0.62f, 0.88f, 0.7f);
        Cyl(low, "LRV_LowGainMast", m.secondary, 0.5f, 0.03f, new Vector3(0f, 0.25f, 0f), Vector3.zero);
        Cyl(low, "LRV_LowGainCone", m.foil, 0.2f, 0.1f, new Vector3(0f, 0.55f, 0f), new Vector3(14f, 0f, 0f));
    }

    // Low-angle forward lamp housings (decorative; the levels supply their own lighting).
    private static void BuildHeadlights(Transform chassis, RoverMats m)
    {
        foreach (float lampX in new[] { -0.55f, 0.55f })
        {
            Cyl(chassis, "LRV_Headlight", m.secondary, 0.08f, 0.16f, new Vector3(lampX, 0.62f, 1.38f), new Vector3(0f, 0f, 90f));
        }
    }

    // Aft science tool palette: sample boxes, geology brushes in their holder, and a folded
    // rotary drill arm - the LRV's rear tool kit.
    private static void BuildSciencePalette(Transform chassis, RoverMats m)
    {
        Transform palette = new GameObject("LRV_ToolPalette").transform;
        palette.SetParent(chassis, false);
        palette.localPosition = new Vector3(0f, 0.45f, -1.25f);

        Box(palette, "LRV_PalettePlate", m.secondary, new Vector3(1.3f, 0.06f, 0.75f), Vector3.zero);
        Box(palette, "LRV_SampleBox", m.foil, new Vector3(0.35f, 0.25f, 0.4f), new Vector3(-0.35f, 0.16f, 0f));
        Box(palette, "LRV_SampleBox", m.primary, new Vector3(0.35f, 0.25f, 0.4f), new Vector3(0.35f, 0.16f, 0f));

        Cyl(palette, "LRV_DrillUpper", m.primary, 0.45f, 0.07f, new Vector3(0f, 0.43f, 0.1f), Vector3.zero);
        Cyl(palette, "LRV_DrillFore", m.secondary, 0.4f, 0.06f, new Vector3(0f, 0.85f, 0.1f), Vector3.zero);
        Cyl(palette, "LRV_DrillBit", m.accent, 0.45f, 0.05f, new Vector3(0f, 1.2f, 0.1f), Vector3.zero);

        // Geology brush set: three bristle brushes in a holder block (the LRV carried these
        // on the rear palette).
        Box(palette, "LRV_BrushHolder", m.secondary, new Vector3(0.22f, 0.1f, 0.12f), new Vector3(-0.42f, 0.32f, -0.22f));
        foreach (float bx in new[] { -0.48f, -0.42f, -0.36f })
        {
            Cyl(palette, "LRV_Brush", m.webbing, 0.26f, 0.025f, new Vector3(bx, 0.46f, -0.22f), new Vector3(9f, 0f, 0f));
        }
    }

    // ------------------------------------------------------------ fenders + mesh floor
    // The four curved fiberglass fenders over the wire wheels (the LRV's most iconic
    // silhouette element). Each is a hollow half-cylinder shell: thin plates fanned along a
    // true arc over the wheel, open underneath so the hollow reads through the shadowing.
    // Positioned over each wheel (x ±1.1, z ±1.55, wheel centre at fender-local origin).
    private static void BuildFenders(Transform chassis, RoverMats m)
    {
        Transform fenders = new GameObject("LRV_Fenders").transform;
        fenders.SetParent(chassis, false);

        foreach (var pos in new[] {
            new Vector3(-1.1f, 0f, 1.55f), new Vector3(1.1f, 0f, 1.55f),
            new Vector3(-1.1f, 0f, -1.55f), new Vector3(1.1f, 0f, -1.55f) })
        {
            bool rear = pos.z < 0f;
            Transform fender = new GameObject("LRV_Fender").transform;
            fender.SetParent(fenders, false);
            fender.localPosition = pos;
            fender.localRotation = Quaternion.identity;

            // Curved shell over the wheel: 7 slim plates fanned along an arc of radius 0.88
            // (a small gap over the 0.76 m wheel), sweeping 130 degrees from front-low
            // over the top toward the rear. Each plate is tilted to follow the arc tangent,
            // so together they read as one thin curved surface tracking the tyre.
            const float shellR = 0.88f;   // arc radius (small gap over the wheel)
            const float startAngle = 25f; // degrees above the ground line on the front side
            const float sweep = 130f;     // total arc over the top
            const int segments = 7;
            float segAngle = sweep / segments;
            float segLen = 2f * shellR * Mathf.Sin(segAngle * 0.5f * Mathf.Deg2Rad) + 0.02f;

            for (int i = 0; i < segments; i++)
            {
                float a = startAngle + segAngle * (i + 0.5f);
                float rad = a * Mathf.Deg2Rad;
                Vector3 centre = new Vector3(0f, Mathf.Sin(rad) * shellR, Mathf.Cos(rad) * shellR);
                // Plate normal must point radially outward: rotation X = (90 - a).
                Box(fender, "LRV_FenderShell", m.fender,
                    new Vector3(0.72f, 0.02f, segLen),
                    centre, new Vector3(90f - a, 0f, 0f));
            }

            // Mud flap hanging from the rear arc end (the LRV had these on the rears).
            if (rear)
            {
                Box(fender, "LRV_MudFlap", m.fender, new Vector3(0.72f, 0.22f, 0.03f),
                    new Vector3(0f, 0.12f, -0.68f));
            }
        }
    }

    // Wire-mesh floor deck: a thin panel with a procedural crosshatch texture sitting just
    // above the solid frame box, so the floor reads as the LRV's open lattice mesh.
    private static void BuildMeshFloor(Transform chassis, RoverMats m)
    {
        Material mat;
        if (m.primary.HasProperty("_Metallic"))
        {
            // In-game (lit): Standard material wearing the procedural mesh texture.
            mat = new Material(Shader.Find("Standard"));
            mat.mainTexture = BuildMeshTexture();
            mat.color = Color.white;
            mat.SetFloat("_Metallic", 0.6f);
            mat.SetFloat("_Glossiness", 0.3f);
        }
        else
        {
            // Flat Garage showroom: keep the unlit look with the mesh texture tinted grey.
            mat = new Material(Shader.Find("Unlit/Texture"));
            mat.mainTexture = BuildMeshTexture();
            mat.color = new Color(0.75f, 0.78f, 0.82f, 1f);
        }

        Transform floor = Box(chassis, "LRV_MeshDeck", mat, new Vector3(1.55f, 0.02f, 2.1f), new Vector3(0f, 0.49f, 0f));
        floor.name = "LRV_MeshDeck";
    }

    // ------------------------------------------------------------ spinning wheel
    /// <summary>
    /// Builds one wheel visual: a wire-mesh tyre barrel with the LRV's chevron tread pattern
    /// (silver herringbone over dark mesh, procedurally textured), a hub disc and the
    /// inboard traction-drive motor. Axle = X so BuggyController's spin (rotation about
    /// local X) rolls it correctly. Returns the transform that should be handed to the
    /// buggy's wheel-visual references.
    /// </summary>
    public static Transform BuildWheelVisual(Transform parent, Vector3 localPos, RoverMats m, float radius, float width)
    {
        // LUNAR TRIALS: big knobby off-road tyres with a spoke hub.
        if (GameEdition.IsTrials) return BuildTrialsWheelVisual(parent, localPos, m, radius, width);

        GameObject go = new GameObject("WheelVisual");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        // Wire-mesh wheel: the LRV tyres are open steel mesh, not rubber. A procedural
        // texture draws the silver chevron tread over the dark mesh crosshatch; only the
        // lit Standard material takes the texture (the unlit garage skin keeps its flat
        // wheel colour).
        if (m.wheel.HasProperty("_Metallic"))
        {
            m.wheel.mainTexture = BuildWheelTexture();
            m.wheel.color = Color.white;      // let the texture paint the wheel
            m.wheel.SetFloat("_Metallic", 0.35f);
            m.wheel.SetFloat("_Glossiness", 0.25f);
        }

        // Mesh tyre barrel (axle along X).
        Cyl(go.transform, "LRV_Tire", m.wheel, width, radius, Vector3.zero, new Vector3(0f, 0f, 90f));
        // Inner hub disc (titanium, with the traction-drive spline detail).
        Cyl(go.transform, "LRV_Hub", m.primary, width * 0.55f, radius * 0.38f, Vector3.zero, new Vector3(0f, 0f, 90f));
        // Small hubcap on the outboard face.
        Cyl(go.transform, "LRV_HubCap", m.wheelOverlay, 0.05f, radius * 0.2f, new Vector3(-0.16f, 0f, 0f), new Vector3(0f, 0f, 90f));
        // Inboard traction drive motor (1/4 hp DC).
        Cyl(go.transform, "LRV_Motor", m.foil, 0.14f, 0.13f, new Vector3(0.14f, 0f, 0f), new Vector3(0f, 0f, 90f));

        return go.transform;
    }

    /// <summary>Shared wire-mesh wheel texture: dark steel base with the LRV's silver
    /// chevron (herringbone) tread bands around the circumference, crossed by the fine
    /// mesh wire grid, and darker toward the sidewalls.</summary>
    private static Texture2D wheelTex;
    private static Texture2D BuildWheelTexture()
    {
        if (wheelTex != null) return wheelTex;
        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
        tex.name = "LRV_MeshWheel";
        tex.wrapMode = TextureWrapMode.Repeat;

        Color meshDark = new Color(0.16f, 0.17f, 0.19f, 1f);
        Color chevronSilver = new Color(0.62f, 0.65f, 0.70f, 1f);
        Color wire = new Color(0.45f, 0.47f, 0.52f, 1f);
        Color sidewall = new Color(0.10f, 0.10f, 0.12f, 1f);
        System.Random rng = new System.Random(20260908);

        for (int y = 0; y < size; y++)
        {
            float v = y / (float)size;
            // Darken toward the tyre's edges (the sidewall shoulders).
            float sideT = Mathf.Clamp01((Mathf.Abs(v - 0.5f) - 0.36f) / 0.14f);
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                // Chevron herringbone: zigzag bands across the width, repeating around the
                // circumference - the LRV mesh wheel's signature read.
                float chev = (u * 12f) + Mathf.Abs(v - 0.5f) * 6f;
                bool band = (chev % 1f) < 0.55f;
                // Fine wire grid over everything (the open mesh structure).
                bool meshH = Mathf.Abs(((u * 40f) % 1f) - 0.5f) < 0.06f;
                bool meshV = Mathf.Abs(((v * 12f) % 1f) - 0.5f) < 0.06f;

                Color c = meshDark;
                if (band) c = chevronSilver;
                if (meshH || meshV) c = Color.Lerp(c, wire, 0.75f);
                // Subtle per-pixel noise so the mesh is not perfectly flat.
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                c.r += n * 0.015f;
                c.g += n * 0.015f;
                c.b += n * 0.015f;
                c = Color.Lerp(c, sidewall, sideT);
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply(true, false);
        wheelTex = tex;
        return tex;
    }

    // ------------------------------------------------------------ mesh floor texture
    /// <summary>Shared wire-mesh floor texture: grey diagonal crosshatch on a darker base,
    /// reading as the LRV's open lattice floor deck.</summary>
    private static Texture2D meshFloorTex;
    private static Texture2D BuildMeshTexture()
    {
        if (meshFloorTex != null) return meshFloorTex;
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
        tex.name = "LRV_MeshFloor";
        tex.wrapMode = TextureWrapMode.Repeat;

        Color baseCol = new Color(0.14f, 0.15f, 0.17f, 1f);
        Color wireCol = new Color(0.52f, 0.55f, 0.60f, 1f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Diagonal crosshatch: two wire directions forming the open lattice.
                bool w1 = Mathf.Abs(((x + y) % 16) - 8) < 1.6f;
                bool w2 = Mathf.Abs(((x - y + size) % 16) - 8) < 1.6f;
                Color c = baseCol;
                if (w1 || w2) c = wireCol;
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply(true, false);
        meshFloorTex = tex;
        return tex;
    }

    // ------------------------------------------------------------ LUNAR TRIALS buggy
    // The Trials edition's own machine (Dune-Hopper-style reference imagery): a tubular
    // spaceframe with a full roll cage, red panelled body sides, an exposed rear engine
    // with exhaust pipes, visible coil-over suspension, a central racing bucket and
    // knobby-tyred wheels. Built from the same primitive helpers as the LRV art; the
    // dark-grey tubes form the cage, the red panels carry the accent, the silver rods
    // are the suspension and exhaust.

    // Structural tube between two local points (roll cage / suspension struts).
    private static Transform Tube(Transform parent, string name, Material mat, Vector3 a, Vector3 b, float radius)
    {
        Vector3 mid = (a + b) * 0.5f;
        Vector3 dir = b - a;
        float len = dir.magnitude;
        Transform t = Cyl(parent, name, mat, len, radius, mid, Vector3.zero);
        if (len > 0.0001f)
            t.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        return t;
    }

    /// <summary>The Trials buggy: spaceframe + roll cage + knobby wheels. Same footprint as
    /// the LRV (wheels at x +/-1.1, z +/-1.55) so the WheelColliders are untouched.</summary>
    public static Transform BuildTrialsChassis(Transform parent, RoverMats m)
    {
        GameObject root = new GameObject("Trials_Chassis");
        root.transform.SetParent(parent, false);
        Transform chassis = root.transform;

        // LUNAR TRIALS body finish: the RetroStyleGames dirt-bike PBR materials (Dirty body
        // + Engine, Built-in pipeline variants), tiled over the panels. Falls back to the
        // skin colours if the materials are missing.
        Material body = Resources.Load<Material>("Trials/M_RSG_Bike_B_Body_Dirty_BRP");
        Material engine = Resources.Load<Material>("Trials/M_RSG_Bike_B_Engine_BRP");
        Material panel = body != null ? body : m.accent;
        Material metal = engine != null ? engine : m.secondary;

        // Floor pan + red side pods between the wheels.
        Box(chassis, "Trials_Floor", m.secondary, new Vector3(1.3f, 0.08f, 2.3f), new Vector3(0f, 0.34f, 0f));
        Box(chassis, "Trials_SideL", panel, new Vector3(0.26f, 0.34f, 1.55f), new Vector3(-0.72f, 0.5f, 0f));
        Box(chassis, "Trials_SideR", panel, new Vector3(0.26f, 0.34f, 1.55f), new Vector3(0.72f, 0.5f, 0f));
        Box(chassis, "Trials_SilverStripeL", m.suit, new Vector3(0.28f, 0.07f, 1.5f), new Vector3(-0.72f, 0.38f, 0f));
        Box(chassis, "Trials_SilverStripeR", m.suit, new Vector3(0.28f, 0.07f, 1.5f), new Vector3(0.72f, 0.38f, 0f));

        // Nose cowl: angled red wedge over the front, with a dark intake mouth.
        Box(chassis, "Trials_NoseTop", panel, new Vector3(1.05f, 0.1f, 0.75f), new Vector3(0f, 0.52f, 1.3f), new Vector3(-14f, 0f, 0f));
        Box(chassis, "Trials_NoseFace", panel, new Vector3(1.0f, 0.3f, 0.16f), new Vector3(0f, 0.42f, 1.6f));
        Box(chassis, "Trials_NoseIntake", m.secondary, new Vector3(0.7f, 0.14f, 0.1f), new Vector3(0f, 0.36f, 1.64f));

        // Front bumper tube + supports.
        Cyl(chassis, "Trials_Bumper", m.suit, 1.15f, 0.055f, new Vector3(0f, 0.44f, 1.78f), new Vector3(0f, 0f, 90f));
        Tube(chassis, "Trials_BumperStrutL", m.secondary, new Vector3(-0.5f, 0.4f, 1.6f), new Vector3(-0.5f, 0.44f, 1.78f), 0.035f);
        Tube(chassis, "Trials_BumperStrutR", m.secondary, new Vector3(0.5f, 0.4f, 1.6f), new Vector3(0.5f, 0.44f, 1.78f), 0.035f);

        // Rear engine bay: dark block + red cowl panels + twin exhaust pipes.
        Box(chassis, "Trials_Engine", metal, new Vector3(0.7f, 0.42f, 0.55f), new Vector3(0f, 0.72f, -1.25f));
        Box(chassis, "Trials_EngineCowlL", panel, new Vector3(0.16f, 0.4f, 0.7f), new Vector3(-0.5f, 0.6f, -1.2f));
        Box(chassis, "Trials_EngineCowlR", panel, new Vector3(0.16f, 0.4f, 0.7f), new Vector3(0.5f, 0.6f, -1.2f));
        Cyl(chassis, "Trials_ExhaustL", m.suit, 0.7f, 0.045f, new Vector3(-0.28f, 0.62f, -1.62f), new Vector3(84f, 0f, 0f));
        Cyl(chassis, "Trials_ExhaustR", m.suit, 0.7f, 0.045f, new Vector3(0.28f, 0.62f, -1.62f), new Vector3(84f, 0f, 0f));

        // Roll cage (dark tubes): main hoop behind the seat, front hoop over the dash,
        // roof rails, rear struts, and a cross brace.
        float cageR = 0.05f;
        Material cageMat = m.secondary;
        Tube(chassis, "Trials_HoopMainL", cageMat, new Vector3(-0.62f, 0.35f, -0.7f), new Vector3(-0.62f, 1.18f, -0.7f), cageR);
        Tube(chassis, "Trials_HoopMainR", cageMat, new Vector3(0.62f, 0.35f, -0.7f), new Vector3(0.62f, 1.18f, -0.7f), cageR);
        Tube(chassis, "Trials_HoopMainTop", cageMat, new Vector3(-0.62f, 1.18f, -0.7f), new Vector3(0.62f, 1.18f, -0.7f), cageR);
        Tube(chassis, "Trials_HoopFrontL", cageMat, new Vector3(-0.62f, 0.4f, 0.6f), new Vector3(-0.62f, 1.08f, 0.6f), cageR);
        Tube(chassis, "Trials_HoopFrontR", cageMat, new Vector3(0.62f, 0.4f, 0.6f), new Vector3(0.62f, 1.08f, 0.6f), cageR);
        Tube(chassis, "Trials_HoopFrontTop", cageMat, new Vector3(-0.62f, 1.08f, 0.6f), new Vector3(0.62f, 1.08f, 0.6f), cageR);
        Tube(chassis, "Trials_RoofRailL", cageMat, new Vector3(-0.62f, 1.08f, 0.6f), new Vector3(-0.62f, 1.18f, -0.7f), cageR);
        Tube(chassis, "Trials_RoofRailR", cageMat, new Vector3(0.62f, 1.08f, 0.6f), new Vector3(0.62f, 1.18f, -0.7f), cageR);
        Tube(chassis, "Trials_RoofCross", cageMat, new Vector3(-0.62f, 1.13f, -0.05f), new Vector3(0.62f, 1.13f, -0.05f), cageR * 0.8f);
        Tube(chassis, "Trials_StrutL", cageMat, new Vector3(-0.62f, 1.18f, -0.7f), new Vector3(-0.5f, 0.4f, -1.5f), cageR);
        Tube(chassis, "Trials_StrutR", cageMat, new Vector3(0.62f, 1.18f, -0.7f), new Vector3(0.5f, 0.4f, -1.5f), cageR);
        Tube(chassis, "Trials_Diagonal", cageMat, new Vector3(-0.62f, 1.18f, -0.7f), new Vector3(0.55f, 0.45f, -1.35f), cageR * 0.8f);

        // Roof rack plate (the flat tray across the cage top).
        Box(chassis, "Trials_RoofTray", m.secondary, new Vector3(1.0f, 0.04f, 0.5f), new Vector3(0f, 1.24f, -0.45f));

        // Light bar along the front roof rail: four spot housings with warm emissive
        // lenses (they glow in the dark prospector challenges; the headlight cone still
        // supplies the actual beam). The trials-genre signature from the reference imagery.
        Material lampLens = new Material(Shader.Find("Standard"));
        lampLens.color = new Color(0.95f, 0.9f, 0.65f, 1f);
        lampLens.EnableKeyword("_EMISSION");
        lampLens.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.5f) * 0.7f);
        for (int i = 0; i < 4; i++)
        {
            float lx = -0.45f + i * 0.3f;
            Cyl(chassis, "Trials_LampHousing", m.secondary, 0.12f, 0.075f, new Vector3(lx, 1.14f, 0.4f), new Vector3(90f, 0f, 0f));
            Cyl(chassis, "Trials_LampLens", lampLens, 0.02f, 0.062f, new Vector3(lx, 1.14f, 0.47f), new Vector3(90f, 0f, 0f));
        }

        // Spare tyre strapped flat to the rear roof tray.
        Transform spare = Cyl(chassis, "Trials_SpareTyre", m.wheel, 0.18f, 0.3f, new Vector3(0f, 1.32f, -0.45f), Vector3.zero);
        spare.name = "Trials_SpareTyre";
        Cyl(chassis, "Trials_SpareRim", m.suit, 0.06f, 0.14f, new Vector3(0f, 1.36f, -0.45f), Vector3.zero);

        // Wing mirrors on short stalks beside the front hoop.
        foreach (float mx in new[] { -0.72f, 0.72f })
        {
            Tube(chassis, "Trials_MirrorStalk", m.secondary, new Vector3(mx * 0.9f, 0.95f, 0.6f), new Vector3(mx * 1.25f, 1.05f, 0.62f), 0.02f);
            Box(chassis, "Trials_Mirror", m.secondary, new Vector3(0.16f, 0.1f, 0.04f), new Vector3(mx * 1.25f, 1.05f, 0.64f));
        }

        // Central racing bucket: dark base + back, red trim stripe.
        Box(chassis, "Trials_SeatBase", m.secondary, new Vector3(0.62f, 0.1f, 0.6f), new Vector3(0f, 0.5f, -0.25f));
        Box(chassis, "Trials_SeatBack", m.secondary, new Vector3(0.6f, 0.62f, 0.12f), new Vector3(0f, 0.78f, -0.55f), new Vector3(-14f, 0f, 0f));
        Box(chassis, "Trials_SeatTrim", m.accent, new Vector3(0.5f, 0.08f, 0.06f), new Vector3(0f, 0.95f, -0.58f), new Vector3(-14f, 0f, 0f));

        // Steering wheel + dash across the front hoop base, plus the nose light pod
        // (four round lamps on the nose face, like the reference buggy).
        Box(chassis, "Trials_Dash", m.secondary, new Vector3(1.1f, 0.18f, 0.22f), new Vector3(0f, 0.62f, 0.45f));
        Cyl(chassis, "Trials_Wheel", m.secondary, 0.04f, 0.15f, new Vector3(0f, 0.68f, 0.6f), new Vector3(-32f, 0f, 0f));
        Material noseLens = new Material(Shader.Find("Standard"));
        noseLens.color = new Color(0.95f, 0.9f, 0.65f, 1f);
        noseLens.EnableKeyword("_EMISSION");
        noseLens.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.5f) * 0.7f);
        for (int i = 0; i < 4; i++)
        {
            float lx = -0.27f + i * 0.18f;
            Cyl(chassis, "Trials_NoseLamp", noseLens, 0.02f, 0.05f, new Vector3(lx, 0.42f, 1.7f), new Vector3(90f, 0f, 0f));
        }

        // Visible coil-over suspension, front and rear (silver rod + red coil).
        foreach (var s in new[] {
            new Vector3(-0.85f, -1.3f), new Vector3(0.85f, -1.3f),
            new Vector3(-0.85f, 1.45f), new Vector3(0.85f, 1.45f) })
        {
            Tube(chassis, "Trials_ShockRod", m.suit, new Vector3(s.x * 0.55f, 0.42f, s.y * 0.86f), new Vector3(s.x, 0.16f, s.y), 0.028f);
            Cyl(chassis, "Trials_ShockCoil", m.accent, 0.26f, 0.055f, new Vector3(s.x * 0.82f, 0.3f, s.y * 0.94f), new Vector3(0f, 0f, s.x < 0f ? 24f : -24f));
        }

        return chassis;
    }

    /// <summary>The Trials wheel: big knobby off-road tyre (procedural chunky tread texture)
    /// on a dark hub with silver spokes. Axle = X for BuggyController's spin.</summary>
    public static Transform BuildTrialsWheelVisual(Transform parent, Vector3 localPos, RoverMats m, float radius, float width)
    {
        GameObject go = new GameObject("WheelVisual");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        if (m.wheel.HasProperty("_Metallic"))
        {
            m.wheel.mainTexture = BuildKnobbyTexture();
            m.wheel.color = Color.white;
            m.wheel.SetFloat("_Metallic", 0.1f);
            m.wheel.SetFloat("_Glossiness", 0.12f);
        }

        // LUNAR TRIALS: a different Simple Drift Tyres style on each wheel (front-left,
        // front-right, rear-left, rear-right) so all four can be compared in one run.
        // Each prefab is auto-scaled to the wheel diameter; its axle is assumed along its
        // local Y and rotated onto the wheel's X spin axle.
        string[] tyreStyles = {
            "Trials/Tyre_Type_1", "Trials/Tyre_Type_4",
            "Trials/Tyre_Type_6", "Trials/Tyre_Type_9",
        };
        bool front = localPos.z > 0f;
        bool right = localPos.x > 0f;
        string styleKey = tyreStyles[(front ? 0 : 2) + (right ? 1 : 0)];
        GameObject tyrePrefab = Resources.Load<GameObject>(styleKey);
        if (tyrePrefab != null)
        {
            GameObject tyre = Object.Instantiate(tyrePrefab, go.transform);
            tyre.name = styleKey;
            tyre.transform.localPosition = Vector3.zero;
            tyre.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);   // axle Y -> wheel X
            var rends = tyre.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                float scale = radius * 2f * 1.02f / Mathf.Max(0.01f, b.size.y);
                tyre.transform.localScale = new Vector3(scale, scale, scale);
            }
        }
        else
        {
            // Fallback: plain procedural tyre barrel.
            Cyl(go.transform, "Trials_Tire", m.wheel, width * 1.15f, radius * 1.04f, Vector3.zero, new Vector3(0f, 0f, 90f));
        }

        // Hub disc + six orange spokes + centre caps (the reference buggy's rim colour).
        Cyl(go.transform, "Trials_Hub", m.secondary, width * 0.5f, radius * 0.55f, Vector3.zero, new Vector3(0f, 0f, 90f));
        for (int i = 0; i < 6; i++)
        {
            Transform spoke = Box(go.transform, "Trials_Spoke", m.accent,
                new Vector3(width * 0.34f, radius * 0.72f, 0.05f), Vector3.zero, Vector3.zero);
            spoke.localRotation = Quaternion.Euler(0f, 0f, i * 30f);
        }
        Cyl(go.transform, "Trials_HubCapOut", m.suit, 0.05f, radius * 0.22f, new Vector3(-width * 0.28f, 0f, 0f), new Vector3(0f, 0f, 90f));
        Cyl(go.transform, "Trials_HubCapIn", m.suit, 0.05f, radius * 0.22f, new Vector3(width * 0.28f, 0f, 0f), new Vector3(0f, 0f, 90f));
        // Inboard motor housing.
        Cyl(go.transform, "Trials_Motor", m.foil, 0.14f, 0.14f, new Vector3(0.16f, 0f, 0f), new Vector3(0f, 0f, 90f));

        return go.transform;
    }

    /// <summary>Shared knobby off-road tyre texture: chunky tread blocks around the
    /// circumference with deep grooves, mud-speckled surface, dark sidewalls.</summary>
    private static Texture2D knobbyTex;
    private static Texture2D BuildKnobbyTexture()
    {
        if (knobbyTex != null) return knobbyTex;
        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
        tex.name = "Trials_KnobbyTyre";
        tex.wrapMode = TextureWrapMode.Repeat;

        Color rubber = new Color(0.13f, 0.13f, 0.14f, 1f);
        Color groove = new Color(0.045f, 0.045f, 0.05f, 1f);
        Color block = new Color(0.2f, 0.2f, 0.21f, 1f);
        Color mud = new Color(0.32f, 0.27f, 0.2f, 1f);
        Color sidewall = new Color(0.09f, 0.09f, 0.1f, 1f);
        System.Random rng = new System.Random(20261018);

        for (int y = 0; y < size; y++)
        {
            float v = y / (float)size;
            float sideT = Mathf.Clamp01((Mathf.Abs(v - 0.5f) - 0.34f) / 0.16f);
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                // Chunky knob blocks: 8 around the circumference with deep grooves between,
                // plus two circumferential channels and mud speckles.
                float knob = u * 8f;
                int k = Mathf.FloorToInt(knob);
                float within = knob - k;
                bool grooveLine = within > 0.78f;
                bool knobTop = within >= 0.15f && within <= 0.7f && (k % 2 == 0 || v > 0.2f);
                bool channel = Mathf.Abs(v - 0.32f) < 0.03f || Mathf.Abs(v - 0.68f) < 0.03f;

                Color c = rubber;
                if (grooveLine || channel) c = groove;
                else if (knobTop) c = block;
                if (rng.NextDouble() < 0.06) c = Color.Lerp(c, mud, (float)rng.NextDouble());
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                c.r += n * 0.01f; c.g += n * 0.01f; c.b += n * 0.01f;
                c = Color.Lerp(c, sidewall, sideT);
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply(true, false);
        knobbyTex = tex;
        return tex;
    }
}
