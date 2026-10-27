using UnityEngine;

/// <summary>
/// Passive proximity sonar on the buggy: whenever a visible ore cluster is within range the
/// rover ticks faster and louder the closer it gets, so a field of nuggets "pings" as you
/// approach it. Ore is always visible - there is nothing to reveal - the ping is a homing
/// aid for the clustered fields the level scatters near craters and hillsides.
/// </summary>
public class ClusterSonar : MonoBehaviour
{
    [Tooltip("Passive proximity sonar radius: ore clusters inside it make the rover tick louder the closer you get.")]
    public float sonarRadius = 90f;

    /// <summary>Compile-time copy of the default sonar radius, shared by the HUD compass ping.</summary>
    public const float SonarRadiusStatic = 90f;

    private float nextSonarTime = 0f;

    private void Update()
    {
        // The sonar is a drive aid, not a jingle: once the run ends (boarding / game over /
        // banner) the pings stop so they don't bleed into the next menu.
        OxygenManager o2 = OxygenManager.Instance;
        if (o2 != null && (o2.gameOver || o2.reachedEnd || o2.endSequenceActive)) return;

        if (Harvestable.NearestCluster(transform.position, sonarRadius, out _, out float dist))
        {
            float prox = 1f - Mathf.Clamp01(dist / sonarRadius);
            float cadence = Mathf.Lerp(1.35f, 0.22f, prox);
            if (Time.time >= nextSonarTime)
            {
                nextSonarTime = Time.time + cadence;
                RoverSounds rover = GetComponent<RoverSounds>();
                if (rover != null) rover.PlaySonarTick(prox);
            }
        }
    }
}