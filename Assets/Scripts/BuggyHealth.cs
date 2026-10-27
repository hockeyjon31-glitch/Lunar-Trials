using System;
using UnityEngine;

/// <summary>
/// Tracks the buggy's hull integrity. Only impacts with boulders (objects carrying a
/// Boulder component) deal damage, and faster impacts deal proportionally more damage.
/// Three heavy hits are enough to destroy the buggy and end the run.
///
/// Sits on the buggy's root GameObject (the one with the Rigidbody) so every collision
/// event for the vehicle's colliders is received here.
/// </summary>
public class BuggyHealth : MonoBehaviour
{
    [Header("Integrity")]
    [Tooltip("Total hull integrity (100 = pristine).")]
    public float maxHealth = 100f;
    [Tooltip("Impact speed (m/s) below which no damage is taken.")]
    public float damageThreshold = 3.5f;
    [Tooltip("Hull integrity lost per m/s of impact speed above the threshold.")]
    public float damagePerSpeed = 2.2f;
    [Tooltip("Minimum time between damage events so a resting contact doesn't grind the bar down.")]
    public float damageCooldown = 0.75f;

    [Header("Impact feedback")]
    [Tooltip("Fraction of the impact speed the buggy keeps after bouncing off a boulder (rebound).")]
    public float reboundFactor = 0.45f;
    [Tooltip("Speed (m/s) above the damage threshold that produces a full-strength screen shake.")]
    public float shakeFullSpeed = 16f;

    public float CurrentHealth { get; private set; }
    public bool IsDestroyed { get; private set; }

    public event Action OnHealthChanged;

    private float lastDamageTime = -999f;

    private void Awake()
    {
        CurrentHealth = maxHealth;
        IsDestroyed = false;
    }

    /// <summary>
    /// Scales the hull's maximum integrity by a Garage-upgrade factor, preserving the same
    /// fraction of health it had (so an upgraded hull feels tougher, not instantly repaired).
    /// </summary>
    public void ApplyMaxHealthMultiplier(float factor)
    {
        float ratio = maxHealth > 0f ? CurrentHealth / maxHealth : 1f;
        maxHealth *= factor;
        CurrentHealth = ratio * maxHealth;
        OnHealthChanged?.Invoke();
    }

    /// <summary>Restores a carried-in fraction of hull integrity (Ridge Survival loops the
    /// scene: the next deploy keeps the damage level the previous leg finished with).</summary>
    public void SetHealthFraction(float fraction)
    {
        CurrentHealth = maxHealth * Mathf.Clamp01(fraction);
        IsDestroyed = false;
        OnHealthChanged?.Invoke();
    }

    private void OnCollisionEnter(Collision c)
    {
        if (IsDestroyed) return;

        // Only big boulders damage the hull; ground and terrain contact is harmless.
        Boulder boulder = c.gameObject.GetComponentInParent<Boulder>();
        if (boulder == null) return;
        // A real hit is no near-miss: cancel any adrenaline payout this rock might have been
        // tracking (crashing into the hazard never rewards the driver).
        boulder.hitByBuggy = true;

        // relativeVelocity magnitude is the closing speed of the impact in m/s.
        float impactSpeed = c.relativeVelocity.magnitude;
        float damage = (impactSpeed - damageThreshold) * damagePerSpeed * boulder.impactMultiplier;
        if (damage <= 0f) return;

        if (Time.time - lastDamageTime < damageCooldown) return;
        lastDamageTime = Time.time;

        TakeDamage(damage);

        // Hard boulder hits knock the buggy back off the rock and shake the camera, so the
        // crash feels physical rather than just a number dropping on the integrity bar.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic && c.contacts.Length > 0)
        {
            Vector3 normal = c.contacts[0].normal;
            Vector3 vel = rb.linearVelocity;
            Vector3 reflected = Vector3.Reflect(vel, normal);
            reflected.y = Mathf.Max(reflected.y, Mathf.Abs(vel.y) * 0.35f); // a little hop upward
            rb.linearVelocity = reflected * (reboundFactor + 0.2f * Mathf.Clamp01(impactSpeed / 24f));
        }

        if (BuggyCamera.Instance != null)
        {
            BuggyCamera.Instance.AddShake(Mathf.Clamp01((impactSpeed - damageThreshold) / shakeFullSpeed));
        }
    }

    public void TakeDamage(float amount)
    {
        if (IsDestroyed) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        OnHealthChanged?.Invoke();

        // CrazyGames first-run onboarding: explain the hull meter the first time it drops
        // (meteor blasts and boulder impacts are the two damage sources). No-op off portal/editor.
        TutorialCoach.NotifyHullDamaged();

        if (CurrentHealth <= 0f)
        {
            IsDestroyed = true;
            if (OxygenManager.Instance != null)
            {
                OxygenManager.Instance.BuggyDestroyed();
            }
        }
    }
}