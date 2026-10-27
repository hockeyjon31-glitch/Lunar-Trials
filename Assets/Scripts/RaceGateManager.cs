using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lunar Race checkpoint manager. The race course is a chain of literal GATE ARCHES spaced
/// roughly like the Harvester oxygen route; each arch spans the canyon lane with a wide
/// trigger, so the buggy must drive through it (not around it) to collect the gate. Gates
/// must be collected in order, and the game never lets a missed gate silently slide: the
/// moment the buggy drives past a gate's arch without collecting it the HUD flashes a red
/// MISSED GATE - HEAD BACK alarm and the compass re-aims at the missed arch (the live-pod
/// compass driver already targets the nearest uncollected gate, which is the missed one).
///
/// The arch itself is styled after the Lunar Canyon Ridge Race gateways: a dark truss
/// gateway with a glowing ENERGY RING across the opening and a skyward beacon beam so every
/// gate is locatable from a distance. Uncollected gates glow amber; collected gates turn
/// green (see GateArchLook, driven by OxygenPod.Collect).
///
/// GameBootstrap spawns the gates and creates one of these trackers; it holds the ordered
/// pod list, builds the arch visuals and monitors misses every frame.
/// </summary>
public class RaceGateManager : MonoBehaviour
{
    [Header("References")]
    public BuggyController buggy;
    public OxygenManager oxygen;
    public HUD hud;

    [Header("Gate arch")]
    [Tooltip("Half-width of each gate arch (total opening = 2x this, metres).")]
    public float gateHalfWidth = 12.5f;
    [Tooltip("Height of the arch's crossbar above the canyon floor (metres).")]
    public float gateHeight = 7.0f;
    [Tooltip("How far PAST a gate's plane the buggy may travel before it counts as missed. "
            + "The course weaves, so the buffer must swallow the driving lane's lateral excursion.")]
    public float missBuffer = 10f;
    [Tooltip("How far PAST a missed gate's plane the buggy may travel before it is snapped "
            + "back to just behind the gate (metres). Supports the never-let-them-slide rule: a "
            + "skipped gate costs a rewind, not a runaway straight to the finish.")]
    public float rewindDistance = 60f;
    [Tooltip("When a missed gate is rewound, how far UP the course behind the arch the buggy "
            + "is dropped (metres) - enough room to regain line and drive cleanly through it.")]
    public float rewindDropBehind = 12f;

    /// <summary>The race's gates in the order they must be passed.</summary>
    public List<OxygenPod> gates = new List<OxygenPod>();

    // Course timeline bounds, filled by GameBootstrap; the HUD timeline reads them off.
    public float courseStartZ = -60f;
    public float courseEndZ = 1084f;

    public static Material MakeUnlit(Color color)
    {
        Material mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = color;
        return mat;
    }

