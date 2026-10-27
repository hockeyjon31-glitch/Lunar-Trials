using System.Collections;
using UnityEngine;

/// <summary>
/// A harvestable resource nugget rendered as a subtle ground pad (e.g. Helium-3) off the
/// oxygen route. Collection is instant at any speed: drive the rover centre right over the
/// pad and it lights up briefly before vanishing, adding its score via
/// OxygenManager.CollectHarvestable. Nuggets spawn in visible ore clusters; the whole
/// cluster is what the proximity sonar pings when the rover gets near.
/// </summary>
public class Harvestable : MonoBehaviour
{
    [Header("Harvest")]
    [Tooltip("This pad's collection radius - set from Harvestable.CurrentPickupRadius at Awake.")]
    public float pickupRadius = 0.5f;
    [Tooltip("Points awarded for collecting this nugget.")]
    public int scoreValue = 100;

    /// <summary>
    /// The active collection radius across the run, set by GameBootstrap from the equipped
    /// rover's gather blade (1.0 m stock, scaled by the vehicle's ScoopMul): collecting is a
    /// stock ability on every rover, so the base pad asks for a drive over the pad and the
    /// line - not an upgrade - is the harvest skill. Purely a Harvestable rule - the oxygen
    /// pods are unaffected by the blade.
    /// </summary>
    public static float CurrentPickupRadius = 1.0f;

    [Header("Visual")]
    [Tooltip("The low ground pad (lights up white when harvested).")]
    public GameObject pad;
    [Tooltip("The faint glowing footprint under the pad.")]
    public GameObject padGlow;
    [Tooltip("Colour of the token flash popup (matches this planet's nugget theme).")]
    public Color popupColor = new Color(0.4f, 1f, 0.85f, 1f);
    [Tooltip("Pad tint (the planet's nugget colour; all nuggets share one colour).")]
    public Color tintColor = new Color(0.4f, 1f, 0.85f, 1f);

    // Cluster support: every nugget belongs to a visible ore cluster so the proximity sonar
    // pings the whole field of ore rather than each pad on its own.
    [System.NonSerialized] public int ClusterId = -1;

    /// <summary>True against the whole scene so the prospector ping can reach every nugget.</summary>
    public static readonly System.Collections.Generic.List<Harvestable> All = new System.Collections.Generic.List<Harvestable>();

    /// <summary>True once the nugget is an active, visible target.</summary>
    public bool Revealed { get; private set; } = true;
    /// <summary>True once the nugget has been harvested.</summary>
    public bool Collected { get; private set; }

    private BuggyController overlapping;

    private void Awake()
    {
        All.Add(this);
        pickupRadius = CurrentPickupRadius;
        // The overlap trigger is kept comfortably larger than the precise collection gate so
        // any part of the rover crossing the pad is detected, while the actual harvest still
        // waits for the rover centre to come close enough (the plow's skill check).
        float triggerRadius = Mathf.Max(pickupRadius + 0.8f, 1.4f);
        Collider triggerCollider = GetComponent<Collider>();
        if (triggerCollider == null)
        {
            triggerCollider = gameObject.AddComponent<SphereCollider>();
            ((SphereCollider)triggerCollider).radius = triggerRadius;
        }
        else
        {
            SphereCollider sc = triggerCollider as SphereCollider;
            if (sc != null) sc.radius = triggerRadius;
        }
        triggerCollider.isTrigger = true;
    }

    private void OnDestroy()
    {
        All.Remove(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (Collected) return;
        BuggyController bc = GetBuggyController(other);
        if (bc != null) overlapping = bc;
    }

    private void OnTriggerExit(Collider other)
    {
        BuggyController bc = GetBuggyController(other);
        if (bc != null && bc == overlapping) overlapping = null;
    }

    private static BuggyController GetBuggyController(Collider other)
    {
        return other.GetComponentInParent<BuggyController>() ?? other.GetComponent<BuggyController>();
    }

    private void Update()
    {
        // Institution: the rover must actually drive across the spot - once any part of the
        // buggy overlaps the pad we wait until the centre line (the chassis transform) is
        // within pickupRadius before harvesting.
        if (Collected || !Revealed || overlapping == null) return;

        float dx = overlapping.transform.position.x - transform.position.x;
        float dz = overlapping.transform.position.z - transform.position.z;
        if (dx * dx + dz * dz <= pickupRadius * pickupRadius)
        {
            Collect();
        }
    }

    private void Collect()
    {
        Collected = true;

        if (OxygenManager.Instance != null)
        {
            OxygenManager.Instance.CollectHarvestable(scoreValue);
        }

        // Harvest cue from the rover's procedural audio.
        RoverSounds rover = overlapping != null ? overlapping.GetComponent<RoverSounds>() : null;
        if (rover != null) rover.PlayCollectChime();

        // Token flash (+N) flying up from where the nugget was harvested, using the exact
        // token gain the HUD counter added (1:1 - the popup number is the token tally).
        int credits = scoreValue;
        if (OxygenManager.Instance != null) credits = Mathf.Max(1, OxygenManager.Instance.LastOreGain);
        ScorePopup.Spawn(transform.position, "+" + credits, popupColor);

        StartCoroutine(FlashAndHide());
    }

    // Lights the pad up bright white for a moment, then hides it to show it was harvested.
    private IEnumerator FlashAndHide()
    {
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        Color[] originals = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            originals[i] = renderers[i].sharedMaterial.color;
            renderers[i].sharedMaterial.color = Color.white;
        }

        yield return new WaitForSeconds(0.16f);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].sharedMaterial.color = originals[i];
        }
        if (pad != null) pad.SetActive(false);
        if (padGlow != null) padGlow.SetActive(false);
    }

    /// <summary>
    /// Finds the nearest ore cluster centre within <paramref name="maxRange"/> (XZ distance)
    /// of <paramref name="origin"/>, using each cluster's remaining (uncollected) members so
    /// a swept-out cluster stops pinging. Used by the proximity sonar so the rover can home
    /// in on a nearby field of ore.
    /// </summary>
    public static bool NearestCluster(Vector3 origin, float maxRange, out Vector3 center, out float dist)
    {
        center = origin;
        dist = float.MaxValue;
        var clusters = new System.Collections.Generic.List<int>();
        for (int i = 0; i < All.Count; i++)
        {
            Harvestable h = All[i];
            if (h.ClusterId >= 0 && !h.Collected && !clusters.Contains(h.ClusterId)) clusters.Add(h.ClusterId);
        }

        for (int c = 0; c < clusters.Count; c++)
        {
            Vector3 late = Vector3.zero;
            int n = 0;
            for (int i = 0; i < All.Count; i++)
            {
                Harvestable h = All[i];
                if (h.ClusterId != clusters[c] || h.Collected) continue;
                late += h.transform.position;
                n++;
            }
            if (n == 0) continue;
            late /= n;

            float dd = new Vector2(late.x - origin.x, late.z - origin.z).magnitude;
            if (dd < dist)
            {
                dist = dd;
                center = late;
            }
        }
        return dist <= maxRange;
    }
}