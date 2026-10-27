using UnityEngine;

/// <summary>
/// A blob of molten rock flung up from Io's lava. On contact with the buggy it chips the hull;
/// when it comes to rest (or its flight time runs out) it splashes out and is destroyed.
/// </summary>
public class LavaGlobule : MonoBehaviour
{
    [Tooltip("Hull damage dealt on touching the buggy.")]
    public float damage = 3f;

    private Rigidbody rb;
    private float life;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        Destroy(gameObject, 7f);
    }

    private void Update()
    {
        if (rb == null) return;
        life += Time.deltaTime;

        // Spent: once the blob is nearly at rest on the ground it has splashed out; kill it so
        // the course doesn't accumulate long-lived lumps of cooling lava.
        if (life > 1f && rb.linearVelocity.magnitude < 0.4f)
        {
            Destroy(gameObject);
            return;
        }

        // Faint trail of sparks behind the flight arc (a grazing blob on screen reads as sparks).
        if (Random.value < 0.25f && BuggyCamera.Instance != null)
        {
            BuggyCamera.Instance.AddShake(0.02f);
        }
    }

    private void OnCollisionEnter(Collision c)
    {
        BuggyHealth hull = c.gameObject.GetComponentInParent<BuggyHealth>();
        if (hull != null)
        {
            hull.TakeDamage(damage);
            if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.5f);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Shared factory used by the race director's lava fountain and Io's survey eruptors: builds
    /// a glowing, gravity-driven globule with a <see cref="Rigidbody"/> and sphere collider.
    /// <paramref name="sideways"/> scales the lateral fling (1 = the fountain's gentle drift,
    /// &gt;1 = an eruptor throwing molten rock OUT OVER THE FLANK - bigger eruptions hurl harder
    /// sideways so the lava field spreads far from the crater).
    /// </summary>
    public static LavaGlobule Spawn(Vector3 origin, float sizeMin, float sizeMax,
        float upMin, float upMax, Color color, Color glow, float damage, float sideways = 1f,
        Material skin = null)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "LavaGlobule";
        go.transform.position = origin;
        float size = Random.Range(sizeMin, sizeMax);
        go.transform.localScale = Vector3.one * size;

        if (skin != null)
        {
            go.GetComponent<MeshRenderer>().sharedMaterial = skin;
        }
        else
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = color;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", glow * 1.6f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
        DestroyImmediate(go.GetComponent<SphereCollider>());
        go.AddComponent<SphereCollider>();

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.9f;
        rb.useGravity = true;
        rb.linearDamping = 0.15f;
        rb.angularDamping = 0.4f;
        rb.linearVelocity = new Vector3(Random.Range(-2.5f, 2.5f) * sideways,
            Random.Range(upMin, upMax), Random.Range(-3f, 3f) * sideways);

        LavaGlobule glob = go.AddComponent<LavaGlobule>();
        glob.damage = damage;
        return glob;
    }
}