using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A self-contained physics playground for tuning the Lunar Trials airtime model: a long
/// run-up, a ramp kicker series at 10/15/20/25 degrees, four REAL gap jumps (3/5/8/12 m), a
/// bump field, two up/down slope mounds and a 5 m drop-off. It builds the stock buggy, a chase
/// camera and the debug overlay from code, so it can live in an otherwise EMPTY scene.
///
/// Menu: Lunar Buggy > Physics Test Bed (creates and opens the scene).
///
/// Controls: WASD drive, SPACE boost, CURSOR KEYS air-flip (Up/Down pitch, Left/Right roll),
/// J hop, R reset to start, 1-6 teleport to a section, F1 toggles the debug overlay.
///
/// This is a development tool - nothing here ships, and it deliberately runs the stock Moon
/// numbers (5.2 m/s^2 gravity, 1.15x tyre grip) so tuning transfers to the real challenges.
/// </summary>
[DefaultExecutionOrder(-500)]
public class PhysicsTestBed : MonoBehaviour
{
    [Header("World")]
    [Tooltip("Gravity used by the test bed. The Moon's tuned driving gravity is 5.2 m/s^2.")]
    public float gravity = 5.2f;

    [Header("Buggy (Moon stock)")]
    [Tooltip("Tyre grip multiplier. The Moon's LevelDefinition uses 1.15.")]
    public float gripMultiplier = 1.15f;
    [Tooltip("Hop-rocket apex height (metres) before the garage's HOP ROCKETS tier.")]
    public float jumpApex = 1.1f;

    private BuggyController buggy;
    private PhysicsDebugOverlay overlay;
    private Material groundMat, platformMat, rampMat, gapMat, bumpMat, slopeMat, edgeMat;

    /// <summary>The buggy this bed drives (the overlay reads it).</summary>
    public BuggyController Buggy => buggy;

    // Section spawn points. Y is ground level; BuggyController.TrialsTeleportTo adds clearance.
    private static readonly Vector3[] SectionSpawns =
    {
        new Vector3(0f, 0f, 4f),       // 1 - start / run-up
        new Vector3(0f, 0f, 60f),      // 2 - ramp series
        new Vector3(36f, 0f, 20f),     // 3 - gap jumps (lane 1)
        new Vector3(0f, 0f, 222f),     // 4 - bump field
        new Vector3(0f, 0f, 303f),     // 5 - slope mounds
        new Vector3(0f, 0f, 392f),     // 6 - drop-off
    };

    private void Awake()
    {
        Physics.gravity = new Vector3(0f, -Mathf.Max(0.1f, gravity), 0f);

        BuildMaterials();
        BuildLighting();
        BuildGround();
        BuildRampSeries();
        BuildGapJumps();
        BuildBumpField();
        BuildSlopes();
        BuildDropOff();
        BuildBuggy();
        BuildCamera();

        overlay = gameObject.AddComponent<PhysicsDebugOverlay>();
        overlay.buggy = buggy;
        overlay.testBed = this;
    }

    private void Update()
    {
        if (buggy == null) return;

        if (Input.GetKeyDown(KeyCode.R)) ResetToSpawn();

        for (int i = 0; i < SectionSpawns.Length; i++)
        {
            KeyCode alpha = (KeyCode)((int)KeyCode.Alpha1 + i);
            KeyCode keypad = (KeyCode)((int)KeyCode.Keypad1 + i);
            if (Input.GetKeyDown(alpha) || Input.GetKeyDown(keypad)) TeleportTo(SectionSpawns[i]);
        }
    }

    /// <summary>Puts the stock buggy back on the start line with all transient state cleared.</summary>
    public void ResetToSpawn()
    {
        TeleportTo(SectionSpawns[0]);
        // The overlay's live sliders may have changed gravity; restore the configured value
        // so a reset always returns the bed to its documented baseline.
        Physics.gravity = new Vector3(0f, -Mathf.Max(0.1f, gravity), 0f);
    }

    /// <summary>Teleports the buggy to a world point (its Y is treated as ground level).</summary>
    public void TeleportTo(Vector3 point)
    {
        if (buggy == null) return;
        buggy.TrialsTeleportTo(point, Quaternion.identity);
    }

