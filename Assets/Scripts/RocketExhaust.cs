using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Animated rocket exhaust for the end-of-level lander. Built entirely from code (no assets), like
/// the rest of the lander:
///   - a three-layer plume (orange body, yellow mid, white-hot core) whose length, width and lean
///     flicker with Perlin noise and swell with the throttle;
///   - shock diamonds that glow along the plume at high thrust;
///   - a flickering point light so the flame lights the lander and the ground;
///   - an ignition surge (a brief flare-up) and a quick cut-off;
///   - a world-space smoke trail that hangs in the air behind a climbing rocket.
/// <see cref="CreateGroundDust"/> makes the radial dust/steam ring blown across the pad.
/// The default sizes match the lander's original static flame (0.9 wide, ~2.6 long, twin engines).
/// </summary>
public class RocketExhaust : MonoBehaviour
{
    private float nozzleRadius = 0.5f;
    private float baseLength = 2.6f;     // idle / descent flame length (the old static flame's size)
    private float maxLength = 4.8f;      // full-thrust length at liftoff
    private int seed;

    private bool lit;
    private float intensity;             // 0..1, how "on" the engine is (ramps quickly)
    private float throttle = 0.5f;       // smoothed 0..1
    private float targetThrottle = 0.5f;
    private float burst;                 // ignition surge, decays to 0
    private float smokeScale;            // 0 = no trail, 1 = full

    private GameObject fx;               // every visual except the smoke, so it can be switched off in one go
    private Transform outer, mid, core;
    private Transform[] diamonds;
    private Material diamondMat;
    private Light glow;
    private ParticleSystem smoke;

    // ------------------------------------------------------------------ public API
    /// <summary>Creates an exhaust whose nozzle sits at <paramref name="localPos"/> under
    /// <paramref name="parent"/>, pointing straight down (-Y).</summary>
    public static RocketExhaust Create(Transform parent, Vector3 localPos, float nozzleRadius,
        float baseLength, float maxLength, int seed)
    {
        GameObject go = new GameObject("Exhaust");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        RocketExhaust e = go.AddComponent<RocketExhaust>();
        e.nozzleRadius = nozzleRadius;
        e.baseLength = baseLength;
        e.maxLength = maxLength;
        e.seed = seed;
        e.Build();
        return e;
    }

    /// <summary>Lights the engine. A fresh ignition flares up briefly, then settles to the throttle.</summary>
    public void Ignite(float startThrottle)
    {
        if (!lit) burst = 1f;
        lit = true;
        targetThrottle = Mathf.Clamp01(startThrottle);
        throttle = Mathf.Min(throttle, 0.3f);   // ramps up visibly from a low burn
    }

    /// <summary>Shuts the engine down (the flame collapses in about a tenth of a second).</summary>
    public void Cutoff()
    {
        lit = false;
        targetThrottle = 0f;
    }

    /// <summary>Sets the throttle target, 0..1 (the plume follows smoothly).</summary>
    public void SetThrottle(float t)
    {
        targetThrottle = Mathf.Clamp01(t);
    }

    /// <summary>Scales the smoke trail: 0 = none (descent), 1 = full (liftoff).</summary>
    public void SetSmokeScale(float s)
    {
        smokeScale = Mathf.Clamp01(s);
    }

    // ------------------------------------------------------------------ build
    private void Build()
    {
        fx = new GameObject("Fx");
        fx.transform.SetParent(transform, false);

        outer = MakeCone("Outer", PlumeMesh(0), FlameMaterial(false, 3000));
        mid = MakeCone("Mid", PlumeMesh(1), FlameMaterial(true, 3001));
        core = MakeCone("Core", PlumeMesh(2), FlameMaterial(true, 3002));

        // Shock diamonds: small bright discs strung along the plume axis.
        diamondMat = new Material(SpriteShader());
        diamondMat.color = new Color(1f, 0.95f, 0.75f, 0f);
        diamondMat.renderQueue = 3003;
        diamonds = new Transform[4];
        for (int i = 0; i < diamonds.Length; i++)
        {
            GameObject d = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            d.name = "Diamond";
            d.transform.SetParent(fx.transform, false);
            Collider c = d.GetComponent<Collider>();
            if (c != null) Destroy(c);
            MeshRenderer mr = d.GetComponent<MeshRenderer>();
            mr.sharedMaterial = diamondMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            diamonds[i] = d.transform;
        }

        // The flame lights its surroundings (warm, flickering).
        GameObject lg = new GameObject("Glow");
        lg.transform.SetParent(fx.transform, false);
        glow = lg.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.62f, 0.28f);
        glow.range = 24f;
        glow.intensity = 0f;
        glow.shadows = LightShadows.None;