    // Shared with CourseWarningSign so the roadside warnings are built from the same materials
    // as the gate arches and read as part of the same course furniture.
    public static Material MakeTruss()
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.17f, 0.19f, 0.22f, 1f);       // dark structure steel
        mat.SetFloat("_Metallic", 0.85f);
        mat.SetFloat("_Glossiness", 0.35f);
        return mat;
    }

    public static Material MakeGlow(Color color, float intensity)
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = Color.Lerp(color, Color.white, 0.35f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * intensity);
        return mat;
    }

    /// <summary>Builds a torus ring mesh lying in the XY plane (a circle when viewed head-on).</summary>
    private static Mesh MakeRingMesh(float majorRadius, float minorRadius)
    {
        const int majorSeg = 36;
        const int minorSeg = 10;
        int nMajor = majorSeg + 1;
        int nMinor = minorSeg + 1;
        Vector3[] verts = new Vector3[nMajor * nMinor];
        int[] idx = new int[majorSeg * minorSeg * 6];

        int vi = 0;
        for (int m = 0; m < nMajor; m++)
        {
            float phi = (float)m / majorSeg * Mathf.PI * 2f;
            for (int n = 0; n < nMinor; n++)
            {
                float theta = (float)n / minorSeg * Mathf.PI * 2f;
                float r = majorRadius + minorRadius * Mathf.Cos(theta);
                verts[vi++] = new Vector3(
                    r * Mathf.Cos(phi),
                    r * Mathf.Sin(phi),
                    minorRadius * Mathf.Sin(theta));
            }
        }

        int ti = 0;
        for (int m = 0; m < majorSeg; m++)
        {
            for (int n = 0; n < minorSeg; n++)
            {
                int a = m * nMinor + n;
                int b = m * nMinor + (n + 1);
                int c = (m + 1) * nMinor + n;
                int d = (m + 1) * nMinor + (n + 1);
                idx[ti++] = a; idx[ti++] = c; idx[ti++] = b;
                idx[ti++] = b; idx[ti++] = c; idx[ti++] = d;
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "GateRingMesh";
        mesh.vertices = verts;
        mesh.triangles = idx;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// Builds the gate ARCH (Lunar Canyon Ridge Race style): two dark truss pylons, a
    /// crossbar, a glowing holographic energy ring across the open mouth, a painted floor
    /// strip and a tall skyward beacon beam so the gate reads from a distance. Also adds a
    /// wide BOX trigger so passing anywhere through the arch collects the gate. In Race the
    /// pod carries no visuals of its own (GameBootstrap skips the core/pillar/beacon), so
    /// the arch IS the whole object; the pod's OxygenPod state still backs the gate's
    /// collected/final logic, and a GateArchLook turns the glow green once collected.
    /// Every gate - the FINISH arch included - starts in the same amber, and only the
    /// collection (driving through it) flips its glow green, so an unpased finish gate
    /// never reads as already cleared.
    /// </summary>
    public static void BuildArch(OxygenPod pod, float halfWidth, float height, string label = "")
    {
        Transform root = pod.transform;

        Color glowCol = new Color(1f, 0.62f, 0.15f);
        Material trussMat = MakeTruss();
        Material glowMat = MakeGlow(glowCol, 1.6f);
        Material baseMat = MakeUnlit(new Color(0.62f, 0.63f, 0.66f, 1f));

        for (int side = -1; side <= 1; side += 2)
        {
            // Pylon column standing beside the driving lane.
            GameObject pylon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pylon.name = "GatePylon";
            pylon.transform.SetParent(root, false);
            pylon.transform.localScale = new Vector3(1.1f, height * 1.05f, 1.1f);
            pylon.transform.localPosition = new Vector3(side * halfWidth, height * 0.5f, 0f);
            MeshRenderer pMr = pylon.GetComponent<MeshRenderer>();
            pMr.sharedMaterial = trussMat;
            Destroy(pylon.GetComponent<Collider>());

            // Base block holding the pylon.
            GameObject gateBase = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gateBase.name = "GateBase";
            gateBase.transform.SetParent(root, false);
            gateBase.transform.localScale = new Vector3(2.4f, 0.8f, 2.4f);
            gateBase.transform.localPosition = new Vector3(side * halfWidth, 0.4f, 0f);
            MeshRenderer bMr = gateBase.GetComponent<MeshRenderer>();
            bMr.sharedMaterial = baseMat;
            Destroy(gateBase.GetComponent<Collider>());
        }

        // Glowing crossbar across the top of the arch.
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "GateBar";
        bar.transform.SetParent(root, false);
        bar.transform.localScale = new Vector3(halfWidth * 2f, 1.0f, 1.0f);
        bar.transform.localPosition = new Vector3(0f, height + 0.3f, 0f);
        MeshRenderer barMr = bar.GetComponent<MeshRenderer>();
        barMr.sharedMaterial = glowMat;
        Destroy(bar.GetComponent<Collider>());

        // Holographic energy ring floating inside the arch mouth.
        GameObject ringGo = new GameObject("GateRing");
        ringGo.transform.SetParent(root, false);
        ringGo.transform.localPosition = new Vector3(0f, height * 0.55f, 0f);
        MeshFilter ringFilter = ringGo.AddComponent<MeshFilter>();
        ringFilter.sharedMesh = MakeRingMesh(halfWidth * 0.62f, 0.28f);
        MeshRenderer ringMr = ringGo.AddComponent<MeshRenderer>();
        ringMr.sharedMaterial = glowMat;

        // Skyward beacon beam so the gate is visible across the whole course.
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "GateBeacon";
        beam.transform.SetParent(root, false);
        beam.transform.localScale = new Vector3(0.7f, 150f, 0.7f);
        beam.transform.localPosition = new Vector3(0f, height * 0.5f + 75f, 0f);
        MeshRenderer beamMr = beam.GetComponent<MeshRenderer>();
        beamMr.sharedMaterial = MakeGlow(glowCol, 0.85f);
        Destroy(beam.GetComponent<Collider>());

        // Painted floor strip across the opening so the lane through the arch is obvious.
        GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        strip.name = "GateStrip";
        strip.transform.SetParent(root, false);
        strip.transform.localScale = new Vector3(halfWidth * 2f, 0.09f, 0.9f);
        strip.transform.localPosition = new Vector3(0f, 0.045f, 0f);
        MeshRenderer stripMr = strip.GetComponent<MeshRenderer>();
        stripMr.sharedMaterial = MakeGlow(glowCol, 0.7f);
        Destroy(strip.GetComponent<Collider>());

        // Label plate ("GATE n" / "FINISH") floating above the arch.
        if (!string.IsNullOrEmpty(label))
        {
            GameObject labelGo = new GameObject("GateLabel");
            labelGo.transform.SetParent(root, false);
            labelGo.transform.localPosition = new Vector3(0f, height + 1.9f, 0f);
            TextMesh tm = labelGo.AddComponent<TextMesh>();
            tm.font = HUD.ResolveDefaultFont();
            tm.text = label;
            tm.fontSize = 40;
            tm.characterSize = 0.042f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.95f, 0.85f, 1f);
            MeshRenderer labelMr = labelGo.GetComponent<MeshRenderer>();
            if (labelMr != null && tm.font != null)
            {
                labelMr.sharedMaterial = tm.font.material;
            }
        }

        // Widen the pod's collection trigger to the whole arch mouth: a box so the buggy
        // collects by driving ANYWHERE through the gate, not just within the pod's radius.
        BoxCollider box = pod.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, height * 0.5f, 0f);
        box.size = new Vector3(halfWidth * 2f, height, 3f);

        // Track the glow visuals so collecting the gate can flip them amber -> green.
        GateArchLook look = pod.gameObject.AddComponent<GateArchLook>();
        look.Init(glowMat);
    }

    /// <summary>Index of the next (earliest uncollected) gate, or -1 when all are done.</summary>
    public int NextTargetIndex()
    {
        for (int i = 0; i < gates.Count; i++)
        {
            if (gates[i] == null || !gates[i].Collected) return i;
        }
        return -1;
    }

    private void Update()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (hud == null) hud = FindAnyObjectByType<HUD>();
        if (buggy == null || hud == null) return;
        if (oxygen == null) oxygen = OxygenManager.Instance;
        if (oxygen != null && (oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive))
        {
            return;
        }

        int idx = NextTargetIndex();
        bool missed = false;
        if (idx >= 0)
        {
            OxygenPod g = gates[idx];
            if (g != null)
            {
                // Missed = clearly PAST the arch's plane (z + buffer) while standing outside
                // the arch's opening. Swinging into the opening at the last moment still
                // counts as passing THROUGH the gate (the box trigger is directionless).
                float dz = buggy.transform.position.z - g.transform.position.z;
                float dx = Mathf.Abs(buggy.transform.position.x - g.transform.position.x);
                if (dz >= missBuffer && dx > gateHalfWidth + 2f) missed = true;

                // Never let a missed gate slide: a player who pushes far past the skipped
                // arch is snapped back to just behind it instead of being allowed to flee
                // the correction forever. The rewind IS the correction, so the missed
                // alarm clears instead of nagging at a spot the player is no longer at.
                if (missed && dz >= rewindDistance)
                {
                    buggy.RepositionDownCourse(g.transform.position.z - rewindDropBehind);
                    missed = false;
                    if (hud != null) hud.ShowHint("GATE RESET - DRIVE BACK THROUGH", 3f);
                }
            }
        }
        hud.missedGate = missed;
    }
}

/// <summary>
/// Small state component on a race gate arch: holds the shared glow material so that when
/// the pod is collected it can flip the arch's ring/beam/strip from amber to green.
/// </summary>
public class GateArchLook : MonoBehaviour
{
    private static readonly Color Collected = new Color(0.3f, 1f, 0.4f, 1f);
    private Material glowMat;

    public void Init(Material glow)
    {
        glowMat = glow;
    }

    /// <summary>Turns the arch's glow green once the gate is passed through.</summary>
    public void SetCollected()
    {
        if (glowMat == null) return;
        glowMat.color = Color.Lerp(Collected, Color.white, 0.35f);
        glowMat.SetColor("_EmissionColor", Collected * 1.6f);
    }
}