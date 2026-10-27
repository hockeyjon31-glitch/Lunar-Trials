using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// End-of-level cinematic. When the final oxygen pod is collected a landing pad is
/// marked on the ground just beyond the cannister. The player must drive the buggy
/// onto the pad; once there, a rocket lander descends in front of it and touches down,
/// its hatch swings down, the buggy drives inside, the hatch closes again, the rocket
/// lifts back off with the buggy aboard, and the congrats board appears
/// (OxygenManager.CompleteLevel) before advancing. Harvest no longer raises a
/// "LEVEL X COMPLETE!" banner here - the board and star ceremony announce the clear.
///
/// GameBootstrap creates this component; it subscribes to OxygenManager.OnFinalPodCollected.
/// </summary>
public class LevelEndSequence : MonoBehaviour
{
    [Header("References")]
    public BuggyController buggy;
    public MoonTerrainGen terrain;
    public OxygenManager oxygen;

    [Header("Landing zone")]
    [Tooltip("How far beyond the final cannister the landing pad marker appears (kept short so it stays inside the drivable zone).")]
    public float padDistanceFromPod = 80f;
    [Tooltip("Fraction of the buggy's reset leash the pad is clamped inside, so it can never spawn unreachable.")]
    public float padMaxReachFactor = 0.85f;
    [Tooltip("How close the buggy must reach the pad centre before boarding begins.")]
    public float padAcceptRadius = 5f;
    [Tooltip("Radius of the painted landing pad on the ground.")]
    public float padRadius = 11f;

    [Tooltip("PROSPECTOR RUN: fraction of the ore field that must be collected before the landing pad banks the clear (every nugget = the PERFECT bonus).")]
    public float prospectorClearFraction = 0.5f;
    [Header("Timing (seconds)")]
    [Tooltip("How long the buggy takes to swivel to face the lander.")]
    public float turnDuration = 0.6f;
    [Tooltip("How long the lander takes to descend and touch down.")]
    public float descentDuration = 3.2f;
    [Tooltip("How long the hatch takes to swing fully open.")]
    public float hatchOpenDuration = 1.5f;
    [Tooltip("How long the buggy takes to drive into the lander.")]
    public float driveInDuration = 3.0f;
    [Tooltip("How long the hatch takes to close behind the buggy.")]
    public float hatchCloseDuration = 1.5f;
    [Tooltip("How long the lander takes to lift back off with the buggy aboard.")]
    public float liftDuration = 3.0f;
    [Tooltip("How high the lander climbs before the banner shows.")]
    public float liftHeight = 55f;
    [Tooltip("Small pause after reaching altitude before the banner shows.")]
    public float finalPause = 0.8f;

    [Header("Lander placement")]
    [Tooltip("How high above the ground the lander starts its descent.")]
    public float spawnHeight = 90f;
    [Tooltip("How far in front of the buggy the lander touches down.")]
    public float touchdownDistance = 24f;

    private enum Phase { Idle, Turn, Descend, HatchOpen, DriveIn, HatchClose, Liftoff, Done }

    private Phase phase = Phase.Idle;
    private bool running;
    private float timer;

    /// <summary>True once the end-of-level cinematic has actually begun. This object exists -
    /// with its component enabled - from level build time, so callers must test this rather
    /// than merely finding the component, or pause ends up blocked for the whole run.</summary>
    public bool IsRunning => running;

    private bool waitingForPad;
    private bool prospectorGateRefused;
    private bool challengeGateRefused;
    private Vector3 padCenter;
    private Transform padRoot;
    private HUD hud;

    private Transform lander;
    private Transform hatchHinge;
    private RocketExhaust exhaustLeft;
    private RocketExhaust exhaustRight;
    private ParticleSystem groundDust;   // dust / steam ring blown across the pad by the engines
    private Rigidbody buggyRb;
    private Transform[] wheelVisuals;

    private Vector3 touchdownPoint;
    private Quaternion facingRotation;
    private Vector3 driveInStart;
    private Vector3 bayInterior;
    private bool driveInStartCaptured;

    private AudioSource rocketSrc;
    private Coroutine burnFade;
    private bool _takeOffTriggered;

    // Lunar Race replaces the "reach the landing pad" objective with a proper FINISH LINE
    // stretched across the canyon's far end. The pad is built there up front, and crossing
    // the line (with every gate passed) begins the boarding sequence. Gates left unpassed
    // bar the finish: the sequence refuses to begin and the missed-gate alarm keeps flashing.
    private bool raceFinishMode;
    private float raceFinishZ;
    private bool raceCompletionRefused;

    // RACE TIME LIMIT: once the BRONZE rival has crossed the line the player is given a short
    // grace before the race auto-concludes. A player still short of the finish after that is
    // NOT left to trundle forever - the screen fades to black (no lander boarding) and the
    // normal race result menu follows, exactly as if the run had ended.
    [Tooltip("After the BRONZE rival crosses the line, how many more seconds the player gets "
            + "to finish before the race auto-concludes (fade to black, no boarding).")]
    public float cutoffAfterBronze = 10f;
    [Tooltip("How long the auto-conclude fade to black takes (seconds).")]
    public float cutoffFadeDuration = 1.2f;
    private float bronzeCrossedAt = -1f;
    private bool cutoffFading;
    private float cutoffFadeAt;
    private GameObject cutoffFadeGo;

