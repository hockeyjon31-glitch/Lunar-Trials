using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Automated smoke test for the physics test bed. It opens
/// <c>Assets/Scenes/PhysicsTestBed.unity</c>, enters Play mode in batch, lets the stock buggy
/// settle, checks the bed was built (buggy, four grounded wheels, camera, overlay, geometry)
/// and then launches the buggy at the 20 degree ramp to prove the airtime system actually
/// produces airtime and a graded landing.
///
/// Batch entry point:
///   Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod PhysicsTestBedSmokeTest.Run
/// It exits with 0 on pass and 1 on failure, so a CI step can gate on it. Development only.
/// </summary>
public static class PhysicsTestBedSmokeTest
{
    private const string ScenePath = "Assets/Scenes/PhysicsTestBed.unity";
    private const string SessionKey = "LunarBuggy.PhysicsTestBedSmokeTest";
    private const string StartKey = SessionKey + ".start";

    private static readonly List<string> failures = new List<string>();

    private static bool hooked;
    private static float playStarted = -1f;
    private static int phase; // 0 = settle checks, 1 = ramp-launch monitor
    private static float phaseStarted;
    private static float maxAirTime;
    private static int landingsBeforeLaunch;
    private static BuggyController buggy;
    private static float nextTrace;
    private static float camYawAtSpin;
    private static float camFovAtSpin;
    private static Vector3 camPosAtSpin;

    [MenuItem("Lunar Buggy/Run Physics Test Bed Smoke Test (Play)", false, 29)]
    public static void Run()
    {
        SessionState.SetBool(SessionKey, true);
        SessionState.SetString(StartKey, DateTime.UtcNow.Ticks.ToString());
        EditorSceneManager.OpenScene(ScenePath);
        Hook();
        EditorApplication.isPlaying = true;
    }

