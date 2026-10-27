using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Moon's active danger (see LevelDefinition.danger): a transient meteor barrage woven
/// into the Harvester route. A storm is telegraphed three ways before it bites - the HUD
/// flashes a warning banner, every incoming meteor paints a glowing red target ring on the
/// ground seconds before it hits, and the streak itself falls visibly fast - so the player
/// can always drive out of the blast radius if they read the terrain.
///
/// GameBootstrap creates one of these per planet whose LevelDefinition declares Meteor.
/// It only runs while the Harvester round is actually playable: game-over and the boarding
/// cinematic both freeze the storm so nothing ruins the landing.
/// </summary>
public class MeteorStorm : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The buggy the storm aims at and damages.")]
    public BuggyController buggy;
    [Tooltip("The terrain the meteors strike (for surface heights and keeping inside the arena).")]
    public MoonTerrainGen terrain;
    [Tooltip("The HUD that shows the storm warning banner.")]
    public HUD hud;
    [Tooltip("The oxygen manager - the storm freezes when the run ends.")]
    public OxygenManager oxygen;
    [Tooltip("When true the cadence is owned elsewhere (RaceHazardDirector): the director calls "
            + "BeginStorm/EndStorm explicitly and this storm never starts one on its own.")]
    public bool manualControl = false;

    [Header("Storm cadence (seconds)")]
    [Tooltip("How long after the run starts before the first storm rolls in.")]
    public float firstStormDelay = 16f;
    [Tooltip("Minimum calm time between storms.")]
    public float calmMin = 12f;
    [Tooltip("Maximum calm time between storms.")]
    public float calmMax = 26f;
    [Tooltip("How long the warning banner shows before the first meteor of a storm lands.")]
    public float warningLead = 3.2f;
    [Tooltip("How long a barrage keeps firing volleys.")]
    public float barrageDuration = 14f;

    [Header("Meteors")]
    [Tooltip("How many volleys each storm fires before standing down.")]
    public int volleys = 10;
    [Tooltip("Maximum meteors per volley (each volley drops 1 to this many).")]
    public int maxMeteorsPerVolley = 3;
    [Tooltip("Scales the meteor AMOUNT a volley drops: 1 = the stock barrage, 0.75 = a "
            + "quarter lighter, 2 = twice as many. The Moon's friendlier first harvest and "
            + "the harvest-grade storm ramp tune this in GameBootstrap.")]
    public float meteorMultiplier = 1f;
    [Tooltip("Shortest random gap between volleys while a barrage is running (seconds). "
            + "Lower = denser meteor field; tune per course in GameBootstrap.")]
    public float volleyGapMin = 1.0f;
    [Tooltip("Longest random gap between volleys while a barrage is running (seconds).")]
    public float volleyGapMax = 1.8f;
    [Tooltip("Fraction of meteors aimed ahead of the rover in a fan; the rest scatter all around.")]
    public float aimAheadChance = 0.8f;
    [Tooltip("Half-angle (degrees) of the forward fan meteors land inside, either side of the rover heading.")]
    public float aimConeHalfDegrees = 75f;
    [Tooltip("Time between the target ring appearing and the meteor spawning above it.")]
    public float telegraphTime = 1.4f;
    [Tooltip("Seconds a streaking meteor takes to fall from spawn height to the surface.")]
    public float meteorTravelTime = 1.0f;
    [Tooltip("Height above the impact point a meteor spawns and starts falling from.")]
    public float meteorSpawnHeight = 190f;
    [Tooltip("How far from the buggy a meteor's impact point is placed (min metres).")]
    public float targetRangeMin = 15f;
    [Tooltip("How far from the buggy a meteor's impact point is placed (max metres).")]
    public float targetRangeMax = 55f;

    [Header("Blast")]
    [Tooltip("Radius of the damaging blast (and the size of the target ring).")]
    public float blastRadius = 12f;
    [Tooltip("Hull damage a dead-centre blast deals (falls off to 0 at the blast edge).")]
    public float maxBlastDamage = 30f;
    [Tooltip("Impact range is BuggyController.ReachDistance(telegraph+travel) based: strikes land "
            + "0.7-1.25x of what the buggy's upgraded Engine/Boost top speed covers from the target "
            + "ring to impact, clamped to targetRangeMin/Max so a faster buggy gets more ground to "
            + "cover and a slower one keeps a fair, closer dodge.")]

    private enum Phase { Calm, Barrage }

    private struct ActiveMeteor
    {
        public Vector3 impact;     // world-space impact point (XZ surface)
        public float ringAt;       // time the target ring appears
        public float spawnAt;      // time the streak spawns above the impact
        public bool ringBuilt;
        public Transform ring;
        public Transform streak;
    }

    private Phase phase = Phase.Calm;
    private float phaseTimer;
    private float nextVolleyAt;
    private int volleysFired;
    private bool warningShowing;
    private readonly List<ActiveMeteor> active = new List<ActiveMeteor>();
    private readonly List<Transform> scorches = new List<Transform>();

    private void Start()
    {
        if (manualControl) phaseTimer = float.MaxValue; // director owns cadence
        else phaseTimer = firstStormDelay;
    }

    private void Update()
    {
        if (buggy == null) return;
        if (!buggy.inputEnabled) return;
        if (oxygen == null) oxygen = OxygenManager.Instance;

        // The storm stands down the moment a run stops being drivable (game over, win banner,
        // or the boarding cinematic playing). Any half-fallen meteors and their rings vanish.
        if (oxygen != null && (oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive))
        {
            Freeze();
            return;
        }

        switch (phase)
        {
            case Phase.Calm:
                phaseTimer -= Time.deltaTime;
                // Only the free-running cadence auto-begins; a manually driven storm waits for
                // the director's BeginStorm call.
                if (!manualControl && phaseTimer <= 0f) BeginStorm();
                break;

            case Phase.Barrage:
                phaseTimer -= Time.deltaTime;
                if (Time.time >= nextVolleyAt) FireVolley();
                if (phaseTimer <= 0f && volleysFired >= volleys) EndStorm();
                break;
        }

        UpdateMeteors();
    }

    /// <summary>True while this storm is actively barraging. The RaceHazardDirector waits on
    /// this to know when a manually-started barrage has finished.</summary>
    public bool BarrageActive => phase == Phase.Barrage;

    /// <summary>Opens a barrage. Under manual control this is the director's trigger; the storm
    /// still paints the warning banner and plays the storm alert itself.</summary>
    public void BeginStorm()
    {
        phase = Phase.Barrage;
        volleysFired = 0;
        phaseTimer = barrageDuration;
        nextVolleyAt = Time.time + warningLead;
        warningShowing = true;
        if (hud != null) hud.ShowWarning("METEOR STORM INBOUND");
        RoverSounds.PlayStormAlert();
    }

    private void FireVolley()
    {
        volleysFired++;
        int count = Random.Range(1, maxMeteorsPerVolley + 1);
        if (meteorMultiplier != 1f)
        {
            // Scale the AMOUNT the stock barrage would drop (not the spread): 0.75 turns an
            // average 2-meteor volley into ~1.5, so "a quarter lighter" is exact.
            count = Mathf.Max(1, Mathf.RoundToInt(count * meteorMultiplier));
        }
        for (int i = 0; i < count; i++) SpawnMeteor();
        nextVolleyAt = Time.time + Random.Range(volleyGapMin, volleyGapMax);
    }

    private void EndStorm()
    {
        phase = Phase.Calm;
        // A manually driven storm surrenders its cadence back to the director (never re-begins).
        phaseTimer = manualControl ? float.MaxValue : Random.Range(calmMin, calmMax);
        HideWarning();
    }

    private void Freeze()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (active[i].ring != null) Destroy(active[i].ring.gameObject);
            if (active[i].streak != null) Destroy(active[i].streak.gameObject);
        }
        active.Clear();
        phase = Phase.Calm;
        phaseTimer = float.MaxValue; // never resumes; the run is over
        HideWarning();
    }

    private void SpawnMeteor()
    {
        Vector3 b = buggy.transform.position;
        Vector3 forward = buggy.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        // Mostly land AHEAD of the rover in a fan either side of its heading, so the strikes
        // come down in the path the player is actually driving and demand a real dodge. A
        // minority still scatters all around so hugging the throttle can't avoid everything.
        bool ahead = Random.value < aimAheadChance;
        float angle = ahead
            ? Random.Range(-aimConeHalfDegrees, aimConeHalfDegrees) * Mathf.Deg2Rad
            : Random.Range(0f, 360f) * Mathf.Deg2Rad;
        // Impact range scales with the buggy's Engine/Boost top speed so the ground it can
        // cover between the target ring and the blast stays a fair dodge: slower buggies get
        // strikes closer, upgraded ones earn strikes farther ahead.
        float reach = buggy.ReachDistance(telegraphTime + meteorTravelTime, 1f);
        float range = Mathf.Clamp(Random.Range(reach * 0.7f, reach * 1.25f), targetRangeMin, targetRangeMax);
        Vector3 dir = ahead
            ? Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f) * forward
            : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 at = b + dir * range;

        // Clamp into the drivable arena so there is always a surface to strike.
        if (terrain != null)
        {
            float ext = terrain.extent - 30f;
            at.x = Mathf.Clamp(at.x, -ext, ext);
            at.z = Mathf.Clamp(at.z, -ext, ext);
        }
        at.y = terrain != null ? terrain.SampleHeight(at) : 0f;

        ActiveMeteor m;
        m.impact = at;
        m.ringAt = Time.time;                 // the ring paints immediately
        m.spawnAt = Time.time + telegraphTime; // the streak follows after the telegraph
        m.ringBuilt = false;
        m.ring = null;
        m.streak = null;
        active.Add(m);
    }

    private void UpdateMeteors()
    {
        if (active.Count == 0) return;

        for (int i = active.Count - 1; i >= 0; i--)
        {
            ActiveMeteor m = active[i];
            bool changed = false;

            if (!m.ringBuilt && Time.time >= m.ringAt)
            {
                m.ringBuilt = true;
                m.ring = BuildTargetRing(m.impact);
                changed = true;
            }

            if (m.streak == null && Time.time >= m.spawnAt)
            {
                m.streak = BuildStreak(m.impact);
                changed = true;
                RoverSounds.PlayMeteorWhistle(m.streak.position, 0.9f);
            }

            if (m.streak != null)
            {
                float u = (Time.time - m.spawnAt) / Mathf.Max(0.001f, meteorTravelTime);
                if (u >= 1f)
                {
                    Impact(m);
                    if (m.ring != null) Destroy(m.ring.gameObject);
                    Destroy(m.streak.gameObject);
                    active.RemoveAt(i);
                    continue;
                }
                // Fall straight down from the spawn height onto the telegraph point.
                Vector3 start = m.impact + Vector3.up * meteorSpawnHeight;
                m.streak.position = Vector3.Lerp(start, m.impact, u);
                m.streak.rotation = Quaternion.LookRotation((m.impact - m.streak.position), Vector3.right);
            }

            if (changed) active[i] = m;
        }
    }

    private void Impact(ActiveMeteor m)
    {
        Vector3 p = m.impact;
        BuildImpactFlash(p);

        float dist = buggy != null ? Vector3.Distance(buggy.transform.position, p) : p.y;
        if (BuggyCamera.Instance != null)
        {
            BuggyCamera.Instance.AddShake(Mathf.Clamp01(1f - dist / (blastRadius * 3f)));
        }
        RoverSounds.PlayMeteorImpact(p, Mathf.Clamp01(1f - dist / (blastRadius * 5f)));

        // Hull damage only inside the blast, strongest at the centre; a near miss rattles the
        // buggy but never touches it. Rogues and pods are untouched - the storm is a pressure
        // on the rover, not a level-destroyer.
        if (dist < blastRadius && buggy != null)
        {
            BuggyHealth hp = buggy.GetComponent<BuggyHealth>();
            if (hp != null)
            {
                float k = 1f - dist / blastRadius;
                hp.TakeDamage(maxBlastDamage * k);
            }

            Rigidbody rb = buggy.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                Vector3 away = buggy.transform.position - p;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f)
                {
                    rb.linearVelocity += away.normalized * (7f * (1f - dist / blastRadius));
                }
            }
        }

        BuildScorch(p);
        // The blast pressure BOUNCES the rivals sideways off line (a nudge, not a slow) -
        // they ride the same course and take a knock without losing pace to the burn.
        RivalRacer.KickNear(p, blastRadius, 1f);
    }

    // ------------------------------------------------------------- meteor pieces

    /// <summary>A translucent red disc on the ground flashing like a spotlight where a meteor
    /// is coming down. Kept light and see-through so impact zones never hide the terrain.</summary>
    private Transform BuildTargetRing(Vector3 impact)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "MeteorTarget";
        go.transform.position = new Vector3(impact.x, impact.y + 0.15f, impact.z);
        go.transform.localScale = new Vector3(blastRadius * 2f, 0.08f, blastRadius * 2f);
        Material mat = HazardMarkPulse.MakeBlinkMaterial(
            new Color(1f, 0.3f, 0.14f, 0.30f),
            new Color(1f, 0.16f, 0.05f));
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        DestroyImmediate(go.GetComponent<Collider>());
        go.AddComponent<HazardMarkPulse>();
        return go.transform;
    }

    /// <summary>A hot stretched streak that falls fast toward its target point.</summary>
    private Transform BuildStreak(Vector3 impact)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "MeteorStreak";
        go.transform.position = impact + Vector3.up * meteorSpawnHeight;
        go.transform.localScale = new Vector3(1.7f, 4.6f, 1.7f);

        Material mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(1f, 0.68f, 0.3f, 1f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(2.2f, 0.9f, 0.25f));
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        DestroyImmediate(go.GetComponent<Collider>());

        // A trailing ember behind the head for the streaking read.
        GameObject tail = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tail.name = "Tail";
        tail.transform.SetParent(go.transform, false);
        tail.transform.localPosition = new Vector3(0f, -3.2f, 0f);
        tail.transform.localScale = new Vector3(0.55f, 1.6f, 0.55f);
        Material tailMat = new Material(Shader.Find("Standard"));
        tailMat.color = new Color(1f, 0.4f, 0.12f, 1f);
        tailMat.EnableKeyword("_EMISSION");
        tailMat.SetColor("_EmissionColor", new Color(1f, 0.3f, 0.08f));
        tail.GetComponent<MeshRenderer>().sharedMaterial = tailMat;
        DestroyImmediate(tail.GetComponent<Collider>());

        return go.transform;
    }

    /// <summary>A fast flash + regolith puff at the blast point, expanding then destroyed.</summary>
    private void BuildImpactFlash(Vector3 p)
    {
        GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.name = "MeteorFlash";
        flash.transform.position = new Vector3(p.x, p.y + 1.2f, p.z);
        MeshRenderer fMr = flash.GetComponent<MeshRenderer>();
        Material fMat = new Material(Shader.Find("Standard"));
        fMat.color = new Color(1f, 0.7f, 0.25f, 1f);
        fMat.EnableKeyword("_EMISSION");
        fMat.SetColor("_EmissionColor", new Color(2.5f, 1.1f, 0.3f));
        fMr.sharedMaterial = fMat;
        DestroyImmediate(flash.GetComponent<Collider>());
        StartCoroutine(FlashRoutine(flash.transform, blastRadius * 1.6f, 0.14f, true));

        GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        puff.name = "MeteorDust";
        puff.transform.position = new Vector3(p.x, p.y + 0.8f, p.z);
        MeshRenderer pMr = puff.GetComponent<MeshRenderer>();
        Material pMat = new Material(Shader.Find("Standard"));
        pMat.color = new Color(0.42f, 0.4f, 0.38f, 1f);
        pMr.sharedMaterial = pMat;
        DestroyImmediate(puff.GetComponent<Collider>());
        StartCoroutine(FlashRoutine(puff.transform, blastRadius * 1.3f, 0.55f, false));
    }

    /// <summary>Scales an impact body up over <paramref name="duration"/> then destroys it.</summary>
    private static IEnumerator FlashRoutine(Transform t, float finalScale, float duration, bool flatten)
    {
        float t0 = 0f;
        while (t0 < duration)
        {
            float k = t0 / duration;
            float s = Mathf.Lerp(0.4f, finalScale, k);
            float y = flatten ? s * 0.5f : s;
            t.localScale = new Vector3(s, Mathf.Max(y, 0.2f), s);
            t0 += Time.deltaTime;
            yield return null;
        }
        Destroy(t.gameObject);
    }

    /// <summary>A small dark scorched disc left where the meteor hit; lingers all level.
    /// Kept subtle (small and faint) so frequent storms don't carve the terrain into a mess.</summary>
    private void BuildScorch(Vector3 p)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "MeteorScorch";
        go.transform.position = new Vector3(p.x, p.y + 0.04f, p.z);
        go.transform.localScale = new Vector3(3.2f, 0.05f, 3.2f);
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.13f, 0.12f, 0.11f, 1f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", 0f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        DestroyImmediate(go.GetComponent<Collider>());

        scorches.Add(go.transform);
        if (scorches.Count > 60)
        {
            Destroy(scorches[0].gameObject);
            scorches.RemoveAt(0);
        }
    }

    private void HideWarning()
    {
        if (!warningShowing) return;
        warningShowing = false;
        if (hud != null) hud.HideWarning();
    }
}