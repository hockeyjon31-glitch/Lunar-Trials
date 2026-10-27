using UnityEngine;

/// <summary>
/// The Lunar Trials BIKE rig: rides the RetroStyleGames dirt-bike model (P_RSG_Bike_B,
/// Built-in-pipeline variant) on a 2-wheel WheelCollider setup driven by the game's own
/// BuggyController. Arcade aids: an upright spring (never falls over), cosmetic lean into
/// turns, and visual steering — the whole front assembly yaws around the steering head.
/// The bike model's own physics (BicycleVehicle controller, colliders, rigidbody) is
/// stripped at build time. STRIP ORDER MATTERS: BicycleVehicle RequireComponents the
/// rigidbody, so scripts are destroyed before the rigidbody.
///
/// GROUND ALIGNMENT: the model's origin sits above its wheel-contact plane, so the rig
/// measures the lowest wheel bottom (world space, at build) and lifts the model onto the
/// collider contact plane. The same alignment is applied to the garage showroom preview.
/// Trials stance: the model is scaled bigger overall and wider (1.4x across the bars).
/// </summary>
public class TrialsBikeRig : MonoBehaviour
{
    public Transform frontSpin;
    public Transform rearSpin;

    /// <summary>The bike model root (after cleaning) — riders attach here.</summary>
    public Transform BikeVisual => bikeVisual;

    private Transform bikeVisual;
    private Rigidbody rb;
    private BuggyController buggy;
    private float visualLean;
    private float visualSteer;

    // Steering assembly (the front end gathered under one pivot) — FALLBACK only, used
    // when the asset's Animator can't do the job.
    private Transform steerPivot;

    // The asset's own Animator, kept ALIVE: if its controller exposes a steer-like float
    // we feed our input there and the animation drives the front end.
    private Animator bikeAnimator;
    private string steerParamName;

    /// <summary>Builds the bike: instantiates the model at the Trials stance, aligns its
    /// ground line to the collider contact plane (<paramref name="contactWorldY"/>), strips
    /// the foreign physics, and gathers the steering assembly.</summary>
    public void Build(BuggyController buggyCtl, Transform root, float contactWorldY)
    {
        buggy = buggyCtl;
        rb = root.GetComponent<Rigidbody>();

        GameObject bike = TrialsBikeBuilder.InstantiateBike(root);

        // Trials stance: bigger overall, and wider by up to 1.4x across the bars.
        bike.transform.localScale = new Vector3(1.4f, 1.22f, 1.22f);

        // Ground-line alignment: measure the model's lowest wheel bottom (world space,
        // AFTER the scale above) and lift it onto the collider contact plane (the root is
        // still at its spawn origin here, so world == local for this shift).
        float bikeBottom = TrialsBikeBuilder.LowestWheelBottomWorld(bike.transform);
        bike.transform.position += Vector3.up * (contactWorldY - bikeBottom);

        TrialsBikeBuilder.StripPhysics(bike);

        bikeVisual = bike.transform;

        // ANIMATOR COOPERATION (when an Animator exists): look for a float we can drive
        // with the steer input (Steer/Steering/Turn/Lean/Handlebar).
        bikeAnimator = bike.GetComponentInChildren<Animator>(true);
        if (bikeAnimator != null)
        {
            if (bikeAnimator.runtimeAnimatorController == null)
            {
                Debug.Log("[Trials] bike Animator has NO controller.");
                bikeAnimator = null;
            }
            else
            {
                var pars = bikeAnimator.parameters;
                Debug.Log("[Trials] bike Animator '" + bikeAnimator.runtimeAnimatorController.name + "' parameters: " +
                    (pars.Length == 0
                        ? "(none)"
                        : string.Join(", ", System.Array.ConvertAll(pars, p => p.name + "(" + p.type + ")"))));
                steerParamName = FindSteerParam(pars);
            }
        }
        else
        {
            Debug.Log("[Trials] bike has NO Animator - pivot steering only.");
        }

        // ALWAYS dump the hierarchy once - we steer by node names, so this is ground truth.
        Debug.Log("[Trials] bike hierarchy:\n" + TrialsBikeBuilder.DumpHierarchy(bike.transform, 0, 4));

        // Node-level steering is the FALLBACK: only when no animator (or no steer
        // parameter) do we gather the front assembly under our own pivot.
        if (bikeAnimator == null || string.IsNullOrEmpty(steerParamName))
            BuildSteeringAssembly(bike.transform);

        // Spin proxies at the wheel positions: BuggyController rotates these about X as its
        // wheel-visual references. (The bike's own mesh wheels are skinned, so a visible
        // spin via transforms is not possible - the proxies keep the buggy code happy.)
        frontSpin = new GameObject("BikeFrontSpin").transform;
        frontSpin.SetParent(bikeVisual, false);
        frontSpin.localPosition = new Vector3(0f, 0f, 0.85f);
        rearSpin = new GameObject("BikeRearSpin").transform;
        rearSpin.SetParent(bikeVisual, false);
        rearSpin.localPosition = new Vector3(0f, 0f, -0.75f);
    }

