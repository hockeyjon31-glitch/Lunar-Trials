using UnityEngine;

/// <summary>
/// An oxygen pod placed along the route. When the buggy drives close enough the pod is
/// "collected", topping up the player's oxygen (see OxygenManager.CollectPod).
/// The pod has a glowing visual and a rotating beacon so it is easy to spot from a distance.
/// </summary>
public class OxygenPod : MonoBehaviour
{
    [Header("Pod")]
    [Tooltip("Distance (in metres) at which the pod is considered collected.")]
    public float pickupRadius = 3.0f;
    [Tooltip("Mark the final pod in the route; collecting it ends the level in victory.")]
    public bool isFinalPod = false;

    [Header("Visual")]
    public GameObject beaconLight;
    public GameObject pickupGlow;
    public float beaconSpinSpeed = 60f;

    private bool collected = false;
    private Collider triggerCollider;

    /// <summary>True once the pod has been collected; collected pods are inert (no longer targets).</summary>
    public bool Collected { get { return collected; } }

    private void Awake()
    {
        // Use a trigger collider so the buggy can enter the pod's radius and collect it,
        // rather than physically bumping into a solid body.
        triggerCollider = GetComponent<Collider>();
        if (triggerCollider == null)
        {
            triggerCollider = gameObject.AddComponent<SphereCollider>();
            ((SphereCollider)triggerCollider).radius = pickupRadius;
        }
        triggerCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (collected) return;

        // Any part of the buggy (wheels/body tagged "Player" or with BuggyController) counts.
        bool isBuggy = other.CompareTag("Player")
                       || other.GetComponentInParent<BuggyController>() != null
                       || other.GetComponent<BuggyController>() != null;

        if (!isBuggy) return;

        RoverSounds rover = other.GetComponentInParent<RoverSounds>() ?? other.GetComponent<RoverSounds>();
        Collect(rover);
    }

    private void Collect(RoverSounds rover)
    {
        collected = true;

        if (OxygenManager.Instance != null)
        {
            OxygenManager.Instance.CollectPod(isFinalPod);
        }

        // Turn off the pod's beacon and glow to show it has been used.
        if (beaconLight != null) beaconLight.SetActive(false);
        if (pickupGlow != null) pickupGlow.SetActive(false);

        // Lunar Race gate arch: flip the arch's energy ring/beam/strip from amber to green.
        GateArchLook arch = GetComponent<GateArchLook>();
        if (arch != null) arch.SetCollected();

        // Air-swoosh as the oxygen is topped up.
        if (rover != null) rover.PlayOxygenCue();
        else
        {
            RoverSounds any = FindAnyObjectByType<RoverSounds>();
            if (any != null) any.PlayOxygenCue();
        }
    }

    private void Update()
    {
        // Spin the beacon so the pod is visible from far away.
        if (beaconLight != null && !collected)
        {
            beaconLight.transform.Rotate(Vector3.up, beaconSpinSpeed * Time.deltaTime);
        }
    }
}
