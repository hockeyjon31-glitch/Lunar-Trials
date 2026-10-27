using UnityEngine;

/// <summary>
/// Mars dust devils: a rotating tan funnel skims across the terrain and shoves the buggy
/// sideways out of its track if the rover gets too close. Active in BOTH Harvester and Race
/// on Mars. Rainless, piercing - a quick telegraphed hazard to glance at the horizon for.
/// </summary>
public class DustDevilDirector : MonoBehaviour
{
    [Header("References (found automatically if left null)")]
    public BuggyController buggy;
    public MoonTerrainGen terrain;
    public OxygenManager oxygen;

    [Header("Cadence (seconds)")]
    [Tooltip("Gap between one devil dissolving and the next forming.")]
    public float calmMin = 13f;
    public float calmMax = 22f;

    [Header("Devil body")]
    [Tooltip("How long a devil stays on the ground (seconds).")]
    public float lifetime = 7f;
    [Tooltip("How fast the funnel skims across the terrain (m/s).")]
    public float speed = 13f;
    [Tooltip("Distance band in front of the buggy where a devil can form (min/max metres).")]
    public float spawnMin = 40f;
    public float spawnMax = 75f;

    [Header("Shove")]
    [Tooltip("Radius of the shove zone around the devil's core (metres).")]
    public float shoveRadius = 11f;
    [Tooltip("How hard the devil shoves the buggy out of its path (lateral m/s kick).")]
    public float shoveKick = 17f;

    [Header("Ridge (Mars)")]
    [Tooltip("Ridge runs get low drifting dust CLOUDS instead of tall funnels - the push comes from a linked tiny gust, like Titan's breeze.")]
    public bool ridgeMode = false;
    [Tooltip("Cloud puff radius (metres) on the ridge.")]
    public float cloudRadius = 7f;

    private Rigidbody rb;
    private float nextSpawnAt;
    private GameObject devil;
    private Transform devilSpin;
    private Vector3 devilPos;
    private Vector3 devilHeading;
    private float devilEndAt;
    private Material devilMat;

    private void Start()
    {
        nextSpawnAt = Time.time + Random.Range(calmMin, calmMax);
        devilMat = new Material(Shader.Find("Standard"));
        devilMat.color = new Color(0.72f, 0.56f, 0.36f, 0.55f);
        devilMat.SetFloat("_Metallic", 0f);
        devilMat.SetFloat("_Glossiness", 0.2f);
        // Translucent furled dust.
        devilMat.SetFloat("_Mode", 3f);
        devilMat.EnableKeyword("_ALPHABLEND_ON");
        devilMat.renderQueue = 3000;
        devilMat.enableInstancing = true;
    }

    private void Update()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (buggy == null || !buggy.inputEnabled) return;
        if (oxygen == null) oxygen = OxygenManager.Instance;
        if (oxygen != null && (oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive)) return;
        if (terrain == null) terrain = FindAnyObjectByType<MoonTerrainGen>();
        if (terrain == null) return;

        if (devil == null)
        {
            if (Time.time >= nextSpawnAt) SpawnDevil();
            return;
        }

        // Move the funnel, hugging the terrain.
        devilPos += devilHeading * speed * Time.deltaTime;
        float y = terrain.SampleHeight(devilPos);
        if (y <= -100f || Time.time >= devilEndAt)
        {
            Dissolve();
            return;
        }
        devilPos.y = y;
        devil.transform.position = devilPos;
        if (devilSpin != null) devilSpin.Rotate(0f, (ridgeMode ? 18f : 85f) * Time.deltaTime, 0f, Space.Self);