    /// <summary>Gathers the front assembly under one steering pivot: forks, bars and the
    /// front wheel + collider yaws around the steering head with the steer input.</summary>
    public void BuildSteeringAssembly(Transform bike)
    {
        // The steering head: the midpoint of the two fork tops, lifted slightly.
        Transform forkL = LunarRoverArt.FindDeep(bike, "Fork_Left");
        Transform forkR = LunarRoverArt.FindDeep(bike, "Fork_Right");
        if (forkL == null || forkR == null)
        {
            Debug.LogWarning("[Trials] steering pivot NOT built - Fork_Left=" +
                (forkL == null ? "MISSING" : "found") + ", Fork_Right=" +
                (forkR == null ? "MISSING" : "found") + ". Front end cannot steer visually.");
            return;
        }

        Vector3 head = (forkL.position + forkR.position) * 0.5f + Vector3.up * 0.12f;
        steerPivot = new GameObject("BikeSteerPivot").transform;
        steerPivot.SetParent(bike, true);                    // world-position-stays
        steerPivot.position = head;

        // The whole front assembly — forks, bars — under one steering pivot, yaws with the
        // steer input. PHYSICS NODES STAY PUT: BuggyController steers the asset's
        // Front_Wheel_Collider via steerAngle; reparenting it under a yawing pivot would
        // double-steer it (steerAngle + pivot yaw) and wreck the handling. Only visual
        // nodes ride the pivot.
        Transform handlebarMain = LunarRoverArt.FindDeep(bike, "Handlebar_Main");
        Transform handleL = LunarRoverArt.FindDeep(bike, "Handle_Left");
        Transform handleR = LunarRoverArt.FindDeep(bike, "Handle_Right");

        // THE CRITICAL ONE: the VISIBLE front end (SK_rsg_LastGuns_bike_Front_Wheel and
        // SK_rsg_LastGuns_rudder_01) are skinned meshes weighted to the rig's
        // 'rudder_bone' (the asset's machine-translated name for the steering head). The
        // Colliders/* nodes are empty shells - only the bone carries the rendered forks,
        // bars and wheel. Without it under the pivot, steering is invisible.
        Transform rudderBone = LunarRoverArt.FindDeep(bike, "rudder_bone");
        if (rudderBone == null)
            Debug.LogWarning("[Trials] rudder_bone NOT FOUND - visible front end will NOT steer.");

        var gathered = new System.Collections.Generic.List<string>();
        foreach (var t in new[] { forkL, forkR, handlebarMain, handleL, handleR, rudderBone })
        {
            if (t == null) continue;
            t.SetParent(steerPivot, true);
            gathered.Add(t.name);
        }
        // Weld: pivot's local Y aligned with the bike up at setup.
        steerPivot.localRotation = Quaternion.identity;
        // Reparenting keeps world placement; the fork bones + front wheel + collider now
        // turn with the pivot's yaw around the steering head.
        Debug.Log("[Trials] steering pivot built at " + head + "; gathered: " +
            (gathered.Count == 0 ? "(nothing!)" : string.Join(", ", gathered)));
    }

