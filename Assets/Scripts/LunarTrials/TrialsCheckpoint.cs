using UnityEngine;

/// <summary>
/// One checkpoint marker on a Lunar Trials course: a lit gate the buggy drives through. On
/// crossing, the run manager stores the respawn point (the fail-fast trials loop will use
/// it once challenge-fail hooks land; today it is stored and ready). Built entirely from
/// code, like everything else in the game.
/// </summary>
public class TrialsCheckpoint : MonoBehaviour
{
    /// <summary>Builds a checkpoint gate at a world position with a given heading (degrees).</summary>
    public static TrialsCheckpoint Create(Vector3 position, float headingDeg)
    {
        GameObject go = new GameObject("TrialsCheckpoint");
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, headingDeg, 0f);
        TrialsCheckpoint cp = go.AddComponent<TrialsCheckpoint>();
        cp.Build();
        return cp;
    }

    private void Build()
    {
        Material litMat = new Material(Shader.Find("Unlit/Color"));
        litMat.color = new Color(0.3f, 1f, 0.6f, 1f);

        // Two pylons marking the gate line, plus a soft trigger volume between them.
        const float halfWidth = 9f;
        CreatePylon(litMat, new Vector3(-halfWidth, 0f, 0f));
        CreatePylon(litMat, new Vector3(halfWidth, 0f, 0f));

        BoxCollider trigger = gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(halfWidth * 2f + 4f, 8f, 3f);
        trigger.center = new Vector3(0f, 3f, 0f);
    }

    private void CreatePylon(Material mat, Vector3 localPos)
    {
        GameObject pylon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pylon.name = "CheckpointPylon";
        pylon.transform.SetParent(transform, false);
        pylon.transform.localScale = new Vector3(0.5f, 3f, 0.5f);
        pylon.transform.localPosition = localPos + Vector3.up * 3f;
        pylon.GetComponent<MeshRenderer>().sharedMaterial = mat;
        Object.Destroy(pylon.GetComponent<Collider>());
    }

    private void OnTriggerEnter(Collider other)
    {
        if (TrialsRunManager.Current == null) return;
        BuggyController buggy = other.GetComponentInParent<BuggyController>();
        if (buggy == null) return;
        TrialsRunManager.Current.SetCheckpoint(transform.position, transform.rotation);
    }
}
