using UnityEngine;

/// <summary>
/// Sprays coloured debris behind the buggy whenever it drives over a hazard surface -
/// orange droplets for lava, brown splashes for mud, green-blue spray for liquid methane
/// lakes, white/silver crystals for ridge ice. The particle speed scales with the buggy's
/// velocity so fast runs throw debris farther, and a lens blotch in the same colour simulates
/// droplets hitting the chase cam. Surface classification mirrors the RaceHazardDirector's
/// live zones and the HarvestFeatureController's survey discs.
/// </summary>
public class SurfaceSpray : MonoBehaviour
{
    [Header("References (found automatically if left null)")]
    public BuggyController buggy;
    public OxygenManager oxygen;
    public HUD hud;

    [Header("Spawn")]
    [Tooltip("Min interval between bursts of particles (seconds).")]
    public float spawnInterval = 0.12f;
    [Tooltip("Base particle count per burst.")]
    public int baseCount = 6;
    [Tooltip("Additional particles per unit of speed.")]
    public float speedCount = 0.4f;
    [Tooltip("How far behind the buggy the emitter sits (metres).")]
    public float emitterOffset = 2.0f;
    [Tooltip("Upward bias on the thrown debris.")]
    public float upBias = 1.6f;

    [Header("Lens blotch")]
    [Tooltip("How long the lens blotch holds (seconds).")]
    public float blotchDuration = 0.55f;
    [Tooltip("Peak blotch opacity.")]
    public float blotchMaxAlpha = 0.42f;

    [Header("Lava (orange)")]
    public Color lavaColor = new Color(1f, 0.55f, 0.12f, 1f);
    public Color lavaBlotch = new Color(1f, 0.45f, 0.08f, 1f);
    public float lavaEmission = 0.9f;

    [Header("Mud (brown)")]
    public Color mudColor = new Color(0.42f, 0.28f, 0.14f, 1f);
    public Color mudBlotch = new Color(0.4f, 0.26f, 0.13f, 1f);

    [Header("Methane lake (green/blue)")]
    public Color lakeColor = new Color(0.32f, 0.74f, 0.8f, 1f);
    public Color lakeBlotch = new Color(0.3f, 0.65f, 0.74f, 1f);

    [Header("Ridge ice (white/silver)")]
    public Color iceColor = new Color(0.92f, 0.96f, 1f, 1f);
    public Color iceBlotch = new Color(0.85f, 0.92f, 1f, 1f);

    // surface ids: 0 lava, 1 mud, 2 lake, 3 ice
    private ParticleSystem ps;
    private ParticleSystem.MainModule psMain;
    private ParticleSystem.VelocityOverLifetimeModule psVel;
    private ParticleSystem.EmissionModule psEmission;
    private ParticleSystemRenderer psRender;
    private Rigidbody rb;
    private RaceHazardDirector raceDir;
    private HarvestFeatureController featCtrl;
    private float nextSpawnAt;
    private float blotchUntil;
    private Color currentBlotch;

