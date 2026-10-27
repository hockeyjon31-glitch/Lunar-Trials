using UnityEngine;

/// <summary>
/// Io's periodic JUPITER ECLIPSE. Every so often Jupiter passes between the sun and its moon;
/// the thin atmosphere chills so fast sulfur frost condenses across the whole surface, the
/// light fogs a pale sulfur-green and the surroundings darken a beat, and the HUD names the
/// event "Jupiter Eclipse: Sulfur Ice Alert!". A storm alert telegraphs each eclipse a few
/// seconds ahead so the player can brake before the chill - then the frost passes and handling
/// returns. Runs on every Io level (harvester survey, race canyon, ridge survival).
///
/// The frost does NOT slow the buggy down. It FREEZES it: the buggy holds the speed it already
/// has, the wheels stop turning, and steering and the brakes go vague, so an eclipse is a
/// committed glide rather than a punishment to be slowed out of. That is why the telegraph
/// matters - the speed you carry into the frost is the speed you are stuck with until it lifts.
/// The whole effect rides the buggy's single glaze channel, on top of every other floor hazard.
/// </summary>
public class IoEclipse : MonoBehaviour
{
    public BuggyController buggy;
    public HUD hud;
    public OxygenManager oxygen;

    [Tooltip("Seconds into the run before the first eclipse.")]
    public float firstMin = 8f;
    public float firstMax = 14f;
    [Tooltip("Gap between eclipses while the run stays live.")]
    public float gapMin = 28f;
    public float gapMax = 46f;
    [Tooltip("How long the warning holds before the frost lands.")]
    public float telegraphTime = 2.8f;
    [Tooltip("How long the surface stays slick while the eclipse is full.")]
    public float eclipseDuration = 6.5f;

    [Header("Frosted surface")]
    [Tooltip("How hard the eclipse freezes the ground, 0-1. This is the buggy's glaze: the " +
             "frost does not slow the rover down, it HOLDS the speed the rover already has, " +
             "stops the wheels turning, and makes steering and the brakes vague for its " +
             "duration. Brake BEFORE the chill lands - once it is down, the rover is committed.")]
    public float slipGlaze = 1f;

    [Header("Atmosphere")]
    public Color eclipseFog = new Color(0.62f, 0.75f, 0.55f);
    public Color eclipseAmbient = new Color(0.28f, 0.34f, 0.24f);

    private enum Ph { Calm, Telegraph, Eclipse }
    private Ph ph = Ph.Calm;
    private float nextAt;
    private float phaseEnd;
    private float blend;
    private Color baseFog;
    private Color baseAmbient;

    private void Start()
    {
        nextAt = Time.time + Random.Range(firstMin, firstMax);
        baseFog = RenderSettings.fogColor;
        baseAmbient = RenderSettings.ambientLight;
        buggy = FindAnyObjectByType<BuggyController>();
        oxygen = OxygenManager.Instance;
        hud = FindAnyObjectByType<HUD>();
    }

    private void Update()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (oxygen == null) oxygen = OxygenManager.Instance;

        // The eclipse only bites while a run is drivable: intro, countdown, game over, win
        // banners and boarding stand it down and snap the surface back to normal.
        bool live = buggy != null && buggy.inputEnabled
            && (oxygen == null || (!oxygen.gameOver && !oxygen.reachedEnd && !oxygen.endSequenceActive));
        if (!live)
        {
            StandDown();
            return;
        }

        switch (ph)
        {
            case Ph.Calm:
                if (Time.time >= nextAt)
                {
                    ph = Ph.Telegraph;
                    phaseEnd = Time.time + telegraphTime;
                    if (hud != null) hud.ShowWarning("Jupiter Eclipse: Sulfur Ice Alert!");
                    RoverSounds.PlayStormAlert();
                    if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.5f);
                }
                break;

            case Ph.Telegraph:
                if (Time.time >= phaseEnd)
                {
                    ph = Ph.Eclipse;
                    phaseEnd = Time.time + eclipseDuration;
                    if (hud != null) hud.HideWarning();
                    // IceResist, not a flat 1: a vehicle with the suspension for frost (the sprint) keeps
        // part of its wheels, steering and brakes through the eclipse, so it is only partly
        // held. Everyone else takes slipGlaze and stops dead.
        buggy.glazeFromEclipse = slipGlaze * (1f - VehicleSystem.IceResist);
                    if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.4f);
                }
                break;

            case Ph.Eclipse:
                if (Time.time >= phaseEnd)
                {
                    ph = Ph.Calm;
                    nextAt = Time.time + Random.Range(gapMin, gapMax);
                    buggy.glazeFromEclipse = 0f;
                }
                break;
        }

        // Ease the atmosphere toward the frost tint while eclipsed and back once it passes.
        blend = Mathf.MoveTowards(blend, ph == Ph.Eclipse ? 1f : 0f, Time.deltaTime / 0.8f);
        RenderSettings.fogColor = Color.Lerp(baseFog, eclipseFog, blend);
        RenderSettings.ambientLight = Color.Lerp(baseAmbient, eclipseAmbient, blend);
    }

    private void StandDown()
    {
        if (ph != Ph.Calm)
        {
            ph = Ph.Calm;
            if (hud != null) hud.HideWarning();
        }
        if (buggy != null && buggy.glazeFromEclipse != 0f)
        {
            buggy.glazeFromEclipse = 0f;
        }
        if (blend != 0f)
        {
            blend = 0f;
            RenderSettings.fogColor = baseFog;
            RenderSettings.ambientLight = baseAmbient;
        }
    }
}