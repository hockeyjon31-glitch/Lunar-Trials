using UnityEngine;

/// <summary>
/// One frame's worth of driving input, aggregated from every scheme the game supports:
///   - keyboard  W/S (throttle), A/D (steer), CURSOR KEYS (air flips - pitch/roll, the
///               trials air control; WASD stays pure ground driving so accidental flips
///               never happen from the throttle key), Space (boost), J (hop),
///               Shift (handbrake / drift)
///   - gamepad   left stick steer/throttle, A/X (button 0) boost, B/Circle (button 1) hop,
///               LB/RB (buttons 4/5) handbrake
///   - touch     on-screen steer zones + boost/brake/hop buttons (MobileControlsUI), and a
///               double-tap on a steer zone holds the handbrake (drift)
///
/// BuggyController calls Refresh() once per frame and then reads the snapshot fields, so the
/// physics never has to know which device the player is on. Touch flags are written by the
/// on-screen controls; everything else is sampled here.
/// </summary>
public static class DrivingInput
{
    // ------------------------------------------------ written by the touch controls
    public static int TouchSteer;       // -1 / 0 / +1 from the held steer zones
    public static bool TouchBoost;
    public static bool TouchBrake;
    public static bool TouchHopPressed; // one-shot, consumed by Refresh
    public static bool TouchHandbrake;  // held: armed by a double-tap on a steer zone

    // ------------------------------------------------------------- device detection
    /// <summary>True when a game controller is connected.</summary>
    public static bool GamepadActive { get; private set; }

    /// <summary>True on phones/tablets (native or mobile browser): this is what enables
    /// auto-throttle. Deliberately does NOT use Input.touchSupported - desktop Chrome reports
    /// touch support whether or not there is a touchscreen, which made the buggy accelerate
    /// on its own on desktop.</summary>
    public static bool MobileDevice { get; private set; }

    /// <summary>True when the on-screen touch controls should be shown. Same as MobileDevice,
    /// but a real touch also reveals them so a touchscreen laptop is still playable with a
    /// finger (it just does not get auto-throttle - the keyboard is right there).</summary>
    public static bool TouchDevice { get; private set; }

    // ---------------------------------------------------------------- frame snapshot
    public static float Throttle { get; private set; } // -1 .. +1
    public static float Steer { get; private set; }    // -1 .. +1
    public static bool Boost { get; private set; }
    public static bool Hop { get; private set; }
    public static bool Handbrake { get; private set; }

    // Air flip controls (cursor keys, TRIALS air control): sampled separately from the
    // ground driving so the throttle key can never flip the buggy by accident.
    public static float AirPitch { get; private set; } // -1 .. +1 (Up = nose up, Down = nose down)
    public static float AirRoll { get; private set; }  // -1 .. +1 (Left = roll left, Right = roll right)

    private static float nextPadProbe;

    /// <summary>Resolves the device flags. Cheap enough to call every frame; both latch true
    /// for the rest of the session. WebGL reports Application.isMobilePlatform as false, so
    /// the browser user-agent check is what identifies phones there.</summary>
    public static void DetectDevice()
    {
        if (!MobileDevice
            && (Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld
                || CrazyGamesBridge.IsMobileDevice))
        {
            MobileDevice = true;
        }
        if (MobileDevice || Input.touchCount > 0)
        {
            TouchDevice = true;
        }
    }

    /// <summary>Clears held touch state. Call when a level starts so a finger that was down
    /// as the previous level ended cannot keep boosting/steering in the new run.</summary>
    public static void ResetHeld()
    {
        TouchSteer = 0;
        TouchBoost = false;
        TouchBrake = false;
        TouchHopPressed = false;
        TouchHandbrake = false;
    }

    /// <summary>Samples every scheme into the snapshot. Hop keys are one-shot; pass the
    /// controller's configured hop key.</summary>
    public static void Refresh(KeyCode hopKey = KeyCode.J)
    {
        // Probe the pad about once a second: GetJoystickNames allocates, and plugging a
        // controller mid-run is fine on that cadence.
        if (Time.realtimeSinceStartup >= nextPadProbe)
        {
            nextPadProbe = Time.realtimeSinceStartup + 1f;
            string[] pads = Input.GetJoystickNames();
            GamepadActive = pads != null && pads.Length > 0;
        }
        DetectDevice();

        float throttle = 0f;
        float steer = 0f;

        // Keyboard: WASD drives the GROUND game only. The cursor keys are reserved for the
        // trials air control (flips) - sharing them with throttle/steer caused accidental
        // flips every time the buggy left the ground with a key held.
        if (Input.GetKey(KeyCode.W)) throttle += 1f;
        if (Input.GetKey(KeyCode.S)) throttle -= 1f;
        if (Input.GetKey(KeyCode.A)) steer -= 1f;
        if (Input.GetKey(KeyCode.D)) steer += 1f;

        // Air flips (cursor keys). Keyboard-only for now; gamepad/touch mappings can follow
        // (right stick / flip buttons) once the trials UI grows them.
        float airPitch = 0f;
        float airRoll = 0f;
        if (Input.GetKey(KeyCode.UpArrow)) airPitch += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) airPitch -= 1f;
        if (Input.GetKey(KeyCode.LeftArrow)) airRoll += 1f;
        if (Input.GetKey(KeyCode.RightArrow)) airRoll -= 1f;
        AirPitch = Mathf.Clamp(airPitch, -1f, 1f);
        AirRoll = Mathf.Clamp(airRoll, -1f, 1f);

        // Gamepad: left stick steers and drives; face buttons boost/hop.
        if (GamepadActive)
        {
            steer += Input.GetAxisRaw("Horizontal");
            float stick = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(stick) > 0.25f) throttle += stick;
        }

        // Touch zones and buttons.
        steer += TouchSteer;
        // Brake also reverses (same as S/Down on a keyboard) so a buggy wedged against a
        // rock can back out; at speed it decelerates first. Auto-throttle below must not
        // override it, which the Mathf.Abs guard takes care of.
        if (TouchBrake) throttle -= 1f;

        // Auto-throttle: a phone has no gas key, so the buggy always rolls forward unless
        // braking. Desktop keeps manual throttle only.
        if (MobileDevice && Mathf.Abs(throttle) < 0.01f) throttle = 1f;

        Throttle = Mathf.Clamp(throttle, -1f, 1f);
        Steer = Mathf.Clamp(steer, -1f, 1f);

        Boost = Input.GetKey(KeyCode.Space)
                || (GamepadActive && Input.GetKey(KeyCode.JoystickButton0))
                || TouchBoost;

        Hop = Input.GetKeyDown(hopKey)
              || (GamepadActive && Input.GetKeyDown(KeyCode.JoystickButton1))
              || TouchHopPressed;

        // Handbrake / drift: held. Either Shift key, either shoulder button on a pad, or the
        // double-tap-and-hold gesture on the touch steer zones.
        Handbrake = Input.GetKey(KeyCode.LeftShift)
                    || Input.GetKey(KeyCode.RightShift)
                    || (GamepadActive && (Input.GetKey(KeyCode.JoystickButton4) || Input.GetKey(KeyCode.JoystickButton5)))
                    || TouchHandbrake;

        TouchHopPressed = false;
    }
}
