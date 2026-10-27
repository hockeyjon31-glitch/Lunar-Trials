using UnityEngine;

/// <summary>
/// A smooth, configurable third-person camera that follows the buggy.
/// The camera keeps a stable offset behind the vehicle and looks at it, with
/// gentle smoothing to avoid jitter when the buggy bumps over craters.
/// </summary>
public class BuggyCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The buggy to follow.")]
    public Transform target;

    [Header("Follow")]
    [Tooltip("Offset from the target that the camera rests at (local to the target's orientation).")]
    public Vector3 followOffset = new Vector3(0f, 3.2f, -7.5f);
    [Tooltip("Multiplies the follow offset (1 = normal chase). The race-start countdown drives "
            + "this from ~1.7 down to 1 so the camera zooms from a wide reveal into the chase shot.")]
    public float offsetScale = 1f;
    [Tooltip("How quickly the camera catches up to the target. Higher = tighter.")]
    public float positionSmoothTime = 0.25f;
    [Tooltip("How quickly the camera re-aligns its rotation.")]
    public float rotationSmoothTime = 0.3f;
    [Tooltip("How quickly the chase heading follows the buggy's steering. Ground driving "
            + "only: the heading is frozen while the buggy is airborne or mid-flip, so the "
            + "camera never swings around a spinning chassis.")]
    public float followYawSmoothTime = 0.18f;
    [Tooltip("Extra look-ahead height so craters don't clip the view.")]
    public float lookHeight = 1.2f;

    [Header("Cinematic shots")]
    [Tooltip("While true, the camera ignores the buggy and frames cinematicLookAt from "
            + "cinematicOffset * offsetScale instead. The level-intro dolly/orbit drives these, "
            + "so zooming the intro means easing offsetScale and drifting the look-at point.")]
    public bool cinematicMode = false;
    [Tooltip("World point the cinematic camera frames and looks at.")]
    public Vector3 cinematicLookAt = Vector3.zero;
    [Tooltip("World-space offset from cinematicLookAt the cinematic camera rests at, scaled by offsetScale.")]
    public Vector3 cinematicOffset = new Vector3(0f, 30f, -58f);

    [Header("Impact shake")]
    [Tooltip("How long a boulder impact shakes the camera.")]
    public float shakeDuration = 0.35f;
    [Tooltip("Peak shake amplitude (world units).")]
    public float shakeMaxAmplitude = 0.5f;
    [Tooltip("Peak rotation jitter (degrees).")]
    public float shakeMaxRotation = 1.1f;
    [Tooltip("Shake noise frequency.")]
    public float shakeFrequency = 42f;

    [Header("Race speed fx (boost / slipstream)")]
    [Tooltip("How far the chase camera pulls OUT of the buggy at FULL surge (extra offsetScale "
            + "multiplier on the follow offset) - the wider shot under top acceleration.")]
    public float speedFxOffset = 0.14f;
    [Tooltip("FOV kick (degrees) added at full surge, widening the frame for a sense of pace.")]
    public float speedFxFovKick = 9f;
    [Tooltip("Peak motion-blur strength (0..1) fed to the speed-blur image effect at full surge.")]
    public float speedFxBlur = 1f;
    [Tooltip("How quickly the speed fx swells in and fades out (blend units per second). Kept "
            + "gentle so the pull-out ramps over a few tenths of a second instead of snapping out.")]
    public float speedFxSmooth = 2.2f;
    [Tooltip("Shapes the speed-fx blend before it reaches the camera: the blend is raised to this "
            + "power, so a moderate ~40 km/h cruise leaves the frame a near-stock chase shot and the "
            + "full pull-out + FOV swing is saved for the very top of the boosted band (60-70 km/h "
            + "on a maxed buggy).")]
    public float speedFxCurvePower = 2.2f;

    /// <summary>Last-created instance; used by BuggyHealth to trigger impact feedback.</summary>
    public static BuggyCamera Instance { get; private set; }

    private Vector3 velocity = Vector3.zero;
    private Vector3 currentOffset;
    private float shakeTimer;
    private float shakeAmount;
    private float currentFx;
    private float baseFov = 60f;
    private BuggyController buggy;
    private SpeedBlur speedBlur;
    // Yaw-only chase heading. Derived from the FLATTENED forward vector (never the full
    // transform yaw) and frozen while airborne/flipping: a chassis passing through vertical
    // makes eulerAngles.y jump to the opposite side, which used to whip the camera around the
    // buggy and read as a zoom lurch mid-flip.
    private float followYaw;
    private float followYawVelocity;

    private void Start()
    {
        Instance = this;
        Camera cam = GetComponent<Camera>();
        if (cam != null) baseFov = cam.fieldOfView;
        // Trials edition: a closer chase frame for precision obstacle driving.
        if (GameEdition.IsTrials) followOffset = followOffset * 0.78f;
        if (target == null) return;
        buggy = target.GetComponent<BuggyController>();
        // Initialise so the first frame isn't a wild jump across the map.
        followYaw = FlattenedYaw(target.forward, target.eulerAngles.y);
        Vector3 focus = buggy != null ? buggy.FocusPoint : target.position + Vector3.up * 0.6f;
        transform.position = focus + Quaternion.Euler(0f, followYaw, 0f) * followOffset;
        currentOffset = followOffset;
        transform.LookAt(focus + Vector3.up * lookHeight);

        speedBlur = gameObject.GetComponent<SpeedBlur>();
        if (speedBlur == null) speedBlur = gameObject.AddComponent<SpeedBlur>();
        if (speedBlur != null) speedBlur.blurAmount = 0f;
    }

    /// <summary>Heading of a flattened vector in degrees, or <paramref name="fallback"/> when
    /// the vector is (nearly) vertical - an inverted chassis has no usable ground heading.</summary>
    private static float FlattenedYaw(Vector3 forward, float fallback)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.04f) return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        return fallback;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPos;
        Vector3 lookTarget;
        // Speed-fx blend shaped for the camera: near zero at moderate cruise, full only near
        // the top of the boosted band. Zero in cinematic shots (their framing is scripted).
        float shaped = 0f;
        // The same blend with the race-only framing zeroed out; the blur reads `shaped`
        // directly because it is wanted in every mode (see below).
        float shapedFraming = 0f;
        if (cinematicMode)
        {
            // Scripted shot: frame the world at cinematicLookAt from cinematicOffset * offsetScale.
            // The level-intro dolly owns these; when it ends, cinematicMode drops and the chase
            // branch below glides back to the buggy (smooth because positionSmoothTime still applies).
            desiredPos = cinematicLookAt + cinematicOffset * offsetScale;
            lookTarget = cinematicLookAt;
        }
        else
        {
            // Chase shot: re-read the follow offset each frame; the race countdown re-tunes it
            // (zoom) live, and the desired offset follows the target's forward so the camera
            // swings with steering.
            currentOffset = followOffset;

            // Speed effect: only while the buggy's boost/slipstream genuinely holds it
            // above its cruise top speed - not by pressing the button anywhere. The
            // RIDGE and the harvest field were both missing this entirely because it was
            // gated on Lunar Race; the blur is a speed cue that belongs to every mode.
            float fxTarget = buggy != null ? buggy.RaceSpeedFxBlend : 0f;
            currentFx = Mathf.MoveTowards(currentFx, fxTarget, speedFxSmooth * Time.deltaTime);

            // Shape the smoothed blend before anything reads it: near zero at moderate
            // cruise, full only near the top of the boosted band.
            shaped = Mathf.Pow(currentFx, speedFxCurvePower);

            // The FRAMING half (pull-back + FOV kick) stays on the two race courses,
            // including the ridge, because those chase shots are built around it. Harvest
            // frames the ore field from its own distance and would lose its composition
            // to a boost-time pull-out, so it gets the blur only.
            shapedFraming = (GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival)
                ? shaped : 0f;
            // Trials air flips hold the chase framing rock-steady: the speed-fx pull-out / FOV
            // swing must not ride along with a flip, or the zoom breathes with every spin.
            if (buggy != null && buggy.FlipControlActive) shapedFraming = 0f;

            // Frame the chassis' rotation-stable centre, not the transform origin, so the
            // camera pivots on the mass and the buggy visibly flips IN FRONT of the player.
            Vector3 focus = buggy != null ? buggy.FocusPoint : target.position + Vector3.up * 0.6f;

            // Heading is yaw-only and frozen in the air. While the wheels are down it eases
            // toward the buggy's flattened forward (steering swings the shot as before); while
            // airborne / flipping / tumbling it holds, so a spin cannot drag the view around.
            bool holdHeading = buggy != null
                && (buggy.FlipControlActive || buggy.IsAirborne || buggy.IsTumbling);
            if (holdHeading)
            {
                followYawVelocity = 0f;
            }
            else
            {
                Vector3 flatForward = target.forward;
                flatForward.y = 0f;
                if (flatForward.sqrMagnitude > 0.04f)
                {
                    float desiredYaw = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
                    followYaw = Mathf.SmoothDampAngle(followYaw, desiredYaw, ref followYawVelocity,
                        Mathf.Max(0.01f, followYawSmoothTime), Mathf.Infinity, Time.deltaTime);
                }
            }

            desiredPos = focus + Quaternion.Euler(0f, followYaw, 0f) * (
                currentOffset * (offsetScale + speedFxOffset * shapedFraming));
            lookTarget = focus + Vector3.up * lookHeight;
        }

        // Smooth position.
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPos,
            ref velocity,
            positionSmoothTime
        );

        // Look at the framing point.
        Quaternion desiredRot = Quaternion.LookRotation(lookTarget - transform.position);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRot,
            1f / Mathf.Max(rotationSmoothTime, 0.01f) * Time.deltaTime
        );

        // Impact shake: a decaying, high-frequency jitter on top of the follow motion so a
        // hard boulder hit reads as a physical knock instead of just a bar draining.
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            float k = shakeAmount * Mathf.Clamp01(shakeTimer / shakeDuration);
            float t = Time.time * shakeFrequency;
            transform.position += transform.right * (Mathf.Sin(t) * 0.55f * shakeMaxAmplitude * k)
                                - transform.up * (Mathf.Sin(t * 1.4f) * 0.45f * shakeMaxAmplitude * k)
                                + transform.forward * (Mathf.Sin(t * 0.7f) * 0.3f * shakeMaxAmplitude * k);
            transform.rotation *= Quaternion.Euler(
                Mathf.Sin(t * 0.9f) * shakeMaxRotation * k,
                Mathf.Cos(t * 0.8f) * shakeMaxRotation * 0.6f * k,
                Mathf.Sin(t * 1.1f) * shakeMaxRotation * 0.7f * k);
        }

        // FOV kick + motion blur ride the same shaped blend (zero outside a chase
        // shot, or when the boost is not genuinely lifting speed, so the pass stays a pure
        // passthrough). The FOV kick takes the race-only framing value; the blur takes the
        // full blend, which is what lets it read on the ridge and in the harvest field too.
        Camera cam = GetComponent<Camera>();
        if (cam != null) cam.fieldOfView = baseFov + speedFxFovKick * shapedFraming;
        if (speedBlur != null) speedBlur.blurAmount = speedFxBlur * shaped;
    }

    /// <summary>Triggers an impact shake. <paramref name="strength"/> is 0..1 and scales the jitter.</summary>
    public void AddShake(float strength)
    {
        shakeTimer = shakeDuration;
        shakeAmount = Mathf.Clamp01(strength);
    }

    /// <summary>True while an impact shake is still playing out anywhere (the decay runs on
    /// SCALED time, so a time-freeze taken mid-shake would strand the camera mid-jitter).
    /// TutorialCoach waits on this before pausing the world for a modal.</summary>
    public static bool AnyShakeActive
    {
        get
        {
            BuggyCamera cam = Instance;
            return cam != null && cam.shakeTimer > 0f;
        }
    }
}