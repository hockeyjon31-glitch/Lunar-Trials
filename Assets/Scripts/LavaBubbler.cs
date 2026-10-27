using UnityEngine;

/// <summary>
/// Sits on a molten pool (Io lava lake, race lava band, volcano crater) and periodically pops
/// a small lava BUBBLE out of the surface: a glowing bump that swells, floats up a touch and
/// silently shrinks away. Purely visual - no collider, no damage (the lake/zone's own hazards
/// do the burning); this just keeps the magma visibly alive.
/// </summary>
public class LavaBubbler : MonoBehaviour
{
    [Tooltip("Where bubbles may pop relative to this object (world-space half-extents).")]
    public Vector2 halfExtents = new Vector2(8f, 8f);
    [Tooltip("Average seconds between bubbles.")]
    public float spawnInterval = 0.7f;
    [Tooltip("Molten body colour of each bubble.")]
    public Color bubbleColor = new Color(1f, 0.45f, 0.1f);
    [Tooltip("How big each popped bubble is (relative to the pool). Distant scenic pools use a higher value so the bubbles read from far off the crest.")]
    public float bubbleScale = 1f;

    private float nextAt;

    private void Start()
    {
        nextAt = Time.time + Random.Range(0f, spawnInterval);
    }

    private void Update()
    {
        if (Time.time < nextAt) return;
        nextAt = Time.time + spawnInterval;
        if (Camera.main != null) SpawnBubble();
    }

    private void SpawnBubble()
    {
        Vector3 pos = transform.position + new Vector3(
            Random.Range(-halfExtents.x, halfExtents.x),
            Random.Range(0f, 0.2f),
            Random.Range(-halfExtents.y, halfExtents.y));

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "LavaBubble";
        go.transform.position = pos;
        float scale = Random.Range(0.4f, 0.85f) * bubbleScale;
        go.transform.localScale = Vector3.one * scale;
        DestroyImmediate(go.GetComponent<Collider>());

        Material mat = LavaArt.MoltenFx();
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            mat.color = bubbleColor;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", bubbleColor * 1.5f);
        }
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;

        LavaBubble bub = go.AddComponent<LavaBubble>();
        bub.dwellScale = scale;
    }
}

/// <summary>One popped bubble: swells quickly, floats up a whisker while it thins, then is
/// destroyed. No physics and no damage - flare only.</summary>
public class LavaBubble : MonoBehaviour
{
    public float dwellScale = 0.6f;
    public float life = 0.85f;
    public float rise = 0.5f;
    private Vector3 start;
    private Vector3 dir;
    private float age;

    private void Start()
    {
        start = transform.position;
        dir = new Vector3(Random.Range(-0.5f, 0.5f), 1f, Random.Range(-0.5f, 0.5f)).normalized;
    }

    private void Update()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / life);
        // Swell then deflate until the bubble pops away to nothing.
        float s = dwellScale * Mathf.Lerp(0.5f, 1f, Mathf.PingPong(t * 1.4f, 1f)) * (1f - t);
        transform.localScale = Vector3.one * Mathf.Max(0.01f, s);
        transform.position = start + dir * rise * t;
        if (age >= life) Destroy(gameObject);
    }
}