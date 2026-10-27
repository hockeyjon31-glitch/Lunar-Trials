using UnityEngine;

/// <summary>
/// Static aerodynamics for the Favonius landsail. Ported from the supercritical-CO2 reference
/// console; every coefficient is a public const so it can be dialled in the Inspector against
/// the original app without touching the maths.
///
/// The flow is the ordinary three-step sail model:
///   1. APPARENT WIND - the true breeze plus the rover's own motion through it. Sailing downwind
///      on Venus is slower than sailing across it, which is what makes trim worth managing.
///   2. DYNAMIC PRESSURE - q = 0.5 * rho * v^2. Venus' atmosphere is ~65 kg/m^3 against Earth's
///      1.225, so a breeze that would be a dead calm at home is a working gale here.
///   3. LIFT AND DRAG - a symmetric NACA 0015 with a cambered trailing flap, linear up to the
///      +/-16 deg stall, then a post-stall blend that lets the force decay instead of growing
///      without bound at 90 deg.
///
/// The resultant of lift and drag is then projected onto the rover's own axes: whatever part
/// lands along forward is driving thrust, whatever part lands sideways is the heel force that
/// tries to tip the thing over.
/// </summary>
public static class SailPhysics
{
    // ---------------------------------------------------------------- rig geometry
    /// <summary>Wing area of the rigid sail (m^2).</summary>
    public const float SailArea = 12f;
    /// <summary>Aspect ratio - span / chord. A rigid wing is stubby, hence the high induced drag.</summary>
    public const float AspectRatio = 3.5f;
    /// <summary>Oswald efficiency factor for the induced-drag term.</summary>
    public const float SpanEfficiency = 0.8f;

    // ---------------------------------------------------------------- airfoil
    /// <summary>Angle of attack at which the wing separates (deg). The reference console
    /// quotes +/-16 for this airfoil.</summary>
    public const float StallAngleDeg = 16f;
    /// <summary>CL per degree of trailing-flap camber. A flap buys lift at the cost of drag.</summary>
    public const float FlapLiftPerDeg = 0.012f;
    /// <summary>CD per degree of flap - flap drag, which is what makes big deflections lossy.</summary>
    public const float FlapDragPerDeg = 0.0045f;
    /// <summary>Profile drag of the clean wing.</summary>
    public const float ProfileDrag = 0.025f;

    // ---------------------------------------------------------------- controls
    public const float MaxTrimDeg = 50f;
    public const float MaxFlapDeg = 35f;
    /// <summary>Fraction of the apparent-wind angle the autopilot trims to. Matches the
    /// reference console's recommendation, which lands the sail just off centreline for max
    /// drive without approaching the stall.</summary>
    public const float TrimRatio = 0.55f;
    /// <summary>Flap the autopilot sets alongside that trim, always to windward (deg).</summary>
    public const float AutopilotFlapDeg = 20f;

    /// <summary>One solved frame of sail state: coefficients, forces and the derived stall flag.</summary>
    public struct Result
    {
        public float apparentSpeed;
        public float apparentAngleDeg;
        public float angleOfAttackDeg;
        public float liftCoefficient;
        public float dragCoefficient;
        public float liftForce;
        public float dragForce;
        public float forwardThrust;
        public float sideForce;
        public bool stalled;
    }

    /// <summary>
    /// Ideal-gas density of the Venus atmosphere (kg/m^3) from temperature and pressure.
    /// CO2 molar mass 0.04401 kg/mol. At 462 C and 92.8 bar this returns about 66.8 - the
    /// figure that makes a 0.85 m/s breeze worth sailing on.
    /// </summary>
    public static float VenusDensity(float tempC, float pressureBar)
    {
        float kelvin = Mathf.Max(tempC + 273.15f, 1f);
        return (pressureBar * 100000f * 0.04401f) / (8.314f * kelvin);
    }

    /// <summary>Default surface conditions: 462 C, 92.8 bar. Matches the reference console.</summary>
    public static float DefaultDensity() => VenusDensity(462f, 92.8f);

    /// <summary>
    /// Apparent wind: the true breeze with the rover's velocity added in, because the air
    /// moves relative to a moving hull. <paramref name="windVector"/> is the direction the air
    /// is travelling TOWARD (so a wind FROM dead ahead is (0,0,-1) in world terms). Returns the
    /// apparent wind vector and the angle it comes from, measured off the rover's nose with
    /// positive to starboard.
    /// </summary>
    public static Vector3 ApparentWind(Vector3 windVector, Vector3 roverVelocity, Transform rover)
    {
        Vector3 apparent = windVector - roverVelocity;

        // Flatten: the sail is a horizontal wing and the run is graded, so vertical air motion
        // is not part of this model.
        Vector2 fwd2 = new Vector2(rover.forward.x, rover.forward.z).normalized;
        Vector2 right2 = new Vector2(rover.right.x, rover.right.z).normalized;
        Vector2 app2 = new Vector2(apparent.x, apparent.z);

        // "Comes from" is the reverse of where the air is going. Projecting that reverse onto the
        // rover's axes gives the bearing the player trims against.
        float along = -Vector2.Dot(app2, fwd2);
        float across = -Vector2.Dot(app2, right2);
        return new Vector3(across, 0f, along);
    }

