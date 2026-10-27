using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Lunar Trials COURSE (Model A): a point-to-point run going FORWARD from the spawn to
/// a finish area at the far end - the skill is driving the line, and the ore (the score
/// layer) sits ON that line. Designed once per challenge build, deterministically:
/// GameBootstrap reseeds Random with the challenge seed before this runs, so every attempt
/// lays the same course and retrying is pure skill. (A closed loop variant is parked for a
/// future circuit race mode.)
///
/// The terrain generator consults the active course while stamping craters (the drive
/// corridor stays clean - the same trick the race lane uses) and pins a flat pad at BOTH
/// ends: the buggy launches from the start pad and finishes in the landing zone.
/// </summary>
public static class TrialsCourse
{
    /// <summary>The course designed for the pending build (null = free arena).</summary>
    public static TrialsCourseData Active { get; private set; }

    /// <summary>Designs the challenge's run. MUST run before MoonTerrainGen.Build so the
    /// terrain can keep the corridor clean and flatten both end pads. spawnXZ = the buggy's
    /// start XZ.</summary>
    public static void Design(ChallengeDef def, Vector2 spawnXZ)
    {
        Active = (def == null || def.courseLength <= 0)
            ? null
            : TrialsCourseData.Design(def, spawnXZ);
        if (Active != null)
        {
            Debug.Log("[Trials] course designed: " + def.id + " - " + Active.points.Length
                + " points, " + Active.lengthMeters.ToString("F0") + " m run, half width "
                + Active.halfWidth.ToString("F1") + " m");
        }
    }
}

/// <summary>One designed course: a dense OPEN path of XZ points (start -> finish) with
/// corridor queries. A gentle seeded weave gives the run its character; both ends are
/// straight so the launch and the finish run-in read clean.</summary>
public class TrialsCourseData
{
    public readonly Vector2[] points;    // dense open path, XZ plane (start -> finish)
    public readonly float halfWidth;     // drive corridor half width
    public readonly float lengthMeters;  // total run length

    public Vector2 StartXZ => points[0];
    public Vector2 FinishXZ => points[points.Length - 1];

    private readonly float[] cumulative; // arc length at each point (for progress queries)
    private readonly Rect bounds;        // expanded bounds; fast reject for spatial queries

    private const int SamplesPerSegment = 6;

    // Dive profile (set via SetDive when the challenge has a jump ramp): the corridor is
    // graded into a guaranteed downhill run-in before the ramp, holds flat through the ramp
    // and its landing zone, then climbs back to natural terrain. Heights in metres.
    private bool hasDive;
    private float diveA, diveB, diveC, diveD, diveDrop;

    /// <summary>True when the corridor carries a dive run-in (terrain gen reads this).</summary>
    public bool HasDive => hasDive;

    /// <summary>Grades a dive run-in into the corridor, ending flat at <paramref name="rampS"/>
    /// (the ramp sits mid-hold, and the landing zone stays flat behind it). MUST run before
    /// MoonTerrainGen.Build - the mesh and SampleHeight both apply this profile.</summary>
    public void SetDive(float rampS)
    {
        hasDive = true;
        diveDrop = 9f;                        // metres of drop - ~15% grade over the run-in
        diveA = Mathf.Clamp01(rampS - 0.16f); // start the dive...
        diveB = rampS - 0.045f;               // ...full drop reached ~23 m before the ramp,
                                              // so the last stretch is FLAT (suspension settles,
                                              // the ramp hits clean)
        diveC = rampS + 0.10f;                // hold flat through the ramp + landing zone
        diveD = rampS + 0.30f;                // climb back before the finish straight
    }

    /// <summary>The dive height offset (NEGATIVE, metres) at a world XZ - corridor-internal,
    /// fading out laterally so the corridor edges stay natural. Pure function of position,
    /// so the generated mesh and SampleHeight always agree.</summary>
    public float DiveDropAt(Vector2 p)
    {
        if (!hasDive || !bounds.Contains(p)) return 0f;
        float dist = Nearest(p, out float s01, out float lateral);
        float half = halfWidth;
        float lateralW = 1f - Mathf.Clamp01((Mathf.Abs(lateral) - half * 0.55f) / (half * 0.75f));
        if (lateralW <= 0f) return 0f;
        return -ProfileAt(s01) * lateralW;
    }

    private float ProfileAt(float s)
    {
        if (s <= diveA || s >= diveD) return 0f;
        if (s < diveB) return diveDrop * Mathf.SmoothStep(0f, 1f, (s - diveA) / (diveB - diveA));
        if (s <= diveC) return diveDrop;
        return diveDrop * (1f - Mathf.SmoothStep(0f, 1f, (s - diveC) / (diveD - diveC)));
    }

