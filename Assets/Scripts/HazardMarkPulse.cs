using UnityEngine;

/// <summary>
/// Makes telegraphed ground markers (meteor target rings, boulder/landslide hazard discs,
/// lava-flow marks, cryo-geyser rings) read like a sweeping spotlight instead of a hard
/// stamp on the terrain: a soft translucent disc that fades in, holds bright, then fades
/// out and sits dim before blinking again. Each marker owns a fresh material (see
/// MakeBlinkMaterial) so damage/era telegraphs never share state.
///
/// Built on Sprites/Default rather than Standard. Standard only survives a WebGL build if
/// something makes Unity keep it (Always Included Shaders, or a shipped material already
/// using it) - and this effect switches Standard into Fade mode WITH emission entirely at
/// runtime, a keyword combination nothing in the project references at build time, so it
/// got stripped and the disc rendered as invisible/opaque in the browser despite working
/// in the editor (which always has every shader loaded). Sprites/Default is a single
/// always-present variant with no per-mode keywords to strip, so there is nothing left for
/// the WebGL build to silently drop. The trade-off: no real point-light emission, so the
/// "glow" at peak is faked by brightening the disc's own colour instead (see BoostColor).
/// </summary>
[RequireComponent(typeof(Renderer))]
public class HazardMarkPulse : MonoBehaviour
{
    [Tooltip("Base tint; alpha is the peak transparency of the glowing disc.")]
    public Color markColor = new Color(1f, 0.55f, 0.1f, 0.32f);
    [Tooltip("Blink cycles per second (spotlight sweep cadence).")]
    public float pulseSpeed = 2.0f;
    [Tooltip("Lowest alpha the disc sits at while 'off' (never fully invisible).")]
    public float floorAlpha = 0.05f;
    [Tooltip("How far toward white the colour lifts at the brightest point of the pulse (0 = no glow boost, 1 = flashes to near-white). Stands in for the emission Standard used to provide.")]
    [Range(0f, 1f)] public float glowBoost = 0.35f;

    private Renderer targetRenderer;
    private Material mat;

    /// <summary>Builds a Sprites/Default material for a translucent telegraph disc. The
    /// emission parameter is accepted for call-site compatibility with the old Standard-based
    /// version but is no longer a separate material state - GlowBoost above fakes the same
    /// look by brightening this colour at the peak of the pulse instead.</summary>
    public static Material MakeBlinkMaterial(Color color, Color emission)
    {
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color"); // last-resort fallback, still no PBR keywords to strip
        Material mat2 = new Material(sh);
        mat2.color = color;
        return mat2;
    }

    private void Awake()
    {
        targetRenderer = GetComponent<Renderer>();
        if (targetRenderer == null) return;
        // sharedMaterial would edit the asset everyone points at; .material clones it per
        // instance so each hazard marker's pulse is independent (matches the old behaviour,
        // which relied on every marker owning its own Standard material for the same reason).
        mat = targetRenderer.material;
        if (mat != null) markColor = mat.color;
    }

    private void OnDestroy()
    {
        // .material above allocated a per-instance clone; clean it up rather than leaking it.
        if (mat != null) Destroy(mat);
    }

    private void Update()
    {
        if (mat == null) return;
        // Spotlight sweep per cycle: quick fade-in, brighter hold, fast fade-out, short gap.
        float phase = Time.time * pulseSpeed;
        phase -= Mathf.Floor(phase);
        float k = phase < 0.16f ? phase / 0.16f
            : phase < 0.46f ? 1f
            : phase < 0.62f ? 1f - (phase - 0.46f) / 0.16f
            : 0f;

        Color c = BoostColor(markColor, k * glowBoost);
        c.a = Mathf.Lerp(floorAlpha, markColor.a, k);
        mat.color = c;
    }

    /// <summary>Lifts a colour toward white by <paramref name="t"/> (0..1) - a cheap stand-in
    /// for the emissive glow Standard's _EmissionColor gave the marker at its pulse peak.</summary>
    private static Color BoostColor(Color color, float t)
    {
        return new Color(
            Mathf.Lerp(color.r, 1f, t),
            Mathf.Lerp(color.g, 1f, t),
            Mathf.Lerp(color.b, 1f, t),
            color.a);
    }
}
