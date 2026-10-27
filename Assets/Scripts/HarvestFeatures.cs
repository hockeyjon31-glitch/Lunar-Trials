using UnityEngine;

/// <summary>Kind of survey feature: a volcanic vent, a liquid methane lake or a mud flat.</summary>
public enum HarvestFeatureKind
{
    None,        // no feature at this point
    MagmaPool,   // Io: mini volcano with a glowing orange crater pool
    MethaneLake, // Titan: liquid methane pond
    MudPatch     // Titan: darker brown slurry that grabs the wheels ("stuck in the mud")
}

/// <summary>A persistent survey-ground feature the player detours to: volcanic vent/magma pool
/// on Io, liquid methane lake on Titan. Each carries a surface hazard (the fluids cut grip and
/// damage the hull while the buggy is inside) and its own ring of harvestables.</summary>
public sealed class HarvestFeature
{
    public HarvestFeatureKind kind;
    public Vector3 center;
    public float radius;    // surface-disc radius (pool or lake)
    public float traction;  // grip multiplier while inside
    public float speedCap;  // top-speed multiplier while inside
    public float dps;       // hull damage per second while inside
    public float tick;      // damage tick interval (seconds)
    public float tickAcc;
}

/// <summary>
/// Applies the pressure of every survey feature at once (mirrors the race director's zone
/// handling): the buggy takes the LOWEST grip/top-speed of any feature it is inside, and each
/// feature ticks its hull damage independently while the rover is in its disc.
/// </summary>
public class HarvestFeatureController : MonoBehaviour
{
    private readonly System.Collections.Generic.List<HarvestFeature> features =
        new System.Collections.Generic.List<HarvestFeature>();
    private BuggyController buggy;
    private BuggyHealth hull;

    private void Awake()
    {
        buggy = FindAnyObjectByType<BuggyController>();
        if (buggy != null) hull = buggy.GetComponent<BuggyHealth>();
    }

    public void Add(HarvestFeature f)
    {
        features.Add(f);
    }

    /// <summary>Returns the feature kind covering a world point, or None. Used by the surface
    /// spray emitter to fling the right coloured debris.</summary>
    public HarvestFeatureKind KindAt(Vector3 p)
    {
        for (int i = 0; i < features.Count; i++)
        {
            HarvestFeature f = features[i];
            float dx = p.x - f.center.x;
            float dz = p.z - f.center.z;
            if (dx * dx + dz * dz <= f.radius * f.radius) return f.kind;
        }
        return HarvestFeatureKind.None;
    }

    private void FixedUpdate()
    {
        if (buggy == null) { buggy = FindAnyObjectByType<BuggyController>(); if (buggy == null) return; }
        if (hull == null && buggy != null) hull = buggy.GetComponent<BuggyHealth>();

        Vector3 p = buggy.transform.position;
        float trac = 1f;
        float spd = 1f;
        bool any = false;
        for (int i = 0; i < features.Count; i++)
        {
            HarvestFeature f = features[i];
            float dx = p.x - f.center.x;
            float dz = p.z - f.center.z;
            if (dx * dx + dz * dz > f.radius * f.radius) continue;
            // Titan mud flats only grab the WHEELS: a mid-air hop clears the bog at full speed.
            // The fuel-pool/magma damage still ticks, so those stay dangerous in the air too.
            if (f.kind == HarvestFeatureKind.MudPatch && buggy.IsAirborne) continue;
            any = true;
            if (f.kind == HarvestFeatureKind.MudPatch)
            {
                // A vehicle that skates the slurry (the sprint) has its mud lifted back toward
                // clean ground by its mudResist, so it wallows far less in the Titan flats.
                float r = VehicleSystem.MudResist;
                trac = Mathf.Min(trac, Mathf.Lerp(f.traction, 1f, r));
                spd = Mathf.Min(spd, Mathf.Lerp(f.speedCap, 1f, r));
            }
            else
            {
                trac = Mathf.Min(trac, f.traction);
                spd = Mathf.Min(spd, f.speedCap);
            }
            if (hull != null && f.dps > 0f)
            {
                f.tickAcc += Time.fixedDeltaTime;
                while (f.tickAcc >= f.tick)
                {
                    hull.TakeDamage(f.dps * f.tick);
                    f.tickAcc -= f.tick;
                    if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.3f);
                }
            }
        }
        if (any)
        {
            buggy.tractionMultiplier = trac;
            buggy.speedCapMultiplier = spd;
        }
        else
        {
            buggy.tractionMultiplier = 1f;
            buggy.speedCapMultiplier = 1f;
        }
    }
}