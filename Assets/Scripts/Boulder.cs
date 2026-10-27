using UnityEngine;

/// <summary>
/// Marker placed on big rock obstacles placed along the route. Only collisions with a
/// Boulder deal hull damage to the buggy (see BuggyHealth), so hitting terrain or the
/// ground does no damage. Allows per-boulder tuning of how punishing the impact is.
///
/// Race-hazard rocks (the tumbling boulders and overhang falls the RaceHazardDirector
/// throws onto canyon courses, flagged <see cref="raceHazard"/>) also run the near-miss
/// adrenaline detector: if the buggy sweeps PAST one with little clearance at speed -
/// and never actually hits it - the player gets a free surge of speed (BuggyController).
/// A real hit (BuggyHealth marks <see cref="hitByBuggy"/>) cancels the payout: survival on
/// a grazing touch is the whole point, and a crash paying a bonus would be insulting.
/// </summary>
public class Boulder : MonoBehaviour
{
    [Tooltip("Damage multiplier applied on top of the buggy's impact-speed damage. 1 is default.")]
    public float impactMultiplier = 1f;

    [Tooltip("True for boulders spawned during canyon-RACE hazard falls (the ones the buggy "
            + "dodges). Only these can grant the near-miss adrenaline surge.")]
    public bool raceHazard = false;

    [Tooltip("True once the player's buggy has actually HIT this rocket - a hit is no near-miss.")]
    public bool hitByBuggy = false;

    [Tooltip("Approximate rock radius (metres) for the near-miss clearance math; set by the "
            + "spawner that builds the rock shape.")]
    public float radius = 0f;

    [Header("Near-miss adrenaline")]
    [Tooltip("Passing with this little lateral CLEARANCE (metres, centre-to-rock-edge minus the "
            + "buggy's half-width) counts as a near miss - close enough to feel the whoosh.")]
    public float nearMissClearanceMax = 4.0f;
    [Tooltip("Minimum buggy speed (km/h) before a near miss pays out - dodging a parked rock at "
            + "a crawl is no surge.")]
    public float nearMissMinSpeedKmh = 14f;
    [Tooltip("Seconds before ANY hazard rock can pay out again; stops a boulder field chaining "
            + "surges into a permanent rocket ship.")]
    public float nearMissGlobalCooldown = 6f;

    // Per-rock trackers: the closest (smallest) clearance seen on the pass in, and the last
    // moment the buggy was actually inside the near band (to settle whether the pass is done).
    private float minClearance = float.MaxValue;
    private float nearWindow = -10f;

    /// <summary>Applies a low-friction, slightly bouncy physics material to every collider of a
    /// rock so wheels rebound off it instead of sticking/pinning the buggy on impact.</summary>
    public static void MakeSlick(GameObject rock)
    {
        PhysicsMaterial mat = new PhysicsMaterial("RockSlick");
        mat.dynamicFriction = 0.05f;
        mat.staticFriction = 0.1f;
        mat.frictionCombine = PhysicsMaterialCombine.Minimum;
        mat.bounciness = 0.45f;
        mat.bounceCombine = PhysicsMaterialCombine.Average;

        Collider[] cols = rock.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++) cols[i].sharedMaterial = mat;
    }

    private void Update()
    {
        // Only live hazard rocks on a race run dodge; a hit (or a non-race boulder) never pays.
        if (!raceHazard || hitByBuggy) return;
        BuggyController pc = RivalRacer.PlayerBuggy;
        if (pc == null) return;

        // Horizontal distance from the player's centre to ours; the clearance is how much room
        // really passed underneath (~1.5m = the buggy's half-width) after the rock's own radius.
        Vector3 delta = pc.transform.position - transform.position;
        delta.y = 0f;
        float d = delta.magnitude;
        float trackLimit = nearMissClearanceMax + radius + 1.5f;
        float clearance = d - radius - 1.5f;

        if (d < trackLimit)
        {
            if (clearance < minClearance) minClearance = clearance;
            nearWindow = Time.time;
        }
        else if (Time.time - nearWindow > 0.4f)
        {
            // Left the near band for more than a moment - the pass (if any) is settled.
            minClearance = float.MaxValue;
        }

        // Nothing climbed toward a near pass yet, or the buggy is still closing.
        if (minClearance >= nearMissClearanceMax) return;
        if (d <= trackLimit + 1.5f) return;     // award once the buggy is clearly moving away

        // Pass settled: closest approach was inside the near band (and not an actual hit), the
        // buggy was moving, and the last surge has worn off.
        if (minClearance >= 0.05f
            && pc.CurrentSpeedKmh >= nearMissMinSpeedKmh
            && Time.time - pc.LastAdrenalineAt >= nearMissGlobalCooldown)
        {
            pc.TriggerAdrenaline();

            // A bright, non-dim flash (ScorePopup floats on its own overlay canvas and never
            // shades the frame like the hint banner would - the surge should feel like speed,
            // not a fade-down).
            ScorePopup.Spawn(pc.transform.position + Vector3.up * 1.5f, "ADRENALINE SURGE!",
                new Color(0.4f, 0.95f, 1f, 1f), 44);
            if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.35f);
            RoverSounds.PlayTokenClink(0.9f);
        }

        // Whatever happened, this rock has handed out its shot for this pass.
        minClearance = float.MaxValue;
    }
}