using UnityEngine;

/// <summary>
/// The red Earth that hangs on Lunar Survival's horizon. As the difficulty sets ramp up,
/// the fog closes in and the Earth darkens toward a blood red - reinforcing that the arena
/// is getting more hostile. The tint is layered on top of the backdrop texture (if one is
/// provided) so the player can drop a custom "EarthRed" image into Assets/Resources/ and it
/// will simply shift colour as the run goes on. Falls back to a flat red disc.
/// </summary>
public class SurvivalEarth : MonoBehaviour
{
    [Tooltip("Backdrop texture to try first (inside Assets/Resources/). Falls back to flat red.")]
    public string texturePath = "EarthRed";
    [Tooltip("Backdrop texture used if the primary one is missing.")]
    public string fallbackTexturePath = "Earth";
    [Tooltip("Distance-fog fade at the start of a run (start/end metres).")]
    public Vector2 fogAtStart = new Vector2(300f, 1200f);
    [Tooltip("Distance-fog fade once the fog has fully closed in.")]
    public Vector2 fogClosedIn = new Vector2(90f, 420f);
    [Tooltip("Tint colour on the texture at the start of a run.")]
    public Color tintStart = new Color(1f, 0.66f, 0.62f);
    [Tooltip("Tint colour once the run is deep: a dark blood red.")]
    public Color tintEnd = new Color(0.22f, 0.02f, 0.02f);

    private Material mat;
    private float danger = 0f;

    public void Build(Vector3 offset, float radius)
    {
        transform.position = offset;
        transform.localScale = Vector3.one * radius * 2f;
        DestroyImmediate(GetComponent<Collider>());

        mat = new Material(Shader.Find("Unlit/Texture"));
        Texture2D tex = Resources.Load<Texture2D>(texturePath);
        if (tex == null) tex = Resources.Load<Texture2D>(fallbackTexturePath);
        if (tex != null)
        {
            mat.mainTexture = tex;
        }
        else
        {
            // No image available: flat red card that still darkens with the fog.
            mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = new Color(0.5f, 0.08f, 0.06f);
        }
        mat.color = tintStart;
        GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>0 = freshly started; 1 = fog fully closed in (deepest danger).</summary>
    public void SetDanger(float t)
    {
        danger = Mathf.Clamp01(t);

        if (mat != null) mat.color = Color.Lerp(tintStart, tintEnd, danger);

        // The fog pulls in around the arena on the same curve, so the Earth both recedes
        // beyond the haze and bleeds darker red at the same time.
        RenderSettings.fogStartDistance = Mathf.Lerp(fogAtStart.x, fogClosedIn.x, danger);
        RenderSettings.fogEndDistance = Mathf.Lerp(fogAtStart.y, fogClosedIn.y, danger);
    }
}