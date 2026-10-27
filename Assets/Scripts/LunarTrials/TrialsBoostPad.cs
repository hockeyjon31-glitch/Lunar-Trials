using UnityEngine;

/// <summary>
/// The trials jump-ramp BOOST pad: a strip on the run-in to a course ramp that shoves the
/// buggy forward while it is over the pad, so the ramp launches with real speed and the
/// flip controls get meaningful air time. Distance-checked in local space against the
/// player buggy (no collider/trigger dependency - WheelColliders are unreliable trigger
/// sources), force is mass-scaled so it works for every vehicle, and the shove is speed-
/// capped so the pad can never run the buggy away.
/// </summary>
public class TrialsBoostPad : MonoBehaviour
{
    [Tooltip("Extra forward acceleration while on the pad (m/s^2).")]
    public float boostAccel = 30f;
    [Tooltip("Forward speed the pad will not push past (m/s).")]
    public float boostSpeedCap = 28f;
    [Tooltip("Half width of the active strip (local X, metres).")]
    public float halfWidth = 4.8f;
    [Tooltip("Local Z where the strip begins (negative = behind the ramp base).")]
    public float zNear = -16f;
    [Tooltip("Local Z where the strip ends (just onto the ramp base).")]
    public float zFar = 1f;
    [Tooltip("Local Y above the ramp base the strip still catches the buggy at (metres).")]
    public float maxY = 4f;

    private Transform buggy;

    private void Start()
    {
        BuggyController b = FindAnyObjectByType<BuggyController>();
        if (b != null) buggy = b.transform;
    }

    private void FixedUpdate()
    {
        if (buggy == null) return;
        Vector3 local = transform.InverseTransformPoint(buggy.position);
        if (Mathf.Abs(local.x) > halfWidth || local.z < zNear || local.z > zFar || local.y > maxY)
            return;
        Rigidbody rb = buggy.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) return;
        Vector3 fwd = transform.forward;
        if (Vector3.Dot(rb.linearVelocity, fwd) < boostSpeedCap)
            rb.AddForce(fwd * boostAccel * rb.mass, ForceMode.Force);
    }
}
