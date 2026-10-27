using UnityEngine;

/// <summary>
/// PROSPECTOR RUN navigational lifeline. The descent is dark and the fog pulls the world in
/// tight, so the canyon walls hide until they are on top of the buggy. Every frame this
/// component fishes for the terrain on the buggy's flanks and for drift off the carved lane,
/// and feeds the nearest call to the HUD compass as a warning line under the dial:
///   - WALL LEFT / WALL RIGHT when a lateral probe bumps terrain inside ~8 m;
///   - a "closing" note when BOTH flanks are near (a sharp narrowing of the lane);
///   - OFF ROUTE when the buggy has drifted past the canyon's own half-width, so the player
///     knows the lane is behind them and the compass pod is the way back.
/// The warning is written only when it CHANGES, so the HUD line stays steady instead of
/// strobing every frame.
/// </summary>
public class ProspectorWallWarning : MonoBehaviour
{
    [Tooltip("Compass + warning UI. Injected by GameBootstrap.")]
    public HUD hud;
    [Tooltip("The buggy. Injected by GameBootstrap.")]
    public BuggyController buggy;
    [Tooltip("Level terrain: supplies the canyon centreline and half width. Injected.")]
    public MoonTerrainGen terrain;

    [Tooltip("Lateral probe length (m): how far a flank can reach before it counts as no wall.")]
    public float probeDistance = 18f;
    [Tooltip("Wall is 'near' (warning) inside this distance.")]
    public float warnDistance = 8f;
    [Tooltip("Wall is 'very near' (emphasis arrows) inside this distance.")]
    public float dangerDistance = 4f;
    [Tooltip("How far past the lane half-width counts as OFF ROUTE.")]
    public float offRoutePadding = 1.5f;

    private string shown = "";

    private void Update()
    {
        if (hud == null || buggy == null || terrain == null) return;

        float x = buggy.transform.position.x;
        float z = buggy.transform.position.z;
        float lateral = x - terrain.RaceCenterX(z);

        string next = "";
        float leftD = ProbeFlank(-buggy.transform.right);
        float rightD = ProbeFlank(buggy.transform.right);
        float closest = Mathf.Min(leftD, rightD);
        if (closest < warnDistance)
        {
            bool leftNear = leftD < rightD;
            bool danger = closest < dangerDistance;
            if (leftNear)
                next = danger ? "WALL LEFT <<" : "WALL LEFT";
            else
                next = danger ? "WALL RIGHT >>" : "WALL RIGHT";
        }
        else if (leftD < warnDistance * 2f && rightD < warnDistance * 2f)
        {
            // Both flanks within medium range: the lane is narrowing (or a bend's walls).
            next = "CLOSING  -  HOLD THE CENTER";
        }

        // Drifted out of the carved lane entirely: the dial's pod is the way back in.
        if (next.Length == 0 && Mathf.Abs(lateral) > terrain.raceCourseHalfWidth + offRoutePadding)
        {
            next = "OFF ROUTE  -  FOLLOW THE DIAL";
        }

        if (next == shown) return;
        shown = next;
        if (shown.Length == 0) hud.HideNavWarning();
        else hud.ShowNavWarning(shown);
    }

    /// <summary>Sends a horizontal probe sideways from the buggy and returns how far the first
    /// terrain/obstacle collider sits (probeDistance when nothing is hit).</summary>
    private float ProbeFlank(Vector3 dir)
    {
        Vector3 origin = buggy.transform.position + Vector3.up * 0.6f;
        RaycastHit hit;
        if (Physics.Raycast(origin, dir, out hit, probeDistance))
        {
            return hit.distance;
        }
        return probeDistance;
    }
}