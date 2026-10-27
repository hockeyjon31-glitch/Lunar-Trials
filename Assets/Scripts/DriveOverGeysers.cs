using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Which pale liquid fills the launch pools (drives the spray palette).</summary>
public enum GeyserLiquid
{
    SulfurDioxide, // Io: pale yellow, almost clear
    Methane,       // Titan: pale green
}

/// <summary>
/// Drive-over launch geysers: a small crater pool filled with a translucent pale liquid
/// (sulphur dioxide on Io, methane on Titan). When the middle of the buggy crosses the
/// pool the geyser fires - a spray burst in the liquid colour, a camera kick, and a
/// vertical launch that throws the rover skyward. Each pool recharges for a moment so one
/// crossing triggers a single launch; the pools are scenery (never damage), gently
/// pulsing so the player can line up the jump from a distance.
/// </summary>
public class DriveOverGeysers : MonoBehaviour
{
    [Header("References (found automatically if left null)")]
    public BuggyController buggy;
    public MoonTerrainGen terrain;
    public OxygenManager oxygen;

    [Header("Pools")]
    public GeyserLiquid liquid = GeyserLiquid.SulfurDioxide;
    [Tooltip("How many launcher pools to dot around the level.")]
    public int geyserCount = 6;
    [Tooltip("Radius of the crater pool (metres); the trigger zone is 85% of this.")]
    public float craterRadius = 4f;
    [Tooltip("Vertical launch speed (m/s) when the buggy centre crosses the pool - a launch "
        + "at the default 22 m/s peaks ~25 m high for roughly 4.5 s of airtime.")]
    public float flingSpeed = 22f;
    [Tooltip("Vertical launch speed in RACE / Ridge Survival runs. A big throw is fun on a "
            + "harvest field but pure lost time against the clock, so races use the gentler "
            + "classic hop (11 m/s ~ 6 m, about 2.2 s of air) and only harvest keeps the full arc.")]
    public float raceFlingSpeed = 11f;
    [Tooltip("A pool recharges this long after firing, so one crossing launches once.")]
    public float rechargeTime = 8f;

    [Header("Placement")]
    [Tooltip("Never place a pool on ground lower than this (metres).")]
    public float minElevation = 3f;
    [Tooltip("Keep pools at least this far (metres) from the spawn/drop-in point.")]
    public float spawnClearance = 60f;
    [Tooltip("Minimum gap between pool centres (metres).")]
    public float minimumSpacing = 45f;
    [Tooltip("Pools further than this above/below spawn are rejected (cliffs, canyon walls).")]
    public float maxAltitudeDelta = 20f;

    private sealed class Crater
    {
        public Vector3 pos;
        public float readyAt = -1f;
        public float phase;
        public MeshRenderer poolRenderer;
    }

    private readonly List<Crater> craters = new List<Crater>();
    private MaterialPropertyBlock pulseBlock;
    private bool poolsBuilt;
    private Color liquidColor = new Color(0.98f, 0.95f, 0.72f, 0.55f);
    private Color liquidEmission = new Color(1f, 0.85f, 0.45f);
    private Material liquidMat;
    private Material bankMat;
    private static Material sprayMat;

    /// <summary>Handed out on first use so the pulse loop can never meet an unbuilt block.</summary>
    private MaterialPropertyBlock PulseBlock
    {
        get
        {
            if (pulseBlock == null) pulseBlock = new MaterialPropertyBlock();
            return pulseBlock;
        }
    }

