using System.Collections.Generic;
using UnityEngine;

/// <summary>How a landing was judged. Ported from the Apex Torque reference: a landing is
/// graded on how close the chassis up-axis was to the surface normal, with roof-first contact
/// entering a bouncy TUMBLE instead of an instant crash.</summary>
public enum TrialsLandingGrade
{
    None,
    Perfect,
    Great,
    Rough,
    Tumble,
    Crash,
    Recovery
}

/// <summary>
/// Airtime trick detection for Lunar Trials, ported from the Apex Torque stunt detector.
///
/// It is deliberately a plain class (not a MonoBehaviour): BuggyController owns one and feeds
/// it the rotation accumulated during each jump. Only the accumulated ANGLE matters, so the
/// detector works no matter which way the buggy spins or which axis the player used.
///
/// Tricks are awarded live in the air (so the popups fire at the moment of the flip) but the
/// score is only BANKED on a clean landing. A tumble or crash wipes the pending score.
/// </summary>
public class TrialsStuntDetector
{
    public struct Trick
    {
        public string name;
        public int points;
        public string type;
    }

    private readonly List<Trick> active = new List<Trick>();
    private int lastPitchCycles;
    private int lastRollCycles;
    private int lastYawHalfCycles;

    /// <summary>Combo grows by 0.5 for every trick landed in the same airtime.</summary>
    public float ComboMultiplier { get; private set; } = 1f;
    /// <summary>Lifetime trick score for this run/attempt.</summary>
    public int TotalScore { get; private set; }
    /// <summary>Points currently in the air, combo applied. Zero once banked/wiped.</summary>
    public int PendingScore => Mathf.RoundToInt(rawScore * ComboMultiplier);
    /// <summary>Sum of the trick cards before the combo multiplier.</summary>
    public int RawScore => rawScore;
    public int LastBankedScore { get; private set; }
    public string LastTrickName { get; private set; }
    public string LastLandingSummary { get; private set; }
    public float LastTrickTime { get; private set; }
    public bool AnyTricks => active.Count > 0;

    public IReadOnlyList<Trick> ActiveTricks => active;

    /// <summary>Called the moment a trick is earned, with the display name and its points.
    /// BuggyController hooks this to the floating score popup.</summary>
    public System.Action<string, int> OnTrick;

    private int rawScore;

    /// <summary>Clears the airborne batch. Call on take-off and after a landing is resolved.</summary>
    public void ResetAirborne()
    {
        active.Clear();
        ComboMultiplier = 1f;
        rawScore = 0;
        lastPitchCycles = 0;
        lastRollCycles = 0;
        lastYawHalfCycles = 0;
        LastTrickName = null;
    }

