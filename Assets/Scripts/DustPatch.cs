using UnityEngine;

/// <summary>
/// A visible patch of loose regolith that cuts the buggy's traction. Spawned as a soft,
/// slightly darker ellipse flat against the terrain (readable while driving) so a skilled
/// player routes around it. BuggyController reads the combined slip each physics step.
/// </summary>
public class DustPatch : MonoBehaviour
{
    /// <summary>The centre on the XZ plane.</summary>
    [System.NonSerialized] public Vector2 center;
    [System.NonSerialized] public float radiusA = 6f;
    [System.NonSerialized] public float radiusB = 4.5f;
    [System.NonSerialized] public float yawDeg = 0f;
    [System.NonSerialized] public float slip = 0.55f;

    /// <summary>Every patch in the world, so the buggy can sum their grip at its position.</summary>
    public static readonly System.Collections.Generic.List<DustPatch> All = new System.Collections.Generic.List<DustPatch>();

    /// <summary>
    /// Blends the slip underneath a world position: 1 = full grip, down to the softest patch.
    /// </summary>
    public static float SlipAt(Vector3 worldPos)
    {
        float slip = 1f;
        for (int i = 0; i < All.Count; i++)
        {
            DustPatch p = All[i];
            float dx = worldPos.x - p.center.x;
            float dz = worldPos.z - p.center.y;
            float c = Mathf.Cos(-p.yawDeg * Mathf.Deg2Rad);
            float s = Mathf.Sin(-p.yawDeg * Mathf.Deg2Rad);
            float lx = c * dx - s * dz;
            float lz = s * dx + c * dz;
            float nx = lx / p.radiusA;
            float nz = lz / p.radiusB;
            if (nx * nx + nz * nz <= 1f)
            {
                slip = Mathf.Min(slip, p.slip);
            }
        }
        return slip;
    }

    /// <summary>Builds a patch at <paramref name="center"/> with its slip <paramref name="tint"/>, plus a soft visual disc.</summary>
    public static DustPatch Build(Transform parent, Vector3 center, float a, float b, float yawDeg, Color tint)
    {
        GameObject go = new GameObject("DustPatch");
        go.transform.SetParent(parent, false);
        go.transform.position = center;

        DustPatch p = go.AddComponent<DustPatch>();
        p.center = new Vector2(center.x, center.z);
        p.radiusA = a;
        p.radiusB = b;
        p.yawDeg = yawDeg;
        All.Add(p);

        p.BuildVisual(tint);
        return p;
    }

    private void OnDestroy()
    {
        All.Remove(this);
    }

    private void BuildVisual(Color tint)
    {
        // Flat disc slightly above the surface (the bootstrap already rests it on terrain).
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
        disc.name = "Visual";
        disc.transform.SetParent(transform, false);
        disc.transform.localPosition = Vector3.zero;
        // Lay the quad flat (normal +Y) with the ellipse axes matching radiusA/radiusB.
        disc.transform.localRotation = Quaternion.Euler(-90f, 0f, yawDeg);
        disc.transform.localScale = new Vector3(radiusA * 2f, radiusB * 2f, 1f);

        DestroyImmediate(disc.GetComponent<Collider>());

        MeshRenderer mr = disc.GetComponent<MeshRenderer>();
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.mainTexture = SoftDiscTexture();
        mat.color = tint;
        mr.sharedMaterial = mat;
    }

    private static Texture2D softDisc;

    private static Texture2D SoftDiscTexture()
    {
        if (softDisc != null) return softDisc;
        const int size = 64;
        softDisc = new Texture2D(size, size, TextureFormat.RGBA32, true);
        softDisc.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = d >= 1f ? (byte)0 : (byte)(Mathf.Pow(1f - d, 1.8f) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        softDisc.SetPixels32(px);
        softDisc.Apply(true, false);
        return softDisc;
    }
}