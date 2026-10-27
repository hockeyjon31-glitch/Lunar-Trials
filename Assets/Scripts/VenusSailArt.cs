using UnityEngine;

/// <summary>
/// The Favonius's bodywork, built from the same primitive vocabulary as the other rovers so it
/// drops straight into the existing chassis pipeline.
///
/// Layout follows the reference console: a monolithic titanium pressure sphere slung under a
/// ceramic-blanketed frame, an uncooled silicon-carbide sensor mast up front, a leeward
/// radiator plate shedding the Stirling's heat, a wide gather blade at the nose, and the
/// 12 m^2 rigid wingsail on a single mast.
///
/// The wing is a CHILD of <c>Sail_Pivot</c> and that pivot is what <see cref="SailRig"/>
/// rotates to trim the sail - so the thing the player sees is the thing the physics is solving.
/// Every part is built without a collider: a rigid wing that can catch on the terrain is worse
/// than no wing at all.
///
/// The reference schematic is a three-wheeler on a triangular base. This one sits on the same
/// four-corner footprint as the rest of the bay, because that is the wheel arrangement the
/// driving model is built around; the 3-wheel conversion is a physics change of its own.
/// </summary>
public static class VenusSailArt
{
    // ------------------------------------------------------------ primitives
    private static Transform Part(PrimitiveType type, Transform parent, string name, Material mat,
        Vector3 scale, Vector3 pos, Vector3 rot)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        go.transform.localPosition = pos;
        if (rot != Vector3.zero) go.transform.localEulerAngles = rot;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        Collider col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        return go.transform;
    }

    private static Transform Box(Transform parent, string name, Material mat, Vector3 scale, Vector3 pos, Vector3 rot = default(Vector3))
        => Part(PrimitiveType.Cube, parent, name, mat, scale, pos, rot);

    // Unity cylinder: axis +Y, height 2, radius 0.5.
    private static Transform Cyl(Transform parent, string name, Material mat, float length, float radius, Vector3 pos, Vector3 rot)
        => Part(PrimitiveType.Cylinder, parent, name, mat, new Vector3(radius * 2f, length / 2f, radius * 2f), pos, rot);

    private static Transform Sphere(Transform parent, string name, Material mat, float r, Vector3 pos)
        => Part(PrimitiveType.Sphere, parent, name, mat, new Vector3(r * 2f, r * 2f, r * 2f), pos, Vector3.zero);

    // ------------------------------------------------------------ body
    /// <summary>
    /// Builds the static Favonius body and hands back the chassis root plus the sail pivot, which
    /// the caller must pass to <see cref="SailRig"/>.
    /// </summary>
    public static Transform BuildChassis(Transform parent, LunarRoverArt.RoverMats m, out Transform sailPivot)
    {
        GameObject root = new GameObject("Favonius_Chassis");
        root.transform.SetParent(parent, false);
        Transform chassis = root.transform;

        // Wide, low frame - a landsail needs a low centre of mass or the heel force tips it.
        Box(chassis, "Fav_Floor", m.secondary, new Vector3(1.8f, 0.12f, 2.7f), new Vector3(0f, 0.44f, -0.05f));
        Box(chassis, "Fav_DeckPlate", m.foil, new Vector3(1.5f, 0.1f, 1.9f), new Vector3(0f, 0.53f, -0.2f));

        // The pressure vessel. 0.72 m monolithic sphere in the real design; carried low and
        // centred, which is where the heat, the mass and the sense of safety all want it.
        Sphere(chassis, "Fav_PressureSphere", m.primary, 0.46f, new Vector3(0f, 0.72f, -0.15f));
        // Inconel stiffening band round the equator.
        Cyl(chassis, "Fav_InconelRib", m.secondary, 0.1f, 0.5f, new Vector3(0f, 0.72f, -0.15f), new Vector3(0f, 0f, 90f));
        // Ceramic aerogel blanket shroud over the aft face - the layer doing the real work.
        Box(chassis, "Fav_AerogelShroud", m.foil, new Vector3(0.9f, 0.7f, 0.2f), new Vector3(0f, 0.78f, -0.55f), new Vector3(-14f, 0f, 0f));

        // SiC sensor mast, raked forward. Uncooled solid-state, so it runs hot and proud.
        Cyl(chassis, "Fav_SiCMast", m.secondary, 1.0f, 0.05f, new Vector3(0.3f, 0.9f, 0.85f), new Vector3(-16f, 0f, 0f));
        Box(chassis, "Fav_OpticalHead", m.accent, new Vector3(0.24f, 0.2f, 0.3f), new Vector3(0.3f, 1.4f, 1.02f));
        Cyl(chassis, "Fav_Anemometer", m.accent, 0.22f, 0.07f, new Vector3(0.3f, 1.55f, 0.95f), Vector3.zero);

        // Leeward radiator: a high-emissivity plate dumping the Stirling's heat astern.
        Box(chassis, "Fav_Radiator", m.secondary, new Vector3(0.06f, 0.62f, 1.1f), new Vector3(-0.92f, 0.95f, -0.35f), new Vector3(0f, 0f, 8f));
        for (int i = -1; i <= 1; i++)
            Box(chassis, "Fav_RadiatorFin", m.accent, new Vector3(0.04f, 0.62f, 0.06f), new Vector3(-0.95f, 0.95f, -0.35f + i * 0.34f));

        // Gather blade: wide reach, but visibly smaller than the prospector's.
        Box(chassis, "Fav_BladeLip", m.accent, new Vector3(2.1f, 0.08f, 0.4f), new Vector3(0f, 0.34f, 1.5f), new Vector3(12f, 0f, 0f));
        Box(chassis, "Fav_BladeBack", m.secondary, new Vector3(2.1f, 0.34f, 0.1f), new Vector3(0f, 0.5f, 1.44f), new Vector3(-8f, 0f, 0f));
        Box(chassis, "Fav_FrameNose", m.primary, new Vector3(1.4f, 0.16f, 0.9f), new Vector3(0f, 0.6f, 0.95f), new Vector3(6f, 0f, 0f));

        sailPivot = BuildSail(chassis, m);
        return chassis;
    }

    // ------------------------------------------------------------ rig
    /// <summary>
    /// The wingsail on its own pivot. The pivot's local Y rotation IS the trim angle, so the
    /// visual and <see cref="SailPhysics"/> read the same number. Wing parts are named so the
    /// rig can find the flap if it ever wants to animate camber.
    /// </summary>
    private static Transform BuildSail(Transform chassis, LunarRoverArt.RoverMats m)
    {
        GameObject pivotGo = new GameObject("Sail_Pivot");
        pivotGo.transform.SetParent(chassis, false);
        pivotGo.transform.localPosition = new Vector3(0f, 0.58f, -0.35f);
        Transform pivot = pivotGo.transform;

        // Mast. Sits on the pivot's centreline and does not rotate visually with it, so it is
        // parented to the chassis instead - a mast that swung round with its own sail would read
        // as broken.
        Cyl(chassis, "Fav_SailMast", m.secondary, 2.5f, 0.06f, new Vector3(0f, 1.7f, -0.35f), Vector3.zero);
        Cyl(pivot, "Fav_SailCap", m.accent, 0.16f, 0.12f, new Vector3(0f, 2.9f, 0f), Vector3.zero);

        // The wing itself: tall in Y, chord along Z, thin in X. A rigid symmetric airfoil with
        // a double-sided solar skin, so it reads as a wing from either tack.
        Box(pivot, "Fav_SailRib", m.primary, new Vector3(0.05f, 2.2f, 1.05f), new Vector3(0f, 1.75f, 0f));
        Box(pivot, "Fav_SailSolarP", m.accent, new Vector3(0.02f, 2.1f, 0.95f), new Vector3(0.045f, 1.75f, 0f));
        Box(pivot, "Fav_SailSolarN", m.accent, new Vector3(0.02f, 2.1f, 0.95f), new Vector3(-0.045f, 1.75f, 0f));
        // Battens across the span, the way a real rigid wing is stiffened.
        for (int i = -2; i <= 2; i++)
            Box(pivot, "Fav_SailBatten", m.secondary, new Vector3(0.07f, 0.05f, 1.05f), new Vector3(0f, 1.75f + i * 0.45f, 0f));

        // Trailing flap on its own child so the rig can deflect it; neutral at build time.
        Transform flap = new GameObject("Fav_SailFlap").transform;
        flap.SetParent(pivot, false);
        flap.localPosition = new Vector3(0f, 1.75f, -0.62f);
        Box(flap, "Fav_SailFlapBlade", m.accent, new Vector3(0.04f, 2.05f, 0.34f), Vector3.zero);

        return pivot;
    }

    /// <summary>Wheel visuals, matching the physics wheel size used by the buggy rig.</summary>
    public static Transform BuildWheelVisual(Transform parent, Vector3 localPos, LunarRoverArt.RoverMats m, float radius, float width)
        => LunarRoverArt.BuildWheelVisual(parent, localPos, m, radius, width);
}