    /// <summary>Re-evaluate the accumulated rotation. Safe to call every physics step: each
    /// trick fires exactly once as its threshold is crossed.</summary>
    public void Evaluate(float pitchDeg, float rollDeg, float yawDeg, float airtimeSeconds)
    {
        float pitchAbs = Mathf.Abs(pitchDeg);
        float rollAbs = Mathf.Abs(rollDeg);
        float yawAbs = Mathf.Abs(yawDeg);

        // --- pitch flips: forgiving at 330 degrees, so a slightly short rotation still counts.
        int pitchCycles = Mathf.FloorToInt((pitchAbs + 30f) / 360f);
        if (pitchCycles > lastPitchCycles)
        {
            bool backflip = pitchDeg > 0f;
            int gained = pitchCycles - lastPitchCycles;
            for (int i = 0; i < gained; i++)
            {
                int count = lastPitchCycles + i + 1;
                string name;
                int pts;
                if (count == 1) { name = backflip ? "BACKFLIP" : "FRONTFLIP"; pts = 600; }
                else if (count == 2) { name = backflip ? "DOUBLE BACKFLIP" : "DOUBLE FRONTFLIP"; pts = 1400; }
                else if (count == 3) { name = backflip ? "TRIPLE BACKFLIP" : "TRIPLE FRONTFLIP"; pts = 2600; }
                else { name = count + "x " + (backflip ? "BACKFLIP!" : "FRONTFLIP!"); pts = 2000 + count * 1000; }
                AddTrick(name, pts, "pitch");
            }
            lastPitchCycles = pitchCycles;
        }

        // --- barrel rolls: 330 degrees, like pitch.
        int rollCycles = Mathf.FloorToInt((rollAbs + 30f) / 360f);
        if (rollCycles > lastRollCycles)
        {
            int gained = rollCycles - lastRollCycles;
            for (int i = 0; i < gained; i++)
            {
                int count = lastRollCycles + i + 1;
                string name;
                int pts;
                if (count == 1) { name = "BARREL ROLL"; pts = 550; }
                else if (count == 2) { name = "DOUBLE BARREL ROLL"; pts = 1300; }
                else { name = count + "x BARREL ROLL!"; pts = 1800 + count * 800; }
                AddTrick(name, pts, "roll");
            }
            lastRollCycles = rollCycles;
        }

        // --- flat spins: half a turn is already worth a card.
        int yawHalfCycles = Mathf.FloorToInt((yawAbs + 25f) / 180f);
        if (yawHalfCycles > lastYawHalfCycles)
        {
            int gained = yawHalfCycles - lastYawHalfCycles;
            for (int i = 0; i < gained; i++)
            {
                int count = lastYawHalfCycles + i + 1;
                int degrees = count * 180;
                AddTrick("FLATSPIN " + degrees + "\u00B0", 250 + count * 250, "yaw");

                // Compound tricks: a spin stacked onto a completed flip.
                if (lastPitchCycles >= 1 && degrees >= 360 && !HasNamed("RODEO"))
                    AddTrick("RODEO " + degrees + "\u00B0!", 1500, "hybrid");
                if (lastRollCycles >= 1 && degrees >= 360 && !HasNamed("CORKSCREW"))
                    AddTrick("CORKSCREW " + degrees + "\u00B0!", 1800, "hybrid");
            }
            lastYawHalfCycles = yawHalfCycles;
        }

        // --- airtime milestones.
        if (airtimeSeconds >= 2f && !HasId("airtime_2s"))
            AddTrick("BIG AIR (2.0s)", 400, "airtime");
        if (airtimeSeconds >= 3.5f && !HasId("airtime_3.5s"))
            AddTrick("MASSIVE AIR (3.5s+)", 1000, "airtime");
    }

    /// <summary>Banks the pending trick score on a clean/great/rough landing. Returns the
    /// number of points added, and leaves <see cref="LastLandingSummary"/> ready for the HUD.</summary>
    public int FinalizeLanding(float landingMultiplier, int bonusPoints, TrialsLandingGrade grade)
    {
        if (grade == TrialsLandingGrade.Tumble || grade == TrialsLandingGrade.Crash)
        {
            LastLandingSummary = grade == TrialsLandingGrade.Crash ? "WIPEOUT!" : "TUMBLING - RECOVER!";
            LastBankedScore = 0;
            ResetAirborne();
            return 0;
        }

        string mainTrick = LastTrickName;
        int final = Mathf.RoundToInt((rawScore * ComboMultiplier + bonusPoints) * landingMultiplier);
        LastBankedScore = final;
        if (final > 0)
        {
            TotalScore += final;
            LastLandingSummary = string.IsNullOrEmpty(mainTrick)
                ? "+" + final + " PTS"
                : mainTrick + "  +" + final + " PTS";
        }
        else
        {
            LastLandingSummary = "";
        }
        ResetAirborne();
        return final;
    }

    /// <summary>Wipes the pending batch without banking it (crash / respawn).</summary>
    public void WipePending()
    {
        LastBankedScore = 0;
        ResetAirborne();
    }

    private bool HasId(string id)
    {
        for (int i = 0; i < active.Count; i++)
            if (active[i].type == id) return true;
        return false;
    }

    private bool HasNamed(string fragment)
    {
        for (int i = 0; i < active.Count; i++)
            if (active[i].name != null && active[i].name.Contains(fragment)) return true;
        return false;
    }

    private void AddTrick(string name, int points, string type)
    {
        active.Add(new Trick { name = name, points = points, type = type });
        rawScore += points;
        ComboMultiplier = Mathf.Round((ComboMultiplier + 0.5f) * 10f) / 10f;
        LastTrickName = name;
        LastTrickTime = Time.time;
        if (OnTrick != null) OnTrick(name, points);
    }
}