    /// <summary>
    /// Solves the airfoil and resolves the forces into the rover's frame.
    /// <paramref name="apwind"/> must come from <see cref="ApparentWind"/>; its x is the
    /// across component, its z the along component. Trim and flap are in degrees.
    /// </summary>
    public static Result Solve(Vector3 apwind, float density, float trimDeg, float flapDeg)
    {
        Result r = new Result();
        r.apparentSpeed = apwind.magnitude;
        r.apparentAngleDeg = r.apparentSpeed > 0.01f
            ? Mathf.Atan2(apwind.x, apwind.z) * Mathf.Rad2Deg
            : 0f;
        r.angleOfAttackDeg = r.apparentAngleDeg - trimDeg;

        if (r.apparentSpeed < 0.01f)
        {
            r.liftCoefficient = 0f;
            r.dragCoefficient = ProfileDrag;
            r.stalled = false;
            return r;
        }

        float aDeg = r.angleOfAttackDeg;
        float aRad = aDeg * Mathf.Deg2Rad;
        float aAbs = Mathf.Abs(aDeg);

        // Lift: 2*pi*sin(a) while attached, blending into the flat-plate curve past the stall
        // so the force peaks near the stall and then falls away instead of peaking at 90 deg.
        float cl;
        if (aAbs <= StallAngleDeg)
        {
            cl = 2f * Mathf.PI * Mathf.Sin(aRad);
        }
        else
        {
            float t = Mathf.Clamp01((aAbs - StallAngleDeg) / (90f - StallAngleDeg));
            float clAtStall = 2f * Mathf.PI * Mathf.Sin(StallAngleDeg * Mathf.Deg2Rad);
            float clPlate = 2f * Mathf.Sin(aRad) * Mathf.Cos(aRad);
            cl = Mathf.Lerp(clAtStall, clPlate, t);
        }
        r.stalled = aAbs > StallAngleDeg;

        // The flap adds camber (lift) only while the flow is still attached - once the wing has
        // separated, deflecting the flap does very little, which is exactly when a skipper stops
        // cranking it.
        float flapEffect = r.stalled ? 0.25f : 1f;
        r.liftCoefficient = cl + flapDeg * FlapLiftPerDeg * flapEffect;

        // Drag: profile + induced (which is what actually caps landsail speed) + flap drag.
        float induced = r.liftCoefficient * r.liftCoefficient / (Mathf.PI * AspectRatio * SpanEfficiency);
        r.dragCoefficient = ProfileDrag + induced + Mathf.Abs(flapDeg) * FlapDragPerDeg;

        float q = 0.5f * density * r.apparentSpeed * r.apparentSpeed;
        r.liftForce = q * SailArea * r.liftCoefficient;
        r.dragForce = q * SailArea * r.dragCoefficient;

        // Resolve in the rover's own frame. Local x is starboard, local z is forward, and the
        // "from" bearing already has the air travelling toward -z at zero apparent angle.
        Vector2 flow = new Vector2(apwind.x, apwind.z).normalized;   // where the air is going
        Vector2 perp = new Vector2(-flow.y, flow.x);
        if (perp.y < 0f) perp = -perp;   // pick the lift side that pushes the rover forward
        Vector2 liftVec = perp * r.liftForce;
        Vector2 dragVec = flow * r.dragForce;
        Vector2 resultant = liftVec + dragVec;

        r.forwardThrust = Mathf.Max(resultant.y, 0f);
        r.sideForce = resultant.x;
        return r;
    }

    /// <summary>The trim angle the autopilot settles on for a given apparent-wind bearing: the
    /// bearing pulled in by <see cref="TrimRatio"/> and clamped, so the sail sits just off
    /// centreline for drive rather than luffing or stalling.</summary>
    public static float AutopilotTrim(float apparentAngleDeg)
        => Mathf.Clamp(apparentAngleDeg * TrimRatio, -MaxTrimDeg, MaxTrimDeg);

    /// <summary>The flap the autopilot sets: always to windward, so it helps rather than hurts.</summary>
    public static float AutopilotFlap(float apparentAngleDeg)
        => apparentAngleDeg >= 0f ? AutopilotFlapDeg : -AutopilotFlapDeg;
}