    /// <summary>Picks the animator float we can drive with the steer input.</summary>
    private static string FindSteerParam(AnimatorControllerParameter[] pars)
    {
        foreach (var want in new[] { "Steer", "Steering", "Turn", "Lean", "Handlebar" })
            foreach (var p in pars)
                if (p.type == AnimatorControllerParameterType.Float &&
                    string.Equals(p.name, want, System.StringComparison.OrdinalIgnoreCase))
                    return p.name;
        return null;
    }

    private void FixedUpdate()
    {
        if (rb == null || bikeVisual == null) return;

        // TRIALS AIR FLIPS: the upright spring stands down while flip control is committed
        // (the controller's latch rides out wheel-graze flickers) - otherwise it fights the
        // roll past 90 degrees and no clean loop is possible. The bike tumbles freely under
        // flip control, then the spring catches it again on the ground.
        if (buggy != null && buggy.FlipControlActive)
            return;

        // Upright spring: roll the bike back toward vertical around its forward axis.
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();
        float roll = Mathf.Atan2(
            Vector3.Dot(transform.right, Vector3.up),
            Vector3.Dot(transform.up, Vector3.up)) * Mathf.Rad2Deg;
        float rollRate = Vector3.Dot(rb.angularVelocity, fwd);
        rb.AddTorque(fwd * (-roll * 2.5f - rollRate * 0.6f), ForceMode.Acceleration);
    }

    private void Update()
    {
        // Cosmetic lean into turns (purely visual - the physics never rolls).
        if (bikeVisual == null) return;
        float speedFrac = Mathf.Clamp01(buggy != null ? buggy.CurrentSpeedKmh / 40f : 0f);
        float target = -DrivingInput.Steer * 28f * speedFrac;
        visualLean = Mathf.Lerp(visualLean, target, Time.deltaTime * 5f);
        bikeVisual.localRotation = Quaternion.Euler(0f, 0f, visualLean);

        // Steering visuals, cooperative first: feed the asset's own Animator our steer
        // input and let its animation move the front end. Only without a live controller
        // (or steer parameter) do we yaw the gathered steering pivot ourselves.
        // SMOOTHED at the same rate as BuggyController's physics steer
        // (steeringSmoothness = 6), so the bars sweep instead of snapping to full lock.
        visualSteer = Mathf.Lerp(visualSteer, DrivingInput.Steer, Time.deltaTime * 6f);
        if (bikeAnimator != null && !string.IsNullOrEmpty(steerParamName))
            bikeAnimator.SetFloat(steerParamName, visualSteer);
        else if (steerPivot != null)
            steerPivot.localRotation = Quaternion.Euler(0f, visualSteer * 30f, 0f);
    }

}

/// <summary>Static construction helpers for the Trials bike model (shared by the in-game
/// rig and the garage showroom preview).</summary>
public static class TrialsBikeBuilder
{
    /// <summary>Instantiates the bike model under <paramref name="parent"/>, lifts it onto
    /// <paramref name="contactWorldY"/> (the collider contact plane in world space) and
    /// strips its foreign physics. Measurement happens BEFORE stripping (it needs the
    /// WheelColliders).</summary>
    public static GameObject InstantiateClean(Transform parent, float contactWorldY)
    {
        GameObject bike = InstantiateBike(parent);
        float bikeBottom = LowestWheelBottomWorld(bike.transform);
        bike.transform.position += Vector3.up * (contactWorldY - bikeBottom);
        StripPhysics(bike);
        return bike;
    }

    /// <summary>Lowers/raises the bike model so its wheel bottoms sit exactly on
    /// <paramref name="floorWorldY"/> (world space) - the showroom floor fix.</summary>
    public static void AlignToFloor(GameObject bike, float floorWorldY)
    {
        float bikeBottom = LowestWheelBottomWorld(bike.transform);
        bike.transform.position += Vector3.up * (floorWorldY - bikeBottom);
    }