    private void Start()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (buggy != null) rb = buggy.GetComponent<Rigidbody>();
        BuildParticleSystem();
    }

    private void Update()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (buggy == null || !buggy.inputEnabled) return;
        if (rb == null) rb = buggy.GetComponent<Rigidbody>();
        if (oxygen == null) oxygen = OxygenManager.Instance;
        if (oxygen != null && (oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive))
        {
            HideBlotch();
            return;
        }

        int surface = DetectSurface();
        if (surface >= 0 && Time.time >= nextSpawnAt && rb != null)
        {
            float speed = rb.linearVelocity.magnitude;
            if (speed > 1.5f)
            {
                // Airborne relieves the spray: as the hop lengthens (AirborneRelease 0->1) fewer
                // crystals are thrown, and a fully cleared hop stops kicking the lens blotch too.
                float relief = buggy != null ? buggy.AirborneRelease : 0f;
                SpawnBurst(surface, speed, relief);
                if (relief < 0.9f) ShowBlotch(surface);
            }
        }

        if (blotchUntil > Time.time)
        {
            float t = Mathf.Clamp01((blotchUntil - Time.time) / blotchDuration);
            float a = t * blotchMaxAlpha;
            if (hud != null) hud.SetSurfaceBlotch(currentBlotch, a);
        }
        else if (blotchUntil > 0f)
        {
            HideBlotch();
        }
    }

    private void HideBlotch()
    {
        blotchUntil = 0f;
        if (hud != null) hud.HideSurfaceBlotch();
    }

    private void BuildParticleSystem()
    {
        GameObject psGo = new GameObject("SurfaceSpray", typeof(ParticleSystem));
        psGo.transform.SetParent(transform, false);
        psGo.transform.localPosition = new Vector3(0f, 0.3f, -emitterOffset);
        psGo.transform.localRotation = Quaternion.identity;

        ps = psGo.GetComponent<ParticleSystem>();
        psMain = ps.main;
        psMain.startLifetime = 0.55f;
        psMain.startRotation3D = false;
        psMain.maxParticles = 160;
        psMain.simulationSpace = ParticleSystemSimulationSpace.World;
        psMain.gravityModifier = 0.55f;
        psMain.loop = false;
        psMain.playOnAwake = false;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.22f;

        // Debris thrown back along the heading + up; set per burst in world space.
        psVel = ps.velocityOverLifetime;
        psVel.enabled = true;
        psVel.space = ParticleSystemSimulationSpace.World;

        psEmission = ps.emission;
        psEmission.enabled = true;
        psEmission.rateOverTime = 0f;

        // Size grows briefly then shrinks (spray splashy droplets, not snow).
        var sizeLifetime = ps.sizeOverLifetime;
        sizeLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.55f);
        sizeCurve.AddKey(0.25f, 1f);
        sizeCurve.AddKey(1f, 0.25f);
        sizeLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        psRender = psGo.GetComponent<ParticleSystemRenderer>();
        psRender.renderMode = ParticleSystemRenderMode.Billboard;
        // Unlit alpha-blended particle shader: the debris must render EXACTLY the spray colour.
        // A lit "Standard" particle material gets atmospheric lighting and fogs out to a purple
        // haze instead of staying lava-orange / mud-brown / lake-blue / ice-white. The legacy
        // name is what the WebGL build pins into Always Included Shaders - using the deprecated
        // "Particles/Alpha Blended" alias here resolves in the editor but strips to null (magenta)
        // in the build, so spell it the exact pinned name with the short alias as fallback.
        Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Particles/Alpha Blended");
        Material mat = new Material(shader);
        mat.SetColor("_TintColor", new Color(1f, 1f, 1f, 1f));
        mat.renderQueue = 3000;
        psRender.sharedMaterial = mat;

        // Force an opaque-white main texture; a missing/custom default would tint the spray.
        Texture2D white = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Color[] whitePx = { Color.white, Color.white, Color.white, Color.white };
        white.SetPixels(whitePx);
        white.Apply(false, true);
        mat.SetTexture("_MainTex", white);
    }

    private int DetectSurface()
    {
        Vector3 p = transform.position;

        if (raceDir == null) raceDir = FindAnyObjectByType<RaceHazardDirector>();
        if (raceDir != null && raceDir.enabled)
        {
            if (raceDir.IsInLava(p)) return 0;
            if (raceDir.IsInLavaLake(p)) return 0;
            if (raceDir.IsInMud(p)) return 1;
            if (raceDir.IsInLake(p)) return 2;
            if (raceDir.IsInIce(p)) return 3;
        }

        if (featCtrl == null) featCtrl = FindAnyObjectByType<HarvestFeatureController>();
        if (featCtrl != null)
        {
            HarvestFeatureKind k = featCtrl.KindAt(p);
            if (k == HarvestFeatureKind.MagmaPool) return 0;
            if (k == HarvestFeatureKind.MudPatch) return 1;
            if (k == HarvestFeatureKind.MethaneLake) return 2;
        }

        return -1;
    }

    private void SpawnBurst(int surface, float speed, float relief = 0f)
    {
        Color col;
        switch (surface)
        {
            case 0: col = lavaColor; break;
            case 1: col = mudColor; break;
            case 2: col = lakeColor; break;
            case 3: col = iceColor; break;
            default: col = Color.white; break;
        }

        // Airborne ships fewer particles and throws them softer (relief 1 = a clean hop clears
        // an ice patch with barely a wisp; landing back on the surface restores the full spray).
        float air = Mathf.Clamp01(relief);
        float velMag = (3.5f + speed * 0.35f) * (1f - air * 0.65f);

        // World-space throw: back along travel + upward intrusion.
        Vector3 throwDir = (-transform.forward + Vector3.up * 0.55f).normalized * velMag;
        psVel.x = new ParticleSystem.MinMaxCurve(throwDir.x * (1f + Random.value * 0.25f));
        psVel.y = new ParticleSystem.MinMaxCurve(throwDir.y * upBias);
        psVel.z = new ParticleSystem.MinMaxCurve(throwDir.z * (1f + Random.value * 0.25f));

        psMain.startColor = col;
        psMain.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.5f);
        psMain.startSize = new ParticleSystem.MinMaxCurve(surface == 3 ? 0.16f : 0.1f, surface == 3 ? 0.34f : 0.22f);
        if (surface == 0)
        {
            psMain.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(col, Color.white, 0.25f));
        }

        int count = Mathf.Max(1, Mathf.RoundToInt((baseCount + speed * speedCount) * (1f - air * 0.8f)));
        psEmission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });

        ps.Play(true);
        nextSpawnAt = Time.time + spawnInterval;
    }

    private void ShowBlotch(int surface)
    {
        switch (surface)
        {
            case 0: currentBlotch = lavaBlotch; break;
            case 1: currentBlotch = mudBlotch; break;
            case 2: currentBlotch = lakeBlotch; break;
            case 3: currentBlotch = iceBlotch; break;
            default: currentBlotch = Color.clear; break;
        }
        blotchUntil = Time.time + blotchDuration;
    }
}