using System.Collections;
using UnityEngine;

/// <summary>
/// Venus ridge thunder. While the survival run is live the storm strokes the crest AHEAD of
/// the buggy with lightning: each strike telegraphs with a pale blink disc (like the boulder /
/// collapse marks), then a bolt slams down. If the buggy is inside the blast radius when it
/// lands, the jolt HURTS the hull AND hard-stalls the car (all momentum lost, driven from
/// rest) - so a near-miss is survivable but costly, and a direct hit pins you in place.
/// GameBootstrap attaches this to the buggy on every Venus mode, not just the ridge.
/// </summary>
public class VenusLightning : MonoBehaviour
{
    [Header("References")]
    public BuggyController buggy;
    public BuggyHealth hull;
    public MoonTerrainGen terrain;
    [Tooltip("Fall along the ridge crest band (the only drivable line up there). Off the ridge " +
             "the buggy roams open ground, so bolts aim across its own line instead.")]
    public bool usesRidgeCrest = true;

    [Header("Pacing")]
    [Tooltip("Random gap between strikes while the run is live (seconds).")]
    public float strikeMin = 6f;
    public float strikeMax = 12f;
    [Tooltip("How long the blink disc telegraphs before the bolt lands (seconds). Strike distance is "
            + "BuggyController.ReachDistance(telegraphTime) based: the bolt lands 0.6-1.05x of what "
            + "the buggy's upgraded Engine/Boost top speed covers during the telegraph (clamped 8m "
            + "minimum) - closer than the old fixed band, and scaled by speed so a faster buggy has "
            + "proportionally more ground to cover.")]
    public float telegraphTime = 2.0f;

    [Header("Impact")]
    [Tooltip("Horizontal radius (m) around the strike point that still catches the buggy. The "
            + "blink warning disc stays its current small size so skimming it is survivable - the "
            + "true catch radius is this, kept off the warning.")]
    public float blastRadius = 9f;
    [Tooltip("Direct-hit hull damage at the bolt centre (scaled like a meteor: falls off to 0 at "
            + "the blast edge, so a grazing jolt is minor but a dead-centre hit matches a meteor).")]
    public float maxBlastDamage = 30f;
    [Tooltip("Seconds the buggy is hard-stopped by a dead-centre jolt (scales with hit distance).")]
    public float stallSeconds = 0.6f;

    private float nextStrike = 2f;
    private float strikeAt;
    private Vector3 strikePoint;
    private bool telegraphed;
    private GameObject mark;

    private void Update()
    {
        if (buggy == null || hull == null || terrain == null) return;
        // The storm only bites once the run is actually live: the intro and pre-leg countdown
        // hold input off (same gate as VenusPressure), and a dead run never gets zapped.
        if (!buggy.inputEnabled || hull.IsDestroyed)
        {
            ClearMark();
            return;
        }

        if (!telegraphed)
        {
            if (Time.time >= nextStrike) Telegraph();
        }
        else if (Time.time >= strikeAt)
        {
            Strike();
        }
    }

