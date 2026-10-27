using UnityEngine;

/// <summary>
/// Slowly rotates a horizon backdrop around its own vertical axis (the planet's pole after any
/// startRotation), so it reads as a living world rather than a static fixture.
/// </summary>
public class BackdropSpin : MonoBehaviour
{
    [Tooltip("Rotational speed (degrees per second).")]
    public float spinSpeed = 1.5f;

    private void Update()
    {
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
    }
}