        // Smoke trail: lives under the exhaust root (not fx) so it keeps simulating after cut-off.
        smoke = NewParticleSystem("Smoke", transform, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 260);
        ParticleSystem.MainModule m = smoke.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.6f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
        m.startSize = new ParticleSystem.MinMaxCurve(1.1f, 2.0f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.8f, 0.6f), new Color(0.85f, 0.85f, 0.88f, 0.5f));
        ParticleSystem.ShapeModule sh = smoke.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 9f;
        sh.radius = nozzleRadius * 0.8f;
        FinishSmokeStyle(smoke, 4.5f, new Color(1f, 0.93f, 0.82f), new Color(0.55f, 0.55f, 0.6f), 0.55f);
        smoke.Play();

        fx.SetActive(false);
    }

    private Transform MakeCone(string name, Mesh mesh, Material mat)
    {
        GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(fx.transform, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go.transform;
    }

    // ------------------------------------------------------------------ per-frame animation
    private void Update()
    {
        float dt = Time.deltaTime;
        intensity = Mathf.MoveTowards(intensity, lit ? 1f : 0f, (lit ? 6f : 9f) * dt);
        throttle = Mathf.MoveTowards(throttle, targetThrottle, 1.8f * dt);
        burst = Mathf.MoveTowards(burst, 0f, 2.2f * dt);

        bool visible = intensity > 0.01f;
        if (fx.activeSelf != visible) fx.SetActive(visible);

        // Smoke follows the throttle while the engine is lit.
        ParticleSystem.EmissionModule em = smoke.emission;
        em.rateOverTime = visible ? smokeScale * Mathf.Lerp(8f, 46f, throttle) * intensity : 0f;

        if (!visible) return;

        float t = Time.time;
        float n1 = Mathf.PerlinNoise(t * 17f, seed * 1.7f);
        float n2 = Mathf.PerlinNoise(t * 29f + 40f, seed * 2.3f);
        float n3 = Mathf.PerlinNoise(t * 23f + 90f, seed * 3.1f);
        float thr = Mathf.Clamp01(throttle + burst * 0.35f);

        float len = Mathf.Lerp(baseLength, maxLength, thr) * intensity * (0.88f + 0.24f * n1);
        float wid = Mathf.Lerp(0.85f, 1.2f, thr) * (0.92f + 0.16f * n2) * Mathf.Lerp(0.4f, 1f, intensity);
        float r = nozzleRadius;

        outer.localScale = new Vector3(wid * r, len, wid * r);
        mid.localScale = new Vector3(wid * r * 0.62f, len * 0.68f * (0.92f + 0.16f * n2), wid * r * 0.62f);
        core.localScale = new Vector3(wid * r * 0.34f, len * 0.42f * (0.9f + 0.2f * n3), wid * r * 0.34f);

        // Lateral sway: the layers lean a few degrees, out of step with each other.
        outer.localRotation = Quaternion.Euler((n2 - 0.5f) * 7f, 0f, (n3 - 0.5f) * 7f);
        mid.localRotation = Quaternion.Euler((n3 - 0.5f) * 9f, 0f, (n1 - 0.5f) * 9f);
        core.localRotation = Quaternion.Euler((n1 - 0.5f) * 5f, 0f, (n2 - 0.5f) * 5f);

        // Shock diamonds appear above ~half throttle and pulse.
        float dv = Mathf.Clamp01((thr - 0.45f) / 0.4f) * intensity;
        Color dc = diamondMat.color;
        dc.a = dv * 0.9f;
        diamondMat.color = dc;
        for (int i = 0; i < diamonds.Length; i++)
        {
            float tt = 0.22f + 0.17f * i;
            float pulse = 0.75f + 0.25f * Mathf.Sin(t * 38f + i * 1.7f);
            float sx = r * wid * 0.55f * (1f - tt * 0.8f) * pulse;
            diamonds[i].localPosition = new Vector3(0f, -len * tt, 0f);
            diamonds[i].localScale = new Vector3(sx, sx * 0.55f, sx);
            diamonds[i].gameObject.SetActive(dv > 0.02f);
        }

        // Warm light, brighter and wider at high thrust, with the ignition surge on top.
        glow.transform.localPosition = new Vector3(0f, -len * 0.35f, 0f);
        glow.intensity = intensity * Mathf.Lerp(1.6f, 3.4f, thr) * (0.85f + 0.3f * n1) + burst * 2.5f;
        glow.range = 20f + 10f * thr;
    }

    // ------------------------------------------------------------------ plume mesh
    private static Mesh[] plumeMeshes;

    /// <summary>Unit plume: nozzle at y = 0, tip at y = -1, radius 1 at the nozzle. Vertex colours
    /// carry the gradient (hot at the nozzle, fading to transparent at the tip).</summary>
    private static Mesh PlumeMesh(int layer)
    {
        if (plumeMeshes == null) plumeMeshes = new Mesh[3];
        if (plumeMeshes[layer] != null) return plumeMeshes[layer];

        Color[][] stops =
        {
            // outer: orange body
            new[] { new Color(1f, 0.5f, 0.1f, 0.85f), new Color(1f, 0.32f, 0.06f, 0.55f), new Color(0.7f, 0.08f, 0.02f, 0f) },
            // mid: yellow-orange
            new[] { new Color(1f, 0.85f, 0.35f, 0.95f), new Color(1f, 0.6f, 0.15f, 0.6f), new Color(1f, 0.3f, 0.05f, 0f) },
            // core: white-hot
            new[] { new Color(1f, 1f, 0.92f, 1f), new Color(1f, 0.93f, 0.55f, 0.7f), new Color(1f, 0.6f, 0.2f, 0f) },
        };
        Color[] s = stops[layer];

        const int seg = 16;
        const int rings = 7;
        Vector3[] v = new Vector3[(rings + 1) * seg];
        Color[] c = new Color[v.Length];
        Vector2[] uv = new Vector2[v.Length];
        for (int ri = 0; ri <= rings; ri++)
        {
            float t = ri / (float)rings;
            // A slight bell: the plume swells just below the nozzle, then tapers to a point.
            float radius = (0.8f + 0.45f * Mathf.Sin(Mathf.Clamp01(t * 3f) * Mathf.PI * 0.5f)) * Mathf.Pow(1f - t, 0.8f);
            Color col = t < 0.35f ? Color.Lerp(s[0], s[1], t / 0.35f) : Color.Lerp(s[1], s[2], (t - 0.35f) / 0.65f);
            for (int si = 0; si < seg; si++)
            {
                float a = si * Mathf.PI * 2f / seg;
                int idx = ri * seg + si;
                v[idx] = new Vector3(Mathf.Cos(a) * radius, -t, Mathf.Sin(a) * radius);
                c[idx] = col;
                uv[idx] = new Vector2(0.5f, 0.5f);
            }
        }
        int[] tris = new int[rings * seg * 6];
        int k = 0;
        for (int ri = 0; ri < rings; ri++)
        {
            for (int si = 0; si < seg; si++)
            {
                int a = ri * seg + si;
                int b = ri * seg + (si + 1) % seg;
                int cc = (ri + 1) * seg + si;
                int d = (ri + 1) * seg + (si + 1) % seg;
                tris[k++] = a; tris[k++] = cc; tris[k++] = b;
                tris[k++] = b; tris[k++] = cc; tris[k++] = d;
            }
        }
        Mesh m = new Mesh();
        m.name = "RocketPlume" + layer;
        m.vertices = v;
        m.colors = c;
        m.uv = uv;
        m.triangles = tris;
        m.RecalculateBounds();
        plumeMeshes[layer] = m;
        return m;
    }

    // ------------------------------------------------------------------ materials
    // Sprites/Default is always present (also in WebGL builds), so it is the safe base. The mid and
    // core layers use the additive particle shader for a proper glow when it has been included in
    // the build (Project Settings > Graphics > Always Included Shaders); otherwise they blend normally.
    private static Shader SpriteShader()
    {
        Shader s = Shader.Find("Sprites/Default");
        if (s == null) s = Shader.Find("Unlit/Color");
        return s;
    }

    private static Material[] flameMats;

    private static Material FlameMaterial(bool additive, int queue)
    {
        if (flameMats == null) flameMats = new Material[3];
        int slot = queue - 3000;
        if (slot >= 0 && slot < 3 && flameMats[slot] != null) return flameMats[slot];

        Shader sh = additive ? Shader.Find("Legacy Shaders/Particles/Additive") : null;
        if (sh == null) sh = SpriteShader();
        Material m = new Material(sh);
        m.renderQueue = queue;
        if (slot >= 0 && slot < 3) flameMats[slot] = m;
        return m;
    }

    private static Material softPuffMat;
    private static Texture2D softPuffTex;

    /// <summary>Soft round smoke sprite (baked once, no assets).</summary>
    private static Material SoftPuffMaterial()
    {
        if (softPuffMat != null) return softPuffMat;
        const int S = 64;
        softPuffTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        softPuffTex.wrapMode = TextureWrapMode.Clamp;
        softPuffTex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[S * S];
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f);
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        }
        softPuffTex.SetPixels(px);
        softPuffTex.Apply();
        softPuffMat = new Material(SpriteShader());
        softPuffMat.mainTexture = softPuffTex;
        return softPuffMat;
    }

    // ------------------------------------------------------------------ particle helpers
    private static ParticleSystem NewParticleSystem(string name, Transform parent, Vector3 localPos,
        Quaternion localRot, int maxParticles)
    {
        GameObject go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // configure while stopped
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxParticles;
        main.gravityModifier = 0f;
        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;

        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = SoftPuffMaterial();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        pr.receiveShadows = false;
        return ps;
    }

    /// <summary>Shared look for the smoke and dust: grows, fades in then out, tumbles slowly, drifts on
    /// noise, and slows down quickly after the initial blast.</summary>
    private static void FinishSmokeStyle(ParticleSystem ps, float growth, Color from, Color to, float peakAlpha)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, 0.12f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(growth,
            new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 1f)));

        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.8f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.25f;

        ParticleSystem.LimitVelocityOverLifetimeModule lim = ps.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.dampen = 0.12f;
        lim.limit = 0.4f;
    }

    // ------------------------------------------------------------------ ground dust
    /// <summary>A ring of dust / steam blasted outward across the ground around the pad. Starts silent:
    /// drive it with <c>rateOverTime</c> (engines on, near the ground) and <c>Emit(n)</c> puffs
    /// (ignition, touchdown). Lives in world space, not under the lander.</summary>
    public static ParticleSystem CreateGroundDust(Vector3 worldPos, float ringRadius)
    {
        // Rotating the emitter 90 degrees about X lays the Circle shape flat, so "outward" is horizontal.
        ParticleSystem ps = NewParticleSystem("GroundDust", null, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 420);
        ps.transform.position = worldPos;

        ParticleSystem.MainModule m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 4f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(10f, 18f);
        m.startSize = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.72f, 0.68f, 0.62f, 0.55f), new Color(0.55f, 0.54f, 0.55f, 0.5f));

        ParticleSystem.ShapeModule sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = ringRadius;
        sh.radiusThickness = 0.35f;
        sh.randomDirectionAmount = 0.08f;

        FinishSmokeStyle(ps, 3f, new Color(0.75f, 0.71f, 0.65f), new Color(0.5f, 0.5f, 0.52f), 0.5f);

        // Billows upward a little as it spreads (all three axes share one curve mode).
        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        // The radial blast should die away fast, leaving the cloud to hang and drift.
        ParticleSystem.LimitVelocityOverLifetimeModule lim = ps.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.dampen = 0.06f;
        lim.limit = 1.5f;

        ps.Play();
        return ps;
    }
}
