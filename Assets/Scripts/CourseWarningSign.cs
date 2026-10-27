using UnityEngine;

/// <summary>
/// A roadside warning signpost, built at the roadside just BEFORE a jump obstacle so the player
/// gets told rather than ambushed: "GAP AHEAD!" on the Ridge's crevices, "HURDLE AHEAD!" on the
/// canyon's rock lines.
///
/// Styled off the checkpoint gate arches (same truss steel and glow materials, via
/// RaceGateManager) so it reads as course furniture rather than a floating UI label, but a
/// signpost rather than an arch - it spans nothing, it just faces the driver.
///
/// Everything here is VISUAL ONLY. Every part has its collider stripped, so the sign can stand
/// on the edge of the driving line without ever being something to crash into or to have to
/// drive around; the obstacle it warns about is the thing the player actually has to deal with.
/// </summary>
public static class CourseWarningSign
{
    /// <summary>Half-width of the board (metres) - wide enough for the longest label.</summary>
    private const float BoardHalfWidth = 3.6f;
    private const float BoardHeight = 2.3f;
    private const float PostHeight = 5.2f;

    /// <summary>Warning amber. Deliberately not the gate arches' amber so a sign never reads as
    /// an uncollected checkpoint.</summary>
    private static readonly Color WarnColor = new Color(1f, 0.72f, 0.12f);

    /// <summary>
    /// Builds one signpost standing at <paramref name="position"/> (already the right height
    /// for the ground under it), facing back down the course at the oncoming buggy.
    /// </summary>
    public static void Build(Transform parent, Vector3 position, float yaw, string text)
    {
        GameObject root = new GameObject("WarningSign_" + text.Replace(" ", string.Empty));
        root.transform.SetParent(parent, false);
        // Face the driver: the course runs toward +Z, so a sign's readable face turns to -Z.
        root.transform.position = position;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        Material truss = RaceGateManager.MakeTruss();
        Material glow = RaceGateManager.MakeGlow(WarnColor, 1.5f);

        // Post.
        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
        post.name = "SignPost";
        post.transform.SetParent(root.transform, false);
        post.transform.localScale = new Vector3(0.42f, PostHeight, 0.42f);
        post.transform.localPosition = new Vector3(0f, PostHeight * 0.5f, 0f);
        post.GetComponent<MeshRenderer>().sharedMaterial = truss;
        Object.Destroy(post.GetComponent<Collider>());

        // Board.
        GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "SignBoard";
        board.transform.SetParent(root.transform, false);
        board.transform.localScale = new Vector3(BoardHalfWidth * 2f, BoardHeight, 0.3f);
        board.transform.localPosition = new Vector3(0f, PostHeight - BoardHeight * 0.5f, 0f);
        board.GetComponent<MeshRenderer>().sharedMaterial = truss;
        Object.Destroy(board.GetComponent<Collider>());

        // Glowing warning strips down each edge of the board, and a hazard chevron on each
        // corner. Cheap geometry, but it is what makes the board read as a warning from far
        // away and from behind, where the text is too small to make out.
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "SignStrip";
            strip.transform.SetParent(root.transform, false);
            strip.transform.localScale = new Vector3(0.34f, BoardHeight, 0.42f);
            strip.transform.localPosition = new Vector3(
                side * (BoardHalfWidth - 0.17f), PostHeight - BoardHeight * 0.5f, 0f);
            strip.GetComponent<MeshRenderer>().sharedMaterial = glow;
            Object.Destroy(strip.GetComponent<Collider>());

            for (int c = 0; c < 2; c++)
            {
                GameObject chevron = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chevron.name = "SignChevron";
                chevron.transform.SetParent(root.transform, false);
                chevron.transform.localScale = new Vector3(1.15f, 0.34f, 0.42f);
                chevron.transform.localPosition = new Vector3(
                    side * (BoardHalfWidth - 0.95f),
                    PostHeight - BoardHeight * 0.5f + (c == 0 ? 0.62f : -0.62f),
                    0f);
                chevron.transform.localRotation = Quaternion.Euler(0f, 0f, side * 38f);
                chevron.GetComponent<MeshRenderer>().sharedMaterial = glow;
                Object.Destroy(chevron.GetComponent<Collider>());
            }
        }

        // Label, floated just in front of the board and turned to face the driver.
        GameObject labelGo = new GameObject("SignLabel");
        labelGo.transform.SetParent(root.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, PostHeight - BoardHeight * 0.5f, -0.22f);
        labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        TextMesh tm = labelGo.AddComponent<TextMesh>();
        tm.font = HUD.ResolveDefaultFont();
        tm.text = text;
        tm.fontSize = 48;
        tm.characterSize = 0.019f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.96f, 0.86f, 1f);
        MeshRenderer labelMr = labelGo.GetComponent<MeshRenderer>();
        if (labelMr != null && tm.font != null) labelMr.sharedMaterial = tm.font.material;

        // Skyward beacon above the board, matching the gate arches: this is what makes the
        // warning visible from the far side of a straight, before the text is legible.
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "SignBeacon";
        beam.transform.SetParent(root.transform, false);
        beam.transform.localScale = new Vector3(0.45f, 40f, 0.45f);
        beam.transform.localPosition = new Vector3(0f, PostHeight + 20f, 0f);
        beam.GetComponent<MeshRenderer>().sharedMaterial = RaceGateManager.MakeGlow(WarnColor, 0.7f);
        Object.Destroy(beam.GetComponent<Collider>());
    }
}