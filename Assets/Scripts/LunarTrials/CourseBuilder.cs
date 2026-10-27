using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dresses the active trials course with tyre edge markers: a low-poly tyre (procedural
/// torus) is stamped every few metres down BOTH edges of the drive corridor, PILED UP -
/// every other step doubles the pile and every fifth step trebles it, so the edges read as
/// proper tyre walls. Everything bakes into ONE combined mesh - a single object, a single
/// draw call for the whole course dressing. No colliders: tyres are soft guides, not walls.
/// </summary>
public static class CourseBuilder
{
    private const float StepMetres = 4.5f;   // one tyre pile per step along the run

    public static void Build(MoonTerrainGen terrain, Transform parent)
    {
        TrialsCourseData course = TrialsCourse.Active;
        if (course == null || terrain == null) return;

        Mesh tyre = TyreMesh(0.55f, 0.22f, 10, 18);

        var verts = new List<Vector3>(4096);
        var tris = new List<int>(8192);
        var uv = new List<Vector2>(4096);
        int step = 0;

        float acc = 0f, next = 0f;
        int n = course.points.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = course.points[i];
            Vector2 b = course.points[(i + 1) % n];
            float segLen = Vector2.Distance(a, b);
            while (next <= acc + segLen && i < n - 1 || (i == n - 1 && next <= course.lengthMeters))
            {
                float t = Mathf.Clamp01((next - acc) / Mathf.Max(0.01f, segLen));
                Vector2 centre = Vector2.Lerp(a, b, t);
                Vector2 dir = (b - a).normalized;

                StampSide(verts, tris, uv, tyre, terrain, centre, dir, course.halfWidth - 0.6f, step);
                StampSide(verts, tris, uv, tyre, terrain, centre, dir, -(course.halfWidth - 0.6f), step);

                next += StepMetres;
                step++;
            }
            acc += segLen;
        }

        Mesh combined = new Mesh { name = "TrialsCourseMarkers" };
        combined.indexFormat = verts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        combined.SetVertices(verts);
        combined.SetUVs(0, uv);
        combined.SetTriangles(tris, 0);
        combined.RecalculateNormals();
        combined.RecalculateBounds();

        Material mat = TyreMaterial();

        var go = new GameObject("TrialsCourseMarkers");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = combined;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        Debug.Log("[Trials] course markers: " + step + " steps, " + (verts.Count / 180) + " tyres, "
            + verts.Count + " verts (1 draw call)");
    }

    /// <summary>Stamps one tyre pile (1-3 high) at <paramref name="offset"/> metres left/right
    /// of the line, resting on the terrain. Every other step piles 2 high, every fifth piles 3.</summary>
    private static void StampSide(List<Vector3> verts, List<int> tris, List<Vector2> uv,
        Mesh tyre, MoonTerrainGen terrain, Vector2 centre, Vector2 dir,
        float offset, int step)
    {
        Vector2 at = centre + new Vector2(-dir.y, dir.x) * offset;
        Vector3 pos = new Vector3(at.x, terrain.SampleHeight(new Vector3(at.x, 0f, at.y)), at.y);

        Quaternion rot = Quaternion.Euler(
            Random.Range(-3f, 3f),
            Random.Range(0f, 360f),
            Random.Range(-3f, 3f));

        int pile = 1 + (step % 2 == 0 ? 1 : 0) + (step % 5 == 0 ? 1 : 0);
        for (int t = 0; t < pile; t++)
            Append(verts, tris, uv, tyre, pos + Vector3.up * (0.40f * t), rot, 1f);
    }

    private static Material _tyreMat;

    /// <summary>The shared tyre material (one instance for the whole course dressing).</summary>
    private static Material TyreMaterial()
    {
        if (_tyreMat == null)
        {
            _tyreMat = new Material(Shader.Find("Standard")) { name = "CourseTyre" };
            _tyreMat.color = new Color(0.09f, 0.09f, 0.10f, 1f);
        }
        return _tyreMat;
    }

    /// <summary>Builds a jump ramp ON the course at progress s: an inclined plank drive
    /// surface plus a rear support block, oriented along the run direction. The plank's box
    /// collider is the launch surface (box colliders are winding-proof and wheels ride them
    /// smoothly); two tyre piles mark the takeoff corners. The ramp spans 9 of the 18 m
    /// corridor, so it is a line choice, not a wall. A BOOST strip on the run-in (yellow
    /// chevrons) shoves the buggy forward so the ramp delivers real air time.</summary>
    public static void BuildRamp(MoonTerrainGen terrain, TrialsCourseData course, float s01,
        Transform parent)
    {
        Vector2 p = course.PointAt(s01);
        Vector2 dir2 = course.DirectionAt(s01);
        Vector3 ground = new Vector3(p.x, terrain.SampleHeight(new Vector3(p.x, 0f, p.y)), p.y);
        Quaternion rot = Quaternion.LookRotation(new Vector3(dir2.x, 0f, dir2.y), Vector3.up);

        const float W = 9f;     // width across the corridor
        const float L = 9f;     // horizontal run of the slope
        const float H = 3.2f;   // takeoff height (~20 degrees - a proper kicker)

        var root = new GameObject("TrialsRamp");
        root.transform.SetParent(parent, false);
        root.transform.position = ground;
        root.transform.rotation = rot;

        // BOOST PAD: a shove-forward strip on the run-in (marked with yellow chevrons).
        // Combined with the dive's downhill speed this is what buys the big air.
        var boostGo = new GameObject("TrialsBoostPad");
        boostGo.transform.SetParent(root.transform, false);
        boostGo.AddComponent<TrialsBoostPad>();
        Material chevMat = new Material(Shader.Find("Unlit/Color")) { name = "BoostChevron" };
        chevMat.color = new Color(1f, 0.85f, 0.15f, 1f);
        for (int i = 0; i < 5; i++)
        {
            GameObject chev = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chev.name = "Chevron";
            chev.transform.SetParent(root.transform, false);
            chev.transform.localScale = new Vector3(W * 0.55f, 0.06f, 1.0f);
            chev.transform.localPosition = new Vector3(0f, 0.05f, -2.5f - i * 2.4f);
            chev.transform.localRotation = Quaternion.Euler(0f, i % 2 == 0 ? 28f : -28f, 0f);
            chev.GetComponent<MeshRenderer>().sharedMaterial = chevMat;
            Object.Destroy(chev.GetComponent<Collider>());
        }

        Material metal = YFMetal.Get(37);

        float angle = Mathf.Atan2(H, L) * Mathf.Rad2Deg;
        float slopeLen = Mathf.Sqrt(L * L + H * H);

        // Inclined plank: its TOP surface runs from ~ground at the entry to H at the
        // takeoff edge. Euler(-angle) pitches the +Z (travel) end UP; sunk 0.16 so the
        // entry lip stays a few centimetres - a bump, not a step.
        GameObject plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plank.name = "RampPlank";
        plank.transform.SetParent(root.transform, false);
        plank.transform.localScale = new Vector3(W, 0.5f, slopeLen);
        plank.transform.localPosition = new Vector3(0f, H / 2f - 0.16f, L / 2f);
        plank.transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);
        plank.GetComponent<MeshRenderer>().sharedMaterial = metal;

        // Rear support block under the takeoff edge.
        GameObject support = GameObject.CreatePrimitive(PrimitiveType.Cube);
        support.name = "RampSupport";
        support.transform.SetParent(root.transform, false);
        support.transform.localScale = new Vector3(W, H, 0.6f);
        support.transform.localPosition = new Vector3(0f, H / 2f, L - 0.3f);
        support.GetComponent<MeshRenderer>().sharedMaterial = metal;

        // Two tyre piles mark the takeoff corners.
        Mesh tyre = TyreMesh(0.55f, 0.22f, 10, 18);
        foreach (int sideX in new[] { -1, 1 })
        {
            Vector3 corner = new Vector3(sideX * (W / 2f + 0.7f), 0f, L + 0.6f);
            for (int t = 0; t < 2; t++)
            {
                GameObject tyreGo = new GameObject("RampTyre");
                tyreGo.transform.SetParent(root.transform, false);
                tyreGo.transform.localPosition = corner + Vector3.up * (0.40f * t);
                tyreGo.AddComponent<MeshFilter>().sharedMesh = tyre;
                tyreGo.AddComponent<MeshRenderer>().sharedMaterial = TyreMaterial();
            }
        }

        Debug.Log("[Trials] ramp built at s=" + s01.ToString("F2") + " (" + ground.ToString("F1") + ")");
    }

    private static void Append(List<Vector3> verts, List<int> tris, List<Vector2> uv,
        Mesh src, Vector3 pos, Quaternion rot, float scale)
    {
        int b = verts.Count;
        Vector3[] sv = src.vertices;
        for (int i = 0; i < sv.Length; i++) verts.Add(pos + rot * (sv[i] * scale));
        Vector2[] su = src.uv;
        for (int i = 0; i < su.Length; i++) uv.Add(su[i]);
        int[] st = src.triangles;
        for (int i = 0; i < st.Length; i++) tris.Add(b + st[i]);
    }

    /// <summary>Procedural low-poly torus lying flat in the XZ plane (a tyre).</summary>
    private static Mesh TyreMesh(float majorR, float minorR, int majorSegs, int minorSegs)
    {
        var verts = new Vector3[majorSegs * minorSegs];
        var norms = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];
        for (int s = 0; s < majorSegs; s++)
        {
            float a = s / (float)majorSegs * Mathf.PI * 2f;
            Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            for (int m = 0; m < minorSegs; m++)
            {
                float b = m / (float)minorSegs * Mathf.PI * 2f;
                Vector3 spoke = new Vector3(Mathf.Cos(b) * radial.x, Mathf.Sin(b), Mathf.Cos(b) * radial.z);
                Vector3 p = radial * majorR + spoke * minorR;
                int idx = s * minorSegs + m;
                verts[idx] = p;
                norms[idx] = spoke;
                uvs[idx] = new Vector2(s / (float)majorSegs, m / (float)minorSegs);
            }
        }
        var tris = new int[majorSegs * minorSegs * 6];
        int ti = 0;
        for (int s = 0; s < majorSegs; s++)
        {
            int s1 = (s + 1) % majorSegs;
            for (int m = 0; m < minorSegs; m++)
            {
                int m1 = (m + 1) % minorSegs;
                int i0 = s * minorSegs + m, i1 = s1 * minorSegs + m;
                int i2 = s * minorSegs + m1, i3 = s1 * minorSegs + m1;
                tris[ti++] = i0; tris[ti++] = i2; tris[ti++] = i1;
                tris[ti++] = i1; tris[ti++] = i2; tris[ti++] = i3;
            }
        }
        var mesh = new Mesh { name = "Tyre" };
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvs;
        mesh.triangles = tris;
        return mesh;
    }
}
