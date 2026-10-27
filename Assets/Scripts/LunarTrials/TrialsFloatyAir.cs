using UnityEngine;

/// <summary>
/// The trials ramp-jump FLOAT: while the buggy is in a real jump (airborne past a short
/// beat, so crest bounces stay grounded-feeling), gravity eases down to a light figure for
/// a genuinely floaty arc; on touchdown it eases straight back to the planet's gravity.
/// Physics.gravity is global, so dynamic props float along too - which reads consistently
/// ("low-g world") rather than wrong.
///
/// Added to the buggy by GameBootstrap for ramp challenges. Restores the planet gravity on
/// destroy (scene reload), so the next run's ApplyPlanetGravity starts clean regardless.
/// </summary>
public class TrialsFloatyAir : MonoBehaviour
{
    [Tooltip("Airborne gravity (m/s^2) - the floaty figure the jump arcs follow.")]
    public float floatyG = 2.8f;
    [Tooltip("Airtime (s) before the float engages - light crest bounces never trigger it.")]
    public float airGateSeconds = 0.35f;
    [Tooltip("How fast gravity eases between figures (m/s^2 per second).")]
    public float easeRate = 14f;

    private float planetG;
    private BuggyController buggy;

    private void Start()
    {
        planetG = Mathf.Abs(Physics.gravity.y);
        BuggyController b = FindAnyObjectByType<BuggyController>();
        if (b != null) buggy = b;
    }

    private void FixedUpdate()
    {
        bool floaty = buggy != null && buggy.IsAirborne && buggy.AirborneTime > airGateSeconds;
        float target = floaty ? floatyG : planetG;
        float current = Mathf.Abs(Physics.gravity.y);
        float next = Mathf.MoveTowards(current, target, easeRate * Time.fixedDeltaTime);
        if (!Mathf.Approximately(next, current))
            Physics.gravity = new Vector3(0f, -next, 0f);
    }

    private void OnDestroy()
    {
        Physics.gravity = new Vector3(0f, -planetG, 0f);
    }
}