    // ------------------------------------------------------------------ materials / light
    private static Material Mat(Color color, float gloss)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
        Material m = new Material(shader) { color = color };
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        return m;
    }

    private void BuildMaterials()
    {
        groundMat = Mat(new Color(0.34f, 0.34f, 0.36f), 0.04f);
        platformMat = Mat(new Color(0.24f, 0.25f, 0.27f), 0.06f);
        rampMat = Mat(new Color(0.88f, 0.45f, 0.16f), 0.18f);
        gapMat = Mat(new Color(0.72f, 0.22f, 0.22f), 0.18f);
        bumpMat = Mat(new Color(0.25f, 0.62f, 0.60f), 0.18f);
        slopeMat = Mat(new Color(0.82f, 0.70f, 0.20f), 0.14f);
        edgeMat = Mat(new Color(1f, 0.85f, 0.20f), 0.45f);
    }

    private void BuildLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.24f, 0.26f, 0.32f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.02f, 0.025f, 0.04f);
        RenderSettings.fogStartDistance = 170f;
        RenderSettings.fogEndDistance = 540f;

        GameObject sunGo = new GameObject("Test Bed Sun");
        sunGo.transform.SetParent(transform, false);
        sunGo.transform.rotation = Quaternion.Euler(47f, -32f, 0f);
        Light sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(0.90f, 0.95f, 1f);
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
    }

    // ------------------------------------------------------------------ geometry helpers
    private GameObject Box(string name, Vector3 center, Vector3 size, Material mat, Quaternion? rotation = null)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(transform, true);
        go.transform.position = center;
        if (rotation.HasValue) go.transform.rotation = rotation.Value;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>A straight kicker ramp whose TOP SURFACE starts at (<paramref name="x"/>,
    /// <paramref name="baseY"/>, <paramref name="zStart"/>) and rises toward +Z. A negative
    /// angle gives a descending ramp starting from the high edge (used for slope down-faces).</summary>
    private void Ramp(string name, float x, float zStart, float width, float length,
        float angleDeg, float baseY, Material mat)
    {
        const float thickness = 0.6f;
        Quaternion rot = Quaternion.Euler(-angleDeg, 0f, 0f);
        // Local point at the top surface's approach edge (see the helper math in the summary).
        Vector3 localEdge = new Vector3(0f, thickness * 0.5f, -length * 0.5f);
        Vector3 center = new Vector3(x, baseY, zStart) - rot * localEdge;
        Box(name, center, new Vector3(width, thickness, length), mat, rot);
    }

    /// <summary>An up-ramp, flat crest and down-ramp forming a driveable mound.</summary>
    private void Mound(string name, float x, float zStart, float width,
        float upLength, float topLength, float downLength, float angleDeg, Material mat)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        float height = upLength * Mathf.Sin(a);
        float upRun = upLength * Mathf.Cos(a);

        Ramp(name + " up", x, zStart, width, upLength, angleDeg, 0f, mat);
        Box(name + " crest",
            new Vector3(x, height - 0.25f, zStart + upRun + topLength * 0.5f),
            new Vector3(width, 0.5f, topLength), mat);
        Ramp(name + " down", x, zStart + upRun + topLength, width, downLength, -angleDeg, height, mat);
    }

    /// <summary>A rounded ridge lying across the lane (CapsuleCollider capsule, half-buried).</summary>
    private void BumpRidge(string name, float x, float z, float radius, float length, float yaw, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(transform, true);
        go.transform.position = new Vector3(x, radius * 0.55f, z);
        // Z=90 lays the cylinder's Y axis along world X, then yaw swings the ridge.
        go.transform.rotation = Quaternion.Euler(0f, yaw, 90f);
        go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    private void Sign(string text, Vector3 position)
    {
        GameObject go = new GameObject("Sign " + text);
        go.transform.SetParent(transform, true);
        go.transform.position = position;
        // TextMesh renders facing +Z; the player approaches from -Z, so turn it around.
        go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        TextMesh mesh = go.AddComponent<TextMesh>();
        Font font = HUD.ResolveDefaultFont();
        if (font != null)
        {
            mesh.font = font;
            // TextMesh does not adopt the font's material automatically at runtime; without
            // this the glyphs render with the default (often invisible) material.
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && font.material != null) renderer.sharedMaterial = font.material;
        }
        mesh.text = text;
        mesh.fontSize = 64;
        mesh.characterSize = 0.45f;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = new Color(0.92f, 0.96f, 1f, 1f);
        mesh.richText = false;
    }

    // ------------------------------------------------------------------ course sections
    private void BuildGround()
    {
        // Start apron (spans the full yard width so the buggy can pick a lane).
        Box("Start apron", new Vector3(0f, -0.5f, 10f), new Vector3(280f, 1f, 60f), groundMat);
        // Main feature corridor: ramps, bumps, slopes, ending at the drop-off ledge.
        Box("Main corridor", new Vector3(0f, -0.5f, 222.5f), new Vector3(40f, 1f, 365f), groundMat);
        // Landing floor five metres below the drop-off ledge.
        Box("Drop landing", new Vector3(0f, -5.5f, 442.5f), new Vector3(90f, 1f, 75f), groundMat);

        // Start line.
        Box("Start line", new Vector3(0f, 0.02f, 4f), new Vector3(24f, 0.04f, 1f), edgeMat);
        Sign("RUN-UP", new Vector3(0f, 3f, 24f));
    }

    private void BuildRampSeries()
    {
        Ramp("Ramp 10", 0f, 80f, 14f, 8f, 10f, 0f, rampMat);
        Ramp("Ramp 15", 0f, 118f, 14f, 8f, 15f, 0f, rampMat);
        Ramp("Ramp 20", 0f, 158f, 14f, 7f, 20f, 0f, rampMat);
        Ramp("Ramp 25", 0f, 205f, 14f, 7f, 25f, 0f, rampMat);

        Sign("RAMP 10 deg", new Vector3(10f, 2.2f, 77f));
        Sign("RAMP 15 deg", new Vector3(10f, 2.2f, 115f));
        Sign("RAMP 20 deg", new Vector3(10f, 2.2f, 155f));
        Sign("RAMP 25 deg", new Vector3(10f, 2.2f, 202f));
    }

    private void BuildGapJumps()
    {
        // Four independent lanes, shared run-up from the apron. Each is an elevated strip over
        // a 2.5 m pit, so a short jump is a real miss (press R) rather than a flat drive-over.
        float[] gaps = { 3f, 5f, 8f, 12f };
        float[] laneX = { 36f, 52f, 68f, 84f };
        const float laneWidth = 8f;
        const float approachEnd = 60f;
        const float rampLength = 4.5f;
        const float rampAngle = 20f;
        const float pitDepth = 2.5f;
        // The lane runs on to the bump field's latitude so a clean jump has somewhere to roll
        // out; only the gap itself is a hole.
        const float laneEnd = 222f;

        float lipZ = approachEnd + rampLength * Mathf.Cos(rampAngle * Mathf.Deg2Rad);
        float lipHeight = rampLength * Mathf.Sin(rampAngle * Mathf.Deg2Rad);

        for (int i = 0; i < gaps.Length; i++)
        {
            float x = laneX[i];
            string tag = gaps[i].ToString("0") + "m";

            // Pit floor. A missed jump lands here; drive back or press R.
            Box("Gap pit " + tag, new Vector3(x, -pitDepth - 0.5f, (40f + laneEnd) * 0.5f),
                new Vector3(laneWidth, 1f, laneEnd - 40f), platformMat);

            // Approach platform, flush with the apron at z=40.
            Box("Gap approach " + tag, new Vector3(x, -pitDepth * 0.5f, (40f + approachEnd) * 0.5f),
                new Vector3(laneWidth, pitDepth, approachEnd - 40f), platformMat);

            Ramp("Gap ramp " + tag, x, approachEnd, laneWidth, rampLength, rampAngle, 0f, rampMat);

            // Landing platform starts one gap-width after the launch lip and runs to laneEnd.
            float landingStart = lipZ + gaps[i];
            float landingLength = laneEnd - landingStart;
            Box("Gap landing " + tag,
                new Vector3(x, -pitDepth * 0.5f, landingStart + landingLength * 0.5f),
                new Vector3(laneWidth, pitDepth, landingLength), platformMat);

            // Painted lip so the exact gap edge is obvious from the approach.
            Box("Gap lip " + tag, new Vector3(x, 0.02f, landingStart + 0.3f),
                new Vector3(laneWidth, 0.04f, 0.6f), gapMat);

            Sign("GAP " + gaps[i].ToString("0") + " m", new Vector3(x, 3.2f, approachEnd - 4f));
            Debug.Log("[PhysicsTestBed] gap " + gaps[i] + " m at x=" + x
                + " (launch lip z=" + lipZ.ToString("0.00") + ", height=" + lipHeight.ToString("0.00") + " m)");
        }
    }

    private void BuildBumpField()
    {
        System.Random rng = new System.Random(20261026);
        for (int i = 0; i < 14; i++)
        {
            float z = 236f + i * 4.4f;
            float radius = 0.22f + (float)rng.NextDouble() * 0.34f;
            float length = 15f + (float)rng.NextDouble() * 3f;
            float yaw = ((float)rng.NextDouble() - 0.5f) * 24f;
            float x = ((float)rng.NextDouble() - 0.5f) * 4f;
            BumpRidge("Bump " + i, x, z, radius, length, yaw, bumpMat);
        }

        Box("Bump field start", new Vector3(0f, 0.02f, 232.5f), new Vector3(18f, 0.04f, 1f), bumpMat);
        Sign("BUMP FIELD", new Vector3(10f, 2.2f, 232f));
    }

    private void BuildSlopes()
    {
        Mound("Slope 10", 0f, 306f, 18f, 12f, 4f, 12f, 10f, slopeMat);
        Mound("Slope 20", 0f, 350f, 18f, 9f, 4f, 9f, 20f, slopeMat);

        Sign("UP / DOWN 10 deg", new Vector3(11f, 2.2f, 312f));
        Sign("UP / DOWN 20 deg", new Vector3(11f, 2.2f, 356f));
    }

    private void BuildDropOff()
    {
        // The main corridor ends at z=405; the landing floor is five metres below.
        Box("Drop lip", new Vector3(0f, 0.02f, 404.3f), new Vector3(40f, 0.05f, 1f), edgeMat);
        Sign("DROP-OFF 5 m", new Vector3(0f, 3f, 398f));

        Box("Drop landing mark", new Vector3(0f, -4.98f, 430f), new Vector3(30f, 0.04f, 1f), edgeMat);
    }

    // ------------------------------------------------------------------ buggy + camera
    private void BuildBuggy()
    {
        GameObject go = new GameObject("Buggy");
        go.transform.SetParent(transform, true);
        go.transform.position = new Vector3(0f, 1.6f, 4f);

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = 330f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 2.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        buggy = go.AddComponent<BuggyController>();
        buggy.jumpApex = jumpApex;
        buggy.autoReset = true;
        buggy.maxTravelFromSpawn = 900f;

        BoxCollider body = go.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, 0.75f, 0f);
        body.size = new Vector3(2.4f, 1.1f, 3.6f);

        SphereCollider pickup = go.AddComponent<SphereCollider>();
        pickup.isTrigger = true;
        pickup.radius = 2.2f;
        pickup.center = new Vector3(0f, 1f, 0f);
        go.tag = "Player";

        // The in-game Trials body and tyres (purely cosmetic; physics is the colliders above).
        LunarRoverArt.RoverMats mats = LunarRoverArt.CreateMats(LunarRoverArt.Historic, true);
        LunarRoverArt.BuildChassis(go.transform, mats);

        Vector3 fl = new Vector3(1.1f, 0f, 1.55f);
        Vector3 fr = new Vector3(-1.1f, 0f, 1.55f);
        Vector3 rl = new Vector3(1.1f, 0f, -1.55f);
        Vector3 rr = new Vector3(-1.1f, 0f, -1.55f);

        buggy.frontLeftWheel = MakeWheel(go.transform, fl);
        buggy.frontRightWheel = MakeWheel(go.transform, fr);
        buggy.rearLeftWheel = MakeWheel(go.transform, rl);
        buggy.rearRightWheel = MakeWheel(go.transform, rr);

        buggy.frontLeftVisual = LunarRoverArt.BuildWheelVisual(go.transform, fl, mats, 0.76f, 0.35f);
        buggy.frontRightVisual = LunarRoverArt.BuildWheelVisual(go.transform, fr, mats, 0.76f, 0.35f);
        buggy.rearLeftVisual = LunarRoverArt.BuildWheelVisual(go.transform, rl, mats, 0.76f, 0.35f);
        buggy.rearRightVisual = LunarRoverArt.BuildWheelVisual(go.transform, rr, mats, 0.76f, 0.35f);
    }

    private WheelCollider MakeWheel(Transform parent, Vector3 localPos)
    {
        GameObject go = new GameObject("WheelCollider");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        WheelCollider wheel = go.AddComponent<WheelCollider>();
        wheel.radius = 0.76f;
        wheel.suspensionDistance = 0.4f;
        wheel.mass = 12f;

        JointSpring spring = wheel.suspensionSpring;
        spring.spring = 10000f;
        spring.damper = 200f;
        spring.targetPosition = 0.5f;
        wheel.suspensionSpring = spring;

        // Moon grip (GameBootstrap uses the level's gripMultiplier on these same baselines).
        WheelFrictionCurve forward = wheel.forwardFriction;
        forward.stiffness = 1.0f * gripMultiplier;
        wheel.forwardFriction = forward;

        WheelFrictionCurve sideways = wheel.sidewaysFriction;
        sideways.stiffness = 0.9f * gripMultiplier;
        wheel.sidewaysFriction = sideways;

        return wheel;
    }

    private void BuildCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.012f, 0.02f);
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 2000f;

        BuggyCamera follow = cam.GetComponent<BuggyCamera>();
        if (follow == null) follow = cam.gameObject.AddComponent<BuggyCamera>();
        follow.target = buggy.transform;
    }
}
