using UnityEngine;

/// <summary>
/// Invisible lateral wind push. Venus carries the only real wind - firmest on the exposed
/// Ridge (no wall to block it), a light breeze across the open survey plains, and barely a
/// nudge down the walled canyon - while Mars' Ridge rides faint dust-moving breezes and
/// Titan's Ridge gets a near-imaginary breeze. A wind-rated vehicle (the Favonius) shrugs most
/// of a gust off, so the same squall reads very differently depending on what you drive
/// (<see cref="VehicleSystem.WindResist"/>). Every gust is TELEGRAPHED before it
/// lands: an arrow beside the compass shows which way the gust shoves with its strength
/// underneath, a short lead time before the push, so the player can brace and hold a line
/// into it. Each gust slams the buggy sideways for a moment, then the calm resumes. Stays
/// quiet whenever the run isn't drivable.
/// </summary>
public class WindGusts : MonoBehaviour
{
    [Header("References (found automatically if left null)")]
    public BuggyController buggy;
    public OxygenManager oxygen;
    public HUD hud;

    [Header("Gust tuning")]
    [Tooltip("How hard a gust shoves the buggy sideways (lateral acceleration, m/s^2).")]
    public float gustStrength = 6f;
    [Tooltip("How long each gust lasts (seconds).")]
    public float gustDuration = 1.8f;
    [Tooltip("Lead time the direction arrow holds before the shove lands (seconds) - enough to brace.")]
    public float warnLead = 1.1f;
    [Tooltip("Calm gap between gusts (seconds).")]
    public float calmMin = 5f;
    public float calmMax = 10f;
    [Tooltip("Nudge of the camera when the gust shove lands.")]
    public float gustShake = 0.5f;

    private Rigidbody rb;
    private float nextGustAt;
    private float gustStartAt;
    private float gustEndAt;
    private bool warningShown;
    private int dir = 1;

    private void Start()
    {
        nextGustAt = Time.time + Random.Range(calmMin, calmMax);
    }

    private void Update()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (buggy == null || !buggy.inputEnabled) return;
        if (oxygen == null) oxygen = OxygenManager.Instance;
        if (oxygen != null && (oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive))
        {
            ClearIndicator();
            return;
        }

        // A new gust approaches: show the direction arrow + strength so the player knows which
        // way to lean, then the shove starts warnLead later.
        if (!warningShown && Time.time >= nextGustAt && Time.time >= gustEndAt)
        {
            dir = Random.value < 0.5f ? 1 : -1;
            warningShown = true;
            if (hud == null) hud = FindAnyObjectByType<HUD>();
            if (hud != null)
            {
                hud.ShowWarning(dir > 0 ? "CROSS WIND! »" : "« CROSS WIND!");
                hud.ShowWindGust(dir, gustStrength);
            }

            gustStartAt = Time.time + warnLead;          // when the force actually lands
            gustEndAt = gustStartAt + gustDuration;      // total warn + push window
            nextGustAt = gustEndAt + Random.Range(calmMin, calmMax);
        }

        if (Time.time >= gustStartAt && Time.time < gustEndAt)
        {
            if (Time.time - gustStartAt <= Time.deltaTime && gustShake > 0f && BuggyCamera.Instance != null)
            {
                BuggyCamera.Instance.AddShake(gustShake);
            }
            Push();
        }
        else if (warningShown && Time.time >= gustEndAt)
        {
            ClearIndicator();
        }
    }

    private void ClearIndicator()
    {
        if (!warningShown) return;
        warningShown = false;
        if (hud == null) hud = FindAnyObjectByType<HUD>();
        if (hud != null)
        {
            hud.HideWindGust();
            hud.HideWarning();
        }
    }

    private void Push()
    {
        if (rb == null) rb = buggy.GetComponent<Rigidbody>();
        if (rb == null) return;
        // A craft built to ride the wind (the Favonius) shrugs most of a gust off, so a squall
        // that upsets a wheeled buggy barely rocks it. See VehicleSystem.WindResist.
        float resist = Mathf.Clamp01(1f - VehicleSystem.WindResist);
        rb.AddForce(new Vector3(dir * gustStrength * resist * rb.mass, 0f, 0f), ForceMode.Force);
    }

    // Used by Mars ridge dust breezes: body of the gust is dust puffs, direction applied here.
    public void SetStrength(float newStrength)
    {
        gustStrength = newStrength;
    }
}