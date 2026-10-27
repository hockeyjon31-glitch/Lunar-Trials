using UnityEngine;

/// <summary>
/// Drives a moon buggy using standard arcade-style controls:
///   W  - accelerate
///   S  - brake / reverse
///   A / D - steer left / right
///   Cursor keys - AIR FLIPS in the Trials edition (Up/Down pitch, Left/Right roll);
///                 they do nothing on the ground
///   Space - boost (drains a boost meter shared with nothing; regenerates over time)
///
/// The vehicle is assembled in the scene from a root GameObject with this component,
/// four child WheelColliders and matching visual wheel meshes.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BuggyController : MonoBehaviour
{
    [Header("Wheels")]
    [Tooltip("Front left and front right wheel colliders (used for steering).")]
    public WheelCollider frontLeftWheel;
    public WheelCollider frontRightWheel;

    [Tooltip("Rear left and rear right wheel colliders (used for drive).")]
    public WheelCollider rearLeftWheel;
    public WheelCollider rearRightWheel;

    [Tooltip("Visual meshes matching each wheel collider, so the wheels spin/turn visibly.")]
    public Transform frontLeftVisual;
    public Transform frontRightVisual;
    public Transform rearLeftVisual;
    public Transform rearRightVisual;

    [Header("Engine")]
    [Tooltip("Maximum drive torque applied to the rear wheels (N*m).")]
    public float maxAccelerationTorque = 720f;
    [Tooltip("Negative torque applied when braking or reversing (N*m). Higher = more reactive stopping.")]
    public float maxBrakeTorque = 1600f;
    [Tooltip("Guaranteed deceleration (m/s^2) applied directly under full braking, so a stop is decisive even while wheels skid on a slippery surface.")]
    public float maxBrakeDecel = 12f;
    [Tooltip("How quickly the engine approaches its target torque. Higher = snappier.")]
    public float accelerationSmoothness = 4f;
    [Tooltip("How quickly the drive torque crosses ZERO when the commanded direction reverses "
            + "(reverse -> forward or forward -> reverse). The normal acceleration ramp is lazy, so "
            + "without a faster crossing the engine spends ~half a second still driving the OLD way "
            + "after you press the opposite pedal - the first press reads as ignored. Higher = the "
            + "new direction bites sooner.")]
    public float directionChangeSmoothness = 13f;

    [Header("Steering")]
    [Tooltip("Maximum steering angle of the front wheels in degrees.")]
    public float maxSteerAngle = 30f;
    [Tooltip("How quickly the wheels approach their steering target.")]
    public float steeringSmoothness = 6f;

    [Header("Boost")]
    [Tooltip("Extra drive torque multiplier while boosting.")]
    public float boostTorqueMultiplier = 1.6f;
    [Tooltip("Boost meter capacity (seconds of boost available).")]
    public float maxBoost = 100f;
    [Tooltip("Boost drained per second while boosting.")]
    public float boostDrainRate = 45f;
    [Tooltip("Boost regenerated per second when not boosting.")]
    public float boostRegenRate = 18f;
    [Tooltip("Seconds the boost meter must stay empty before it starts regenerating, so you can't chain-deplete it endlessly.")]
    public float boostRegenDelay = 1.2f;

    [Header("Adrenaline surge (boulder near-miss)")]
    [Tooltip("Top speed (km/h) added while an adrenaline surge is active - the free burst of "
            + "speed rewarded for shaving a race boulder.")]
    public float adrenalineSpeedBonusKmh = 7f;
    [Tooltip("Traction torque multiplier while an adrenaline surge is active (a real push, but "
            + "lighter than a held boost).")]
    public float adrenalineTorqueMultiplier = 1.35f;
    [Tooltip("Duration of an adrenaline surge (seconds).")]
    public float adrenalineDuration = 2.5f;
    [Tooltip("Boost meter instantly topped up by an adrenaline surge, so the surge also visibly "
            + "juices the tank.")]
    public float adrenalineBoostRefill = 20f;

    [Header("Speed shelf")]
    [Tooltip("Soft cap on road speed in km/h (compromise between cruiser realism and arcade pace).")]
    public float maxSpeedKmh = 32f;
    [Tooltip("Speed cap while boosting (boost tops out ~42 km/h).")]
    public float boostSpeedKmh = 42f;
    [Tooltip("Extra top speed (km/h) gained while running in another racer's slipstream (0 = off).")]
    public float slipSpeedBonusKmh = 7f;
    [Tooltip("How quickly the shelf sheds excess speed above the cap (m/s per second). Rolling friction coasts the boost momentum down naturally instead of snapping to the cap.")]
    public float coastDeceleration = 5f;

    /// <summary>
    /// Furthest ground the buggy can cover during a warning window, in metres: the upgraded
    /// Engine/Boost top speed (km/h, both already scaled by UpgradeSystem on build) converted to
    /// m/s, times the warning seconds. Timed ground hazards (Venus lightning, meteor strikes,
    /// ridge collapses) place their impact at a FRACTION of this reach ahead, so a faster buggy
    /// must cover proportionally more ground while a slower one still gets a fair dodge. Uses the
    /// boosted cap so the reach stays honest when Space is held at warning time.
    /// </summary>
    public float ReachDistance(float warningSeconds, float fraction = 1f)
    {
        float topMs = Mathf.Max(maxSpeedKmh, boostSpeedKmh) / 3.6f;
        return topMs * warningSeconds * fraction;
    }

    [Header("Stability / Safety")]
    [Tooltip("Tilt (degrees) beyond which the anti-flip starts pushing the buggy back upright. "
            + "Deliberately high: a hard slide carries enough lateral pressure to lean the buggy "
            + "up onto two wheels before the anti-roll intervenes.")]
    public float antiRollThreshold = 16f;
    [Tooltip("Strength of the corrective torque that keeps the buggy upright. Kept gentle on "
            + "purpose - a fast slide should be able to genuinely tip the buggy over.")]
    public float antiRollTorque = 14f;
    [Tooltip("Tilt (degrees) at which the buggy counts as all but fully inverted and is stood "
            + "back up outright (heading preserved) - the last-resort net. Below this, past "
            + "horizontal, the anti-roll pushes harder so a tumble lands on the wheels.")]
    public float fullFlipAngle = 160f;
    [Tooltip("Extra righting push once the buggy is past horizontal, so a roll-over completes "
            + "and lands on the wheels rather than stalling on its side.")]
    public float flipRecoveryBoost = 1.6f;
    [Tooltip("How much anti-roll survives while the handbrake is held. A hard sliding turn is "
            + "allowed to tip the buggy - that is the risk that makes the drift worth taking.")]
    public float driftAntiRollScale = 0.4f;
    [Tooltip("If the buggy drops below this world Y, it is treated as having fallen through.")]
    public float fallResetHeight = -40f;
    [Tooltip("If the buggy travels farther than this (horizontal) from its spawn, it is teleported back.")]
    public float maxTravelFromSpawn = 1200f;
    [Tooltip("Whether to auto-reset the buggy when it falls out of bounds or flips.")]
    public bool autoReset = true;

    [Header("Rival bump (two-wheel shove)")]
    [Tooltip("Angular velocity (rad/s) a rival sideswipe tips the buggy onto its two outer "
            + "wheels, away from the collision (scaled with speed).")]
    public float rivalBumpRollVelocity = 3.2f;
    [Tooltip("Angular velocity (rad/s) a nose-to-tail bump pitches the buggy (follower's nose "
            + "lifts; a rear-ended leader's tail lifts).")]
    public float rivalBumpPitchVelocity = 1.8f;
    [Tooltip("Direct lateral shove (m/s) separating the two buggies at contact, so they visibly "
            + "disengage instead of grinding.")]
    public float rivalBumpShove = 2.2f;
    [Tooltip("Fraction of the buggy's forward speed a ram hands back after the collision rips it "
            + "away, so a rear-end costs a beat of momentum instead of the whole run.")]
    public float rivalBumpMomentumKeep = 0.7f;

    [Header("Race downforce (ground-glue)")]
    [Tooltip("Extra downward force, as a fraction of gravity and scaled up with speed, that "
            + "presses the tyres onto the surface so the buggy stays planted over bumps and bites "
            + "through a fast turn. Race mode only (set by GameBootstrap); 0 = off. Deliberately a "
            + "gentle glue, not a blanket - the low-gravity hop is part of the world's character.")]
    public float raceDownforce = 0f;

    [Header("Hazard floor hooks (set by course hazards)")]
    [Tooltip("Extra grip multiplier applied over dust patches (lava, rain or lake floors cut it).")]
    public float tractionMultiplier = 1f;
    [Tooltip("Multiplier on the top speed shelf while driving hazard floors (0 = pinned, 1 = normal).")]
    public float speedCapMultiplier = 1f;
    [Header("Ice glaze (frozen surface)")]
    [Tooltip("Io's sulfur-frost eclipse, 0-1. Written by IoEclipse every frame.")]
    public float glazeFromEclipse;
    [Tooltip("Ice patches under the wheels, 0-1. Written by the race hazard director every frame.")]
    public float glazeFromPatch;
    /// <summary>Combined glaze: the strongest frozen surface asking for it wins, so a patch
    /// inside an eclipse holds at whichever bites harder instead of stacking. Ice does NOT slow
    /// the buggy down: it makes the buggy HOLD the speed it arrived at. The wheels stop turning,
    /// steering goes mushy and the brakes barely bite, so a frosted patch commits you to your
    /// line instead of parking you in it.</summary>
    public float surfaceGlaze => Mathf.Max(glazeFromEclipse, glazeFromPatch);
    [Tooltip("Grip multiplier at full glaze.")]
    public float glazeGrip = 0.6f;
    [Tooltip("Fraction of steering authority left at full glaze. Lower = harder to change direction.")]
    public float glazeSteer = 0.3f;
    [Tooltip("Fraction of brake authority left at full glaze. Lower = harder to put the brakes on.")]
    public float glazeBrake = 0.2f;
    [Tooltip("m/s per second the glide pulls the buggy back toward the speed it arrived at.")]
    public float glazeHoldRate = 3.5f;
    [Tooltip("Below this ground speed (m/s) the glaze eases off, so a buggy that crawls to a dead " +
             "stop on a frozen patch can still drive itself off instead of being stranded.")]
    public float glazeEscapeMs = 2.5f;

    /// <summary>Glaze as resolved for the current physics step - the incoming <see cref="surfaceGlaze"/> " +
    /// after the standstill escape valve, so every drive system below reads one consistent value.</summary>
    private float currentGlaze;
    /// <summary>The ground speed latched on the frame the glaze appeared. -1 while clear.</summary>
    private float glazeHoldMs = -1f;

    /// <summary>True while NOT a single wheel is touching the ground (mid-hop). Ground
    /// hazards that only grab the WHEELS - Titan's methane mud - release while airborne, so a
    /// well-timed hop carries the buggy clean over them without losing speed.</summary>
    public bool IsAirborne
    {
        get
        {
            return !(frontLeftWheel != null && frontLeftWheel.isGrounded)
                && !(frontRightWheel != null && frontRightWheel.isGrounded)
                && !(rearLeftWheel != null && rearLeftWheel.isGrounded)
                && !(rearRightWheel != null && rearRightWheel.isGrounded);
        }
    }

    [Tooltip("Continuous seconds the buggy has been airborne (all four wheels off the ground; "
            + "zero the instant a wheel touches). Ice sheds its freeze the longer this climbs, so a "
            + "penalty the longer this climbs, so a hop softens an ice patch and a full leap clears it.")]
    public float AirborneTime { get; private set; }
    [Tooltip("Airborne seconds needed to fully shed the traction/top-speed penalty of an ice patch. "
            + "0 releases instantly the moment the buggy leaves the ground; bigger ramps it in.")]
    public float airborneReleaseSeconds = 0.15f;
    /// <summary>0 while grounded, 1 after a full hop - the fraction of the ice freeze already
    /// already shrugged off by being airborne. Read by the race hazard director and surface spray.</summary>
    public float AirborneRelease => Mathf.Clamp01(AirborneTime / Mathf.Max(0.01f, airborneReleaseSeconds));

    /// <summary>Seconds the buggy is held in a hard stall (vehicle struck stop). While ticking,
    /// the rigidbody is kinematic and the whole FixedUpdate drive is suspended.</summary>
    private float stallTimer;

    /// <summary>Violently halts the buggy for <paramref name="seconds"/> (used by Venus ridge
    /// lightning, which slams a stopped car when the bolt lands nearby).</summary>
    public void StallFromStrike(float seconds)
    {
        stallTimer = Mathf.Max(stallTimer, seconds);
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }

    [Header("Reversing")]
    [Tooltip("Motor torque multiplier applied while reversing. Below 1 = reverse is a gentle " +
            "manoeuvring gear, not a second forward gear; a backwards launch no longer out-shoves " +
            "the forward drive.")]
    public float reverseTorqueMultiplier = 0.9f;
    [Tooltip("Reverse plane-speed cap (km/h); backing up gets its own, softer shelf than forward.")]
    public float reverseSpeedKmh = 18f;
    [Tooltip("Seconds S must stay held while the buggy is (nearly) stopped before reverse drive " +
            "arms. A tap of S then brakes to a stop and HOLDS, instead of blending straight into " +
            "a reverse launch. Already rolling backward keeps reverse armed instantly.")]
    public float reverseArmSeconds = 0.18f;
    [Tooltip("Forward/backward speed (m/s) under which the reverse-arming timer may accumulate.")]
    public float reverseArmSpeed = 0.6f;
    [Tooltip("Guaranteed deceleration (m/s^2) applied while W is held and the buggy still rolls " +
            "backward, so swapping reverse -> forward is as decisive as brake -> stop (the brake " +
            "pedal always had this assist; the forward pedal never did).")]
    public float maxReverseBrakeDecel = 8f;
    [Tooltip("Extra tyre stiffness while creeping or backing up. Reversing is pure rear-wheel drive " +
            "with the front axle steering, which skates on the stock grip - this tighter bite keeps " +
            "the tail planted through backward turns and off ledges.")]
    public float reverseGripBoost = 0.45f;
    [Tooltip("How fast the reverse-grip stiffness blends in and out (per second of physics).")]
    public float reverseGripRamp = 5f;

    private float reverseArmTimer;

    [Header("Trials edition (LUNAR_TRIALS builds only)")]
    [Tooltip("Drive torque multiplier at crawl speed, fading to the high-speed scale at the " +
            "top-speed shelf - inch-perfect obstacle control in the Trials challenges.")]
    public float trialsTorqueLowSpeed = 1.3f;
    [Tooltip("Drive torque multiplier once the buggy is at its top-speed shelf.")]
    public float trialsTorqueHighSpeed = 0.8f;
    [Tooltip("Anti-roll strength multiplier at crawl speed (fades back to 1 with speed), so " +
            "slow obstacle crawling is forgiving while high-speed flips stay a real risk.")]
    public float trialsLowSpeedRollAssist = 1.8f;
    [Tooltip("Pitch torque while airborne, from the CURSOR Up/Down keys: Up leans the nose " +
            "up, Down leans it down - for landing ramps and rock tops flat. Engages only after " +
            "a real beat of airtime (tiny crest bounces never trigger it), eases in, and the " +
            "spin is capped so a held key can never wind an unrealistic spin.")]
    public float trialsAirPitchTorque = 24f;
    [Tooltip("Roll torque while airborne, from the CURSOR Left/Right keys - the side-flip " +
            "control (barrel rolls and mid-air landing correction). Same airtime gate and " +
            "spin cap as the pitch control.")]
    public float trialsAirRollTorque = 24f;
    [Tooltip("Total spin cap (rad/s) while air-flipping: lower for easier, more controllable " +
            "flips while tuning (held flips complete a loop slower and with more room to correct).")]
    public float trialsAirSpinCap = 7.0f;
    [Tooltip("Flip COMMIT seconds: once a flip engages, the commit holds for the whole jump and " +
            "only WHEEL-CONTACT time spends it, so a brief wheel graze (pitch flips sweep through " +
            "the wheels-down attitude low over the ramp) cannot cut the control off. The commit " +
            "also keeps the upright assist stood down until the buggy really lands (0.15 s of " +
            "continuous ground contact cancels it). The commit never applies torque by itself: " +
            "torque follows the keys being held right now.")]
    public float trialsFlipCommitSeconds = 1.0f;
    [Tooltip("Reversal strength: braking against an existing spin (pressing the OPPOSITE key) is " +
            "this many times stronger than spinning up, so changing direction is crisp instead of " +
            "a long fight against the momentum. 1 = symmetric.")]
    public float trialsAirCounterBoost = 2.0f;
    [Tooltip("Engine angular damping (1/s) while airborne in the Trials edition. The ground value " +
            "returns the moment a wheel touches down. Kept low so flips carry momentum; the " +
            "fade-out after you let go is governed by trialsAirAngularDrag.")]
    public float trialsAirEngineDamping = 0.15f;

    private float flipLatch;        // >0 once a flip has engaged this jump; only wheel contact spends it
    private Vector3 flipAxis = Vector3.right; // axis the pilot is driving right now (debug overlay)
    private float groundedStreak;   // continuous grounded seconds (a real landing cancels the latch)
    private float groundAngularDamping = -1f; // the Rigidbody's ground damping, captured once

    [Header("Trials airtime & landing")]
    [Tooltip("Extra upward kick (m/s) added the moment the buggy leaves a RAMP, scaled by speed. "
            + "Fires only on an upward launch off sloped ground, so flat-ground bumps and "
            + "drop-offs keep their normal arc.")]
    public float trialsRampPopSpeed = 1.8f;
    [Tooltip("Minimum horizontal speed (m/s) for the ramp take-off pop.")]
    public float trialsRampPopMinSpeed = 6f;
    [Tooltip("Angular damping (per second) applied to a spin the pilot is NOT driving with a flip "
            + "key, so a released flip settles instead of spinning all the way to the ground.")]
    public float trialsAirAngularDamping = 1.2f;
    [Tooltip("Restitution (0..1) of the vertical impact on a rough or tumbling landing. The "
            + "reference stunt model uses ~0.48 for a deliberately bouncy big-wheel feel.")]
    public float trialsLandingBounce = 0.48f;
    [Tooltip("Minimum upward kick (m/s) added to a tumble bounce, so the chassis visibly hops "
            + "instead of instantly settling on its roof.")]
    public float trialsTumbleBounceKick = 2f;
    [Tooltip("Seconds a tumble may settle before an upside-down wreck is confirmed.")]
    public float trialsTumbleSettleSeconds = 0.9f;
    [Tooltip("Chassis-up dot surface-normal below which a touchdown counts as a tumble.")]
    public float trialsTumbleDot = 0.35f;
    [Tooltip("Chassis-up dot surface-normal at which a tumble has recovered onto its wheels.")]
    public float trialsTumbleRecoverDot = 0.72f;
    [Tooltip("How long continuous wheel contact must last before it counts as a real landing "
            + "(short flickers over a crest never grade).")]
    public float trialsLandingContactSeconds = 0.08f;
    [Tooltip("Angular drag in the air when no flip input is held (1/s). Higher values make a "
            + "spin slow down on its own so you can counter it more easily.")]
    public float trialsAirAngularDrag = 1.8f;

    /// <summary>Most recent landing judgement, for the HUD and the physics debug overlay.</summary>
    public TrialsLandingGrade LastLandingGrade { get; private set; }
    public float LastLandingAngle { get; private set; }
    public float LastLandingSpeedKmh { get; private set; }
    public float LastAirTime { get; private set; }
    public float LastApexHeight { get; private set; }
    public int LandingCount { get; private set; }
    /// <summary>True from a bad touchdown until the chassis recovers onto its wheels (or the run
    /// fails). While tumbling the upright assist stands down and the chassis bounces.</summary>
    public bool IsTumbling { get; private set; }
    public float TumbleTime { get; private set; }
    public float AccumulatedPitchDeg { get; private set; }
    public float AccumulatedRollDeg { get; private set; }
    public float AccumulatedYawDeg { get; private set; }
    /// <summary>Committed flip-control seconds left (0 = not flipping).</summary>
    public float FlipLatchSeconds => flipLatch;
    /// <summary>World axis the current flip is rotating around.</summary>
    public Vector3 FlipAxis => flipAxis;
    /// <summary>Airborne trick scoring for this run.</summary>
    public TrialsStuntDetector Stunts { get; } = new TrialsStuntDetector();
    /// <summary>The chassis' rotation-stable centre - what the chase camera frames so a flip
    /// spins IN FRONT of the camera instead of swinging the whole view around.</summary>
    public Vector3 FocusPoint => rb != null ? rb.worldCenterOfMass : transform.position + Vector3.up * 0.6f;

    // Airtime state machine (separate from the ice AirborneTime timer, which resets on the
    // first wheel touch; a real landing needs a short debounce).
    private bool airPhase;                 // in a jump, not yet resolved
    private float airRunTime;
    private float airTakeoffY;
    private float airApexY;
    private float airContactTime;
    private bool rampPopDone;
    private Vector3 airLastVelocity;
    private Vector3 lastGroundNormal = Vector3.up;
    private float landingContactAngle;
    private float landingImpactSpeed;
    private bool landingRoofHit;
    private int landingWheelHits;
    private float takeoffYaw;
    private float tumbleSettleTime;

    /// <summary>True while flip control is committed (airborne past the gate, or riding out
    /// the commit window over a graze). The upright assists read this to stand down.</summary>
    public bool FlipControlActive => flipLatch > 0f;

    [Header("Drift / Handbrake")]
    [Tooltip("Rear sideways grip kept while handbraking (fraction of the live stiffness). The "
            + "tail breaks loose while the front axle keeps its bite, so the buggy rotates.")]
    public float handbrakeSidewaysScale = 0.25f;
    [Tooltip("Rear forward grip kept while handbraking (1 = unchanged). A little less helps the "
            + "locked rear break traction without the tail spinning instantly.")]
    public float handbrakeForwardScale = 0.7f;
    [Tooltip("How fast the handbrake grip change blends in and out (per second of physics).")]
    public float handbrakeGripRamp = 8f;
    [Tooltip("Rear drive torque kept while handbraking (0-1). Locking the rear is what makes a "
            + "handbrake turn, so the throttle is cut back while the lever is up.")]
    public float handbrakeTorqueScale = 0.35f;
    [Tooltip("Extra yaw (rad/s^2 at full steer and speed) the handbrake adds so the tail comes "
            + "round decisively instead of just skidding wide.")]
    public float handbrakeYawAssist = 1.1f;

    [Header("Hop")]
    [Tooltip("Key that fires the hop rockets. Space stays the boost sprint, so the hop rides "
            + "its own key (the briefing prompts list it).")]
    public KeyCode jumpKey = KeyCode.J;
    [Tooltip("How high the hop rockets throw the chassis (metres), before the Garage HOP ROCKETS "
            + "tier. A HEIGHT, not a speed: the launch velocity is derived from it as "
            + "sqrt(2 * g * apex), so this stays the tuning dial even when each world runs its "
            + "own gravity. Set per world from the planet's LevelDefinition.jumpApex.")]
    public float jumpApex = 0.90f;
    [Tooltip("Used when no planet definition supplies a jump apex.")]
    public float defaultJumpApex = 0.90f;
    [Tooltip("Minimum interval between hops (seconds).")]
    public float hopCooldown = 0.8f;
    [Tooltip("Multiplied by the Garage HOP ROCKETS tier (1 = stock).")]
    public float jumpFactor = 1f;
    [Tooltip("How far below the chassis the ground-check ray reaches; the hop only fires when "
            + "it can touch ground, so it cannot be chained into endless air.")]
    public float groundCheckDistance = 1.4f;

    [Header("Race stuck-reset")]
    [Tooltip("Rescue used by Lunar Race: when the buggy makes no forward progress for a short "
            + "while (e.g. trapped in a collapsed-section hole), it is lifted back onto the "
            + "course centre line 20m up-course so it does not land inside the same obstacle. "
            + "Enabled only for race runs.")]
    public bool raceStuckReset = false;
    [Tooltip("How long without forward progress before the stuck-rescue trips (seconds).")]
    public float stuckNoProgressTime = 5f;
    [Tooltip("Minimum forward (down-course) travel per sample to count as 'making progress'.")]
    public float stuckProgressThreshold = 0.5f;
    [Tooltip("How often the stuck detector samples position (seconds).")]
    public float stuckSampleInterval = 0.25f;
    private float stuckSampleTimer;
    private float stuckNoProgressTimer;
    private float stuckWindowStartZ;

    [Tooltip("Whether the player can drive. Turned off while the end-of-level boarding plays.")]
    public bool inputEnabled = true;

    // Runtime state (read by UI / other systems)
    public float CurrentSpeedKmh { get; private set; }
    /// <summary>Live physics body (the debug overlay and test bed read velocity/rotation).</summary>
    public Rigidbody Body => rb;
    /// <summary>World velocity of the chassis (m/s).</summary>
    public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
    /// <summary>Seconds into the current jump, or 0 when on the ground.</summary>
    public float AirPhaseTime => airPhase ? airRunTime : 0f;
    /// <summary>Height above the take-off point reached in the current jump (or the last one).</summary>
    public float ApexHeight => airPhase
        ? Mathf.Max(0f, airApexY - airTakeoffY)
        : LastApexHeight;
    /// <summary>True while a real jump is in progress (take-off resolved, landing pending).</summary>
    public bool InAirtimePhase => airPhase;
    /// <summary>Last sampled surface normal under the chassis (Vector3.up when unsampled).</summary>
    public Vector3 GroundNormal => lastGroundNormal;
    /// <summary>Signed yaw rate (rad/s) around the buggy's own up axis. The handbrake-skid audio
    /// reads its magnitude to tell a real handbrake TURN from a held lever that changes little.</summary>
    public float YawRate => rb != null ? Vector3.Dot(rb.angularVelocity, transform.up) : 0f;
    /// <summary>True while the player holds the brake and the rover is rolling forward (used for the braking-crunch sound).</summary>
    public bool IsBraking { get; private set; }
    public float BoostAmount { get; private set; }
    public bool IsBoosting { get; private set; }
    /// <summary>True while a boulder near-miss adrenaline surge is running (free top speed).</summary>
    public bool IsAdrenalineActive => Time.time < adrenalineUntil;
    /// <summary>World clock at the last adrenaline surge (for the near-miss cooldown gate).</summary>
    public float LastAdrenalineAt { get; private set; } = -10f;
    /// <summary>Race-mode camera/effects blend, 0..1. Climbs ONLY while boost / slipstream /
    /// adrenaline genuinely hold the buggy ABOVE its normal top speed - pressing the button at
    /// a standstill or low speed reads 0, and it swells as the surge actually lifts the pace.</summary>
    public float RaceSpeedFxBlend { get; private set; }

    /// <summary>Combined grip under the buggy (1 = full traction; loose dust patches dip it).</summary>
    public float CurrentTraction { get; private set; } = 1f;

    /// <summary>Signed throttle input (-1 reverse .. +1 full accel); the rover audio hums on this.</summary>
    public float ThrottleInput => _throttleTarget;

    /// <summary>World position the buggy started at (the reset leash is measured from here).</summary>
    public Vector3 SpawnPosition => spawnPosition;

    /// <summary>
    /// Refills the boost tank to its (possibly Garage-upgraded) maximum. Used after the
    /// Garage scales maxBoost, since Awake already ran when the component was added.
    /// </summary>
    public void RechargeToMaxBoost()
    {
        BoostAmount = maxBoost;
    }

    /// <summary>Starts an adrenaline surge: a few seconds of FREE extra top speed (plus a tank
    /// top-up) rewarded for shaving a race boulder without touching it. No meter cost and no
    /// boost key - the surge runs regardless of input.</summary>
    public void TriggerAdrenaline(float bonusKmh = 0f)
    {
        adrenalineUntil = Time.time + adrenalineDuration;
        if (bonusKmh > 0f) adrenalineSpeedBonusKmh = bonusKmh;
        BoostAmount = Mathf.Min(maxBoost, BoostAmount + adrenalineBoostRefill);
        LastAdrenalineAt = Time.time;
    }

    /// <summary>Physics of a physical rival collision (see RivalRacer): hands back most of the
    /// forward speed the impact just ate (so a ram costs a beat of momentum, not the whole run),
    /// shoves this buggy aside, and tips it onto its outer wheels away from the impact. A
    /// broadside rides the two wheels on the far side for a beat, while a nose-to-tail bump
    /// pitches it (rear lifts if it was rear-ended, nose lifts if it rammed the rival's tail).
    /// The buggy's own anti-roll torque walks it back upright, so the moment reads as a wobble,
    /// not a flip.</summary>
    public void RivalBump(Vector3 shoveDir)
    {
        if (rb == null || rb.isKinematic || stallTimer > 0f) return;
        shoveDir.y = 0f;
        if (shoveDir.sqrMagnitude < 0.0001f) return;
        shoveDir.Normalize();

        float speedT = Mathf.Clamp01(CurrentSpeedKmh / 40f);
        float lateral = Vector3.Dot(shoveDir, transform.right);
        float along = Vector3.Dot(shoveDir, transform.forward);

        if (Mathf.Abs(lateral) >= Mathf.Abs(along))
        {
            // Broadside: spin about the forward axis so the side FACING the rival lifts (the
            // buggy rides its two outer wheels away from the collision direction).
            Vector3 axis = transform.forward * (-Mathf.Sign(lateral)
                * rivalBumpRollVelocity * (0.55f + 0.45f * speedT));
            rb.AddTorque(axis, ForceMode.VelocityChange);
        }
        else
        {
            // Nose-to-tail: shoved backward (rammed another's tail) tips the nose UP; shoved
            // forward (rear-ended) tips the REAR up instead - mirror the pitch direction.
            float sign = along < 0f ? -1f : 1f;
            Vector3 axis = transform.right * (sign
                * rivalBumpPitchVelocity * (0.5f + 0.5f * speedT));
            rb.AddTorque(axis, ForceMode.VelocityChange);
        }

        // Hand back most of the forward speed the collision just ate, so a rear-end costs a beat
        // of momentum rather than stopping the buggy dead against a brick wall.
        Vector3 vel = rb.linearVelocity;
        float fwdBefore = Vector3.Dot(preStepVelocity, transform.forward);
        float fwdNow = Vector3.Dot(vel, transform.forward);
        float fwdTarget = fwdBefore * rivalBumpMomentumKeep;
        if (fwdBefore > 0.5f && fwdNow < fwdTarget)
            vel += transform.forward * (fwdTarget - fwdNow);

        // Separate the two cars LATERALLY only - a nose-to-tail bump must not tack a backward
        // kick onto the momentum the impact already cost. A dead-centre hit biases toward the
        // player's steering side so the buggy still slides off the rival's tail instead of
        // grinding behind it.
        Vector3 latShove = Vector3.Project(shoveDir, transform.right);
        if (latShove.sqrMagnitude < 0.04f)
            latShove = transform.right * (currentSteer >= 0f ? 1f : -1f);
        vel += latShove.normalized * (rivalBumpShove * (0.5f + 0.5f * speedT));
        rb.linearVelocity = vel;
    }

    private Rigidbody rb;
    private Vector3 preStepVelocity;
    private float currentTorque;
    private float currentSteer;
    private float currentBrake;
    private bool boostHeld;
    private float emptySince = -1f;
    private float adrenalineUntil = -10f;
    private bool wantHop;
    private float lastHopTime = -10f;
    private Vector3 spawnPosition;
    private MoonTerrainGen terrain;

    /// <summary>How quickly the brake builds to its full strength once S is held (seconds).</summary>
    private const float BrakeRampSeconds = 0.12f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Centre of mass is a per-vehicle height (VehicleSystem.comHeight): the wheeled rovers sit
        // at the stock -0.15, while the Favonius is ballasted lower to settle its tall wingsail.
        rb.centerOfMass = new Vector3(0f, VehicleSystem.ComHeight, 0f);
        BoostAmount = maxBoost;

        // Airtime tricks announce themselves the moment they are earned. The score only banks
        // on a clean landing (see ResolveLanding), but the popup is live feedback mid-flip.
        Stunts.OnTrick = delegate(string trickName, int points)
        {
            if (Camera.main == null) return;
            ScorePopup.Spawn(transform.position + Vector3.up * 2.6f, trickName,
                new Color(0.55f, 1f, 0.92f, 1f), 46);
        };
    }

    private void Start()
    {
        spawnPosition = transform.position;
        stuckWindowStartZ = transform.position.z;
        terrain = FindAnyObjectByType<MoonTerrainGen>();
    }

    private void Update()
    {
        // Read input in Update (frame based), apply physics in FixedUpdate. DrivingInput
        // aggregates keyboard + gamepad + touch zones into one snapshot so every
        // control scheme drives the same physics.
        DrivingInput.Refresh(jumpKey);
        float throttle = inputEnabled ? DrivingInput.Throttle : 0f;
        float steer = inputEnabled ? DrivingInput.Steer : 0f;

        boostHeld = inputEnabled && DrivingInput.Boost;

        // Hop: queued here (frame-based), applied to the rigidbody velocity in FixedUpdate.
        if (inputEnabled && DrivingInput.Hop) wantHop = true;

        // Manage boost meter.
        // Glaze kills the drive outright, so a boost here would be all noise and no shove -
        // the readout, the sound and the surge FX all stand down with it.
        IsBoosting = boostHeld && BoostAmount > 0f && surfaceGlaze < 0.5f;
        if (IsBoosting)
        {
            BoostAmount = Mathf.Max(0f, BoostAmount - boostDrainRate * Time.deltaTime);
            if (BoostAmount <= 0f) emptySince = Time.time; // just ran dry - start the regen delay
        }
        else
        {
            // A spent boost meter must sit for a moment before refilling, so a depleted tank
            // can't be topped back up instantly by hammering the key.
            if (BoostAmount <= 0f)
            {
                if (emptySince <= 0f) emptySince = Time.time;
                if (Time.time - emptySince >= boostRegenDelay)
                {
                    BoostAmount = Mathf.Min(maxBoost, BoostAmount + boostRegenRate * Time.deltaTime);
                }
            }
            else
            {
                BoostAmount = Mathf.Min(maxBoost, BoostAmount + boostRegenRate * Time.deltaTime);
                if (BoostAmount > 0f) emptySince = -1f;
            }
        }

        // Smooth the steering for natural feel.
        float targetSteer = steer * maxSteerAngle;
        currentSteer = Mathf.Lerp(currentSteer, targetSteer, steeringSmoothness * Time.deltaTime);

        // Smooth the throttle target (kept here; applied to torque in FixedUpdate).
        _throttleTarget = throttle;
    }

    private float _throttleTarget;
    private float reverseGripMult = 1f;
    private float handbrakeGripFwd = 1f;
    private float handbrakeGripSide = 1f;

    /// <summary>True while the driver is holding the handbrake (drift). The anti-roll reads it so
    /// a hard sliding turn is allowed to tip the buggy.</summary>
    public bool Drifting { get; private set; }

    private void FixedUpdate()
    {
        // Remember the velocity this step started with, BEFORE the solver resolves any contact:
        // a rival ram then reads how much forward speed the collision is about to eat (see
        // RivalBump) so it can hand most of it back.
        if (rb != null) preStepVelocity = rb.linearVelocity;

        // Track continuous air time: climbs while all four wheels are off the ground, drops
        // instantly the moment any wheel touches, so the ice-release ramp restarts each hop.
        AirborneTime = IsAirborne ? AirborneTime + Time.fixedDeltaTime : 0f;

        // Hard stall (Venus lightning strike): the whole drive is suspended - steering, torque,
        // hops - until the jolt clears, then physics resumes from rest.
        if (stallTimer > 0f)
        {
            stallTimer -= Time.fixedDeltaTime;
            if (stallTimer <= 0f)
            {
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            return;
        }

        // Handbrake: held for a drift, released to hook the tail back up. Only the player drives
        // it, so the cinematic/boarding phases never engage it.
        Drifting = inputEnabled && DrivingInput.Handbrake;

        // Hop rockets: fling the chassis upward when queued, on cooldown, and within reach of
        // the ground (so it cannot be chained into endless air). Vertical velocity takeover is
        // more predictable than an impulse for a rover this responsive.
        if (wantHop)
        {
            wantHop = false;
            if (Time.time - lastHopTime >= hopCooldown && CanHop())
            {
                lastHopTime = Time.time;
                Vector3 vel = rb.linearVelocity;
                vel.y = HopLaunchSpeed() * jumpFactor;
                rb.linearVelocity = vel;
            }
        }

// Loose regolith patches cut grip: blend toward the slip under the chassis (grip
        // recovers smoothly when the buggy exits, rather than snapping).
        //
        // The ice glaze is resolved ONCE here, before anything else reads it. Frozen ground
        // does not drag the buggy down - it commits the buggy to the speed it already has,
        // kills the drive, and makes steering and the brakes vague. The escape valve keeps
        // ice from ever stranding the player: below glazeEscapeMs the glaze fades out, so a
        // buggy that has all but stopped can crawl off a frozen patch on its own wheels.
        currentGlaze = surfaceGlaze;
        float glazeSpeed = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude;
        if (currentGlaze > 0.01f && glazeSpeed < glazeEscapeMs)
            currentGlaze = Mathf.Min(currentGlaze, glazeSpeed / Mathf.Max(0.01f, glazeEscapeMs));

        float targetTraction = DustPatch.SlipAt(rb.position) * tractionMultiplier
            * Mathf.Lerp(1f, glazeGrip, currentGlaze);
        CurrentTraction = Mathf.MoveTowards(CurrentTraction, targetTraction, Time.fixedDeltaTime * 3f);

        float torque = (inputEnabled ? _throttleTarget : 0f) * maxAccelerationTorque * CurrentTraction;
        // Trials edition: low-speed torque bias - extra crawl authority near zero, softer push
        // toward the shelf, so obstacle sections are controlled rather than twitchy.
        if (GameEdition.IsTrials)
        {
            Vector3 planarVel = rb.linearVelocity;
            planarVel.y = 0f;
            float speedFrac = Mathf.Clamp01(planarVel.magnitude / Mathf.Max(1f, maxSpeedKmh / 3.6f));
            torque *= Mathf.Lerp(trialsTorqueLowSpeed, trialsTorqueHighSpeed, speedFrac);
        }
        // Glazed: the wheels stop turning. No drive, no reverse, nothing the throttle asks for -
        // the buggy keeps whatever it arrived with.
        torque *= 1f - currentGlaze;
        if (torque < 0f) torque *= reverseTorqueMultiplier; // reverse is a manoeuvring gear: deliberately softer than forward
        if (IsBoosting || IsAdrenalineActive) torque *= IsBoosting ? boostTorqueMultiplier : adrenalineTorqueMultiplier;
        // Handbraking: cut the rear drive so the locked back end actually breaks traction.
        if (Drifting) torque *= handbrakeTorqueScale;

        // Approach torque smoothly each physics step. A DIRECTION CHANGE (the commanded torque
        // opposing the torque still being applied) crosses zero at a faster rate: otherwise the
        // lazy ramp would keep driving the buggy the OLD way for ~half a second after you press
        // the opposite pedal, so the first press of a new direction felt ignored and needed a
        // second, longer press. Once the torque is back on the commanded side it ramps normally.
        float rampSmoothness = accelerationSmoothness;
        if (Mathf.Abs(torque) > 1f && Mathf.Abs(currentTorque) > 1f
            && (torque < 0f) != (currentTorque < 0f))
        {
            rampSmoothness = directionChangeSmoothness;
        }
        currentTorque = Mathf.MoveTowards(currentTorque, torque,
            maxAccelerationTorque * rampSmoothness * Time.fixedDeltaTime);

        // Apply steering to front wheels. Dust also dulls the steering so the buggy
        // ploughs near-straight through a patch unless boosted; ice dulls it far harder,
        // so changing direction on a frozen patch is a long, vague fight.
        float steerAngle = inputEnabled ? currentSteer * Mathf.Lerp(1f, 0.55f, 1f - CurrentTraction) : 0f;
        steerAngle *= Mathf.Lerp(1f, glazeSteer, currentGlaze);
        frontLeftWheel.steerAngle = steerAngle;
        frontRightWheel.steerAngle = steerAngle;

        // Brake ramp shared by all four wheels: builds to full in ~0.12s once S is held while
        // rolling forward, released just as quickly. Front wheels brake as well - letting them
        // coast wasted half the available grip, which is what made stops feel mushy. On ice the
        // ceiling drops, so the brakes are very hard to put on.
        // ------------------------------------------------------- brake / reverse decision
        // ONE shared decision per physics step (all four wheels + the decel assists read it):
        //   - S while rolling FORWARD        -> brake decisively (wheel brakes + direct decel).
        //   - S while (nearly) stopped       -> HOLD on the brakes until reverse has armed
        //                                        (reverseArmSeconds), then reverse engages.
        //   - already rolling BACKWARD       -> reverse stays armed instantly (no re-arm lag).
        bool sHeld = inputEnabled && _throttleTarget < -0.05f;
        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        if (sHeld && forwardSpeed <= reverseArmSpeed) reverseArmTimer += Time.fixedDeltaTime;
        else reverseArmTimer = 0f;
        bool reverseArmed = reverseArmTimer >= reverseArmSeconds || forwardSpeed < -0.15f;

        bool brakeWanted = sHeld && (forwardSpeed > 0.6f || !reverseArmed);
        IsBraking = brakeWanted;
        float brakeCeiling = maxBrakeTorque * Mathf.Lerp(1f, glazeBrake, currentGlaze);
        float brakeRamp = maxBrakeTorque / BrakeRampSeconds * Time.fixedDeltaTime;
        currentBrake = Mathf.MoveTowards(currentBrake, brakeWanted ? brakeCeiling : 0f, brakeRamp);
        frontLeftWheel.brakeTorque = currentBrake;
        frontRightWheel.brakeTorque = currentBrake;

        // Direct deceleration under full braking guarantees a decisive stop even while a wheel
        // is skidding on regolith (arcade-friendly; no waiting on tyre friction to bite). Ice
        // scales this down with everything else, so it is no longer a decisive stop.
        // GROUNDED ONLY: in the air the buggy is stuck with its momentum - no air brakes.
        if (brakeWanted && !IsAirborne)
        {
            Vector3 vel = rb.linearVelocity;
            vel.y = 0f;
            float sp = vel.magnitude;
            if (sp > 0.2f)
            {
                float ceiling = maxBrakeDecel * Mathf.Lerp(1f, glazeBrake, currentGlaze);
                float apply = Mathf.Min(ceiling, sp / (Time.fixedDeltaTime + 1e-4f));
                rb.AddForce(vel / -sp * apply * rb.mass, ForceMode.Force);
            }
        }

        // Mirrored assist for the W pedal: while the buggy still rolls backward, shed that
        // momentum at a guaranteed (softer) rate so reverse -> forward is as decisive as
        // brake -> stop. Ice scales it down with everything else. GROUNDED ONLY - momentum
        // lock in the air.
        bool forwardAssistWanted = inputEnabled && _throttleTarget > 0.05f && forwardSpeed < -0.6f;
        if (forwardAssistWanted && !IsAirborne)
        {
            Vector3 vel = rb.linearVelocity;
            vel.y = 0f;
            float sp = vel.magnitude;
            if (sp > 0.2f)
            {
                float ceiling = maxReverseBrakeDecel * Mathf.Lerp(1f, glazeBrake, currentGlaze);
                float apply = Mathf.Min(ceiling, sp / (Time.fixedDeltaTime + 1e-4f));
                rb.AddForce(vel / -sp * apply * rb.mass, ForceMode.Force);
            }
        }

        // Apply drive torque / braking to rear wheels.
        ApplyDriveTorque(rearLeftWheel, currentTorque, _throttleTarget, brakeWanted, reverseArmed);
        ApplyDriveTorque(rearRightWheel, currentTorque, _throttleTarget, brakeWanted, reverseArmed);

        // Trials edition: AIR FLIP CONTROL on the CURSOR KEYS - Up/Down pitch the nose,
        // Left/Right roll (side flips). WASD stays pure ground driving.
        //
        // HOW IT WORKS NOW (the old version spun on after the key was released and was slow to
        // reverse - see the notes below):
        //   * TORQUE FOLLOWS THE KEYS HELD THIS STEP. The commit latch only decides whether flip
        //     control is ALLOWED (it rides out wheel-graze flickers); it never pushes the buggy by
        //     itself, so letting go of a key stops driving the spin immediately.
        //   * Each held axis chases a target spin rate (key * trialsAirSpinCap). Spinning up uses
        //     trialsAir*Torque (rad/s^2); pressing the OPPOSITE key brakes trialsAirCounterBoost
        //     times harder, with no ease-in, so a reversal is crisp.
        //   * Pitch and roll are independent, so both keys together give a combined spin.
        //   * The axes are the take-off heading's right/forward - the same ground-locked axes the
        //     trick scoring accumulates about, so what you drive is what gets scored. The
        //     direction mapping is unchanged from before (Up = +pitch about the right axis,
        //     Left = +roll about the forward axis): flip a sign in DrivingInput if one reads reversed.
        //   * One damping model in the air: low engine damping, plus trialsAirAngularDrag on any
        //     axis the pilot is not driving. (The Rigidbody's 2.5/s ground damping used to stack on
        //     top of two more damping terms.)
        if (GameEdition.IsTrials)
        {
            if (groundAngularDamping < 0f) groundAngularDamping = rb.angularDamping;
            rb.angularDamping = (inputEnabled && IsAirborne) ? trialsAirEngineDamping : groundAngularDamping;
        }

        if (GameEdition.IsTrials && inputEnabled && stallTimer <= 0f && !rb.isKinematic)
        {
            float dt = Time.fixedDeltaTime;
            float pitchIn = DrivingInput.AirPitch;
            float rollIn = DrivingInput.AirRoll;
            bool pitchHeld = Mathf.Abs(pitchIn) > 0.05f;
            bool rollHeld = Mathf.Abs(rollIn) > 0.05f;
            bool flipHeld = pitchHeld || rollHeld;

            if (IsAirborne) groundedStreak = 0f;
            else groundedStreak += dt;

            // Engaged = a real beat of airtime (tiny crest bounces never trigger it).
            bool engaged = IsAirborne && AirborneTime > 0.12f;
            if (flipHeld && engaged) flipLatch = trialsFlipCommitSeconds;     // arm for this jump
            else if (!IsAirborne) flipLatch = Mathf.Max(0f, flipLatch - dt);  // only ground contact spends it
            if (groundedStreak > 0.15f) flipLatch = 0f;                       // a real landing ends it

            bool canDriveSpin = engaged || (flipLatch > 0f && groundedStreak <= 0.15f);
            bool airSpinZone = IsAirborne || flipLatch > 0f;

            // Unity clamps angular velocity to maxAngularVelocity (default 7): lift it for the whole
            // jump so the cap is reachable and releasing a key never snaps the spin down to 7.
            rb.maxAngularVelocity = airSpinZone ? trialsAirSpinCap + 0.5f : 7f;

            if (flipHeld && canDriveSpin)
            {
                float yawRad = (airPhase ? takeoffYaw : GroundHeadingYaw()) * Mathf.Deg2Rad;
                Vector3 pitchAxis = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
                Vector3 rollAxis = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
                flipAxis = (pitchHeld && (!rollHeld || Mathf.Abs(pitchIn) >= Mathf.Abs(rollIn))) ? pitchAxis : rollAxis;

                Vector3 av = rb.angularVelocity;
                float wPitch = Vector3.Dot(av, pitchAxis);
                float wRoll = Vector3.Dot(av, rollAxis);

                if (pitchHeld)
                    av += pitchAxis * (ApproachSpin(wPitch, pitchIn * trialsAirSpinCap, trialsAirPitchTorque, dt) - wPitch);
                if (rollHeld)
                    av += rollAxis * (ApproachSpin(wRoll, rollIn * trialsAirSpinCap, trialsAirRollTorque, dt) - wRoll);

                // The axis the pilot is NOT driving fades out, so a leftover roll does not
                // linger while you pitch (and vice versa).
                float drag = Mathf.Clamp01(trialsAirAngularDrag * dt);
                if (!pitchHeld) av -= pitchAxis * (Vector3.Dot(av, pitchAxis) * drag);
                if (!rollHeld) av -= rollAxis * (Vector3.Dot(av, rollAxis) * drag);

                if (av.sqrMagnitude > trialsAirSpinCap * trialsAirSpinCap)
                    av = av.normalized * trialsAirSpinCap;
                rb.angularVelocity = av;
            }
            else if (IsAirborne && AirborneTime > 0.25f)
            {
                // No flip key held: the spin fades at trialsAirAngularDrag (1/s), so a released
                // flip coasts a little and settles, and the opposite key stops it quickly.
                rb.angularVelocity *= Mathf.Clamp01(1f - trialsAirAngularDrag * dt);
            }
        }

        // Reverse grip: while the player is actively backing up (or the rover is still sliding
        // backward after letting off) the tyres bite harder, so the tail stays planted through
        // backward steering and off ledges. Implemented as an overlay ratio on the CURRENT
        // stiffness (not a cached base), so the Survival grip ramp keeps working underneath and
        // the boost simply removes itself again when driving forward resumes.
        bool reversing = _throttleTarget < -0.05f
                         || Vector3.Dot(rb.linearVelocity, transform.forward) < -0.4f;
        float revTarget = (inputEnabled && reversing) ? 1f + reverseGripBoost * CurrentTraction : 1f;
        float revNext = Mathf.MoveTowards(reverseGripMult, revTarget, reverseGripRamp * Time.fixedDeltaTime);
        float revRatio = revNext / reverseGripMult;
        if (inputEnabled && Mathf.Abs(revRatio - 1f) > 0.0001f)
        {
            ScaleWheelFriction(frontLeftWheel, revRatio);
            ScaleWheelFriction(frontRightWheel, revRatio);
            ScaleWheelFriction(rearLeftWheel, revRatio);
            ScaleWheelFriction(rearRightWheel, revRatio);
            reverseGripMult = revNext;
        }

        // Handbrake drift: slacken the REAR tyres only, so the tail steps out while the front
        // axle keeps its bite and the buggy rotates around its nose. Applied as an overlay ratio
        // on the live stiffness (the same idiom as the reverse grip), so it composes with the
        // Survival ramp and the reverse boost underneath and simply restores itself on release.
        float handFwdTarget = Drifting ? handbrakeForwardScale : 1f;
        float handSideTarget = Drifting ? handbrakeSidewaysScale : 1f;
        float handFwdNext = Mathf.MoveTowards(handbrakeGripFwd, handFwdTarget, handbrakeGripRamp * Time.fixedDeltaTime);
        float handSideNext = Mathf.MoveTowards(handbrakeGripSide, handSideTarget, handbrakeGripRamp * Time.fixedDeltaTime);
        float handFwdRatio = handFwdNext / handbrakeGripFwd;
        float handSideRatio = handSideNext / handbrakeGripSide;
        if (Mathf.Abs(handFwdRatio - 1f) > 0.0001f || Mathf.Abs(handSideRatio - 1f) > 0.0001f)
        {
            ScaleWheelAxes(rearLeftWheel, handFwdRatio, handSideRatio);
            ScaleWheelAxes(rearRightWheel, handFwdRatio, handSideRatio);
            handbrakeGripFwd = handFwdNext;
            handbrakeGripSide = handSideNext;
        }

        // A touch of yaw so the tail comes round decisively (arcade handbrake turn) - the
        // friction change alone can read as a wide skid on the shared stock grip. Scaled by steer
        // and speed, and only once there is some speed to rotate around.
        if (Drifting)
        {
            float driftSpeed = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude;
            if (driftSpeed > 1.5f)
            {
                float steerFrac = Mathf.Clamp(currentSteer / Mathf.Max(1f, maxSteerAngle), -1f, 1f);
                float speedFrac = Mathf.Clamp01(driftSpeed / 9f);
                float forwardSign = Vector3.Dot(rb.linearVelocity, transform.forward) < 0f ? -1f : 1f;
                rb.AddTorque(Vector3.up * steerFrac * forwardSign * handbrakeYawAssist * speedFrac,
                    ForceMode.Acceleration);
            }
        }

        // While the boarding sequence drives the buggy, clamp the wheels to a hard stop.
        if (!inputEnabled)
        {
            frontLeftWheel.motorTorque = 0f;
            frontRightWheel.motorTorque = 0f;
            rearLeftWheel.motorTorque = 0f;
            rearRightWheel.motorTorque = 0f;
            frontLeftWheel.brakeTorque = maxBrakeTorque;
            frontRightWheel.brakeTorque = maxBrakeTorque;
            rearLeftWheel.brakeTorque = maxBrakeTorque;
            rearRightWheel.brakeTorque = maxBrakeTorque;
        }

        // Update visual wheel meshes (not during the boarding cinematics; the sequence
        // spins the wheels itself so they don't fight the scripted pose).
        if (inputEnabled)
        {
            UpdateWheelVisual(frontLeftWheel, frontLeftVisual);
            UpdateWheelVisual(frontRightWheel, frontRightVisual);
            UpdateWheelVisual(rearLeftWheel, rearLeftVisual);
            UpdateWheelVisual(rearRightWheel, rearRightVisual);
        }

        // Expose forward speed in km/h for the UI.
        // Speed shelf: cap plane speed at 32 km/h (42 while boosting) so the buggy
        // keeps a grounded LRV personality on the top end. When the boost ends the cap
        // sheds the excess speed through rolling friction instead of snapping, so the
        // rover coasts down with its momentum.
        Vector3 plane = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        // A clean slipstream draft lifts the shelf itself (free top speed while snug behind
        // another racer) - reversed driving or a parked buggy wakes nothing.
        bool goingForward = Vector3.Dot(plane, transform.forward) >= 0f;
        float slipKmh = (goingForward && slipSpeedBonusKmh > 0f)
            ? Slipstream.BonusKmh(transform.position) / Slipstream.MaxBonusKmh * slipSpeedBonusKmh
            : 0f;
        float capMs = ((IsBoosting ? boostSpeedKmh
            : (goingForward ? maxSpeedKmh : reverseSpeedKmh))
            + slipKmh + (IsAdrenalineActive ? adrenalineSpeedBonusKmh : 0f))
            / 3.6f * speedCapMultiplier;
        float planeSpeed = plane.magnitude;

        // AIRBORNE: the shelf is off. A jump has to carry the momentum it left the ramp
        // with - capping plane speed in mid-air used to bleed off exactly the pace the
        // gap jumps and flips are built around. The glaze latch is ground authority only,
        // so it is cleared the moment the wheels leave the surface.
        if (IsAirborne)
        {
            glazeHoldMs = -1f;
        }
        else if (currentGlaze > 0.01f)
        {
            // FROZEN, NOT SLOWED. The normal top-speed shelf is suspended entirely and the
            // speed the buggy arrived with is latched instead, then held: it neither winds up
            // nor bleeds off. Hitting a patch at pace keeps that pace, and the only thing the
            // player can change on ice is the direction it points.
        if (glazeHoldMs < 0f) glazeHoldMs = planeSpeed;
        if (glazeHoldMs > glazeEscapeMs)
        {
            // The hold's authority scales with how frozen the ground actually is. A full freeze
            // pins you hard, but a PARTIAL one (the sprint keeps 40% of the grip) is only a light
            // leash - the wheels still turn, so that vehicle can genuinely pick speed back up
            // instead of merely being unable to lose any.
            float rate = glazeHoldRate * currentGlaze;
            float held = Mathf.MoveTowards(planeSpeed, glazeHoldMs, rate * Time.fixedDeltaTime);
                if (Mathf.Abs(held - planeSpeed) > 0.0001f)
                {
                    plane = plane.normalized * held;
                    rb.linearVelocity = new Vector3(plane.x, rb.linearVelocity.y, plane.z);
                }
            }
        }
        else
        {
            glazeHoldMs = -1f;
            if (planeSpeed > capMs)
            {
                float over = planeSpeed - capMs;
                over = Mathf.MoveTowards(over, 0f, coastDeceleration * Time.fixedDeltaTime);
                plane = plane.normalized * (capMs + over);
                rb.linearVelocity = new Vector3(plane.x, rb.linearVelocity.y, plane.z);
            }
        }

        Vector3 flatVel = rb.linearVelocity;
        flatVel.y = 0f;
        CurrentSpeedKmh = flatVel.magnitude * 3.6f;

        // Race ground-glue: a speed-scaled share of gravity pressed straight down onto the tyres,
        // so the buggy stays planted over bumpy ground and the grip bites through a fast turn. It
        // only grows with speed (a parked buggy is untouched) and it is zero outside Race mode
        // (the field is set by GameBootstrap), so the survey drives keep their lighter feel.
        if (raceDownforce > 0f && inputEnabled && !rb.isKinematic)
        {
            float dfSpeedFrac = Mathf.Clamp01(planeSpeed / Mathf.Max(1f, maxSpeedKmh / 3.6f));
            rb.AddForce(Vector3.down
                * (rb.mass * Mathf.Abs(Physics.gravity.y) * raceDownforce * dfSpeedFrac));
        }

        // Race speed-fx blend: how hard the boost/slipstream/adrenaline is GENUINELY carrying
        // the buggy. It rides the buggy's position inside the boosted band (0 at a standstill
        // press, 1 at that bounty's top): a real mid-race boost or draft lights up immediately,
        // while jabbing the button in neutral stays dark. Stronger plants get both a taller
        // band and the swell factors below, so more speed/engine/boost reads as more pronounced.
        float mult = speedCapMultiplier;
        float nowMs = flatVel.magnitude;
        bool surgeActive = IsBoosting || IsAdrenalineActive || slipKmh > 0f;
        float surge = 0f;
        if (surgeActive)
        {
            float topMs = ((IsBoosting ? boostSpeedKmh : maxSpeedKmh)
                + slipKmh + (IsAdrenalineActive ? adrenalineSpeedBonusKmh : 0f))
                / 3.6f * mult;
            float ride = Mathf.Clamp01(nowMs / Mathf.Max(0.5f, topMs));
            surge = ride * ride;
        }
        // A punchier power plant also smears harder: the blend scales with how muscular the
        // buggy's engine+boost package is (bigger caps = more pronounced effect under pace).
        float powerK = Mathf.Max(0.5f, (boostSpeedKmh - maxSpeedKmh) / 3.6f * mult);
        float powerT = Mathf.Clamp01(powerK / 2.2f);
        float muscleT = Mathf.Clamp01((maxSpeedKmh + boostSpeedKmh) / 100f);
        RaceSpeedFxBlend = Mathf.Clamp01(surge
            * (0.45f + 0.55f * powerT) * (0.55f + 0.45f * muscleT));

        // Airtime: ramp pop, spin damping, trick accumulation, bounce/landing grading and the
        // tumble state machine. Runs after the drive so it sees this step's final velocity.
        if (!rb.isKinematic) UpdateAirtime();

        // While input is frozen (boarding sequence, game over) the buggy is moved by the
        // cinematics (and may be kinematic), so the physics safety net must not interfere.
        if (inputEnabled)
        {
            MaintainUpright();
            CheckForReset();
            if (raceStuckReset && !rb.isKinematic && terrain != null && terrain.raceEnabled)
                UpdateStuckDetection();
        }
    }

    /// <summary>
    /// The upward takeover speed that peaks the chassis at <see cref="jumpApex"/> metres under
    /// the gravity actually in force (v = sqrt(2gh)), so a world running lighter gravity needs a
    /// gentler push for the same height - and, in return, gets a longer, floatier arc through
    /// the air. Reading Physics.gravity rather than a cached copy keeps the hop and the physics
    /// engine from ever disagreeing.
    ///
    /// The garage's HOP ROCKETS tier multiplies this, and because apex goes as v^2 a tier is
    /// worth the SQUARE of its factor in height (see UpgradeSystem's garage readout).
    /// </summary>
    private float HopLaunchSpeed()
    {
        float g = Mathf.Max(0.1f, Mathf.Abs(Physics.gravity.y));
        float apex = jumpApex > 0f ? jumpApex : defaultJumpApex;
        return Mathf.Sqrt(2f * g * apex);
    }

    /// <summary>One step of a spin-rate change toward a target. Spinning up uses the plain rate;
    /// braking against an existing spin (the opposite key) uses trialsAirCounterBoost times it.</summary>
    private float ApproachSpin(float current, float target, float spinUpRate, float dt)
    {
        bool opposing = Mathf.Abs(current) > 0.5f && Mathf.Sign(current) != Mathf.Sign(target);
        float rate = spinUpRate * (opposing ? Mathf.Max(1f, trialsAirCounterBoost) : 1f);
        return Mathf.MoveTowards(current, target, rate * dt);
    }

    // Rolls the buggy back toward upright, but gently enough that a fast slide onto two wheels
    // can still go right over. Past horizontal it pushes harder to FINISH the tumble and land on
    // the wheels again; only all-but-fully-inverted does it stand up outright. The intent is that
    // a hard drift can flip you, you tumble, and you come back down on your wheels - never stuck
    // on the roof.
    private void MaintainUpright()
    {
        // TRIALS AIR FLIPS: while flip control is committed, the upright assist and the
        // full-flip reset stand down completely - otherwise the righting push past 90
        // degrees bends the spin axis and the near-inverted reset kills the loop mid-flip.
        // Landing attitude is the pilot's job while flipping; the commit cancels the
        // instant the buggy is really back on the ground.
        if (GameEdition.IsTrials && FlipControlActive)
            return;
        // A bounce/tumble owns its own righting nudge and settle timer; the last-resort
        // full-flip snap here would cut the bounce short and hide the wreck.
        if (IsTumbling)
            return;

        Vector3 up = transform.up;
        float tilt = Vector3.Angle(up, Vector3.up);

        if (tilt > fullFlipAngle)
        {
            // All but fully inverted: there is no clean righting axis left out here, so stand it
            // back up (preserving heading) and kill the spin. The last-resort net.
            float heading = transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, heading, 0f);
            rb.angularVelocity = Vector3.zero;
            return;
        }

        if (tilt <= antiRollThreshold) return;

        // Rotate back toward upright along the shortest arc. The push ramps in with tilt, and
        // past horizontal it is boosted so a roll-over carries through to the wheels instead of
        // stalling on its side.
        Vector3 axis = Vector3.Cross(up, Vector3.up).normalized;
        if (axis.sqrMagnitude < 0.0001f) return;

        float strength = antiRollTorque * Mathf.Clamp01((tilt - antiRollThreshold) / 40f);
        if (tilt > 90f) strength *= flipRecoveryBoost;
        // A hard sliding turn is allowed to tip the buggy - that is the cost of the drift.
        if (Drifting) strength *= driftAntiRollScale;
        // Trials edition: near-stationary tips get a much stronger righting push, so slow
        // obstacle crawling is forgiving; at speed the flip risk stays a real stakes.
        if (GameEdition.IsTrials)
        {
            Vector3 planarVel = rb.linearVelocity;
            planarVel.y = 0f;
            float speedFrac = Mathf.Clamp01(planarVel.magnitude / Mathf.Max(1f, maxSpeedKmh / 3.6f));
            strength *= Mathf.Lerp(trialsLowSpeedRollAssist, 1f, speedFrac);
        }
        rb.AddTorque(axis * strength, ForceMode.Acceleration);
    }

    // =============================================================== airtime / landing
    // Ported from the Apex Torque airtime model: take-off pop, accumulated rotation, trick
    // detection, graded touchdowns, and a bouncy tumble that only fails once it has SETTLED
    // upside down. Kept in the existing WheelCollider vehicle - the reference is a feel guide,
    // not a replacement rig.

    private static readonly Vector3[] RoofContactPoints =
    {
        new Vector3(-0.75f, 1.35f, 0.9f),
        new Vector3(0.75f, 1.35f, 0.9f),
        new Vector3(-0.75f, 1.35f, -1.0f),
        new Vector3(0.75f, 1.35f, -1.0f),
    };
    private readonly RaycastHit[] roofHitBuffer = new RaycastHit[8];

    /// <summary>Per-step airtime state machine. Runs after the drive so it sees the final
    /// velocity and can add pops/bounces without them being immediately overwritten.</summary>
    private void UpdateAirtime()
    {
        float dt = Time.fixedDeltaTime;
        bool airborne = IsAirborne;
        SampleSurface(out Vector3 normal, out int wheelHits, out int roofHits);
        if (wheelHits > 0 || roofHits > 0) lastGroundNormal = normal;

        // ---- take-off -------------------------------------------------------------
        if (!airPhase && airborne && AirborneTime > 0.06f)
        {
            airPhase = true;
            airRunTime = 0f;
            airContactTime = 0f;
            airTakeoffY = rb.position.y;
            airApexY = airTakeoffY;
            airLastVelocity = rb.linearVelocity;
            rampPopDone = false;
            IsTumbling = false;
            TumbleTime = 0f;
            tumbleSettleTime = 0f;
            AccumulatedPitchDeg = 0f;
            AccumulatedRollDeg = 0f;
            AccumulatedYawDeg = 0f;
            takeoffYaw = GroundHeadingYaw();
            Stunts.ResetAirborne();
        }

        // ---- in the air -----------------------------------------------------------
        if (airPhase)
        {
            airRunTime += dt;
            airApexY = Mathf.Max(airApexY, rb.position.y);
            airLastVelocity = rb.linearVelocity;

            // Accumulate rotation about GROUND-LOCKED axes captured at take-off. Because the
            // axes never re-derive from the tumbling transform, passing vertical cannot flip
            // the sign of the trick (the same reason the flip torque latches its axis).
            float yawRad = takeoffYaw * Mathf.Deg2Rad;
            Vector3 pitchAxis = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
            Vector3 rollAxis = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
            Vector3 av = rb.angularVelocity;
            AccumulatedPitchDeg += Vector3.Dot(av, pitchAxis) * Mathf.Rad2Deg * dt;
            AccumulatedRollDeg += Vector3.Dot(av, rollAxis) * Mathf.Rad2Deg * dt;
            AccumulatedYawDeg += av.y * Mathf.Rad2Deg * dt;

            // Ramp pop: a short, speed-scaled upward kick the instant the wheels leave sloped
            // ground while the chassis is already climbing. Flat-ground bumps never fire it.
            if (!rampPopDone && airRunTime < 0.12f && airLastVelocity.y > 0.5f
                && lastGroundNormal.y < 0.995f)
            {
                float planar = new Vector2(airLastVelocity.x, airLastVelocity.z).magnitude;
                if (planar >= trialsRampPopMinSpeed)
                {
                    rampPopDone = true;
                    Vector3 v = rb.linearVelocity;
                    v.y += trialsRampPopSpeed * Mathf.Clamp01(planar / 15f);
                    rb.linearVelocity = v;
                }
            }

            // Aerodynamic rotational damping only when the pilot is NOT driving the spin, so a
            // held flip keeps its authority while a released one settles.
            // (The Trials edition owns its air damping in FixedUpdate - trialsAirAngularDrag - so this
            // older term would stack on top of it.)
            if (!FlipControlActive && trialsAirAngularDamping > 0f && !GameEdition.IsTrials)
                rb.angularVelocity *= 1f / (1f + trialsAirAngularDamping * dt);

            Stunts.Evaluate(AccumulatedPitchDeg, AccumulatedRollDeg, AccumulatedYawDeg, airRunTime);

            if (!airborne)
            {
                // Capture the FIRST contact frame - 80 ms later the chassis may already have
                // been shoved upright by the collision, which would misgrade the landing.
                if (airContactTime <= 0f)
                {
                    landingContactAngle = Vector3.Angle(transform.up, normal);
                    landingImpactSpeed = Mathf.Max(0f, -airLastVelocity.y);
                    landingRoofHit = roofHits > 0;
                    landingWheelHits = wheelHits;
                }
                airContactTime += dt;
                if (airContactTime >= trialsLandingContactSeconds) ResolveLanding();
            }
            else
            {
                airContactTime = 0f;
            }
        }

        // ---- bouncing / tumbling --------------------------------------------------
        if (IsTumbling) UpdateTumble(dt, airborne, normal, wheelHits, roofHits);
    }

    /// <summary>Resolve the touchdown captured by <see cref="UpdateAirtime"/>: grade it, score
    /// the tricks, and either plant the buggy or start a bounce/tumble.</summary>
    private void ResolveLanding()
    {
        airPhase = false;
        LastAirTime = airRunTime;
        LastApexHeight = Mathf.Max(0f, airApexY - airTakeoffY);
        LastLandingAngle = landingContactAngle;
        LastLandingSpeedKmh = CurrentSpeedKmh;
        LastAirTime = airRunTime;
        float airTime = airRunTime;
        airRunTime = 0f;

        // Tiny crest hops are not landings - no grade, no bonus, no popup.
        if (airTime < 0.3f)
        {
            LastLandingGrade = TrialsLandingGrade.None;
            Stunts.ResetAirborne();
            return;
        }

        LandingCount++;
        float upDot = Mathf.Cos(landingContactAngle * Mathf.Deg2Rad);
        bool extreme = landingRoofHit || upDot < trialsTumbleDot;

        TrialsLandingGrade grade;
        float multiplier;
        int bonus;
        if (extreme)
        {
            grade = TrialsLandingGrade.Tumble;
            multiplier = 0f;
            bonus = 0;
        }
        else if (landingContactAngle <= 16f)
        {
            grade = TrialsLandingGrade.Perfect;
            multiplier = 4f;
            bonus = 2500;
        }
        else if (landingContactAngle <= 32f)
        {
            grade = TrialsLandingGrade.Great;
            multiplier = 2f;
            bonus = 1200;
        }
        else
        {
            grade = TrialsLandingGrade.Rough;
            multiplier = 1f;
            bonus = 500;
        }

        LastLandingGrade = grade;
        Stunts.FinalizeLanding(multiplier, bonus, grade);

        // Physical response: clean landings kill the spin and hand back a little forward drive;
        // rough landings bounce; a tumble gets restitution + a random tumble impulse.
        Vector3 v = rb.linearVelocity;
        switch (grade)
        {
            case TrialsLandingGrade.Perfect:
                v.y = Mathf.Abs(v.y) * 0.18f;
                rb.linearVelocity = v + transform.forward * 1.5f;
                rb.angularVelocity *= 0.2f;
                break;
            case TrialsLandingGrade.Great:
                v.y = Mathf.Abs(v.y) * 0.24f;
                rb.linearVelocity = v + transform.forward * 0.9f;
                rb.angularVelocity *= 0.25f;
                break;
            case TrialsLandingGrade.Rough:
                v.y = Mathf.Abs(v.y) * trialsLandingBounce * 0.6f;
                rb.linearVelocity = v;
                rb.angularVelocity *= 0.35f;
                break;
            case TrialsLandingGrade.Tumble:
                v.y = Mathf.Abs(v.y) * Mathf.Clamp01(trialsLandingBounce)
                    + trialsTumbleBounceKick * Mathf.Clamp01(landingImpactSpeed / 8f);
                rb.linearVelocity = v;
                rb.angularVelocity += new Vector3(
                    Random.Range(-2.4f, 2.4f), Random.Range(-1.2f, 1.2f), Random.Range(-3.2f, 3.2f));
                IsTumbling = true;
                TumbleTime = 0f;
                tumbleSettleTime = 0f;
                break;
        }

        SpawnLandingPopup(grade);
    }

    /// <summary>Bouncy tumbling while the chassis is on its side/roof. The trial only fails once
    /// the wreck has been SLOW for <see cref="trialsTumbleSettleSeconds"/> AND is still inverted;
    /// a player who lands back on the wheels recovers automatically.</summary>
    private void UpdateTumble(float dt, bool airborne, Vector3 normal, int wheelHits, int roofHits)
    {
        TumbleTime += dt;

        if (airborne) return;

        // Bounce while there is still downward speed at a contact. This is the "bouncy big
        // wheel" feel from the reference: the buggy hops, rolls and needs a beat to settle.
        Vector3 v = rb.linearVelocity;
        if (v.y < -1f && (wheelHits > 0 || roofHits > 0))
        {
            v.y = Mathf.Abs(v.y) * Mathf.Clamp01(trialsLandingBounce) + 0.6f;
            rb.linearVelocity = v;
            rb.angularVelocity += new Vector3(
                Random.Range(-1.6f, 1.6f), Random.Range(-0.9f, 0.9f), Random.Range(-2.2f, 2.2f));
        }

        // The player can still fight the tumble with the flip keys - same ground-locked axes.
        float pitchIn = DrivingInput.AirPitch;
        float rollIn = DrivingInput.AirRoll;
        if (Mathf.Abs(pitchIn) > 0.05f || Mathf.Abs(rollIn) > 0.05f)
        {
            float yawRad = GroundHeadingYaw() * Mathf.Deg2Rad;
            Vector3 axis = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad)) * pitchIn
                         + new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad)) * rollIn;
            rb.AddTorque(axis * 6f, ForceMode.Acceleration);
        }

        float upDot = Vector3.Dot(transform.up, normal);
        float speed = rb.linearVelocity.magnitude;
        float angSpeed = rb.angularVelocity.magnitude;

        if (upDot > trialsTumbleRecoverDot && wheelHits >= 2)
        {
            EndTumble(TrialsLandingGrade.Recovery);
            return;
        }

        if (speed < 1.8f && angSpeed < 0.6f)
        {
            tumbleSettleTime += dt;
            if (tumbleSettleTime >= trialsTumbleSettleSeconds && upDot < trialsTumbleDot)
            {
                CrashFromTumble();
                return;
            }
        }
        else
        {
            tumbleSettleTime = 0f;
        }

        // Gentle righting nudge after half a second, so a low-speed roll-over always finishes
        // on the wheels instead of leaving the player stranded on the roof.
        if (TumbleTime > 0.5f && upDot < trialsTumbleRecoverDot)
        {
            Vector3 rightAxis = Vector3.Cross(transform.up, normal);
            if (rightAxis.sqrMagnitude > 0.0001f)
                rb.AddTorque(rightAxis.normalized * 5f, ForceMode.Acceleration);
        }
    }

    private void EndTumble(TrialsLandingGrade grade)
    {
        IsTumbling = false;
        LastLandingGrade = grade;
        tumbleSettleTime = 0f;
        if (grade == TrialsLandingGrade.Recovery && Camera.main != null)
        {
            ScorePopup.Spawn(transform.position + Vector3.up * 2.4f, "RECOVERED!",
                new Color(0.55f, 1f, 0.75f, 1f), 40);
        }
    }

    private void CrashFromTumble()
    {
        IsTumbling = false;
        LastLandingGrade = TrialsLandingGrade.Crash;
        Stunts.WipePending();

        // A real trials attempt fails and offers an instant retry. Outside a trials run the
        // buggy is simply stood back up, so campaign driving can never wedge on its roof.
        if (TrialsRunManager.TrialsActive)
        {
            TrialsRunManager.Current.TryFailPanel("WRECKED - STUCK UPSIDE DOWN");
            return;
        }

        rb.position += Vector3.up * 0.8f;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        rb.angularVelocity = Vector3.zero;
        if (Camera.main != null)
        {
            ScorePopup.Spawn(transform.position + Vector3.up * 2.4f, "WRECKED!",
                new Color(1f, 0.4f, 0.35f, 1f), 44);
        }
    }

    private void SpawnLandingPopup(TrialsLandingGrade grade)
    {
        if (Camera.main == null) return;
        string text;
        Color color;
        switch (grade)
        {
            case TrialsLandingGrade.Perfect:
                text = "PERFECT LANDING!"; color = new Color(1f, 0.87f, 0.35f, 1f); break;
            case TrialsLandingGrade.Great:
                text = "GREAT LANDING"; color = new Color(0.55f, 1f, 0.75f, 1f); break;
            case TrialsLandingGrade.Rough:
                text = "BOUNCY LANDING"; color = new Color(1f, 0.72f, 0.38f, 1f); break;
            case TrialsLandingGrade.Tumble:
                text = "TUMBLING - RECOVER!"; color = new Color(1f, 0.45f, 0.35f, 1f); break;
            default:
                return;
        }
        // Append the banked trick score when there is one.
        if (!string.IsNullOrEmpty(Stunts.LastLandingSummary)) text += "\n" + Stunts.LastLandingSummary;
        ScorePopup.Spawn(transform.position + Vector3.up * 2.6f, text, color, 40);
    }

    /// <summary>Average surface normal and contact counts under the chassis. Wheel hits give
    /// the landing plane; short downward rays from the roll cage detect roof-first contact.</summary>
    private void SampleSurface(out Vector3 normal, out int wheelHits, out int roofHits)
    {
        normal = Vector3.zero;
        wheelHits = 0;
        AccumulateWheelNormal(frontLeftWheel, ref normal, ref wheelHits);
        if (frontRightWheel != frontLeftWheel)
            AccumulateWheelNormal(frontRightWheel, ref normal, ref wheelHits);
        if (rearLeftWheel != frontLeftWheel && rearLeftWheel != frontRightWheel)
            AccumulateWheelNormal(rearLeftWheel, ref normal, ref wheelHits);
        if (rearRightWheel != frontLeftWheel && rearRightWheel != frontRightWheel
            && rearRightWheel != rearLeftWheel)
            AccumulateWheelNormal(rearRightWheel, ref normal, ref wheelHits);

        roofHits = 0;
        int normalCount = wheelHits;
        for (int i = 0; i < RoofContactPoints.Length; i++)
        {
            Vector3 p = transform.TransformPoint(RoofContactPoints[i]);
            int n = Physics.RaycastNonAlloc(p, Vector3.down, roofHitBuffer, 0.35f,
                ~0, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < n; h++)
            {
                if (roofHitBuffer[h].collider.transform.root == transform.root) continue;
                roofHits++;
                // Only use the roof normal when no wheel is grounded - a wheel's own contact
                // is the better landing plane whenever we have one.
                if (normalCount == 0)
                {
                    normal += roofHitBuffer[h].normal;
                    normalCount = 1;
                }
                break;
            }
        }

        normal = normalCount > 0 ? normal.normalized : Vector3.up;
    }

    private static void AccumulateWheelNormal(WheelCollider wheel, ref Vector3 normal, ref int count)
    {
        if (wheel == null || !wheel.isGrounded) return;
        if (!wheel.GetGroundHit(out WheelHit hit)) return;
        normal += hit.normal;
        count++;
    }

    /// <summary>Flattened chassis heading in degrees. Safe to call while inverted: the pitched
    /// forward vector is projected onto the ground and the Euler fallback is only a last resort.</summary>
    private float GroundHeadingYaw()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude > 0.02f)
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        Vector3 v = rb != null ? rb.linearVelocity : Vector3.zero;
        v.y = 0f;
        if (v.sqrMagnitude > 0.5f)
            return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
        return transform.eulerAngles.y;
    }

    /// <summary>Wheel at index 0=FL, 1=FR, 2=RL, 3=RR (used by the physics debug overlay).</summary>
    public WheelCollider GetWheel(int index)
    {
        switch (index)
        {
            case 0: return frontLeftWheel;
            case 1: return frontRightWheel;
            case 2: return rearLeftWheel;
            case 3: return rearRightWheel;
            default: return null;
        }
    }

    // Rescue: if the buggy stops making forward (down-course) progress for a few seconds -
    // wedged in a collapse pit, pinned against debris - lift it back onto the course line at
    // its current Z so the run continues fair. Only armed for Lunar Race.
    private void UpdateStuckDetection()
    {
        stuckSampleTimer += Time.deltaTime;
        if (stuckSampleTimer < stuckSampleInterval) return;
        stuckSampleTimer = 0f;

        if (transform.position.z - stuckWindowStartZ >= stuckProgressThreshold)
        {
            // The buggy is moving down-course again; restart the clock.
            stuckNoProgressTimer = 0f;
            stuckWindowStartZ = transform.position.z;
            return;
        }

        stuckNoProgressTimer += stuckSampleInterval;
        if (stuckNoProgressTimer >= stuckNoProgressTime)
        {
            RaceStuckReset();
        }
    }

    private void RaceStuckReset()
    {
        // Park 20m back UP the course from where the buggy got wedged, so it is not dropped
        // straight back on top of the same obstacle (and the obstacle has a moment to clear).
        float cz = Mathf.Clamp(transform.position.z - 20f, MoonTerrainGen.RaceStartZ, terrain.RaceFinishZ);
        float cx = terrain.RaceCenterX(cz);
        float groundY = terrain.SampleHeight(new Vector3(cx, 0f, cz));

        transform.position = new Vector3(cx, groundY + 2f, cz);
        FaceDownCourse(cz);
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.6f);
        stuckNoProgressTimer = 0f;
        stuckWindowStartZ = cz;
    }

    /// <summary>
    /// Teleports the buggy onto the race course at course-Z <paramref name="cz"/>, placed on
    /// the centre line and facing back down it. Used by the gate manager to snap a player who
    /// sped past a missed gate back behind it (they must drive through it to collect it).
    /// Lifts it off the floor, zeroes velocity and resets the stuck-window so the "no
    /// progress" rescue does not fire.
    /// </summary>
    public void RepositionDownCourse(float cz)
    {
        if (terrain == null) terrain = FindAnyObjectByType<MoonTerrainGen>();
        if (terrain == null) return;
        cz = Mathf.Clamp(cz, MoonTerrainGen.RaceStartZ, terrain.RaceFinishZ);
        float cx = terrain.RaceCenterX(cz);
        float groundY = terrain.SampleHeight(new Vector3(cx, 0f, cz));

        transform.position = new Vector3(cx, groundY + 2f, cz);
        FaceDownCourse(cz);
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        stuckNoProgressTimer = 0f;
        stuckWindowStartZ = cz;
    }

    private void CheckForReset()
    {
        if (!autoReset) return;

        Vector3 pos = transform.position;
        float dx = pos.x - spawnPosition.x;
        float dz = pos.z - spawnPosition.z;
        float travelled = Mathf.Sqrt(dx * dx + dz * dz);

        if (pos.y < fallResetHeight || travelled > maxTravelFromSpawn)
        {
            ResetBuggy();
        }
    }

    private void ResetBuggy()
    {
        float groundY = 0f;
        if (terrain != null) groundY = terrain.SampleHeight(spawnPosition);

        transform.position = new Vector3(spawnPosition.x, groundY + 2f, spawnPosition.z);
        FaceDownCourse(spawnPosition.z);
        // Teleporting is fine on a kinematic body, but setting velocities is not supported
        // there; during the boarding sequence the cinematics own the buggy anyway.
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>Trials restart: instant, clean reposition to a challenge spawn or checkpoint.
    /// Kills all transient drive state (torque ramp, brake ramp, reverse arming, queued hop)
    /// so the attempt begins exactly like a fresh level load, just instantly.</summary>
    public void TrialsTeleportTo(Vector3 position, Quaternion rotation)
    {
        float groundY = terrain != null ? terrain.SampleHeight(position) : position.y;
        transform.position = new Vector3(position.x, groundY + 1.5f, position.z);
        transform.rotation = rotation;
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        currentTorque = 0f;
        currentBrake = 0f;
        reverseArmTimer = 0f;
        wantHop = false;
    }

    // Points the buggy back down the course after an off-track reset. On the race courses the
    // heading follows the centreline tangent at that Z (so it never steps straight into a wall);
    // elsewhere it just faces +Z, the route's overall travel direction.
    private void FaceDownCourse(float wz)
    {
        if (terrain == null || !terrain.raceEnabled)
        {
            transform.rotation = Quaternion.identity;
            return;
        }
        float d = 0.5f;
        Vector3 heading = new Vector3(
            terrain.RaceCenterX(wz + d) - terrain.RaceCenterX(wz),
            0f, d).normalized;
        transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
    }

    private void ApplyDriveTorque(WheelCollider wheel, float torque, float throttleInput,
        bool brakingToStop, bool reverseArmed)
    {
        // currentBrake holds the shared ramp (set once per FixedUpdate for all four wheels),
        // so left/right and front/rear always agree and the brake never lurches.
        //
        // The brake/reverse decision itself is made ONCE per physics step in FixedUpdate and
        // passed in here: rolling forward or not-yet-armed -> brakes; armed -> reverse motor.
        // Decide from the BUGGY's travel direction, NEVER the wheel's rpm: the instant reverse
        // drive bites, the wheel rpm flips sign, so the old `wheel.rpm < -0.5f` test fired the
        // moment the buggy started backing up - it zeroed the motor and stamped the brakes on,
        // cutting the very torque that was moving the buggy. Held reverse therefore stuttered
        // in place (a nudge, a stall, a nudge) and could not climb out of a boulder or a wall.
        if (throttleInput < -0.05f)
        {
            if (brakingToStop)
            {
                wheel.brakeTorque = currentBrake;
                wheel.motorTorque = 0f;
            }
            else
            {
                // Armed and not rolling forward: the (deliberately softer) reverse motor takes
                // over, already ramped from the braking phase so the pull-away is smooth.
                wheel.brakeTorque = 0f;
                wheel.motorTorque = torque;
            }
        }
        else if (Mathf.Abs(throttleInput) < 0.05f)
        {
            // Coasting -> let the vehicle roll, small brake to prevent runaway.
            wheel.brakeTorque = 5f;
            wheel.motorTorque = 0f;
        }
        else
        {
            wheel.brakeTorque = 0f;
            wheel.motorTorque = torque;
        }
    }

    private static void ScaleWheelFriction(WheelCollider wheel, float ratio)
    {
        if (wheel == null) return;
        WheelFrictionCurve fwd = wheel.forwardFriction;
        fwd.stiffness *= ratio;
        wheel.forwardFriction = fwd;
        WheelFrictionCurve side = wheel.sidewaysFriction;
        side.stiffness *= ratio;
        wheel.sidewaysFriction = side;
    }

    // Scales a wheel's forward and sideways stiffness by separate ratios (used by the handbrake,
    // which slackens the rear sideways grip far harder than the forward grip).
    private static void ScaleWheelAxes(WheelCollider wheel, float fwdRatio, float sideRatio)
    {
        if (wheel == null) return;
        WheelFrictionCurve fwd = wheel.forwardFriction;
        fwd.stiffness *= fwdRatio;
        wheel.forwardFriction = fwd;
        WheelFrictionCurve side = wheel.sidewaysFriction;
        side.stiffness *= sideRatio;
        wheel.sidewaysFriction = side;
    }

    /// <summary>True while the chassis is low enough for the hop rockets to push against the
    /// ground: a short downward ray from just above the centre. Landing on a slope or crest
    /// still counts, so hops work climbing ridges. The buggy's own colliders are skipped.</summary>
    private bool CanHop()
    {
        Vector3 start = rb.position + Vector3.up * 0.25f;
        RaycastHit[] hits = Physics.RaycastAll(start, Vector3.down, groundCheckDistance);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.transform.root == transform.root) continue;
            return true;
        }
        return false;
    }

    private void UpdateWheelVisual(WheelCollider collider, Transform visual)
    {
        if (collider == null || visual == null) return;

        collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
        visual.position = pos;
        visual.rotation = rot;
        // Rotate the visual around its local X axis to show rolling spin.
        visual.Rotate(Vector3.right, collider.rpm * Time.deltaTime * 360f / 60f, Space.Self);
        // Repeated Rotate calls accumulate floating-point error that de-normalizes the
        // quaternion; normalizing keeps the Inspector / GUIView from complaining.
        visual.rotation = visual.rotation.normalized;
    }
}
