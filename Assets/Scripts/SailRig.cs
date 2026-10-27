using UnityEngine;

/// <summary>
    /// The wingsail's brain. Runs only while the Favonius is the equipped vehicle, and only on
    /// the worlds that carry standing wind - Venus and the Mars ridge. Everywhere else it stays
    /// inert, because a wingsail has no business in vacuum.
    ///
/// Three jobs, once a physics step:
///
///  1. OWN THE WIND. There is no persistent breeze in the level yet (<see cref="WindGusts"/>
///     only throws discrete telegraphed shoves), so the rig keeps its own: a slow, wandering
///     crosswind in the 0.2-2.5 m/s band the reference console quotes, with the occasional
///     squall. <see cref="SetWind"/> lets a level or a test override it.
///
///  2. TRIM. The sail is worked automatically to the apparent wind, eased toward the target
///     rather than snapped, so the wing visibly swings across the stern as the run turns. The
///     target is the reference console's own autopilot rule: pull the apparent-wind bearing in
///     to 55% of its value and flap to windward. Driving it by hand would mean new input, and
///     this build wants the sail to be a reward, not a chore.
///
///  3. DRIVE. Lift and drag are resolved onto the rover's axes and whatever lands along forward
///     is applied as an acceleration (mass-normalised, so tuning is in m/s^2 and does not
///     shift if the hull mass is ever changed). The push tapers to nothing at top speed, so the
///     sail tops the rover out rather than launching it, and a fraction of the side force is
///     fed back as a roll moment so hard wind is felt in the cornering.
/// </summary>
public class SailRig : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("The pivot the sail hangs from. Its local Y rotation is the trim angle.")]
    public Transform sailPivot;
    [Tooltip("The trailing flap blade, deflected to show camber.")]
    public Transform sailFlap;

    [Header("Atmosphere")]
    [Tooltip("Venus surface density (kg/m^3). 66.8 at 462 C and 92.8 bar. The whole point of the " +
             "vehicle: a 0.85 m/s breeze here is a gale on Earth by a factor of fifty.")]
    public float airDensity = 66.8f;
    [Tooltip("Ambient breeze speed (m/s). Venus nominal is around 0.85.")]
    public float windSpeedMs = 0.85f;
    [Tooltip("How far the breeze wanders either side of nominal (m/s).")]
    public float windSpeedSwing = 0.45f;
    [Tooltip("Bearing the wind blows FROM, in degrees. 90 = from starboard, i.e. a pure crosswind " +
             "down the graded corridor, which is the sailing-friendly case.")]
    public float windFromDeg = 90f;
    [Tooltip("How far the bearing wanders either side (degrees).")]
    public float windSwingDeg = 55f;
    [Tooltip("Seconds between squall checks; a squall is a short, sharp gust of wind.")]
    public float squallMin = 4f;
    public float squallMax = 9f;
    [Tooltip("Peak extra breeze during a squall (m/s).")]
    public float squallStrength = 1.5f;

    [Header("Drive")]
    [Tooltip("Top acceleration the sail can deliver (m/s^2), before the vehicle's own " +
             "WindDriveMul. Around 6-7 is a strong shove without breaking traction.")]
    public float maxAccel = 6.5f;
    [Tooltip("Fraction of the side force fed back as a roll moment. Keeps the wind felt in " +
             "the corners without ever fighting the buggy's uprighting. Kept low - a tall wingsail " +
             "is easily rolled in a turn, and the Favonius already carries its mass low to settle it.")]
    public float heelGain = 0.04f;

    // ---- solved state, read-only for debugging and any future HUD
    public float ApparentWindMs { get; private set; }
    public float ApparentAngleDeg { get; private set; }
    public float TrimDeg { get; private set; }
    public float FlapDeg { get; private set; }
    public float ThrustAccel { get; private set; }
    public bool Stalled { get; private set; }

    private BuggyController buggy;
    private Rigidbody rb;
    private float windTargetMs;
    private float windTargetFromDeg;
    private float squallUntil;
    private float nextSquallAt;
    private bool windOverridden;

    private void Start()
    {
        buggy = GetComponent<BuggyController>();
        rb = GetComponent<Rigidbody>();
        windTargetMs = windSpeedMs;
        windTargetFromDeg = windFromDeg;
        nextSquallAt = Time.time + Random.Range(squallMin, squallMax);
        TrimDeg = sailPivot != null ? sailPivot.localEulerAngles.y : 0f;
    }

    /// <summary>Lets a level, a test or a future weather system drive the wind directly,
    /// switching off the wandering breeze and squalls for as long as it holds on.</summary>
    public void SetWind(float speedMs, float fromDeg)
    {
        windTargetMs = speedMs;
        windTargetFromDeg = fromDeg;
        windSpeedMs = speedMs;
        windFromDeg = fromDeg;
        windOverridden = true;
    }

    /// <summary>True on the worlds where The Favonius's wingsail can work at all: Venus, its home
    /// atmosphere, plus the Mars ridge, which genuinely carries standing wind. Returns false on
    /// the airless worlds (the Moon) and on the ones with no wind (Io).</summary>
    public static bool SailWorld(PlanetTheme theme, bool ridge)
        => theme == PlanetTheme.Venus || (theme == PlanetTheme.Mars && ridge);

    /// <summary>Tunes the rig to the world's air. Venus is the real deal: 92.8 bar at 462 C,
    /// 66.8 kg/m^3, where even a 0.85 m/s breeze is a gale. The Mars ridge has real standing
    /// wind but its true density is ~0.020 kg/m^3 - some 3300 times thinner - so an honest
    /// pressure sail there would make no thrust whatsoever. The rig therefore keeps the world's
    /// own (faster, thin-air) breeze for the look and the swing, but floors the density at a
    /// gameplay value so the rover is genuinely worth taking onto the ridge.</summary>
    public void ApplyAtmosphere(PlanetTheme theme, bool ridge)
    {
        if (theme == PlanetTheme.Mars)
        {
            airDensity = 45f;      // floored from the true ~0.020
            windSpeedMs = 1.3f;    // thin air needs a harder breeze to make the same pressure
            windSpeedSwing = 0.5f;
            squallStrength = 1.2f;
        }
        // Venus keeps the field defaults, which are already its real numbers.
        windTargetMs = windSpeedMs;
        windTargetFromDeg = windFromDeg;
    }

    private void FixedUpdate()
    {
        if (buggy == null || rb == null) return;
        ThrustAccel = 0f;
        Stalled = false;

        // The breeze is only meaningful while the rover is actually being driven; stay quiet
        // through the intro, the boarding sequence and the end cutscene.
        if (!buggy.inputEnabled) { ApplyTrim(0f, 0f); return; }

        Vector3 wind = WanderWind();
        Vector3 apwind = SailPhysics.ApparentWind(wind, rb.linearVelocity, transform);

        ApparentWindMs = apwind.magnitude;
        ApparentAngleDeg = SailPhysics.Solve(apwind, airDensity, 0f, 0f).apparentAngleDeg;

        // Work the sail, then solve with the trim it actually ended up at, so what is drawn and
        // what is applied can never disagree.
        float trimTarget = SailPhysics.AutopilotTrim(ApparentAngleDeg);
        float flapTarget = SailPhysics.AutopilotFlap(ApparentAngleDeg);
        ApplyTrim(trimTarget, flapTarget);
        SailPhysics.Result r = SailPhysics.Solve(apwind, airDensity, TrimDeg, FlapDeg);
        Stalled = r.stalled;

        float drive = VehicleSystem.WindDriveMul;
        if (drive <= 0f) return;

        // Thrust into the rover's nose, tapering to nothing at top speed so the sail extends the
        // rev range instead of acting as a bulldozer. Uses the same mass-normalised AddForce
        // idiom as the buggy's own braking, so it is an acceleration and not a force.
        float maxMs = Mathf.Max(buggy.maxSpeedKmh / 3.6f, 1f);
        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float headroom = 1f - Mathf.Clamp01(Mathf.Max(forwardSpeed, 0f) / maxMs);
        if (headroom <= 0.001f) return;

        float accel = Mathf.Min(r.forwardThrust / Mathf.Max(rb.mass, 1f), maxAccel) * drive * headroom;
        ThrustAccel = accel;
        rb.AddForce(transform.forward * accel * rb.mass, ForceMode.Force);

        // A little of the side force as roll, so a hard crosswind is felt through the seat.
        if (Mathf.Abs(r.sideForce) > 0.01f)
            rb.AddTorque(transform.forward * -r.sideForce * heelGain, ForceMode.Force);
    }

    /// <summary>Drifts the breeze and throws the occasional squall, then converts "wind from
    /// <paramref name="fromDeg"/>" into a velocity vector the air is travelling along.</summary>
    private Vector3 WanderWind()
    {
        if (!windOverridden)
        {
            if (Time.time >= nextSquallAt)
            {
                squallUntil = Time.time + Random.Range(0.8f, 2.2f);
                nextSquallAt = Time.time + Random.Range(squallMin, squallMax);
            }
            float baseMs = Mathf.Max(windTargetMs + Random.Range(-windSpeedSwing, windSpeedSwing), 0.05f);
            float squall = Time.time < squallUntil ? squallStrength : 0f;
            windSpeedMs = Mathf.Lerp(windSpeedMs, baseMs + squall, Time.fixedDeltaTime * 1.5f);
            float targetFrom = windTargetFromDeg + Random.Range(-windSwingDeg, windSwingDeg);
            windFromDeg = Mathf.LerpAngle(windFromDeg, targetFrom, Time.fixedDeltaTime * 0.8f);
        }

        // "From" bearing: the air travels the other way.
        float travel = (windFromDeg + 180f) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(travel), 0f, Mathf.Cos(travel)) * windSpeedMs;
    }

    /// <summary>Eases the wing toward the commanded trim and flaps the trailing edge, so the
    /// sail visibly works instead of snapping between angles.</summary>
    private void ApplyTrim(float trimTarget, float flapTarget)
    {
        float k = Time.fixedDeltaTime * 3.5f;
        TrimDeg = Mathf.LerpAngle(TrimDeg, trimTarget, k);
        FlapDeg = Mathf.Lerp(FlapDeg, flapTarget, k);
        if (sailPivot != null) sailPivot.localRotation = Quaternion.Euler(0f, TrimDeg, 0f);
        if (sailFlap != null) sailFlap.localRotation = Quaternion.Euler(0f, FlapDeg, 0f);
    }
}