    private void Telegraph()
    {
        telegraphed = true;
        // The bolt lands a fraction of the REACHABLE distance ahead: closer than the old fixed
        // 18-85m band, and scaled by the buggy's upgraded Engine/Boost top speed so a faster car
        // has to cover proportionally more ground during the telegraph to clear the disc while a
        // slower one still gets a fair, closer dodge.
        float reach = buggy.ReachDistance(telegraphTime, 1f);
        float z = buggy.transform.position.z + Mathf.Max(8f, Random.Range(reach * 0.6f, reach * 1.05f));
        float x;
        if (usesRidgeCrest)
        {
            // The ridge funnels the buggy onto one crest band, so the bolts fall along it.
            float half = Mathf.Max(2f, terrain.raceRidgeCrestHalfWidth * 0.85f);
            x = terrain.RaceCenterX(z) + Random.Range(-half, half) * (0.5f - Random.value);
        }
        else
        {
            // Survey and canyon runs are not funnelled into a lane, so there is no course centre to
            // fall along. Aim across the buggy's own line instead - the bolt still lands ahead of
            // it, which is what keeps the telegraph readable and the dodge fair.
            float half = Mathf.Max(6f, terrain.raceCourseHalfWidth * 0.85f);
            x = buggy.transform.position.x + Random.Range(-half, half) * (0.5f - Random.value);
        }
        strikePoint = new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)), z);
        strikeAt = Time.time + telegraphTime;

        mark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mark.name = "LightningMark";
        mark.transform.position = strikePoint + Vector3.up * 0.18f;
        mark.transform.localScale = new Vector3(7f, 0.08f, 7f);
        Material mat = HazardMarkPulse.MakeBlinkMaterial(
            new Color(0.62f, 0.58f, 1f, 0.34f),
            new Color(0.5f, 0.45f, 1f));
        mark.GetComponent<MeshRenderer>().sharedMaterial = mat;
        DestroyImmediate(mark.GetComponent<Collider>());
        mark.AddComponent<HazardMarkPulse>();
        if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.12f);
    }

    private void Strike()
    {
        ClearMark();
        telegraphed = false;
        nextStrike = Time.time + Random.Range(strikeMin, strikeMax);

        SpawnBolt(strikePoint);
        RoverSounds.PlayLightningStrike(strikePoint, 1f);
        if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.5f);

        // Damage radius is horizontal: a bolt that lands beside the buggy - even one crest
        // side away - still jolts it, so the telegraph matters and jumping the disc clears it.
        // Damage FALLS OFF from the centre like a direct meteor hit (30 dead-centre, like the
        // storm's maxBlastDamage) to nothing at the blast edge, so a grazing jolt is minor but a
        // dead-centre hit nearly wrecks the hull AND stops the buggy dead. The blink disc is NOT
        // resized to this radius - it stays the small warning size, so it reads fair up close.
        Vector3 bp = buggy.transform.position;
        float dx = bp.x - strikePoint.x;
        float dz = bp.z - strikePoint.z;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        if (d <= blastRadius)
        {
            float k = Mathf.Clamp01(1f - d / blastRadius);

            // The vehicle decides how much of a bolt it cares about. The Favonius's grounded
            // titanium sphere is effectively immune: a direct strike chips the paint and the
            // car keeps rolling, where anything on wheels would be pinned and restarted from
            // rest. Resilient vehicles still get the flash, scaled to how much it did bother them.
            float dmg = maxBlastDamage * k * VehicleSystem.LightningDamageMul;
            float stall = stallSeconds * k * VehicleSystem.LightningStallMul;
            float severity = Mathf.Clamp01(k * (dmg + stall * 30f) / (maxBlastDamage + 0.5f));

            if (dmg > 0.01f) hull.TakeDamage(dmg);
            if (stall > 0.01f) buggy.StallFromStrike(stall);
            if (severity > 0.05f) AddHitGlow(buggy.transform);
            if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.15f + 0.85f * severity);
        }
    }

    /// <summary>An electric glow globe that mounts on the buggy for a moment after a direct hit:
    /// a pale blue fizz that flashes wide then fades while the car is stalled. Cosmetic only,
    /// parents to the buggy so it rides along during the jolt.</summary>
    private void AddHitGlow(Transform host)
    {
        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "LightningHitGlow";
        glow.transform.SetParent(host, false);
        glow.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        Material mat = new Material(Shader.Find("Standard"));
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.55f, 0.75f, 1f) * 2.4f);
        mat.color = new Color(0.55f, 0.75f, 1f, 0.4f);
        glow.GetComponent<MeshRenderer>().sharedMaterial = mat;
        DestroyImmediate(glow.GetComponent<Collider>());
        StartCoroutine(GlowFadeRoutine(glow, mat));
    }

    private static IEnumerator GlowFadeRoutine(GameObject glow, Material mat)
    {
        float t0 = 0f;
        float duration = 0.45f;
        while (t0 < duration)
        {
            float k = t0 / duration;
            glow.transform.localScale = Vector3.one * Mathf.Lerp(2.2f, 9f, k);
            Color c = mat.color;
            c.a = 0.4f * (1f - k);
            mat.color = c;
            t0 += Time.deltaTime;
            yield return null;
        }
        Destroy(glow);
    }

    /// <summary>A brief electric bolt from the storm deck down to the ridge plus a flash light,
    /// then everything is cleaned up automatically.</summary>
    private static void SpawnBolt(Vector3 ground)
    {
        float cloudY = ground.y + 36f;
        Vector3 mid = new Vector3(ground.x, (ground.y + cloudY) * 0.5f, ground.z);

        GameObject bolt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bolt.name = "LightningBolt";
        bolt.transform.position = mid;
        bolt.transform.localScale = new Vector3(0.45f, (cloudY - ground.y) * 0.5f, 0.45f);
        bolt.transform.rotation = Quaternion.Euler(
            Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(-10f, 10f));
        Material boltMat = new Material(Shader.Find("Standard"));
        boltMat.SetFloat("_Mode", 3f);
        boltMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        boltMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        boltMat.SetInt("_ZWrite", 0);
        boltMat.DisableKeyword("_ALPHATEST_ON");
        boltMat.EnableKeyword("_ALPHABLEND_ON");
        boltMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        boltMat.EnableKeyword("_EMISSION");
        boltMat.SetColor("_EmissionColor", new Color(0.65f, 0.75f, 1f) * 2.2f);
        boltMat.color = new Color(0.85f, 0.9f, 1f, 0.95f);
        bolt.GetComponent<MeshRenderer>().sharedMaterial = boltMat;
        DestroyImmediate(bolt.GetComponent<Collider>());
        Destroy(bolt, 0.28f);

        GameObject glow = new GameObject("LightningFlash");
        Light l = glow.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(0.7f, 0.8f, 1f);
        l.intensity = 7f;
        l.range = 30f;
        glow.transform.position = ground + Vector3.up * 7f;
        Destroy(glow, 0.18f);
    }

    private void ClearMark()
    {
        if (mark != null) { Destroy(mark); mark = null; }
    }
}