    /// <summary>Re-subscribes after the Play-mode domain reload wipes the static callbacks.</summary>
    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(SessionKey, false)) return;
        Hook();
    }

    private static void Hook()
    {
        if (hooked) return;
        hooked = true;
        Application.logMessageReceived += OnLog;
        EditorApplication.update += Tick;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        // Our own assertions are reported separately; do not double-count them.
        if (condition != null && condition.Contains("[SMOKE]")) return;
        if (failures.Count < 25) failures.Add("log: " + condition);
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false)) return;

        // A hard watchdog so a stuck batch run can never hang a build machine.
        long ticks;
        if (long.TryParse(SessionState.GetString(StartKey, "0"), out ticks) && ticks > 0)
        {
            double elapsed = (DateTime.UtcNow - new DateTime(ticks)).TotalSeconds;
            if (elapsed > 150d)
            {
                Fail("timeout after " + elapsed.ToString("0") + " s");
                Finish();
                return;
            }
        }

        if (!EditorApplication.isPlaying) return;
        if (playStarted < 0f) playStarted = Time.realtimeSinceStartup;
        float t = Time.realtimeSinceStartup - playStarted;

        if (phase == 0)
        {
            if (t < 4f) return;
            RunSettleChecks();
            if (failures.Count > 0) { Finish(); return; }
            StartLaunch(t);
            return;
        }

        if (phase == 2)
        {
            CheckSpinTest(t);
            return;
        }

        buggy = buggy != null ? buggy : UnityEngine.Object.FindAnyObjectByType<BuggyController>();
        if (buggy != null)
        {
            // Self-heal if the launch teleport was swallowed by an interpolation write-back:
            // only inside the first half second, and only if the buggy is still near the apron.
            if (t - phaseStarted < 0.5f && buggy.transform.position.z < 120f)
            {
                PlaceBuggy(new Vector3(0f, 0f, 180f), Quaternion.identity);
                if (buggy.Body != null) buggy.Body.linearVelocity = new Vector3(0f, 0f, 8.2f);
            }

            // Simulate the held throttle a player uses up a kicker: DrivingInput is keyboard-only,
            // so nudge the planar velocity toward a steady climb speed until the lip. This keeps
            // the test honest (it still launches through the real ramp pop / airtime code) rather
            // than letting the buggy stall on the slope with no input.
            if (buggy.Body != null && !buggy.IsAirborne && buggy.transform.position.z < 214f)
            {
                Vector3 v = buggy.Body.linearVelocity;
                float forwardSpeed = Vector3.Dot(v, Vector3.forward);
                if (forwardSpeed < 8.2f)
                {
                    v += Vector3.forward * Mathf.Min(0.6f, (8.2f - forwardSpeed) * 0.25f);
                    buggy.Body.linearVelocity = v;
                }
            }

            maxAirTime = Mathf.Max(maxAirTime, buggy.LastAirTime, buggy.AirPhaseTime);
            if (t >= nextTrace)
            {
                nextTrace = t + 0.5f;
                Debug.Log("[SMOKE] trace t=" + t.ToString("0.0")
                    + " pos=" + buggy.transform.position.ToString("0.00")
                    + " speed=" + buggy.CurrentSpeedKmh.ToString("0.0") + " km/h"
                    + " air=" + buggy.AirPhaseTime.ToString("0.00")
                    + " last=" + buggy.LastAirTime.ToString("0.00")
                    + " grade=" + buggy.LastLandingGrade);
            }
        }

        if (t - phaseStarted < 8f) return;
        Check(maxAirTime > 0.25f, "20 deg ramp launch produced airtime",
            "max air " + maxAirTime.ToString("0.00") + " s");
        Check(buggy != null && (buggy.LandingCount > landingsBeforeLaunch || buggy.IsAirborne),
            "jump resolved to a graded landing or is still airborne",
            buggy != null ? "landings " + buggy.LandingCount + " grade " + buggy.LastLandingGrade : "no buggy");
        if (buggy == null || failures.Count > 0) { Finish(); return; }
        StartSpinTest(t);
    }

    // ------------------------------------------------------------------ camera spin test
    private static float spinStarted = -1f;

    private static void StartSpinTest(float t)
    {
        // Park the buggy high in the air so nothing else moves the camera, then roll the
        // chassis by hand. The camera heading and FOV must stay put while the buggy rolls in
        // front of it (the flip-framing fix), and the camera must not pick up any roll.
        PlaceBuggy(new Vector3(0f, 9f, 4f), Quaternion.identity);

        phase = 2;
        phaseStarted = t;
        spinStarted = -1f;
        Debug.Log("[SMOKE] setting up the mid-air camera spin test");
    }

    private static void CheckSpinTest(float t)
    {
        // Let the chase camera glide back to the buggy and settle before measuring.
        if (spinStarted < 0f)
        {
            PlaceBuggy(new Vector3(0f, 9f, 4f), Quaternion.identity);
            if (t - phaseStarted < 1.8f) return;
            Check(buggy.IsAirborne, "buggy reads airborne for the spin test");

            Camera camAtStart = Camera.main;
            if (camAtStart != null)
            {
                Vector3 flat = camAtStart.transform.forward;
                flat.y = 0f;
                camYawAtSpin = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                camFovAtSpin = camAtStart.fieldOfView;
                camPosAtSpin = camAtStart.transform.position;
            }
            spinStarted = t;
            Debug.Log("[SMOKE] spun the buggy in the air to test camera framing");
            return;
        }

        // Deterministic roll about a mixed world pitch/roll axis (roughly 220 deg/s).
        float angle = 220f * (t - spinStarted);
        PlaceBuggy(new Vector3(0f, 9f, 4f), Quaternion.AngleAxis(angle, new Vector3(1f, 0f, 1f).normalized));

        if (t - spinStarted < 0.9f) return;

        Camera cam = Camera.main;
        Check(cam != null, "camera available mid-flip");

        float spinUp = buggy.transform.up.y;
        Check(spinUp < 0.92f, "buggy really rolled in front of the camera", "up.y=" + spinUp.ToString("0.00"));

        if (cam != null)
        {
            Vector3 flat = cam.transform.forward;
            flat.y = 0f;
            float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float yawDrift = Mathf.Abs(Mathf.DeltaAngle(camYawAtSpin, yaw));
            Check(yawDrift < 6f, "camera heading held through the spin", "drift " + yawDrift.ToString("0.0") + " deg");
            Check(Mathf.Abs(cam.transform.right.y) < 0.05f, "camera did not roll with the buggy",
                "right.y=" + cam.transform.right.y.ToString("0.000"));
            Check(Mathf.Abs(cam.fieldOfView - camFovAtSpin) < 1f, "camera FOV held through the spin",
                cam.fieldOfView.ToString("0.0") + " vs " + camFovAtSpin.ToString("0.0"));

            Vector2 d = new Vector2(cam.transform.position.x - camPosAtSpin.x,
                cam.transform.position.z - camPosAtSpin.z);
            Check(d.magnitude < 1.5f, "camera did not swing around the buggy", "moved " + d.magnitude.ToString("0.00") + " m");
        }

        Finish();
    }

    private static void RunSettleChecks()
    {
        PhysicsTestBed bed = UnityEngine.Object.FindAnyObjectByType<PhysicsTestBed>();
        Check(bed != null, "test bed present (scene script bound)");
        if (bed == null) return;

        buggy = bed.Buggy;
        Check(buggy != null, "buggy built");
        if (buggy == null) return;

        Check(buggy.frontLeftWheel != null && buggy.frontRightWheel != null
            && buggy.rearLeftWheel != null && buggy.rearRightWheel != null, "four wheel colliders wired");
        Check(buggy.frontLeftVisual != null && buggy.rearRightVisual != null, "wheel visuals wired");

        int grounded = 0;
        for (int i = 0; i < 4; i++)
        {
            WheelCollider wheel = buggy.GetWheel(i);
            if (wheel != null && wheel.isGrounded) grounded++;
        }
        Check(grounded >= 3, "buggy settled on its wheels", grounded + "/4 grounded");

        float y = buggy.transform.position.y;
        Check(y > 0.3f && y < 2.5f, "buggy rests on the apron (not sunk or falling)", "y=" + y.ToString("0.00"));

        BuggyCamera follow = UnityEngine.Object.FindAnyObjectByType<BuggyCamera>();
        Check(follow != null && follow.target == buggy.transform, "chase camera follows the buggy");
        Camera cam = Camera.main;
        Check(cam != null, "main camera present");
        if (cam != null)
        {
            Check(Mathf.Abs(cam.fieldOfView - 60f) < 1.5f, "camera FOV at rest (no zoom breathing)",
                "fov=" + cam.fieldOfView.ToString("0.0"));
            Vector3 flat = cam.transform.forward;
            flat.y = 0f;
            Check(flat.sqrMagnitude > 0.05f, "camera looks along the ground plane (no roll)");
        }

        Check(UnityEngine.Object.FindAnyObjectByType<PhysicsDebugOverlay>() != null, "debug overlay attached");

        Check(GameObject.Find("Ramp 25") != null, "25 deg ramp built");
        Check(GameObject.Find("Gap pit 12m") != null, "12 m gap lane built");
        Check(GameObject.Find("Bump 13") != null, "bump field built");
        Check(GameObject.Find("Slope 20 crest") != null, "20 deg mound built");
        Check(GameObject.Find("Drop landing") != null, "drop-off landing floor built");
        Check(GameObject.Find("Sign GAP 12 m") != null, "signage built");

        Debug.Log("[SMOKE] settle checks complete: " + (failures.Count == 0 ? "PASS" : "FAIL"));
    }

    private static void StartLaunch(float t)
    {
        // Put the buggy 25 m short of the 20 degree kicker with a steady climb speed (just under
        // the 32 km/h shelf) and let it fly. The teleport is re-asserted through the Rigidbody
        // (not just the transform) because this runs on EditorApplication.update, outside the
        // player loop, where a transform write can be undone by interpolation before the next
        // physics step.
        buggy.autoReset = false;
        PlaceBuggy(new Vector3(0f, 0f, 180f), Quaternion.identity);
        if (buggy.Body != null)
        {
            buggy.Body.linearVelocity = new Vector3(0f, 0f, 8.2f);
            buggy.Body.angularVelocity = Vector3.zero;
        }
        landingsBeforeLaunch = buggy.LandingCount;
        maxAirTime = 0f;
        phase = 1;
        phaseStarted = t;
        nextTrace = t;
        Debug.Log("[SMOKE] launched at the 20 deg ramp");
    }

    // ------------------------------------------------------------------ helpers
    /// <summary>Places the buggy through the Rigidbody so the move survives interpolation.</summary>
    private static void PlaceBuggy(Vector3 position, Quaternion rotation)
    {
        buggy.TrialsTeleportTo(position, rotation);
        if (buggy.Body == null) return;
        buggy.Body.isKinematic = false;
        buggy.Body.position = buggy.transform.position;
        buggy.Body.rotation = rotation;
        Physics.SyncTransforms();
        buggy.Body.linearVelocity = Vector3.zero;
        buggy.Body.angularVelocity = Vector3.zero;
    }

    private static void Check(bool ok, string label, string detail = "")
    {
        if (ok)
        {
            Debug.Log("[SMOKE] OK   " + label + (detail.Length > 0 ? " - " + detail : ""));
            return;
        }
        Fail(label + (detail.Length > 0 ? " - " + detail : ""));
    }

    private static void Fail(string message)
    {
        failures.Add(message);
        Debug.LogError("[SMOKE] FAIL " + message);
    }

    private static void Finish()
    {
        SessionState.SetBool(SessionKey, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        hooked = false;

        if (failures.Count == 0)
        {
            Debug.Log("[SMOKE] RESULT PASS");
            Exit(0);
            return;
        }

        Debug.LogError("[SMOKE] RESULT FAIL (" + failures.Count + ")");
        for (int i = 0; i < failures.Count; i++) Debug.LogError("[SMOKE]   " + failures[i]);
        Exit(1);
    }

    private static void Exit(int code)
    {
        if (Application.isBatchMode) EditorApplication.Exit(code);
        else EditorApplication.isPlaying = false;
    }
}