    private TrialsCourseData(List<Vector2> dense, float halfWidth)
    {
        points = dense.ToArray();
        this.halfWidth = halfWidth;

        cumulative = new float[points.Length];
        float total = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            cumulative[i] = total;
            if (i > 0) total += Vector2.Distance(points[i - 1], points[i]);
        }
        lengthMeters = total;

        Vector2 min = points[0], max = points[0];
        foreach (Vector2 p in points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        float pad = halfWidth + 40f;   // corridor + crater-radius margin
        bounds = Rect.MinMaxRect(min.x - pad, min.y - pad, max.x + pad, max.y + pad);
    }

    /// <summary>Builds the challenge's run: control points marching forward (+Z) from the
    /// spawn with an alternating weave, smoothed with an open Catmull-Rom pass.</summary>
    public static TrialsCourseData Design(ChallengeDef def, Vector2 spawnXZ)
    {
        const int CtrlCount = 6;
        float step = def.courseLength / (CtrlCount - 1);
        var ctrl = new List<Vector2>(CtrlCount);
        int side = Random.value < 0.5f ? -1 : 1;
        for (int i = 0; i < CtrlCount; i++)
        {
            // Clean straights at both ends (launch + finish run-in); the weave lives between.
            float lat = (i == 0 || i == CtrlCount - 1) ? 0f : side * step * Random.Range(0.12f, 0.30f);
            ctrl.Add(new Vector2(spawnXZ.x + lat, spawnXZ.y + step * i));
            side = -side;
        }

        // Open Catmull-Rom: duplicated end controls clamp the curve at both terminals.
        var dense = new List<Vector2>((CtrlCount - 1) * SamplesPerSegment + 1);
        for (int i = 0; i < CtrlCount - 1; i++)
        {
            Vector2 p0 = ctrl[Mathf.Max(0, i - 1)];
            Vector2 p1 = ctrl[i];
            Vector2 p2 = ctrl[i + 1];
            Vector2 p3 = ctrl[Mathf.Min(CtrlCount - 1, i + 2)];
            for (int j = 0; j < SamplesPerSegment; j++)
                dense.Add(Catmull(p0, p1, p2, p3, j / (float)SamplesPerSegment));
        }
        dense.Add(ctrl[CtrlCount - 1]);

        return new TrialsCourseData(dense, 9f);
    }

    private static Vector2 Catmull(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>The path point at progress s (0 = start pad, 1 = finish area).</summary>
    public Vector2 PointAt(float s01)
    {
        float target = Mathf.Clamp01(s01) * lengthMeters;
        int n = points.Length;
        for (int i = 1; i < n; i++)
        {
            if (target <= cumulative[i] || i == n - 1)
            {
                float segLen = cumulative[i] - cumulative[i - 1];
                float t = segLen > 1e-5f ? (target - cumulative[i - 1]) / segLen : 0f;
                return Vector2.LerpUnclamped(points[i - 1], points[i], t);
            }
        }
        return points[n - 1];
    }

    /// <summary>Unit direction of the line at progress s (DirectionAt(1) = the finish
    /// run-in direction).</summary>
    public Vector2 DirectionAt(float s01)
    {
        Vector2 d = PointAt(Mathf.Clamp01(s01 + 0.004f)) - PointAt(Mathf.Clamp01(s01 - 0.004f));
        return d.sqrMagnitude < 1e-8f ? Vector2.up : d.normalized;
    }

    /// <summary>Nearest point on the path to p: reports the progress s (0..1) of that
    /// nearest point and the SIGNED lateral distance (positive = left of the line).</summary>
    public float Nearest(Vector2 p, out float s01, out float lateralSigned)
    {
        float best = float.MaxValue;
        s01 = 0f;
        lateralSigned = 0f;
        int n = points.Length;
        for (int i = 0; i < n - 1; i++)
        {
            Vector2 a = points[i];
            Vector2 ab = points[i + 1] - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            float d = Vector2.Distance(p, a + ab * t);
            if (d < best)
            {
                best = d;
                float cross = ab.x * (p - a).y - ab.y * (p - a).x;
                s01 = (cumulative[i] + Mathf.Sqrt(len2) * t) / lengthMeters;
                lateralSigned = cross >= 0f ? d : -d;
            }
        }
        return best;
    }

    /// <summary>True when p sits inside the drive corridor (plus <paramref name="extra"/>
    /// metres of margin).</summary>
    public bool InsideCorridor(Vector2 p, float extra)
    {
        if (!bounds.Contains(p)) return false;
        return Nearest(p, out _, out _) <= halfWidth + extra;
    }
}
