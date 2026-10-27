using UnityEngine;

/// <summary>
/// Full-screen speed streak for the race camera. Blurs the scene outward from the screen
/// centre by <see cref="blurAmount"/> (0..1) - an arcade boost / slipstream sense of pace.
/// Runs as a built-in render-pipeline image effect (OnRenderImage), so the screen-space HUD
/// (drawn on top of the camera target) stays crisp while the terrain smears past.
///
/// Amount 0 short-circuits to a plain blit, so in menus / harvest / a standing boost the
/// frame costs nothing extra.
/// </summary>
[RequireComponent(typeof(Camera))]
public class SpeedBlur : MonoBehaviour
{
    [Tooltip("Blur strength 0..1; driven by the buggy's race speed fx (boost/slipstream).")]
    public float blurAmount = 0f;
    [Tooltip("How many taps the radial streak blends (more = heavier blur, slightly pricier).")]
    public int taps = 9;
    [Tooltip("Streak distance in texels at blurAmount 1 (scaled by edge falloff on the rim).")]
    public float maxStrength = 26f;

    private Material mat;

    private static Shader LoadShader()
    {
        Shader s = Shader.Find("Hidden/SpeedBlur");
        if (s == null) s = Resources.Load<Shader>("SpeedBlur");
        return s;
    }

    private void Awake()
    {
        Shader s = LoadShader();
        if (s != null && s.isSupported)
        {
            mat = new Material(s);
            mat.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    private void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (mat == null || blurAmount <= 0.001f || taps < 2)
        {
            Graphics.Blit(src, dest);
            return;
        }

        mat.SetFloat("_Amount", Mathf.Clamp01(blurAmount));
        mat.SetInt("_Taps", Mathf.Clamp(taps, 2, 12));
        mat.SetFloat("_MaxStrength", Mathf.Max(1f, maxStrength));
        Graphics.Blit(src, dest, mat);
    }
}