    /// <summary>Instantiates the bike model under <paramref name="parent"/> (no stripping
    /// yet - call <see cref="StripPhysics"/> after measuring wheel positions).</summary>
    public static GameObject InstantiateBike(Transform parent)
    {
        GameObject prefab = Resources.Load<GameObject>("Trials_Bike_Clean");
        if (prefab == null)
        {
            Debug.LogError("[Trials] bike prefab missing from Resources (Trials_Bike_Clean).");
            return null;
        }
        GameObject bike = Object.Instantiate(prefab, parent);
        bike.name = "TrialsBike_Visual";
        bike.transform.localPosition = Vector3.zero;
        bike.transform.localRotation = Quaternion.identity;
        bike.transform.localScale = Vector3.one;
        return bike;
    }

    /// <summary>Removes the asset's own physics and DRIVER scripts. STRIP ORDER: MonoBehaviours
    /// first (BicycleVehicle RequireComponents the rigidbody - removing the rigidbody first
    /// fails with "depends on it"), then the rigidbody, then any leftover colliders.
    /// IMPORTANT: the Animator is deliberately KEPT — the asset's own animation owns the
    /// front-end bones, and we cooperate with it (feeding our steer input into its
    /// controller parameter) instead of fighting it per-frame.</summary>
    public static void StripPhysics(GameObject bike)
    {
        foreach (var mb in bike.GetComponentsInChildren<MonoBehaviour>(true)) Object.Destroy(mb);
        foreach (var r in bike.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(r);
        foreach (var c in bike.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        NormalizeRotations(bike.transform);
    }

    /// <summary>Guards against the editor's "QuaternionToEuler: Input quaternion was not
    /// normalized" error: normalizes every localRotation in the hierarchy and logs any node
    /// that arrived denormalized (so we learn where bad data came from).</summary>
    public static void NormalizeRotations(Transform node)
    {
        Quaternion q = node.localRotation;
        float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (Mathf.Abs(m - 1f) > 1e-4f)
        {
            Debug.LogWarning($"[Trials] denormalized rotation on '{node.name}' (magnitude {m:F4}) - normalized.");
            node.localRotation = m < 1e-6f ? Quaternion.identity : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }
        for (int i = 0; i < node.childCount; i++) NormalizeRotations(node.GetChild(i));
    }

    /// <summary>Lowest wheel-bottom height (world Y) across every WheelCollider in the
    /// bike hierarchy. Returns 0 when none are found (already stripped).</summary>
    public static float LowestWheelBottomWorld(Transform bike)
    {
        float min = float.MaxValue;
        bool found = false;
        Collect(bike, ref min, ref found);
        return found ? min : 0f;
    }

    /// <summary>Shallow text dump of the hierarchy for diagnostics (depth-limited; deeper
    /// levels collapse into a "... (n more)" line).</summary>
    public static string DumpHierarchy(Transform t, int depth, int maxDepth)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < depth; i++) sb.Append("  ");
        sb.Append(t.name).Append('\n');
        if (depth >= maxDepth)
        {
            if (t.childCount > 0)
            {
                for (int i = 0; i < depth + 1; i++) sb.Append("  ");
                sb.Append("... (").Append(t.childCount).Append(" more)\n");
            }
            return sb.ToString();
        }
        for (int i = 0; i < t.childCount; i++) sb.Append(DumpHierarchy(t.GetChild(i), depth + 1, maxDepth));
        return sb.ToString();
    }

    private static void Collect(Transform node, ref float min, ref bool found)
    {
        var wc = node.GetComponent<WheelCollider>();
        if (wc != null)
        {
            found = true;
            min = Mathf.Min(min, node.position.y - wc.radius);
        }
        foreach (Transform child in node) Collect(child, ref min, ref found);
    }
}