    private static Material MakeSolid(Color color, float glossiness)
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        mat.SetFloat("_Glossiness", glossiness);
        return mat;
    }

    /// <summary>A private, tinted copy of YFMetal swatch 37 so a hull part takes a metallic finish
    /// in its own colour without importing a new swatch - ZERO extra build size. The swatch's
    /// grain, normal and spec/gloss are kept; only the diffuse tint changes.</summary>
    private static Material MetalTint(Color tint)
    {
        Material mat = YFMetal.New(37);
        mat.color = tint;
        return mat;
    }

    // The landing pad must be reachable from the final pod's position, which on the thicker
    // atmospheres (Io/Titan) sits beyond the fog's end. Standard materials would render it fully
    // fog-coloured there (invisible), so the whole pad + beacon is built from Unlit/Color, which
    // ignores fog and always reads as the bright ground marker.
    private static Material MakeSolidUnlit(Color color)
    {
        Material mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = color;
        return mat;
    }

    private void Start()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (terrain == null) terrain = FindAnyObjectByType<MoonTerrainGen>();
        if (oxygen == null) oxygen = OxygenManager.Instance;
        if (oxygen != null) oxygen.OnFinalPodCollected += OnFinalPod;
        if (buggyRb == null && buggy != null) buggyRb = buggy.GetComponent<Rigidbody>();
        hud = FindAnyObjectByType<HUD>();

        wheelVisuals = new[]
        {
            buggy != null ? buggy.frontLeftVisual : null,
            buggy != null ? buggy.frontRightVisual : null,
            buggy != null ? buggy.rearLeftVisual : null,
            buggy != null ? buggy.rearRightVisual : null,
        };

        // Lunar Race (and Ridge Survival) run/finish style: the finish line and pad are laid
        // at the course's far end up front; the run ends by driving THROUGH the gantry.
        if (GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival)
        {
            raceFinishMode = true;
            raceFinishZ = terrain != null ? terrain.RaceFinishZ
                : MoonTerrainGen.RaceStartZ + (terrain != null ? terrain.raceLength : 1144f);

            if (terrain != null)
            {
                padCenter = new Vector3(
                    terrain.RaceCenterX(raceFinishZ),
                    0f,
                    raceFinishZ);
                padCenter.y = terrain.SampleHeight(padCenter);
            }
            else
            {
                padCenter = new Vector3(0f, 0f, raceFinishZ);
            }

            BuildFinishGantry();
            BuildLandingPad();
            waitingForPad = true;
            // (No "RACE TO THE FINISH LINE" hint - the 3-2-1-GO countdown ceremony owns the
            // start of a race, and the countdown's GO banner hands over to the HUD.)
            return;
        }

        // LUNAR TRIALS: the finish area stands at the course's far end FROM THE START (no
        // mid-run pad placement, no rocket boarding) - the run ends by driving into the
        // marked landing zone past the checkered line.
        if (ChallengeRunner.Active != null && TrialsCourse.Active != null && terrain != null)
        {
            padCenter = new Vector3(TrialsCourse.Active.FinishXZ.x, 0f, TrialsCourse.Active.FinishXZ.y);
            padCenter.y = terrain.SampleHeight(padCenter);
            BuildTrialsFinishLine();
            BuildLandingPad();
            waitingForPad = true;
        }
    }

    private void OnDestroy()
    {
        if (oxygen != null) oxygen.OnFinalPodCollected -= OnFinalPod;
        if (groundDust != null) Destroy(groundDust.gameObject);
    }

    /// <summary>
    /// Called when the final pod is collected: marks a landing pad on the ground just
    /// past the cannister. The buggy keeps driving; reaching the pad starts Begin().
    /// </summary>
    private void OnFinalPod()
    {
        // In Lunar Race the gates are checkpoint ARCHES: the final gate's collection must NOT
        // paint a mid-course pad (that is the Harvester behaviour). The finish line already
        // stands at the course's far end and the run ends by driving through it.
        if (GameFlow.Mode == GameMode.Race || GameFlow.IsRidgeSurvival) return;
        // LUNAR TRIALS course: the finish area is already standing at the run's end - no
        // mid-run pad placement (and nothing behind the buggy, ever).
        if (ChallengeRunner.Active != null && TrialsCourse.Active != null) return;
        if (waitingForPad || running || buggy == null) return;
        waitingForPad = true;

        // PROSPECTOR RUN: the pad is a canyon goal, not a survey scatter. Park it ON the carved
        // floor 50-100 m beyond the final cannister (padDistanceFromPod = 80), always ahead of
        // the buggy and never up on the cliff walls this dark descent is built around.
        if (GameFlow.Mode == GameMode.Prospector && terrain != null && buggy != null)
        {
            Transform finalPodAnchor = FindFinalPod();
            Vector3 anchorInFront = finalPodAnchor != null ? finalPodAnchor.position : buggy.transform.position;
            float padZ = anchorInFront.z + padDistanceFromPod;
            padZ = Mathf.Clamp(padZ, buggy.transform.position.z + 30f, terrain.RaceFinishZ - 6f);
            padCenter = new Vector3(terrain.RaceCenterX(padZ), 0f, padZ);
            padCenter.y = terrain.SampleHeight(padCenter);
            BuildLandingPad();
            if (hud != null) hud.ShowHint("REACH THE LANDING ZONE",
                ChallengeRunner.Active != null ? 6f : 0f);
            return;
        }

        // Place the pad BEYOND the cannister, but only within the buggy's FORWARD 180 degrees
        // (a pad that spawns behind the player reads as a bug), and on the flattest patch of
        // that semicircle so the painted deck is not half-buried by a slope. The sweep runs
        // across the buggy's forward semicircle in 15 degree steps at padDistanceFromPod from
        // the cannister; each candidate is scored by terrain unevenness under the disc, with a
        // gentle bias toward straight-ahead so "flattest" slightly favours "directly ahead".
        Transform finalPod = FindFinalPod();
        Vector3 podPos = finalPod != null ? finalPod.position : buggy.transform.position;
        Vector3 buggyFwd = buggy.transform.forward;
        buggyFwd.y = 0f;
        if (buggyFwd.sqrMagnitude < 0.01f) buggyFwd = Vector3.forward;
        buggyFwd.Normalize();

        if (terrain != null)
        {
            const int sweepCount = 13;          // -90 .. +90 degrees, 15 degree steps
            float bestScore = float.MaxValue;
            Vector3 bestPos = podPos + buggyFwd * padDistanceFromPod;
            for (int i = 0; i < sweepCount; i++)
            {
                float angleDeg = -90f + 180f * i / (sweepCount - 1);
                Vector3 candidate = podPos + Quaternion.Euler(0f, angleDeg, 0f) * buggyFwd * padDistanceFromPod;

                // Safety clamp: keep the pad inside the drivable zone. The buggy's reset leash
                // (maxTravelFromSpawn) is measured from its spawn, so a pad pushed past that by a
                // long route would be unreachable (the buggy teleports back before arriving).
                Vector3 flat = candidate - buggy.SpawnPosition;
                flat.y = 0f;
                float maxPadRange = buggy.maxTravelFromSpawn * Mathf.Clamp01(padMaxReachFactor);
                if (flat.sqrMagnitude > maxPadRange * maxPadRange)
                    candidate = buggy.SpawnPosition + flat.normalized * maxPadRange;

                // Flattest wins; straighter angles get a small scoring bonus (metres of
                // perceived unevenness) so near-ties resolve toward the buggy's heading.
                float score = TerrainUnevenness(candidate) + Mathf.Abs(angleDeg) / 90f * 1.2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestPos = candidate;
                }
            }
            padCenter = bestPos;

            // Sit the deck on the HIGHEST ground under the disc (plus a whisker), so no part
            // of the pad texture disappears under the terrain even on a gentle slope.
            padCenter.y = MaxPadGroundHeight(padCenter) + 0.05f;
        }
        else
        {
            Vector3 dir = buggyFwd;
            padCenter = podPos + dir * padDistanceFromPod;
            padCenter.y = 0f;
        }

        BuildLandingPad();

        if (hud != null) hud.ShowHint("REACH THE LANDING ZONE",
            ChallengeRunner.Active != null ? 6f : 0f);
    }

    /// <summary>How unlevel the terrain is under the pad disc: the largest height difference
    /// between the centre and a ring of points around the pad's rim. Lower = flatter.</summary>
    private float TerrainUnevenness(Vector3 centre)
    {
        const int ring = 8;
        float centreHeight = terrain.SampleHeight(centre);
        float worst = 0f;
        for (int i = 0; i < ring; i++)
        {
            float a = i * Mathf.PI * 2f / ring;
            Vector3 p = centre + new Vector3(Mathf.Sin(a) * padRadius * 0.92f, 0f, Mathf.Cos(a) * padRadius * 0.92f);
            worst = Mathf.Max(worst, Mathf.Abs(terrain.SampleHeight(p) - centreHeight));
        }
        return worst;
    }

    /// <summary>The highest terrain height anywhere on the pad's footprint (centre + rim ring),
    /// so the deck can be lifted just clear of every sample instead of slicing into a slope.</summary>
    private float MaxPadGroundHeight(Vector3 centre)
    {
        const int ring = 8;
        float top = terrain.SampleHeight(centre);
        for (int i = 0; i < ring; i++)
        {
            float a = i * Mathf.PI * 2f / ring;
            Vector3 p = centre + new Vector3(Mathf.Sin(a) * padRadius * 0.92f, 0f, Mathf.Cos(a) * padRadius * 0.92f);
            top = Mathf.Max(top, terrain.SampleHeight(p));
        }
        return top;
    }

    private Transform FindFinalPod()
    {
        foreach (OxygenPod pod in FindObjectsByType<OxygenPod>(FindObjectsInactive.Include))
            if (pod.isFinalPod) return pod.transform;
        return null;
    }

    /// <summary>PROSPECTOR RUN clear gate: true once the collected ore reaches the required
    /// fraction of the field (0.5). An empty field reads as open so a degenerate build can
    /// never soft-lock the run.</summary>
    private bool ProspectorGateOpen(OxygenManager o)
    {
        if (o.TotalHarvestables <= 0) return true;
        return (float)o.HarvestablesCollected / o.TotalHarvestables >= prospectorClearFraction;
    }

    /// <summary>Called when the buggy reaches the landing pad: freezes the run and starts boarding.</summary>
    public void Begin()
    {
        if (running || buggy == null) return;
        running = true;

        if (oxygen != null) oxygen.BeginEndSequence(); // stop the oxygen clock, the run is over
        EvaluateNewgroundsSecrets(); // arrival values, before the bonus conversion drains the tank
        if (hud != null) hud.HideHint();
        if (hud != null) hud.HideRaceCutoffCountdown(); // the race is over: drop the cutoff warning
        if (hud != null) hud.ClearLandingZone();

        buggyRb = buggy.GetComponent<Rigidbody>();
        buggy.inputEnabled = false;

        // The rover has stepped onto the metal landing pad - play the clank as it boards.
        RoverSounds roverAudio = buggy.GetComponent<RoverSounds>();
        if (roverAudio != null) roverAudio.PlayLandingPadClank();

        // Freeze the buggy so it stops SOLID on the deck (velocity zeroed, kinematic, wheels
        // off) - in the trials format this freeze IS the finish: no rocket follows.
        buggyRb = buggy.GetComponent<Rigidbody>();
        buggy.inputEnabled = false;
        buggyRb.linearVelocity = Vector3.zero;
        buggyRb.angularVelocity = Vector3.zero;
        buggyRb.isKinematic = true;
        if (buggy.frontLeftWheel != null) buggy.frontLeftWheel.enabled = false;
        if (buggy.frontRightWheel != null) buggy.frontRightWheel.enabled = false;
        if (buggy.rearLeftWheel != null) buggy.rearLeftWheel.enabled = false;
        if (buggy.rearRightWheel != null) buggy.rearRightWheel.enabled = false;

        // LUNAR TRIALS: no rocket - the rocket is superfluous for the trials format. Driving
        // into the finish area IS the completion: conclude the run straight to the win board.
        if (ChallengeRunner.Active != null)
        {
            running = false;
            if (oxygen != null) oxygen.CompleteLevel();
            return;
        }

        buggyRb = buggy.GetComponent<Rigidbody>();

        // The lander lands right in front of where the buggy parked on the pad.
        Vector3 forward = buggy.transform.forward;
        forward.y = 0f;
        forward.Normalize();
        touchdownPoint = buggy.transform.position + forward * touchdownDistance;
        if (terrain != null)
        {
            // Harvester: the buggy is parked on the raised deck, so the lander touches down on
            // the DECK plane beside it rather than the terrain the pad floats above. (Other
            // modes keep the terrain sample - their pads sit flush with the ground.)
            touchdownPoint.y = GameFlow.Mode == GameMode.Harvester
                ? padCenter.y
                : terrain.SampleHeight(touchdownPoint);
        }
        else touchdownPoint.y = 0f;

        Vector3 toShip = touchdownPoint - buggy.transform.position;
        toShip.y = 0f;
        toShip.Normalize();
        facingRotation = Quaternion.LookRotation(toShip, Vector3.up);

        // (The freeze already happened up top - it now doubles as the trials finish.)

        BuildLander();

        phase = Phase.Turn;
        timer = 0f;
    }

    /// <summary>
    /// Newgrounds secret-medal checks that must read the run's ARRIVAL state. Called the moment
    /// the buggy reaches the pad, before the end-of-level bonus conversion empties the tank
    /// (which would otherwise make "zero oxygen" true for every cleared run). The conditions
    /// mirror state the game already tracks: the oxygen manager's tank and ore counters, and
    /// <see cref="BuggyHealth"/> for hull.
    /// </summary>
    private void EvaluateNewgroundsSecrets()
    {
        if (oxygen == null) return;

        // LAST GASP: reached the pad on an empty tank (the zero-oxygen grace let the buggy coast in).
        if (oxygen.maxOxygen > 0f && oxygen.CurrentOxygen <= 0f)
            NewgroundsBridge.AwardLastGasp();

        // RAGGED END: reached the pad under 5% hull.
        BuggyHealth health = buggy != null ? buggy.GetComponent<BuggyHealth>() : null;
        if (health != null && health.maxHealth > 0f
            && health.CurrentHealth / health.maxHealth < 0.05f)
            NewgroundsBridge.AwardRaggedEnd();

        // HARVESTING HERO: 80%+ of a Harvester world's ore field banked.
        if (GameFlow.Mode == GameMode.Harvester
            && oxygen.TotalHarvestables > 0
            && (float)oxygen.HarvestablesCollected / oxygen.TotalHarvestables >= 0.80f)
            NewgroundsBridge.AwardHarvestingHero();
    }

    private void Update()
    {
        if (!running)
        {
            if (waitingForPad && buggy != null)
            {
                if (raceFinishMode)
                {
                    // RACE TIME LIMIT: once the BRONZE rival finishes, the player gets a short
                    // grace to cross the line before the race auto-concludes. Guarded by the
                    // running flag above - if boarding already began, this never fires. Once the
                    // fade concludes the run, reachedEnd turns on and kills this whole block, so
                    // the cutoff cannot re-arm and fade over the result board.
                    if (oxygen == null || (oxygen.reachedEnd || oxygen.gameOver))
                    {
                        if (hud != null) hud.HideRaceCutoffCountdown();
                        return;
                    }
                    if (!cutoffFading)
                    {
                        if (bronzeCrossedAt < 0f)
                        {
                            foreach (RivalRacer r in RivalRacer.All())
                            {
                                if (r != null && r.tier == RivalRacer.Tier.Bronze && r.CrossedFinish)
                                {
                                    bronzeCrossedAt = Time.time;
                                    break;
                                }
                            }
                        }
                        if (bronzeCrossedAt >= 0f)
                        {
                            // The post-bronze grace: warn the player with a live countdown of
                            // exactly how long they have to cross the line before the race
                            // auto-concludes.
                            float remaining = cutoffAfterBronze - (Time.time - bronzeCrossedAt);
                            if (remaining > 0f)
                            {
                                if (hud != null) hud.SetRaceCutoffCountdown(remaining);
                            }
                            else
                            {
                                if (hud != null) hud.HideRaceCutoffCountdown();
                                BeginCutoff();
                            }
                        }
                    }
                    if (cutoffFading)
                    {
                        if (hud != null) hud.HideRaceCutoffCountdown();
                        AdvanceCutoffFade();
                        return;
                    }

                    // Finish-crossing ends the race. Every gate must have been passed THROUGH;
                    // a skipped gate refuses the finish (the HUD alarm keeps flashing "HEAD BACK").
                    // Ridge Survival has NO gates: crossing the line always clears the leg.
                    if (buggy.transform.position.z >= raceFinishZ)
                    {
                        if (GameFlow.IsRidgeSurvival)
                        {
                            waitingForPad = false;
                            Begin();
                        }
                        else if (oxygen != null && oxygen.PodsCollected >= oxygen.TotalPods)
                        {
                            waitingForPad = false;
                            Begin();
                        }
                        else if (!raceCompletionRefused)
                        {
                            raceCompletionRefused = true;
                            if (hud != null) hud.ShowHint("GATE MISSED - HEAD BACK");
                            if (hud != null) hud.missedGate = true;
                        }
                    }
                    else
                    {
                        raceCompletionRefused = false;
                    }
                }
                else
                {
                    // Keep the orange compass marker aimed at the pad while the player drives in
                    // (the pad is usually beyond the fog line, so the dial is the main guide).
                    if (hud != null) hud.SetLandingZone(buggy.transform.position, padCenter);

                    // Ease the buggy up onto the raised deck as it rolls in: the pad sits on the
                    // HIGHEST ground under it, so on a bumpy world the deck floats above the local
                    // terrain and the buggy would otherwise drive straight through it. The lift
                    // blends from ground level at the rim to full deck height at the accept ring,
                    // so it reads as driving up the apron ramp onto the pad.
                    Vector3 toPadFlat = buggy.transform.position - padCenter;
                    toPadFlat.y = 0f;
                    float padDist = toPadFlat.magnitude;
                    if (buggyRb != null && padDist < padRadius)
                    {
                        float groundY = terrain != null ? terrain.SampleHeight(buggy.transform.position) : padCenter.y;
                        float deckY = padCenter.y + 0.12f;
                        float blend = Mathf.Clamp01((padRadius - padDist) / Mathf.Max(0.5f, padRadius - padAcceptRadius));
                        float targetY = Mathf.Lerp(groundY, deckY, blend * blend);
                        Vector3 lifted = buggyRb.position;
                        lifted.y = Mathf.MoveTowards(lifted.y, targetY, 7f * Time.deltaTime);
                        buggyRb.position = lifted;
                    }

                    Vector3 b = buggy.transform.position;
                    b.y = 0f;
                    Vector3 p = padCenter;
                    p.y = 0f;
                    if ((b - p).sqrMagnitude < padAcceptRadius * padAcceptRadius)
                    {
                        // PROSPECTOR RUN: the descent banks the clear only once at least HALF the
                        // ore is aboard, and banks the PERFECT bonus when every nugget is. Reaching
                        // the pad short of the gate refuses the clear and points back at the field.
                        if (GameFlow.Mode == GameMode.Prospector
                            && oxygen != null && !ProspectorGateOpen(oxygen))
                        {
                            if (!prospectorGateRefused)
                            {
                                prospectorGateRefused = true;
                                if (hud != null) hud.ShowHint("ORE SHORT - COLLECT HALF TO CLEAR");
                            }
                            return;
                        }
                        // LUNAR TRIALS: the challenge's ore target is the completion gate -
                        // reaching the pad short of it refuses the clear and points back.
                        if (ChallengeRunner.Active != null && !ChallengeRunner.OreGateOpen(oxygen))
                        {
                            if (!challengeGateRefused)
                            {
                                challengeGateRefused = true;
                                int have = oxygen != null ? oxygen.HarvestablesCollected : 0;
                                if (hud != null) hud.ShowHint("MORE ORE - "
                                    + have + " / " + ChallengeRunner.Active.oreTarget, 4f);
                            }
                            return;
                        }
                        waitingForPad = false;
                        if (TrialsRunManager.Current != null)
                        {
                            ChallengeRunner.RecordClear(TrialsRunManager.Current.Elapsed);
                            TrialsRunManager.Current.Complete();
                        }
                        Begin();
                    }
                    else
                    {
                        prospectorGateRefused = false;
                        challengeGateRefused = false;
                    }
                }
            }
            return;
        }
        timer += Time.deltaTime;

        switch (phase)
        {
            case Phase.Turn:
                // Swivel to face the touchdown point before the lander comes down.
                buggyRb.MoveRotation(Quaternion.Slerp(
                    buggy.transform.rotation, facingRotation,
                    Mathf.Clamp01(timer / turnDuration)));
                if (timer >= turnDuration)
                {
                    // Engines come alive as the lander starts burning down toward the pad.
                    phase = Phase.Descend;
                    timer = 0f;
                    SetBurn(true, 0.7f);
                    SetFlame(true, 0.5f);
                }
                break;

            case Phase.Descend:
                float t = Mathf.Clamp01(timer / descentDuration);
                float eased = t * t * (3f - 2f * t);
                lander.position = new Vector3(
                    touchdownPoint.x,
                    touchdownPoint.y + spawnHeight * (1f - eased),
                    touchdownPoint.z);
                // Retro-burn: the engines throttle up in a final flare, kicking dust off the pad.
                SetThrottle(Mathf.Lerp(0.5f, 0.95f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.95f, t))));
                UpdateGroundDust(lander.position.y - touchdownPoint.y, 0.6f);
                RocketShake(0.22f * Mathf.Clamp01(1f - (lander.position.y - touchdownPoint.y) / 25f));
                if (t >= 1f)
                {
                    lander.position = touchdownPoint;
                    SetFlame(false);
                    PuffGroundDust(35);           // engine cut-off: one last puff off the pad
                    RocketShake(0.35f);           // touchdown thump
                    UpdateGroundDust(999f, 0f);
                    SetBurn(false, 0f);
                    phase = Phase.HatchOpen;
                    timer = 0f;
                }
                break;

            case Phase.HatchOpen:
                float ho = Mathf.Clamp01(timer / hatchOpenDuration);
                hatchHinge.localRotation = Quaternion.Euler(-90f * ho, 0f, 0f);
                if (ho >= 1f) { phase = Phase.DriveIn; timer = 0f; }
                break;

            case Phase.DriveIn:
                // Recompute the bay's interior point from the lander's CURRENT (grounded)
                // pose: the value captured during construction is 90 m up in the air.
                if (!driveInStartCaptured)
                {
                    driveInStartCaptured = true;
                    driveInStart = buggy.transform.position;
                    bayInterior = lander.TransformPoint(new Vector3(0f, 1.2f, 2.0f));
                    bayInterior.y = Mathf.Max(bayInterior.y, buggy.transform.position.y - 0.5f);
                }
                float dt = Mathf.Clamp01(timer / driveInDuration);
                float de = dt * dt * (3f - 2f * dt);
                buggyRb.MovePosition(Vector3.Lerp(driveInStart, bayInterior, de));
                buggyRb.MoveRotation(facingRotation);
                if (dt < 1f) SpinWheels(220f * Time.deltaTime);
                if (dt >= 1f)
                {
                    buggyRb.MovePosition(bayInterior);
                    buggyRb.MoveRotation(facingRotation);
                    phase = Phase.HatchClose;
                    timer = 0f;
                }
                break;

            case Phase.HatchClose:
                float hc = Mathf.Clamp01(timer / hatchCloseDuration);
                hatchHinge.localRotation = Quaternion.Euler(-90f * (1f - hc), 0f, 0f);
                // The buggy is now inside the rocket: immediately fire the launch audio
                // ("Initiating Launch Sequence" then the burn roar) as the hatch seals shut.
                if (!_takeOffTriggered)
                {
                    _takeOffTriggered = true;
                    SetFlame(true, 1f);           // ignition surge, then full thrust
                    SetSmoke(1f);                 // exhaust trail on
                    PuffGroundDust(70);           // the blast hits the pad
                    SetBurn(true, 1.0f);
                    RoverSounds.PlayTakeOff(lander.position);
                }
                UpdateGroundDust(0f, 0.7f);       // steady dust ring while the engines build up on the pad
                RocketShake(0.18f);               // engines rumbling on the pad
                if (hc >= 1f)
                {
                    // Boarded: carry the buggy with the lander.
                    buggy.transform.SetParent(lander, true);
                    phase = Phase.Liftoff;
                    timer = 0f;
                }
                break;

            case Phase.Liftoff:
                // Climb straight back up with the buggy riding in the bay.
                float lt = Mathf.Clamp01(timer / liftDuration);
                float le = lt * lt * (3f - 2f * lt);
                // The rocket shudders on the pad as thrust builds, settling as it climbs clear.
                float shake = 0.09f * (1f - Mathf.SmoothStep(0f, 1f, le * 1.6f));
                lander.position = new Vector3(
                    touchdownPoint.x,
                    touchdownPoint.y + liftHeight * le,
                    touchdownPoint.z) + Tremor(shake);
                SetThrottle(Mathf.Lerp(1f, 0.8f, le));
                UpdateGroundDust(liftHeight * le, 1f);
                RocketShake(Mathf.Lerp(0.5f, 0.12f, le));
                if (lt >= 1f)
                {
                    lander.position = new Vector3(
                        touchdownPoint.x, touchdownPoint.y + liftHeight, touchdownPoint.z);
                    phase = Phase.Done;
                    timer = 0f;
                }
                break;

            case Phase.Done:
                if (timer >= finalPause)
                {
                    SetFlame(false);
                    SetBurn(false, 0f);
                    running = false;
                    if (oxygen != null) oxygen.CompleteLevel();
                }
                break;
        }
    }

    private void SpinWheels(float degrees)
    {
        foreach (Transform visual in wheelVisuals)
        {
            if (visual != null) visual.Rotate(Vector3.right, degrees, Space.Self);
        }
    }

    // ------------------------------------------------------- auto-conclude (race time limit)

    /// <summary>
    /// Starts the race time-limit auto-conclude: the player ran out of time after bronze
    /// crossed the line, so the screen fades to black (NO lander boarding) and then the run
    /// is concluded through the normal race-result path. Freezes the buggy and the clock.
    /// </summary>
    private void BeginCutoff()
    {
        cutoffFading = true;
        cutoffFadeAt = Time.time;

        // The run is over: stop the clock, lock the buggy where it stands.
        if (oxygen != null) oxygen.BeginEndSequence();
        if (buggy != null)
        {
            buggy.inputEnabled = false;
            Rigidbody rb = buggy.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        if (hud != null) hud.HideHint();
        if (hud != null) hud.HideRaceCutoffCountdown();

        // Full-screen black veil above the HUD, exactly like the ridge-fall fade.
        Canvas canvas = UiKit.CreateRootCanvas("RaceCutoffFade", 400);
        cutoffFadeGo = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        cutoffFadeGo.transform.SetParent(canvas.transform, false);
        Image img = cutoffFadeGo.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;
        UiKit.StretchToFull(cutoffFadeGo.GetComponent<RectTransform>());
    }

    /// <summary>Fades the auto-conclude veil to black, then concludes the race to the normal
    /// result menu (no boarding cutscene).</summary>
    private void AdvanceCutoffFade()
    {
        float t = Mathf.Clamp01((Time.time - cutoffFadeAt) / cutoffFadeDuration);
        if (cutoffFadeGo != null)
        {
            Image img = cutoffFadeGo.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, t);
        }
        if (t >= 1f)
        {
            // The fade canvas must DIE before the result board builds (it sits ABOVE the
            // result screens' sort order, so leaving it up would hide them).
            if (cutoffFadeGo != null) Destroy(cutoffFadeGo);
            cutoffFadeGo = null;
            cutoffFading = false;
            // Conclude through the normal race-result path, but flagged as timed out so the
            // DNF time is not written to the world's fastest-time board.
            if (oxygen != null) oxygen.ConcludeRaceTimeout();
        }
    }

    // ------------------------------------------------------- exhaust + dust control (see RocketExhaust)

    /// <summary>Lights (or cuts) both engines. Lighting flares briefly, then settles to the throttle.</summary>
    private void SetFlame(bool on, float throttle = 1f)
    {
        if (exhaustLeft != null) { if (on) exhaustLeft.Ignite(throttle); else exhaustLeft.Cutoff(); }
        if (exhaustRight != null) { if (on) exhaustRight.Ignite(throttle); else exhaustRight.Cutoff(); }
    }

    private void SetThrottle(float throttle)
    {
        if (exhaustLeft != null) exhaustLeft.SetThrottle(throttle);
        if (exhaustRight != null) exhaustRight.SetThrottle(throttle);
    }

    private void SetSmoke(float amount)
    {
        if (exhaustLeft != null) exhaustLeft.SetSmokeScale(amount);
        if (exhaustRight != null) exhaustRight.SetSmokeScale(amount);
    }

    /// <summary>Ground effect: the engines blow a ring of dust across the pad, strongest right at the
    /// ground and fading as the lander gains height.</summary>
    private void UpdateGroundDust(float height, float strength)
    {
        if (groundDust == null) return;
        float ground = Mathf.Clamp01(1f - height / 38f);
        ParticleSystem.EmissionModule em = groundDust.emission;
        em.rateOverTime = 95f * strength * ground * ground;
    }

    private void PuffGroundDust(int count)
    {
        if (groundDust != null) groundDust.Emit(count);
    }

    /// <summary>Rocket rumble through the chase camera (BuggyCamera.AddShake, 0..1). Calling it every
    /// frame keeps the shake going; it dies away by itself once the calls stop.</summary>
    private static void RocketShake(float strength)
    {
        if (strength > 0.01f && BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(strength);
    }

    /// <summary>Small random shudder for the lander (metres), used while thrust builds on the pad.</summary>
    private static Vector3 Tremor(float amplitude)
    {
        if (amplitude <= 0f) return Vector3.zero;
        float t = Time.time;
        return new Vector3(
            Mathf.PerlinNoise(t * 38f, 0.3f) - 0.5f,
            Mathf.PerlinNoise(t * 41f, 7.1f) - 0.5f,
            Mathf.PerlinNoise(t * 35f, 3.7f) - 0.5f) * (2f * amplitude);
    }

    /// <summary>Fades the rocket-burn sound in/out (Play/Stop with volume ramps so it never clicks).</summary>
    private void SetBurn(bool on, float targetVolume)
    {
        if (rocketSrc == null) return;
        if (burnFade != null) StopCoroutine(burnFade);
        burnFade = StartCoroutine(FadeBurn(on, targetVolume));
    }

    private System.Collections.IEnumerator FadeBurn(bool on, float targetVolume)
    {
        float target = on ? targetVolume : 0f;
        if (on)
        {
            rocketSrc.volume = 0f;
            rocketSrc.Play();
        }
        while (true)
        {
            rocketSrc.volume = Mathf.MoveTowards(rocketSrc.volume, target, Time.deltaTime * 2.4f);
            if (!on && rocketSrc.volume <= 0.001f)
            {
                if (rocketSrc.isPlaying) rocketSrc.Stop();
                yield break;
            }
            if (on && rocketSrc.volume >= target - 0.001f) yield break;
            yield return null;
        }
    }

    // ------------------------------------------------------- trials finish line

    // LUNAR TRIALS finish line: two tall red pylons and a crossbar over the course, with a
    // checkered strip painted across the corridor at the line. Oriented along the finish
    // run-in direction. Purely visual - the landing pad beside it is the accept zone.
    private void BuildTrialsFinishLine()
    {
        if (TrialsCourse.Active == null) return;
        TrialsCourseData course = TrialsCourse.Active;
        Transform root = new GameObject("TrialsFinishLine").transform;
        root.position = new Vector3(course.FinishXZ.x, padCenter.y, course.FinishXZ.y);
        Vector2 d = course.DirectionAt(1f);
        root.rotation = Quaternion.LookRotation(new Vector3(d.x, 0f, d.y), Vector3.up);

        float halfW = course.halfWidth + 1.5f;
        Material pylonMat = MakeSolidUnlit(new Color(0.95f, 0.2f, 0.18f, 1f));
        Material barMat = MakeSolidUnlit(new Color(1f, 0.95f, 0.7f, 1f));
        Material whiteMat = MakeSolidUnlit(new Color(0.95f, 0.95f, 0.9f, 1f));
        Material darkMat = MakeSolidUnlit(new Color(0.08f, 0.08f, 0.09f, 1f));

        for (int side = -1; side <= 1; side += 2)
        {
            Transform pylon = CreatePart(PrimitiveType.Cube, root, "FinishPylon",
                new Vector3(1.2f, 8f, 1.2f), new Vector3(side * halfW, 4f, 0f), pylonMat);
            pylon.name = "FinishPylon";
        }
        CreatePart(PrimitiveType.Cube, root, "FinishBar",
            new Vector3(halfW * 2f, 0.9f, 0.9f), new Vector3(0f, 8.4f, 0f), barMat);

        const int checks = 12;
        float w = halfW * 2f / checks;
        for (int i = 0; i < checks; i++)
        {
            CreatePart(PrimitiveType.Cube, root, "FinishCheck",
                new Vector3(w * 0.92f, 0.09f, 1.8f),
                new Vector3(-halfW + w * (i + 0.5f), 0.05f, 0f),
                (i % 2 == 0) ? whiteMat : darkMat);
        }
    }

    // ------------------------------------------------------- landing pad construction

    // A flat painted disc with glowing perimeter markers and a tall beacon column so
    // the boarding point is obvious from afar. Purely visual (no colliders).
    private void BuildLandingPad()
    {
        padRoot = new GameObject("LandingPad").transform;
        padRoot.position = padCenter;

        // The deck is dressed in the Yughues metal 23 (bright brushed steel); the inner disc
        // is a darker instance of the same swatch so the painted-deck two-tone still reads.
        Material baseMat = YFMetal.Get(23);
        Material innerMat = YFMetal.New(23);
        innerMat.color = new Color(0.55f, 0.55f, 0.58f, 1f);
        Material markMat = MakeSolidUnlit(new Color(0.25f, 1f, 0.5f, 1f));

        // Painted disc sitting on the regolith (cylinder diameter = 2 * radius).
        CreatePart(PrimitiveType.Cylinder, padRoot, "PadBase",
            new Vector3(padRadius * 2f, 0.08f, padRadius * 2f), new Vector3(0f, 0.04f, 0f), baseMat);
        // Inner circle for a two-tone "painted deck" look.
        CreatePart(PrimitiveType.Cylinder, padRoot, "PadCircle",
            new Vector3(padRadius * 1.4f, 0.05f, padRadius * 1.4f), new Vector3(0f, 0.12f, 0f), innerMat);

        // Glowing perimeter marker posts.
        int postCount = 12;
        for (int i = 0; i < postCount; i++)
        {
            float a = i * 360f / postCount * Mathf.Deg2Rad;
            CreatePart(PrimitiveType.Cylinder, padRoot, "PadMark",
                new Vector3(0.18f, 0.5f, 0.18f),
                new Vector3(Mathf.Sin(a) * padRadius * 0.92f, 0.25f, Mathf.Cos(a) * padRadius * 0.92f), markMat);
        }

        // Tall beacon column so the zone is visible from a distance.
        CreatePart(PrimitiveType.Cylinder, padRoot, "PadBeacon",
            new Vector3(0.18f, 16f, 0.18f), new Vector3(0f, 16f, 0f), markMat);
        // Second beacon at the same spot but shorter/thicker reads as a blinking stack.
        CreatePart(PrimitiveType.Cylinder, padRoot, "PadBeacon",
            new Vector3(0.22f, 10f, 0.22f), new Vector3(0f, 10f, 0f), markMat);

        // Sloped apron skirt: a ring of quads from the deck edge down to the sampled terrain,
        // so on a bumpy world the pad reads as a ramped mound rather than a floating disc -
        // and the buggy's eased ride up onto the deck has a visible ramp under it.
        // (Double-sided winding: the skirt is visible from above AND from the side.)
        if (terrain != null)
        {
            const int segs = 24;
            const float innerR = 10.5f;
            float outerR = padRadius + 2.4f;
            Vector3[] verts = new Vector3[(segs + 1) * 2];
            int[] tris = new int[segs * 12];
            for (int i = 0; i <= segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 outerWorld = padCenter + dir * outerR;
                float outerLocalY = terrain.SampleHeight(outerWorld) - padCenter.y;
                verts[i * 2] = new Vector3(dir.x * innerR, 0.03f, dir.z * innerR);
                verts[i * 2 + 1] = new Vector3(dir.x * outerR, outerLocalY, dir.z * outerR);
                if (i < segs)
                {
                    int b = i * 12;
                    int v0 = i * 2;
                    // Both windings so the skirt never vanishes from a grazing angle.
                    tris[b + 0] = v0; tris[b + 1] = v0 + 1; tris[b + 2] = v0 + 2;
                    tris[b + 3] = v0 + 1; tris[b + 4] = v0 + 3; tris[b + 5] = v0 + 2;
                    tris[b + 6] = v0 + 2; tris[b + 7] = v0 + 1; tris[b + 8] = v0;
                    tris[b + 9] = v0 + 2; tris[b + 10] = v0 + 3; tris[b + 11] = v0 + 1;
                }
            }
            Mesh apronMesh = new Mesh();
            apronMesh.vertices = verts;
            apronMesh.triangles = tris;
            apronMesh.RecalculateNormals();
            GameObject apron = new GameObject("PadApron");
            apron.transform.SetParent(padRoot, false);
            apron.AddComponent<MeshFilter>().sharedMesh = apronMesh;
            apron.AddComponent<MeshRenderer>().sharedMaterial =
                MakeSolidUnlit(new Color(0.33f, 0.35f, 0.39f, 1f));
        }
    }

    // ------------------------------------------------------- finish-line construction

    // Lunar Race's proper finish line: two tall pylons at the canyon edges with a gantry
    // crossbar above, and a checkered strip painted across the whole course floor at the
    // line. Unlit so it stays vividly visible even through the thick-atmosphere fog.
    private void BuildFinishGantry()
    {
        Transform root = new GameObject("FinishGantry").transform;
        root.position = padCenter;

        float halfW = terrain != null
            ? (GameFlow.CurrentCourse == RaceCourse.Ridge
                ? terrain.raceRidgeCrestHalfWidth - 1f
                : terrain.raceCourseHalfWidth - 2f)
            : 12f;

        Material pylonMat = MakeSolidUnlit(new Color(0.95f, 0.2f, 0.18f, 1f));
        Material crossbarMat = MakeSolidUnlit(new Color(1f, 0.95f, 0.7f, 1f));
        Material whiteMat = MakeSolidUnlit(new Color(0.95f, 0.95f, 0.9f, 1f));
        Material darkMat = MakeSolidUnlit(new Color(0.08f, 0.08f, 0.09f, 1f));

        for (int side = -1; side <= 1; side += 2)
        {
            Transform pylon = CreatePart(PrimitiveType.Cube, root, "FinishPylon",
                new Vector3(1.4f, 9f, 1.4f), new Vector3(side * halfW, 4.5f, 0f), pylonMat);
            pylon.gameObject.name = "FinishPylon";
        }

        // Crossbar spanning the canyon at the top of the pylons.
        CreatePart(PrimitiveType.Cube, root, "FinishBar",
            new Vector3(halfW * 2f, 1.0f, 1.0f), new Vector3(0f, 9.6f, 0f), crossbarMat);

        // Checkered strip on the floor across the whole course at the line.
        const int checks = 12;
        float w = halfW * 2f / checks;
        for (int i = 0; i < checks; i++)
        {
            CreatePart(PrimitiveType.Cube, root, "FinishCheck",
                new Vector3(w * 0.92f, 0.09f, 1.6f),
                new Vector3(-halfW + w * (i + 0.5f), 0.05f, 0f),
                (i % 2 == 0) ? whiteMat : darkMat);
        }
    }

    // ------------------------------------------------------- lander construction

    // The lander is a tall rocket on corner legs; the space between the legs is the
    // cargo bay (dark walls + ceiling, open floor), with a hatch that drops forward to
    // form a bridge the buggy drives over into the bay.
    private void BuildLander()
    {
        lander = new GameObject("Lander").transform;
        lander.position = touchdownPoint + Vector3.up * spawnHeight;

        Vector3 toShip = touchdownPoint - buggy.transform.position;
        toShip.y = 0f;
        toShip.Normalize();
        lander.rotation = Quaternion.LookRotation(toShip, Vector3.up);

        // The outer hull now rides YFMetal swatch 37 (tinted per part) so it reads as real metal
        // instead of flat colour - and because it reuses the swatch already in the build, the
        // tinted copies add nothing to the download.
        Material hullMat = MetalTint(new Color(0.86f, 0.88f, 0.92f, 1f));   // fuselage + hatch: pale steel
        Material finMat = MetalTint(new Color(0.40f, 0.42f, 0.46f, 1f));    // fins: darker steel for contrast
        Material noseMat = MetalTint(new Color(0.85f, 0.25f, 0.18f, 1f));   // nose: red metal
        Material darkMat = MakeSolid(new Color(0.05f, 0.05f, 0.06f, 1f), 0.05f);
        Material accentMat = MakeSolid(new Color(0.95f, 0.5f, 0.1f, 1f), 0.2f);
        Material metalMat = YFMetal.Get(37);

        // --- Cargo bay (below the deck, between and under the legs) ---
        // Deck / ceiling of the bay.
        CreatePart(PrimitiveType.Cube, lander, "BayCeiling", new Vector3(10f, 0.4f, 8f), new Vector3(0f, 3.4f, 0f), darkMat);
        // Side and back walls enclose the bay; the front (facing the buggy) stays open.
        CreatePart(PrimitiveType.Cube, lander, "BayWall", new Vector3(0.3f, 3.2f, 8f), new Vector3(-5f, 1.6f, 0f), darkMat);
        CreatePart(PrimitiveType.Cube, lander, "BayWall", new Vector3(0.3f, 3.2f, 8f), new Vector3(5f, 1.6f, 0f), darkMat);
        CreatePart(PrimitiveType.Cube, lander, "BayBack", new Vector3(10f, 3.2f, 0.3f), new Vector3(0f, 1.6f, 3.95f), darkMat);

        // Corner legs.
        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                CreatePart(PrimitiveType.Cylinder, lander, "Leg",
                    new Vector3(0.3f, 3.4f, 0.3f), new Vector3(x * 4.4f, 1.7f, z * 3.0f), metalMat);
            }
        }

        // Twin engines + flames beneath the deck, clear of the buggy's path.
        CreatePart(PrimitiveType.Cylinder, lander, "Engine", new Vector3(1.2f, 0.7f, 1.2f), new Vector3(-3f, 2.4f, 0f), metalMat);
        CreatePart(PrimitiveType.Cylinder, lander, "Engine", new Vector3(1.2f, 0.7f, 1.2f), new Vector3(3f, 2.4f, 0f), metalMat);
        // Animated exhaust (RocketExhaust): the nozzles sit under the engine cylinders (y = 1.7). At idle
        // the plume keeps the old static flame's size (about 0.9 wide, 2.6 long) and stretches to ~4.8 m
        // at full thrust. Both stay dark until the engines are lit.
        exhaustLeft = RocketExhaust.Create(lander, new Vector3(-3f, 1.7f, 0f), 0.5f, 2.6f, 4.8f, 1);
        exhaustRight = RocketExhaust.Create(lander, new Vector3(3f, 1.7f, 0f), 0.5f, 2.6f, 4.8f, 2);
        groundDust = RocketExhaust.CreateGroundDust(touchdownPoint + Vector3.up * 0.4f, 3.4f);

        // --- Fuselage on top of the deck ---
        CreatePart(PrimitiveType.Cylinder, lander, "Fuselage", new Vector3(2.2f, 5f, 2.2f), new Vector3(0f, 8.5f, 0f), hullMat);
        CreatePart(PrimitiveType.Cylinder, lander, "AccentRing", new Vector3(2.25f, 0.4f, 2.25f), new Vector3(0f, 6.2f, 0f), accentMat);
        CreatePart(PrimitiveType.Sphere, lander, "Nose", new Vector3(1.5f, 2.8f, 1.5f), new Vector3(0f, 15.2f, 0f), noseMat);

        // Small fins near the base of the fuselage.
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f * Mathf.Deg2Rad + Mathf.PI * 0.25f;
            Transform fin = CreatePart(PrimitiveType.Cube, lander, "Fin",
                new Vector3(1.4f, 2.2f, 0.15f), new Vector3(Mathf.Sin(a) * 2.4f, 4.8f, Mathf.Cos(a) * 2.4f), finMat);
            fin.localRotation = Quaternion.Euler(0f, -i * 90f - 45f, 20f);
        }

        // --- Hatch: a panel hinged at its bottom edge on the open (-Z) side ---
        // The interior point the buggy drives to (deep inside the bay, local +Z).
        bayInterior = lander.TransformPoint(new Vector3(0f, 1.2f, 2.0f));

        GameObject hingeGo = new GameObject("HatchHinge");
        hingeGo.transform.SetParent(lander, false);
        hingeGo.transform.localPosition = new Vector3(0f, 0f, -3.95f);
        hatchHinge = hingeGo.transform;

        Transform hatch = CreatePart(PrimitiveType.Cube, hatchHinge, "HatchPanel",
            new Vector3(4.4f, 2.6f, 0.25f), new Vector3(0f, 1.32f, 0f), hullMat);
        hatch.name = "HatchPanel";

        // Rocket-burn rumble that swells as the lander descends and lifts off.
        AudioSource src = lander.gameObject.AddComponent<AudioSource>();
        src.spatialBlend = 0.9f;
        src.loop = true;
        src.volume = 0f;
        src.playOnAwake = false;
        src.clip = RoverSounds.BakeRocketClip();
        rocketSrc = src;
    }

    private Transform CreatePart(PrimitiveType type, Transform parent, string name, Vector3 scale, Vector3 localPos, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        go.transform.localPosition = localPos;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        return go.transform;
    }
}