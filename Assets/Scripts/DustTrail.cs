using UnityEngine;

/// <summary>Kicks regolith dust up behind the buggy's rear wheels, scaled by road speed
/// and denser while boosting. Ported visually from the repo rover's dust field (150
/// particles, soft round sprite, ~0.35 world units, ~0.65 opacity, no depth write).</summary>
public class DustTrail : MonoBehaviour
{
    [Tooltip("Peak emission rate (particles/sec) at full speed.")]
    public float maxRate = 90f;

    private ParticleSystem ps;
    private BuggyController buggy;
    private bool attached;

    /// <summary>Builds the particle system under <paramref name="anchor"/> (the buggy root)
    /// just behind/under the rear wheel line, tinted with the planet's regolith.</summary>
    public void Attach(BuggyController control, Transform anchor, Color tint)
    {
        buggy = control;

        GameObject go = new GameObject("WheelDust");
        go.transform.SetParent(anchor, false);
        go.transform.localPosition = new Vector3(0f, 0.35f, -1.55f);

        ps = go.AddComponent<ParticleSystem>();
        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        Material mat = DustMaterial();
        if (mat == null)
        {
            // Stripped/missing particle shader: degrade gracefully, never crash the level.
            Debug.LogWarning("DustTrail: no particle shader available; dust disabled.");
            renderer.enabled = false;
            attached = true;
            enabled = false;
            return;
        }
        renderer.sharedMaterial = mat;

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.maxParticles = 150; // repo dust budget
        main.startLifetime = 1.1f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
        main.startRotation = 0f;
        main.gravityModifier = -0.15f; // dust poofs up a touch, then leisurely fades
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(2.2f, 0.1f, 0.1f); // span the two rear wheels

        // Fade out over the lifetime while drifting gently upward.
        Gradient grad = new Gradient();
        Color dim = new Color(tint.r * 0.7f, tint.g * 0.7f, tint.b * 0.7f, 1f);
        grad.SetKeys(
            new[] { new GradientColorKey(tint, 0f), new GradientColorKey(dim, 1f) },
            new[] { new GradientAlphaKey(0.65f, 0f), new GradientAlphaKey(0f, 1f) });
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(grad);

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        AnimationCurve grow = new AnimationCurve(
            new Keyframe(0f, 0.6f),
            new Keyframe(1f, 1.6f));
        size.size = new ParticleSystem.MinMaxCurve(1f, grow);

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        attached = true;
    }

    private void Update()
    {
        if (!attached || buggy == null) return;

        float speedNorm = Mathf.Clamp01(buggy.CurrentSpeedKmh / 42f);
        if (speedNorm < 0.02f && !buggy.IsBoosting)
        {
            if (ps.isPlaying)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        if (!ps.isPlaying) ps.Play();
        // Fetched fresh each frame: a cached module wrapper can go stale across scene
        // reloads in the editor ("do not create your own module instances").
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTimeMultiplier = speedNorm * maxRate * (buggy.IsBoosting ? 1.6f : 1f);
    }

    private static Material dustMaterial;

    private static Material DustMaterial()
    {
        if (dustMaterial != null) return dustMaterial;

        Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Sprites/Default"); // always included, transparent
        if (shader == null) return null; // caller disables the renderer instead of crashing

        // Soft dot (bright centre, feathered edge) built at runtime so no asset is needed.
        const int res = 32;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.name = "DustDot";
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
        mat.name = "DustMat";
        mat.mainTexture = tex;
        dustMaterial = mat;
        return dustMaterial;
    }
}
