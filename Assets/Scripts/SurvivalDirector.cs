using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives Lunar Survival: one continuous effort with no planets and no boarding. A route of
/// oxygen canisters zig-zags haphazardly across a large static arena, generated lazily so
/// only a short run of canisters exists near the buggy (the world "scrolls" past endlessly).
/// Each canister is given a big random lateral swing and an irregular forward hop so no two
/// sit in a line - they genuinely scatter across the map.
///
/// A communication link follows the route corridor around the buggy's forward progress. The
/// buggy must stay within range of that corridor: drifting sideways from the path raises a
/// "GOING OUT OF COMMUNICATION RANGE" warning with a live proximity meter, and drifting out of
/// range ends the run (recorded as game over). This keeps the player bound to the route without
/// the chain having to hunt them down.
///
/// A difficulty "set" is cleared by <see cref="canistersPerSet"/> canisters being driven past
/// (not necessarily collected), so missing a canister never blocks progress. Each cleared set
/// ramps the difficulty: the tyres get slicker, the ground is re-generated bumpier, a few more
/// dodge hazards are dropped on the route, and canisters are scattered wider apart (see
/// <see cref="scatterStep"/>). Canisters are labelled 1..N
/// within each set purely as guidance (the order is suggested, not enforced).
/// The run ends when the buggy dies or loses communication; OxygenManager records the survival
/// time and hands off to the Leaderboard screen.
/// </summary>
public class SurvivalDirector : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Average spacing between consecutive canisters (metres).")]
    public float podSpacing = 115f;
    [Tooltip("Lateral step between route rows (metres) - the scale of the haphazard swings.")]
    public float rowStep = 165f;
    [Tooltip("How many canisters are kept ahead of the buggy in route order.")]
    public int lookaheadPods = 12;
    [Tooltip("Objects farther than this (horizontal, metres) are recycled/destroyed (non-canisters).")]
    public float despawnRadius = 950f;
    [Tooltip("Canisters this far behind (in route order) beyond the buggy are cleaned up.")]
    public int keepBehind = 4;

    [Header("Difficulty ramp")]
    [Tooltip("Canisters driven past per difficulty set (progress is not tied to collecting).")]
    public int canistersPerSet = 6;
    [Tooltip("Tyre friction multiplier each step.")]
    public float gripStep = 0.97f;
    [Tooltip("Lower clamp on the grip multiplier after ramping.")]
    public float gripFloor = 0.66f;
    [Tooltip("Extra terrain roughness (bump amplitude) added each step.")]
    public float bumpStep = 0.7f;
    [Tooltip("Bumpiness frequency multiplier each step.")]
    public float bumpScaleStep = 1.02f;
    [Tooltip("Additional dodge hazards spawned per step.")]
    public int hazardsPerStep = 2;
    [Tooltip("Scatter multiplier applied each difficulty set, widening the canister placement so later sets spread them further apart.")]
    public float scatterStep = 1.05f;

    [Header("Comms range")]
    [Tooltip("Metres the buggy may drift from the route corridor before the run ends.")]
    public float commRadius = 600f;
    [Tooltip("Fraction of commRadius past which the out-of-range warning appears (0..1).")]
    public float commWarn = 0.7f;

    [Header("Wiring")]
    public MoonTerrainGen terrain;
    public BuggyController buggy;
    public OxygenManager oxygen;
    public HUD hud;
    /// <summary>Red Earth backdrop on the horizon; darkens as the fog closes in each set.</summary>
    public SurvivalEarth survivalEarth;

    /// <summary>Factory: creates an oxygen canister (never final) at a world position.</summary>
    public Func<Vector3, int, GameObject> spawnPod;
    /// <summary>Factory: creates a dodge hazard (boulder) at a world position.</summary>
    public Func<Vector3, int, GameObject> spawnHazard;

    private readonly List<GameObject> liveObjects = new List<GameObject>();
    private readonly List<Vector3> routePoints = new List<Vector3>(); // spawn-order canister positions
    private Vector3 cursor;             // world position of the last canister spawned
    private int zDir = 1;               // serpentine heading along Z (+1 or -1)
    private Vector3 lastSpawnPos;       // position of the canister at the chain frontier
    private float zMin, zMax, xMin, xMax;
    private int spawnIndex;

    private int clearedCount;
    private int rampDone;
    private float bumpAmplitude, bumpScale;
    private static int hazardSerial;
    private static Font labelFont;

    private void Start()
    {
        if (oxygen == null) oxygen = OxygenManager.Instance;
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (terrain == null) terrain = FindAnyObjectByType<MoonTerrainGen>();

        float extent = terrain != null ? terrain.extent : 1800f;
        zMin = -extent + 420f;
        zMax = extent - 420f;
        xMin = -extent + 500f;
        xMax = extent - 500f;

        if (oxygen != null)
        {
            bumpAmplitude = terrain != null ? terrain.bumpAmplitude : 1.5f;
            bumpScale = terrain != null ? terrain.bumpScale : 0.045f;
        }

        cursor = buggy != null ? buggy.transform.position : Vector3.zero;
        lastSpawnPos = cursor;
        cursor.z = Mathf.Clamp(cursor.z, zMin, zMax);
        cursor.x = Mathf.Clamp(cursor.x, xMin, xMax);
    }

    private void OnDestroy()
    {
    }

    private void Update()
    {
        if (oxygen == null || oxygen.gameOver || oxygen.reachedEnd || buggy == null) return;

        // Keep extending the canister chain so there are always lookaheadPods canisters ahead
        // of the buggy in ROUTE order (not metres). Because clearedCount advances in spawn order
        // and spawnIndex tracks every spawn, "spawnIndex - clearedCount" is the number of
        // unpicked canisters left to find - guaranteed continuous no matter how the serpentine
        // folds, unlike measuring straight-line metres which a looping path can squeeze or stretch.
        while (spawnIndex - clearedCount < lookaheadPods)
        {
            SpawnNextCanister();
        }
        PruneFarBehind();

        // Difficulty sets clear on forward progress (canisters driven past), not collection,
        // so missing a canister never blocks the run. Only advance once per cleared set.
        int cleared = BuggyClearedCount();
        int setsCleared = cleared / Mathf.Max(1, canistersPerSet);
        if (setsCleared > rampDone)
        {
            rampDone = setsCleared;
            ApplyRamp(setsCleared);
            if (hud != null) hud.SetSurvivalSet(setsCleared + 1);
        }

        EvaluateCommsRange(cleared);
        PublishRouteDirection();
    }

    /// <summary>
    /// Feeds the HUD compass the position of the NEXT canister: the nearest LIVE canister that has
    /// NOT yet been collected, regardless of direction. Collected canisters are inert (their beacon
    /// and glow are off) and are skipped as targets, so as you collect them the compass picks the
    /// nearest remaining un-collected canister. The distance readout is the straight-line distance
    /// to it, and the count is how many un-collected live canisters remain (reaching 0 if the
    /// respawn chain stalls).
    /// </summary>
    private void PublishRouteDirection()
    {
        if (hud == null || buggy == null) return;

        Vector3 buggyPos = buggy.transform.position;
        Vector3 nearest = buggyPos;
        bool found = false;
        float best = float.MaxValue;
        int unCollected = 0;

        // Scan the live canisters (those carrying a RouteIndex marker). Skip collected (inert)
        // ones, then take the nearest remaining regardless of direction as the next target.
        for (int i = 0; i < liveObjects.Count; i++)
        {
            GameObject go = liveObjects[i];
            if (go == null) continue;
            RouteIndex ri = go.GetComponent<RouteIndex>();
            if (ri == null) continue;                       // not a canister (e.g. a hazard)
            OxygenPod pod = go.GetComponent<OxygenPod>();
            if (pod != null && pod.Collected) continue;     // inert - collected, skip as target

            unCollected++;
            float d = HorizontalDistance(buggyPos, go.transform.position);
            if (d < best) { best = d; nearest = go.transform.position; found = true; }
        }

        if (!found)
        {
            hud.SetRouteDirection(buggyPos, nearest, 0);
            return;
        }
        hud.SetRouteDirection(buggyPos, nearest, unCollected);
    }

    /// <summary>
    /// Keeps the buggy within communication range of the route corridor around its forward
    /// progress. Danger is the buggy's distance to the nearest route point in a window around
    /// where it currently is, so staying on/near the path keeps the link strong while driving
    /// sideways off it warns and - once far enough out - is game over.
    /// </summary>
    private void EvaluateCommsRange(int cleared)
    {
        float distToCorridor = RouteCorridorDistance(cleared);
        float danger = commRadius > 0f ? distToCorridor / commRadius : 0f;
        if (danger >= 1f)
        {
            if (oxygen != null) oxygen.OutOfRange();
            if (hud != null) hud.SetCommLoss(1f, true);
            return;
        }
        if (hud != null) hud.SetCommLoss(danger, danger >= commWarn);
    }

    /// <summary>
    /// Straight-line distance from the buggy to the nearest route point, scanning a window of
    /// the route behind and ahead of its current progress. This measures "how far off the path"
    /// the buggy has drifted, independent of how far it still has to travel forward.
    /// </summary>
    private float RouteCorridorDistance(int cleared)
    {
        float best = float.MaxValue;
        int lo = Mathf.Max(0, clearedCount - 6);
        int hi = Mathf.Min(routePoints.Count - 1, clearedCount + lookaheadPods + 4);
        for (int i = lo; i <= hi; i++)
        {
            best = Mathf.Min(best, HorizontalDistance(buggy.transform.position, routePoints[i]));
        }
        return best;
    }

    /// <summary>How far ahead of the buggy (metres) the canister chain is maintained.</summary>
    private float KeepAhead()
    {
        return lookaheadPods * Mathf.Max(1f, podSpacing);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        Vector3 d = a - b;
        d.y = 0f;
        return d.magnitude;
    }

    /// <summary>Adds one haphazardly-placed canister to the chain and returns its world position.</summary>
    private void SpawnNextCanister()
    {
        AdvanceCursor();

        Vector3 pos = new Vector3(cursor.x, 0f, cursor.z);
        if (terrain != null) pos.y = terrain.SampleHeight(pos) + 2f;
        int number = (spawnIndex % Mathf.Max(1, canistersPerSet)) + 1;
        routePoints.Add(new Vector3(pos.x, 0f, pos.z));
        if (spawnPod != null)
        {
            GameObject pod = spawnPod(pos, spawnIndex);
            liveObjects.Add(pod);
            RouteIndex marker = pod.AddComponent<RouteIndex>();
            marker.index = spawnIndex;
            StampNumber(pod, number);
        }
        spawnIndex++;
        lastSpawnPos = pos;
    }

    // Steps the serpentine head one canister onward along Z, but with a big random lateral
    // swing and an irregular forward hop so the canisters scatter haphazardly rather than
    // lining up in a row. The path still snakes back and forth across the whole arena, so
    // the player is always guided loosely onward and coverage stays even.
    private void AdvanceCursor()
    {
        // Scatter widens with each difficulty set, spreading later canisters further apart so
        // chaining them together - and keeping them in range - gets progressively harder.
        float setMult = Mathf.Pow(scatterStep, clearedCount / Mathf.Max(1, canistersPerSet));

        // Irregular forward hop along the current serpentine heading.
        cursor.z += zDir * podSpacing * UnityEngine.Random.Range(0.75f, 1.5f) * setMult;

        // Haphazard lateral swing (sometimes a big detour).
        float swing = UnityEngine.Random.Range(0.45f, 1.05f) * rowStep * setMult * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
        cursor.x += swing;

        // Gently pull the walk back toward the middle of the X range so scatter swings and
        // wall-bounces together never let it pile up against one edge.
        float centreX = (xMin + xMax) * 0.5f;
        cursor.x += (centreX - cursor.x) * 0.04f;

        // Turn the serpentine around at the Z walls.
        if (cursor.z > zMax) { cursor.z = zMax; zDir = -1; }
        else if (cursor.z < zMin) { cursor.z = zMin; zDir = 1; }

        // Bounce the walk back off the X walls (keep it inside the arena).
        if (cursor.x < xMin) cursor.x = xMin + UnityEngine.Random.Range(0f, rowStep);
        else if (cursor.x > xMax) cursor.x = xMax - UnityEngine.Random.Range(0f, rowStep);
    }

    /// <summary>
    /// Number of canisters the buggy has driven past, measured geometrically along spawn order:
    /// the buggy has "cleared" point i once it is nearer to point i+1 than to point i. This is
    /// monotonic in spawn order and works for any serpentine shape, so missed canisters never
    /// block a set and the chain can never be outrun. Scans only forward from the last cleared
    /// point, so it stays cheap no matter how long the run lasts.
    /// </summary>
    private int BuggyClearedCount()
    {
        Vector2 b = new Vector2(buggy.transform.position.x, buggy.transform.position.z);
        while (clearedCount + 1 < routePoints.Count)
        {
            Vector2 a = new Vector2(routePoints[clearedCount].x, routePoints[clearedCount].z);
            Vector2 n = new Vector2(routePoints[clearedCount + 1].x, routePoints[clearedCount + 1].z);
            if (Vector2.Distance(b, n) >= Vector2.Distance(b, a)) break;
            clearedCount++;
        }
        return clearedCount;
    }

    /// <summary>
    /// Stamps a set-relative number (1..canistersPerSet) above a canister as guidance only - the
    /// order is suggested, never enforced, and progress is measured geometrically, not by collecting.
    /// </summary>
    private static void StampNumber(GameObject pod, int number)
    {
        if (pod == null) return;
        if (labelFont == null) labelFont = UiKit.ResolveDefaultFont();

        string objName = "SetNumber";
        for (int i = pod.transform.childCount - 1; i >= 0; i--)
        {
            Transform c = pod.transform.GetChild(i);
            if (c.name == objName) Destroy(c.gameObject);
        }

        GameObject lbl = new GameObject(objName);
        lbl.transform.SetParent(pod.transform, false);
        lbl.transform.localPosition = new Vector3(0f, 3.6f, 0f);
        lbl.transform.localScale = Vector3.one * 0.6f;

        TextMesh tm = lbl.AddComponent<TextMesh>();
        tm.font = labelFont;
        tm.text = number.ToString();
        tm.fontSize = 60;
        tm.characterSize = 0.12f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = new Color(0.3f, 1f, 1f, 0.95f);
        lbl.AddComponent<FaceCamera>();
    }

    /// <summary>Keeps a world-space label facing the camera so set numbers stay readable.</summary>
    private sealed class FaceCamera : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;
        }
    }

    /// <summary>Stores a canister's spawn-order route index so pruning cleans up in route order.</summary>
    private sealed class RouteIndex : MonoBehaviour
    {
        public int index;
    }

    /// <summary>Drops extra dodge hazards near the current cursor area.</summary>
    private void SpawnHazards(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = cursor;
            float lateral = UnityEngine.Random.Range(18f, 55f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            pos.x = Mathf.Clamp(pos.x + lateral, xMin, xMax);
            pos.z += UnityEngine.Random.Range(-60f, 60f);
            pos.y = terrain != null ? terrain.SampleHeight(pos) + 0.1f : pos.y;
            if (spawnHazard != null)
            {
                GameObject h = spawnHazard(pos, hazardSerial++);
                liveObjects.Add(h);
            }
        }
    }

    private void PruneFarBehind()
    {
        if (buggy == null) return;
        Vector3 b = buggy.transform.position;
        // Canisters are recycled once they fall a set amount BEHIND the buggy in route order
        // (never by straight-line distance - a long looping chain must not be chopped while the
        // buggy still has to reach it). Non-canisters (hazards) are still pruned by distance.
        int behindLimit = Mathf.Max(0, clearedCount - keepBehind);
        for (int i = liveObjects.Count - 1; i >= 0; i--)
        {
            GameObject go = liveObjects[i];
            if (go == null) { liveObjects.RemoveAt(i); continue; }
            RouteIndex marker = go.GetComponent<RouteIndex>();
            if (marker != null)
            {
                if (marker.index < behindLimit)
                {
                    Destroy(go);
                    liveObjects.RemoveAt(i);
                }
                continue;
            }
            Vector3 d = go.transform.position - b;
            d.y = 0f;
            if (d.magnitude > despawnRadius)
            {
                Destroy(go);
                liveObjects.RemoveAt(i);
            }
        }
    }

    private void ApplyRamp(int setIndex)
    {
        // 1) Tyres get slicker (re-limit what can be added back by future steps).
        if (buggy != null)
        {
            ApplyGrip(buggy.frontLeftWheel);
            ApplyGrip(buggy.frontRightWheel);
            ApplyGrip(buggy.rearLeftWheel);
            ApplyGrip(buggy.rearRightWheel);
        }

        // 2) More dodge hazards on the route.
        SpawnHazards(hazardsPerStep * setIndex);

        // 3) Re-generate the ground bumpier (async-ish so the held frame is short).
        bumpAmplitude += bumpStep;
        bumpScale *= bumpScaleStep;
        StartCoroutine(RebumpGround());

        // 4) The fog closes in and the horizon Earth bleeds darker red as the run deepens.
        if (survivalEarth != null) survivalEarth.SetDanger(setIndex / 8f);
    }

    private void ApplyGrip(WheelCollider wheel)
    {
        if (wheel == null) return;
        var fwd = wheel.forwardFriction;
        fwd.stiffness = Mathf.Max(gripFloor, fwd.stiffness * gripStep);
        wheel.forwardFriction = fwd;
        var side = wheel.sidewaysFriction;
        side.stiffness = Mathf.Max(gripFloor, side.stiffness * gripStep);
        wheel.sidewaysFriction = side;
    }

    private IEnumerator RebumpGround()
    {
        // We do NOT lock the buggy's input: regenerating the surface below a driving buggy
        // would otherwise stall it to a stop. The gentle Y re-seat plus preserved velocity
        // below keeps the run flowing through the terrain shift.
        if (hud != null) hud.ShowHint("TERRAIN SHIFTING...");

        yield return null; // let the physics step settle before the regeneration frame

        if (terrain != null) terrain.RebuildWithNewRoughness(bumpAmplitude, bumpScale);

        // Keep the buggy at the same spot AND speed on the new surface (no sudden stop).
        if (buggy != null && terrain != null)
        {
            Rigidbody rb = buggy.GetComponent<Rigidbody>();
            Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;
            Vector3 bp = buggy.transform.position;
            bp.y = terrain.SampleHeight(bp) + 0.8f;
            buggy.transform.position = bp;
            if (rb != null) rb.linearVelocity = vel;
        }

        // Re-seat live canisters onto the new surface.
        for (int i = 0; i < liveObjects.Count; i++)
        {
            GameObject go = liveObjects[i];
            if (go == null || terrain == null) continue;
            Collider col = go.GetComponent<Collider>();
            if (col != null && !col.isTrigger) continue; // skip solid hazards, keep canisters
            Vector3 p = go.transform.position;
            p.y = terrain.SampleHeight(p) + (go.GetComponent<OxygenPod>() != null ? 2f : 0.1f);
            go.transform.position = p;
        }

        yield return new WaitForSeconds(0.5f);
        if (hud != null) hud.HideHint();
    }
}