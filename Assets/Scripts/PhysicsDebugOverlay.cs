using UnityEngine;

/// <summary>
/// Developer overlay for <see cref="PhysicsTestBed"/>: live speed, per-wheel contact /
/// suspension / slip, angular velocity, airtime and apex, accumulated flip rotation, landing
/// grade, the flip commit latch and the tumble state - plus sliders for the handful of airtime
/// dials worth tuning by feel.
///
/// F1 toggles it. It is a plain IMGUI overlay (OnGUI) so it needs no Canvas, font asset or
/// prefab and works in the bare test scene. Development only.
/// </summary>
public class PhysicsDebugOverlay : MonoBehaviour
{
    [Tooltip("Buggy to read. The test bed assigns this.")]
    public BuggyController buggy;
    [Tooltip("Test bed that supplies the reset button. Optional.")]
    public PhysicsTestBed testBed;
    [Tooltip("Whether the overlay starts visible.")]
    public bool visible = true;
    [Tooltip("Key that hides/shows the overlay.")]
    public KeyCode toggleKey = KeyCode.F1;

    private GUIStyle panelStyle, headerStyle, labelStyle, dimStyle;
    private Texture2D panelTex;
    private Vector2 scroll;
    private float fps;

    // Baseline tuning captured on the first frame, for the DEFAULTS button.
    private bool defaultsCaptured;
    private float defRampPop, defAirDamping, defLandingBounce, defTumbleSettle;
    private float defPitchTorque, defRollTorque, defCommit, defSpinCap, defSpinDrag, defGravity, defMaxSpeed;

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey)) visible = !visible;

        float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
        fps = Mathf.Lerp(fps, 1f / dt, 0.08f);

        if (defaultsCaptured && buggy != null)
        {
            defRampPop = buggy.trialsRampPopSpeed;
            defAirDamping = buggy.trialsAirAngularDamping;
            defLandingBounce = buggy.trialsLandingBounce;
            defTumbleSettle = buggy.trialsTumbleSettleSeconds;
            defPitchTorque = buggy.trialsAirPitchTorque;
            defRollTorque = buggy.trialsAirRollTorque;
            defCommit = buggy.trialsFlipCommitSeconds;
            defSpinCap = buggy.trialsAirSpinCap;
            defSpinDrag = buggy.trialsAirAngularDrag;
            defMaxSpeed = buggy.maxSpeedKmh;
            defGravity = Physics.gravity.y;
        }
    }

    private void OnDestroy()
    {
        if (panelTex != null) Destroy(panelTex);
    }

    private void OnGUI()
    {
        if (!visible || buggy == null) return;
        EnsureStyles();

        float width = Mathf.Min(380f, Screen.width - 16f);
        float height = Mathf.Min(Screen.height - 16f, 720f);
        Rect rect = new Rect(Screen.width - width - 12f, 12f, width, height);

        GUI.Box(rect, GUIContent.none, panelStyle);
        GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f));
        scroll = GUILayout.BeginScrollView(scroll, false, false);

        GUILayout.Label("PHYSICS TEST BED", headerStyle);
        GUILayout.Label("F1 hide   R reset   1-6 tp   WASD/SPACE   ARROWS flip   J hop", dimStyle);
        GUILayout.Space(4f);

        DrawState();
        DrawAirtime();
        DrawLanding();
        DrawWheels();
        DrawTuning();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawState()
    {
        string state;
        if (buggy.IsTumbling) state = "<color=#ff7b62>TUMBLING</color>";
        else if (buggy.IsAirborne) state = "<color=#7fe3ff>AIRBORNE</color>";
        else state = "<color=#8cffb0>GROUNDED</color>";

        Vector3 v = buggy.Velocity;
        Vector3 av = buggy.Body != null ? buggy.Body.angularVelocity : Vector3.zero;
        int grounded = 0;
        for (int i = 0; i < 4; i++)
        {
            WheelCollider w = buggy.GetWheel(i);
            if (w != null && w.isGrounded) grounded++;
        }

        GUILayout.Label("State  " + state
            + "    speed <b>" + buggy.CurrentSpeedKmh.ToString("0.0") + "</b> km/h"
            + "    vertical " + v.y.ToString("+0.0;-0.0;0.0") + " m/s", labelStyle);
        GUILayout.Label("Wheels down <b>" + grounded + "/4</b>"
            + "    ground N.y " + buggy.GroundNormal.y.ToString("0.00")
            + "    fps " + fps.ToString("0"), labelStyle);
        GUILayout.Label("Angular velocity <b>" + av.magnitude.ToString("0.00") + "</b> rad/s"
            + "    (x " + av.x.ToString("+0.00;-0.00;0.00")
            + "  y " + av.y.ToString("+0.00;-0.00;0.00")
            + "  z " + av.z.ToString("+0.00;-0.00;0.00") + ")", labelStyle);
        GUILayout.Label("Flip latch <b>" + buggy.FlipLatchSeconds.ToString("0.00") + " s</b>"
            + "    axis (" + buggy.FlipAxis.x.ToString("0.00") + ", "
            + buggy.FlipAxis.z.ToString("0.00") + ")", labelStyle);
    }

    private void DrawAirtime()
    {
        GUILayout.Space(6f);
        GUILayout.Label("AIRTIME", headerStyle);
        GUILayout.Label("Current  <b>" + buggy.AirPhaseTime.ToString("0.00") + " s</b>"
            + "    apex <b>" + buggy.ApexHeight.ToString("0.00") + " m</b>"
            + "    last " + buggy.LastAirTime.ToString("0.00") + " s / "
            + buggy.LastApexHeight.ToString("0.00") + " m", labelStyle);
        GUILayout.Label("Rotation  pitch " + buggy.AccumulatedPitchDeg.ToString("0")
            + " deg   roll " + buggy.AccumulatedRollDeg.ToString("0")
            + " deg   yaw " + buggy.AccumulatedYawDeg.ToString("0") + " deg", labelStyle);
    }

    private void DrawLanding()
    {
        GUILayout.Space(6f);
        GUILayout.Label("LANDING", headerStyle);
        GUILayout.Label("Grade <b>" + buggy.LastLandingGrade + "</b>"
            + "    angle " + buggy.LastLandingAngle.ToString("0.0") + " deg"
            + "    at " + buggy.LastLandingSpeedKmh.ToString("0") + " km/h"
            + "    count " + buggy.LandingCount, labelStyle);

        float tumble = buggy.TumbleTime;
        GUILayout.Label("Tumble  " + (buggy.IsTumbling ? "ACTIVE" : "idle")
            + "    time " + tumble.ToString("0.00") + " s", dimStyle);
    }

    private void DrawWheels()
    {
        GUILayout.Space(6f);
        GUILayout.Label("WHEELS", headerStyle);
        GUILayout.Label(WheelLine(0, "FL"), labelStyle);
        GUILayout.Label(WheelLine(1, "FR"), labelStyle);
        GUILayout.Label(WheelLine(2, "RL"), labelStyle);
        GUILayout.Label(WheelLine(3, "RR"), labelStyle);
    }

    private string WheelLine(int index, string name)
    {
        WheelCollider wheel = buggy.GetWheel(index);
        if (wheel == null) return name + "  (missing)";

        bool grounded = wheel.isGrounded;
        float suspension = 0f;
        float slipForward = 0f;
        float slipSideways = 0f;
        float load = 0f;

        if (grounded && wheel.GetGroundHit(out WheelHit hit))
        {
            // The WheelCollider transform sits at the top of the suspension: the contact point's
            // local Y gives the current travel (radius + compressed suspension).
            Vector3 local = wheel.transform.InverseTransformPoint(hit.point);
            float travel = -local.y - wheel.radius;
            suspension = Mathf.Clamp01(1f - travel / Mathf.Max(0.01f, wheel.suspensionDistance));
            slipForward = hit.forwardSlip;
            slipSideways = hit.sidewaysSlip;
            load = hit.force;
        }

        string dot = grounded ? "<color=#8cffb0>G</color>" : "<color=#ff9a6b>A</color>";
        string nose = name + " " + dot;
        if (!grounded) return nose + "  --";

        return nose + "  susp " + (suspension * 100f).ToString("0") + "%"
            + "  slip " + slipForward.ToString("+0.00;-0.00;0.00")
            + " / " + slipSideways.ToString("+0.00;-0.00;0.00")
            + "  load " + load.ToString("0") + " N";
    }

    private void DrawTuning()
    {
        GUILayout.Space(6f);
        GUILayout.Label("LIVE TUNING", headerStyle);

        buggy.trialsRampPopSpeed = Slider("Ramp pop (m/s)", buggy.trialsRampPopSpeed, 0f, 5f);
        buggy.trialsAirAngularDamping = Slider("Air damping", buggy.trialsAirAngularDamping, 0f, 4f);
        buggy.trialsLandingBounce = Slider("Landing bounce", buggy.trialsLandingBounce, 0f, 1f);
        buggy.trialsTumbleSettleSeconds = Slider("Tumble settle (s)", buggy.trialsTumbleSettleSeconds, 0.2f, 2f);
        buggy.trialsAirPitchTorque = Slider("Pitch torque", buggy.trialsAirPitchTorque, 0f, 80f);
        buggy.trialsAirRollTorque = Slider("Roll torque", buggy.trialsAirRollTorque, 0f, 80f);
        buggy.trialsFlipCommitSeconds = Slider("Flip commit (s)", buggy.trialsFlipCommitSeconds, 0.2f, 2.5f);
        buggy.trialsAirSpinCap = Slider("Spin cap (rad/s)", buggy.trialsAirSpinCap, 3f, 14f);
        buggy.trialsAirAngularDrag = Slider("Air spin drag", buggy.trialsAirAngularDrag, 0f, 6f);
        buggy.maxSpeedKmh = Slider("Cruise (km/h)", buggy.maxSpeedKmh, 15f, 80f, "0");
        float gravity = Slider("Gravity (m/s2)", -Physics.gravity.y, 1f, 12f);
        Physics.gravity = new Vector3(0f, -Mathf.Max(0.1f, gravity), 0f);

        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        if (testBed != null && GUILayout.Button("RESET (R)")) testBed.ResetToSpawn();
        if (GUILayout.Button("DEFAULTS")) RestoreDefaults();
        GUILayout.EndHorizontal();
    }

    private float Slider(string label, float value, float min, float max, string format = "0.00")
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(132f));
        value = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.Label(value.ToString(format), labelStyle, GUILayout.Width(50f));
        GUILayout.EndHorizontal();
        return value;
    }

    private void RestoreDefaults()
    {
        if (!defaultsCaptured) return;
        buggy.trialsRampPopSpeed = defRampPop;
        buggy.trialsAirAngularDamping = defAirDamping;
        buggy.trialsLandingBounce = defLandingBounce;
        buggy.trialsTumbleSettleSeconds = defTumbleSettle;
        buggy.trialsAirPitchTorque = defPitchTorque;
        buggy.trialsAirRollTorque = defRollTorque;
        buggy.trialsFlipCommitSeconds = defCommit;
        buggy.trialsAirSpinCap = defSpinCap;
        buggy.trialsAirAngularDrag = defSpinDrag;
        buggy.maxSpeedKmh = defMaxSpeed;
        Physics.gravity = new Vector3(0f, Mathf.Min(-0.1f, defGravity), 0f);
    }

    private void EnsureStyles()
    {
        if (panelStyle != null) return;

        panelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        panelTex.SetPixel(0, 0, new Color(0.02f, 0.03f, 0.05f, 0.88f));
        panelTex.Apply();

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = panelTex;
        panelStyle.border = new RectOffset(0, 0, 0, 0);
        panelStyle.padding = new RectOffset(0, 0, 0, 0);

        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = 15;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.richText = true;
        headerStyle.normal.textColor = new Color(0.55f, 1f, 0.92f, 1f);

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 12;
        labelStyle.richText = true;
        labelStyle.wordWrap = false;
        labelStyle.normal.textColor = new Color(0.80f, 0.86f, 0.93f, 1f);

        dimStyle = new GUIStyle(labelStyle);
        dimStyle.fontSize = 11;
        dimStyle.normal.textColor = new Color(0.58f, 0.64f, 0.72f, 1f);
    }
}
