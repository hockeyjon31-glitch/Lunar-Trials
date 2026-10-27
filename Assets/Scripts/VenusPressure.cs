using UnityEngine;

/// <summary>
/// Venus's crushing atmosphere. While the run is live (buggy input unlocked - not during the
/// intro or pre-race countdown) the planet's pressure and heat chip the hull at a steady
/// rate: 1% of maximum integrity every TWO seconds (0.5%/s - gentler than the old 1%/s so the
/// atmosphere stays a clock, not a death spiral). The bar visibly bleeds down over the course
/// of a run, forcing pace. GameBootstrap attaches this to the buggy when the level is VENUS,
/// in both Harvester and Race.
///
/// The FAVONIUS never ticks: its titanium body ignores the crush entirely, which is the
/// vehicle's real Venus edge (its hull is otherwise no stronger than the Mars Rover's - the
/// immunity, not raw numbers, is what it pays for).
/// </summary>
public class VenusPressure : MonoBehaviour
{
    [Header("References")]
    public BuggyHealth health;
    public BuggyController buggy;

    [Header("Tuning")]
    [Tooltip("Hull fraction lost per tick (0.01 = 1% of the ORIGINAL stock hull). The rate is "
            + "measured against the ORIGINAL hull, so it does NOT scale up with Hull Garage "
            + "upgrades: an improved hull takes proportionally less damage and lasts longer "
            + "under pressure. One tick lands every damageTick seconds (default 2 -> 1% per 2s).")]
    public float damagePerTick = 0.01f;
    [Tooltip("Seconds between hull-fraction ticks (2 = a 1% chip every two seconds).")]
    public float damageTick = 2f;

    // The hull's maximum integrity BEFORE any Garage upgrade was applied (set by GameBootstrap
    // at attach, before the upgrade multipliers run), so 1% always means 1% of the stock
    // 100% hull, not of the upgraded tank - improvements stretch the survival window.
    public float originalMaxHealth;

    private float accumulator;

    private void Update()
    {
        // The atmosphere only bites once the run is actually live: the intro and the race
        // countdown hold input off, and that is exactly when the planet should NOT be eating
        // hull. Recovered instantly once the player is driving.
        if (health == null || buggy == null || !buggy.inputEnabled) { accumulator = 0f; return; }
        if (health.IsDestroyed) return;
        // The FAVONIUS's titanium sphere is immune to the atmosphere - it trades the raw hull
        // size every other model carries for being the one rig that never bleeds on Venus.
        if (VehicleSystem.IsSail) { accumulator = 0f; return; }
        if (originalMaxHealth <= 0f) originalMaxHealth = health.maxHealth;

        accumulator += Time.deltaTime;
        if (accumulator >= damageTick)
        {
            accumulator = 0f;
            health.TakeDamage(originalMaxHealth * damagePerTick);
        }
    }
}