        if (rb == null) rb = buggy.GetComponent<Rigidbody>();
        Vector3 toBuggy = buggy.transform.position - devilPos;
        toBuggy.y = 0f;
        float dist = toBuggy.magnitude;
        // On the ridge the dust cloud is purely visual - the tiny linked gust is what pushes.
        if (!ridgeMode && rb != null && dist < shoveRadius)
        {
            Vector3 away = dist > 0.01f ? toBuggy.normalized : buggy.transform.right;
            rb.AddForce(away * shoveKick * rb.mass, ForceMode.Impulse);
            if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.5f);
        }
    }

    private void SpawnDevil()
    {
        if (ridgeMode)
        {
            SpawnDustCloud();
            return;
        }

        Vector3 b = buggy.transform.position;
        float dist = Random.Range(spawnMin, spawnMax);
        Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        devilPos = b + new Vector3(dir.x * dist, 0f, dir.z * dist);
        float y = terrain.SampleHeight(devilPos);
        if (y <= -100f) { Reschedule(); return; }

        devilPos.y = y;
        float headingDeg = Random.Range(0f, 360f);
        devilHeading = Quaternion.Euler(0f, headingDeg, 0f) * Vector3.forward;

        // A proper funnel: a tight ground foot that flares into a wide mouth overhead, built
        // from stepped cylinder segments, with a slight canted lean like a real dust devil.
        devil = new GameObject("DustDevil");
        devil.transform.position = devilPos;
        devil.transform.rotation = Quaternion.Euler(
            Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(-6f, 6f));

        const int steps = 8;
        const float totalH = 16f;
        float stepH = totalH / steps;
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)(steps - 1);           // 0 = ground, 1 = mouth
            float radius = Mathf.Lerp(0.9f, 3.6f, t * t); // squat foot, flared top
            float cy = 0.5f + i * stepH;
            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            seg.name = "FunnelSeg";
            seg.transform.SetParent(devil.transform, false);
            seg.transform.localPosition = new Vector3(0f, cy, 0f);
            seg.transform.localScale = new Vector3(radius * 2f, stepH * 0.5f, radius * 2f);
            seg.GetComponent<MeshRenderer>().sharedMaterial = devilMat;
            DestroyImmediate(seg.GetComponent<Collider>());
        }

        // Revolving material: three rings of dust chunks spinning around the funnel axis, so
        // the swirl of debris reads clearly at a glance.
        devilSpin = new GameObject("DustSpin").transform;
        devilSpin.SetParent(devil.transform, false);
        for (int ring = 0; ring < 3; ring++)
        {
            float h = 3f + ring * 5f;                       // spread down the funnel
            float ringR = 3.2f + ring * 0.9f;               // wider orbits higher up
            for (int c = 0; c < 7; c++)
            {
                float a = (c / 7f) * Mathf.PI * 2f;
                GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chunk.name = "DustChunk";
                chunk.transform.SetParent(devilSpin, false);
                chunk.transform.localPosition = new Vector3(Mathf.Cos(a) * ringR, h, Mathf.Sin(a) * ringR);
                float cs = Random.Range(0.35f, 0.65f);
                chunk.transform.localScale = new Vector3(cs, cs * Random.Range(0.4f, 1f), cs);
                chunk.GetComponent<MeshRenderer>().sharedMaterial = devilMat;
                DestroyImmediate(chunk.GetComponent<Collider>());
            }
        }

        devilEndAt = Time.time + lifetime;
        if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.35f);
    }

    /// <summary>Ridge variant: a low, wide, slow-drifting dust cloud hugging the crest instead of
    /// a tall funnel. Purely visual - the gust push comes from the linked tiny WindGusts.</summary>
    private void SpawnDustCloud()
    {
        Vector3 b = buggy.transform.position;
        float dist = Random.Range(spawnMin, spawnMax * 0.6f);
        Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        devilPos = b + new Vector3(dir.x * dist, 0f, dir.z * dist);
        float y = terrain.SampleHeight(devilPos);
        if (y <= -100f) { Reschedule(); return; }
        devilPos.y = y + 0.4f;
        devilHeading = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

        devil = new GameObject("DustCloud");
        devil.transform.position = devilPos;

        float r = Random.Range(cloudRadius * 0.7f, cloudRadius);
        float h = Random.Range(2.6f, 4.2f);
        float cy = h * 0.35f;

        // Body: a few squashed translucent puffs staggered in a loose drift-line.
        for (int p = 0; p < 3; p++)
        {
            float pr = r * Random.Range(0.55f, 0.8f);
            GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            puff.name = "CloudPuff";
            puff.transform.SetParent(devil.transform, false);
            float offsetX = Mathf.Lerp(-r * 0.8f, r * 0.8f, p / 2f);
            puff.transform.localPosition = new Vector3(offsetX, cy, Random.Range(-1.2f, 1.2f));
            puff.transform.localScale = new Vector3(pr * 2f, h * 0.13f, pr * 2f);
            puff.GetComponent<MeshRenderer>().sharedMaterial = devilMat;
            DestroyImmediate(puff.GetComponent<Collider>());
        }

        // Slow swirl: a handful of loose dust motes circling the cloud body.
        devilSpin = new GameObject("DustSwirl").transform;
        devilSpin.SetParent(devil.transform, false);
        for (int c = 0; c < 6; c++)
        {
            float a = (c / 6f) * Mathf.PI * 2f;
            GameObject mote = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mote.name = "DustMote";
            mote.transform.SetParent(devilSpin, false);
            mote.transform.localPosition = new Vector3(Mathf.Cos(a) * r * 0.8f, cy + h * 0.4f, Mathf.Sin(a) * r * 0.8f);
            float cs = Random.Range(0.25f, 0.5f);
            mote.transform.localScale = new Vector3(cs, cs * Random.Range(0.3f, 0.7f), cs);
            mote.GetComponent<MeshRenderer>().sharedMaterial = devilMat;
            DestroyImmediate(mote.GetComponent<Collider>());
        }

        devilEndAt = Time.time + lifetime;
        if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.12f);
    }

    private void Reschedule()
    {
        nextSpawnAt = Time.time + Random.Range(calmMin, calmMax);
    }

    private void Dissolve()
    {
        if (devil != null) Destroy(devil);
        devil = null;
        devilSpin = null;
        nextSpawnAt = Time.time + Random.Range(calmMin, calmMax);
    }
}