    private void Start()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        EnsurePools();
    }

    /// <summary>
    /// Builds the pools and the materials they share. Called from Start and again from Update,
    /// because the rover is not always spawned by the time this component starts - and the old
    /// early return in Start left such a level with no geysers at all, silently.
    /// </summary>
    private void EnsurePools()
    {
        if (poolsBuilt || buggy == null) return;
        poolsBuilt = true;
        if (terrain == null) terrain = FindAnyObjectByType<MoonTerrainGen>();

        ApplyPalette();
        liquidMat = MakeLiquidMaterial();
        bankMat = MakeBankMaterial();

        BuildCraters();
        for (int i = 0; i < craters.Count; i++) BuildCraterVisual(craters[i]);
    }

    private void ApplyPalette()
    {
        if (liquid == GeyserLiquid.Methane)
        {
            liquidColor = new Color(0.62f, 1f, 0.58f, 0.55f);
            liquidEmission = new Color(0.55f, 1f, 0.5f);
        }
        else
        {
            liquidColor = new Color(0.98f, 0.95f, 0.72f, 0.55f);
            liquidEmission = new Color(1f, 0.85f, 0.45f);
        }
    }

    private void BuildCraters()
    {
        Vector3 start = buggy.transform.position;
        bool race = GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival;
        bool centredCourse = race && terrain != null && terrain.raceEnabled;

        int placed = 0;
        for (int i = 0; i < geyserCount * 40 && placed < geyserCount; i++)
        {
            Vector3 p;
            if (centredCourse)
            {
                // Races run straight down-course: bias the pools along the lane ahead so the
                // player actually crosses them, rather than scattering across the whole field.
                float cz = Mathf.Clamp(start.z + Random.Range(25f, 240f),
                    MoonTerrainGen.RaceStartZ, terrain.RaceFinishZ);
                p = new Vector3(terrain.RaceCenterX(cz) + Random.Range(-55f, 55f), 0f, cz);
            }
            else
            {
                Vector2 q = Random.insideUnitCircle * 420f;
                p = new Vector3(q.x, 0f, q.y);
            }

            float y = terrain != null ? terrain.SampleHeight(p) : 0f;
            if (y <= minElevation) continue;
            if (Mathf.Abs(y - start.y) > maxAltitudeDelta) continue;
            p.y = y;
            if ((p - start).magnitude < spawnClearance) continue;

            bool crowded = false;
            for (int k = 0; k < craters.Count; k++)
            {
                if ((craters[k].pos - p).magnitude < minimumSpacing) { crowded = true; break; }
            }
            if (crowded) continue;

            // Slope check: reject cliff faces and canyon walls - the pool must sit on
            // ground the buggy can actually reach and drive across.
            float probe = craterRadius * 1.6f;
            float minY = y, maxY = y;
            for (int a = 0; a < 4; a++)
            {
                float ang = a * Mathf.PI * 0.5f + Random.value * 0.6f;
                Vector3 s = p + new Vector3(Mathf.Cos(ang) * probe, 0f, Mathf.Sin(ang) * probe);
                float sy = terrain != null ? terrain.SampleHeight(s) : y;
                if (sy < minY) minY = sy;
                if (sy > maxY) maxY = sy;
            }
            if (maxY - minY > 4.5f) continue;

            craters.Add(new Crater { pos = p, phase = Random.Range(0f, 100f), readyAt = -1f });
            placed++;
        }
    }

    private void BuildCraterVisual(Crater c)
    {
        // Dark bank underneath so the pale translucent liquid reads as a filled crater.
        GameObject bank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bank.name = "GeyserBank";
        bank.transform.position = c.pos + Vector3.up * 0.08f;
        bank.transform.localScale = new Vector3(craterRadius * 2.3f, 0.11f, craterRadius * 2.3f);
        bank.GetComponent<MeshRenderer>().sharedMaterial = bankMat;
        DestroyImmediate(bank.GetComponent<Collider>());

        // The translucent liquid pool - this is the marked-to-cross region.
        GameObject pool = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pool.name = "GeyserPool";
        pool.transform.position = c.pos + Vector3.up * 0.2f;
        pool.transform.localScale = new Vector3(craterRadius * 1.45f, 0.07f, craterRadius * 1.45f);
        MeshRenderer pr = pool.GetComponent<MeshRenderer>();
        pr.sharedMaterial = liquidMat;
        DestroyImmediate(pool.GetComponent<Collider>());
        c.poolRenderer = pr;
    }

    private void Update()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (buggy == null) return;
        EnsurePools();

        // Gentle pool shimmer: a slow glow pulse marks each launcher from a distance.
        for (int i = 0; i < craters.Count; i++)
        {
            Crater c = craters[i];
            if (c.poolRenderer == null) continue;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.8f + c.phase);
            MaterialPropertyBlock pb = PulseBlock;
            pb.Clear();
            pb.SetColor("_EmissionColor", liquidEmission * (0.35f + 0.45f * pulse));
            c.poolRenderer.SetPropertyBlock(pb);
        }

        if (!buggy.inputEnabled) return;
        if (oxygen == null) oxygen = OxygenManager.Instance;
        if (oxygen != null && (oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive)) return;

        Vector3 b = buggy.transform.position;
        float trigger = craterRadius * 0.85f;
        for (int i = 0; i < craters.Count; i++)
        {
            Crater c = craters[i];
            if (Time.time < c.readyAt) continue;
            Vector3 flat = b - c.pos;
            flat.y = 0f;
            if (flat.magnitude > trigger) continue;
            Launch(c);
        }
    }

    private void Launch(Crater c)
    {
        c.readyAt = Time.time + rechargeTime;

        Rigidbody rb = buggy.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Vertical velocity takeover matches the hop rockets: predictable, respects the
            // buggy's jump tuning, preserves the player's horizontal momentum. Races run on
            // the gentler hop - A few lost seconds is fun on a harvest field but pure
            // handicap against the clock.
            bool race = GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival;
            float speed = race ? raceFlingSpeed : flingSpeed;
            Vector3 vel = rb.linearVelocity;
            vel.y = speed * Mathf.Max(0.5f, buggy.jumpFactor);
            rb.linearVelocity = vel;
        }

        if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.85f);
        RoverSounds.PlayEruptRumble(c.pos, 1.2f);
        RoverSounds.PlayMeteorWhistle(c.pos, 0.9f);
        StartCoroutine(SprayRoutine(c));
    }

    private IEnumerator SprayRoutine(Crater c)
    {
        Material pm = SprayMaterial();

        // Three spray layers so the blast reads as a volume, not a single puff:
        //   1) the hot jet - a dense sustained cone that collapses after a third of a second,
        //   2) a wash of soft mist that billows wide and lingers longest,
        //   3) thin fast streaks punched through it a beat later for the "shot" texture.
        ParticleSystem jet = pm != null ? BuildSpray(c.pos, pm, liquidColor,
            260f, 9f, 16f, 0.35f, 0.85f, 1.4f, -0.85f, 12f, 0.9f, 420, 0.95f) : null;
        ParticleSystem mist = pm != null ? BuildSpray(c.pos + Vector3.up * 0.3f, pm, liquidColor,
            110f, 2f, 5f, 1.6f, 3.4f, 1.9f, -0.35f, 40f, 1.6f, 240, 0.38f) : null;
        ParticleSystem streaks = pm != null ? BuildSpray(c.pos, pm, liquidColor,
            0f, 14f, 20f, 0.12f, 0.3f, 1.0f, -0.4f, 8f, 0.5f, 120, 1f, 24, 0.16f) : null;

        if (jet != null)
        {
            jet.Play();
            StartCoroutine(StopEmittingAfter(jet, 0.32f));
        }
        if (mist != null)
        {
            mist.Play();
            StartCoroutine(StopEmittingAfter(mist, 0.5f));
        }
        if (streaks != null) streaks.Play();

        // Plume silhouette behind the spray - the towering column the higher fling reads
        // against, even taller and bulkier than the old stub so the shot never looks cheap.
        GameObject plume = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        plume.name = "GeyserPlume";
        plume.transform.position = c.pos + Vector3.up * 10f;
        plume.transform.localScale = new Vector3(2.3f, 8f, 2.3f);
        plume.transform.rotation = Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(0f, 360f), 0f);
        // Copy the pool material so the plume's fade never fights the pulsing pool. Falls back to
        // the shared spray material rather than throwing if the liquid material never got built.
        Material plumeSource = liquidMat != null ? liquidMat : SprayMaterial();
        Material plumeMat = plumeSource != null ? new Material(plumeSource) : null;
        MeshRenderer plumeRenderer = plume.GetComponent<MeshRenderer>();
        plumeRenderer.sharedMaterial = plumeMat;
        DestroyImmediate(plume.GetComponent<Collider>());

        float a0 = Mathf.Clamp01(liquidColor.a * 1.5f);
        float t = 0f;
        const float dur = 1.9f;
        while (t < dur)
        {
            float k = 1f - Mathf.Clamp01(t / dur);
            t += Time.deltaTime;
            plume.transform.localScale = new Vector3(2.3f + (1f - k) * 1.6f, 8f, 2.3f + (1f - k) * 1.6f);

            MaterialPropertyBlock pb = new MaterialPropertyBlock();
            Color fade = liquidColor;
            fade.a = a0 * k;
            pb.SetColor("_Color", fade);
            if (plumeMat != null && plumeMat.HasProperty("_EmissionColor"))
                pb.SetColor("_EmissionColor", liquidEmission * k);
            plumeRenderer.SetPropertyBlock(pb);
            yield return null;
        }

        Destroy(plume);
        yield return new WaitForSeconds(1.1f);
        if (jet != null) Destroy(jet.gameObject);
        if (mist != null) Destroy(mist.gameObject);
        if (streaks != null) Destroy(streaks.gameObject);
    }

    /// <summary>Stops new emission after a window so a sustained layer feeds for a beat and
    /// then lets its remaining particles live out their lifetime (no snap-cut mid-puff).</summary>
    private static IEnumerator StopEmittingAfter(ParticleSystem ps, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    /// <summary>Builds one spray layer: soft billboarded dots in the liquid colour rising out
    /// of a cone. rate > 0 feeds a sustained emitter (the caller stops it via StopEmittingAfter);
    /// otherwise a one-shot burst of `burst` particles fires at `burstDelay`.</summary>
    private static ParticleSystem BuildSpray(Vector3 pos, Material mat, Color tint,
        float rate, float speedMin, float speedMax, float sizeMin, float sizeMax,
        float lifetime, float gravity, float cone, float radius, int maxP, float alpha,
        int burst = 0, float burstDelay = 0f)
    {
        GameObject go = new GameObject("GeyserSpray");
        go.transform.position = pos + Vector3.up * 0.2f;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sharedMaterial = mat;

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = maxP;
        main.startLifetime = lifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = 0f;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        Gradient grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
            new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(0f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = radius;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTimeMultiplier = rate;
        if (burst > 0)
            emission.SetBursts(new[] { new ParticleSystem.Burst(burstDelay, (short)burst) });
        return ps;
    }

    private Material MakeLiquidMaterial()
    {
        Shader shader = Shader.Find("Standard");
        bool standard = shader != null;
        if (shader == null) shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material m = new Material(shader);
        m.name = "GeyserLiquid";
        if (standard)
        {
            m.SetFloat("_Mode", 3);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = 3000;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", liquidEmission * 0.6f);
        }
        m.SetColor("_Color", liquidColor);
        return m;
    }

    private Material MakeBankMaterial()
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
        Material m = new Material(shader);
        m.name = "GeyserBank";
        m.color = new Color(0.16f, 0.15f, 0.13f);
        return m;
    }

    private static Material SprayMaterial()
    {
        if (sprayMat != null) return sprayMat;

        Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Sprites/Default"); // always included, transparent
        if (shader == null) return null;

        // Soft dot (bright centre, feathered edge) built at runtime so no asset is needed.
        const int res = 32;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.name = "GeyserDot";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float r = res * 0.5f - 0.5f;
        Color[] px = new Color[res * res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = (x - r) / r, dy = (y - r) / r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = Mathf.Pow(a, 2.2f);
                px[y * res + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();

        Material mat = new Material(shader);
        mat.name = "GeyserDotMat";
        mat.mainTexture = tex;
        sprayMat = mat;
        return sprayMat;
    }
}