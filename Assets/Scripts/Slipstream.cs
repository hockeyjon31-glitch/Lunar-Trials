using UnityEngine;

/// <summary>
/// Slipstream (drafting): a racer creeping into the still air behind another racer ahead
/// picks up free top speed - the wake lets it run above its own shelf without spending boost.
/// Everyone gets the benefit: the player drafts the rivals, and each rival drafts the other
/// rivals (and the player when it leads). The bonus ramps with closeness: it only sits at
/// its full 5-10 km/h within a few metres dead behind the leader and fades out near the
/// pocket's lips. The pocket exists only BEHIND a MOVING leader - a parked car wakes nothing.
///
/// The course runs down +Z, so "ahead" is the larger z, and the lateral window is left/right.
/// </summary>
public static class Slipstream
{
    /// <summary>Depth of the slip pocket AHEAD of the leader along the course (metres).</summary>
    public const float Reach = 6f;

    /// <summary>Half-width of the slip pocket (metres either side of the leader's line).</summary>
    public const float LateralWindow = 3.2f;

    /// <summary>Core slot (metres) behind the leader where the draft is fully effective.</summary>
    public const float Core = 2.5f;

    /// <summary>Top of the bonus ramp (km/h) - a solid middle of the 5-10 km/h ask.</summary>
    public const float MaxBonusKmh = 7f;

    /// <summary>Extra top speed a follower earns from the wake pocket ahead, 0..MaxBonusKmh.</summary>
    public static float BonusKmh(Vector3 followerPos)
    {
        float best = 0f;
        System.Collections.Generic.List<RivalRacer> rivals = RivalRacer.Rivals;
        for (int i = 0; i < rivals.Count; i++)
        {
            RivalRacer r = rivals[i];
            if (r == null) continue;
            float b = BonusFromLeader(followerPos, r.transform.position, r.CrossedFinish);
            if (b > best) best = b;
        }
        BuggyController player = RivalRacer.PlayerBuggy;
        if (player != null && player.CurrentSpeedKmh > 2f)
        {
            float b = BonusFromLeader(followerPos, player.transform.position, false);
            if (b > best) best = b;
        }
        return best;
    }

    /// <summary>Ramp for a single leader: full inside the core, fading toward the pocket lips.</summary>
    private static float BonusFromLeader(Vector3 follower, Vector3 leader, bool leaderFinished)
    {
        if (leaderFinished) return 0f;
        float behind = leader.z - follower.z;   // course runs +Z, so a positive gap is leader ahead
        if (behind > 0.5f && behind <= Reach)
        {
            float lateral = Mathf.Abs(leader.x - follower.x);
            if (lateral <= LateralWindow)
            {
                float coreT = Mathf.Clamp01((Reach - behind) / (Reach - Core));
                float latT = 1f - Mathf.Clamp01(lateral / LateralWindow);
                return MaxBonusKmh * Mathf.Min(coreT, latT);
            }
        }
        return 0f;
    }
}