/// <summary>
/// This IS the Lunar Trials project: the edition is always on. (The shared-codebase days in
/// the Lunar Harvest repo used a LUNAR_TRIALS scripting define; the dedicated project has
/// no other edition, so the gate is a constant.)
/// </summary>
public static class GameEdition
{
    /// <summary>Always true in this project.</summary>
    public static bool IsTrials => true;
}
