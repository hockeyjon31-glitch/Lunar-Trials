using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 3D showroom for the Garage: a slowly revolving model of the EQUIPPED vehicle - the Apollo
/// Lunar Roving Vehicle (via LunarRoverArt) or the Mars Rover (via MarsRoverArt), the same
/// procedural bodies the game drives - rendered into a RenderTexture and shown as a RawImage
/// in the menu. The selected/hovered upgrade glows its part:
///   engine -> aluminum frame, boost -> gold foil bay,
///   oxygen -> crew suits / PLSS, hull -> secondary hull plating,
///   jump -> rear thruster pods.
/// The Hull Plate tiers tarnish the frame toward forged steel. Call Refresh() after a purchase
/// so tarnish and part visibility follow the new levels. Everything sits on
/// layer 8 so the menu's main camera never sees it.
/// </summary>
public class GaragePreview : MonoBehaviour
{
    private const int PreviewLayer = 8;
    private int previewLayer = PreviewLayer;
    private VehicleSystem.Vehicle previewVehicle;
    private float camDist = 10f;

    private static readonly Color Glow = new Color(0.4f, 1f, 0.78f);
    private static readonly Color Steel = new Color(0.62f, 0.63f, 0.66f);
    private static readonly Color ThrusterBase = new Color(0.34f, 0.35f, 0.39f);

    private readonly List<Material> materials = new List<Material>();
    // The resting colour of every material we glow, captured from the material itself, so
    // un-glowing always restores exactly what was there (never a colour guessed from a preset).
    private readonly Dictionary<Material, Color> baseColor = new Dictionary<Material, Color>();

    private LunarRoverArt.RoverMats lrv;
    private Color primaryOriginal;

    private RenderTexture target;
    private Camera cam;
    private GameObject sceneRoot;
    private GameObject buggyRoot;
    private GameObject thrusterL, thrusterR;
    private Material thrusterMat;
    private string selected = "";

    /// <summary>Creates the RawImage viewport + off-screen showroom. Returns the RawImage.
    /// <paramref name="vehicle"/> picks which vehicle's body to build (null = the equipped
    /// vehicle); <paramref name="layer"/> isolates this showroom's models from every other
    /// preview camera (the garage uses 8, the shop's cards use 9..12) and
    /// <paramref name="camDistance"/> pulls the framing camera closer/further so the model
    /// fills viewports of different aspect ratios.</summary>
    public RawImage Build(Transform parent, Vector2 anchor, Vector2 anchoredPos, Vector2 size,
        VehicleSystem.Vehicle vehicle = null, int layer = PreviewLayer, float camDistance = 10f)
    {
        previewVehicle = vehicle;
        previewLayer = layer;
        camDist = camDistance;

        GameObject imgGo = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
        imgGo.transform.SetParent(parent, false);
        RectTransform rt = imgGo.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        // 1.5x supersample so the showroom stays crisp on screens larger than the 1080p reference.
        target = new RenderTexture(Mathf.RoundToInt(size.x * 1.5f), Mathf.RoundToInt(size.y * 1.5f), 24);
        RawImage raw = imgGo.GetComponent<RawImage>();
        raw.texture = target;
        raw.raycastTarget = false;

        BuildScene();
        return raw;
    }

    private void BuildScene()
    {
        sceneRoot = new GameObject("GarageShowroom");
        SetLayer(sceneRoot.transform);

        GameObject stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stand.name = "Stand";
        stand.transform.SetParent(sceneRoot.transform, false);
        stand.transform.localScale = new Vector3(1.7f, 0.16f, 1.7f);
        stand.transform.localPosition = new Vector3(0f, -1.35f, 0f);
        stand.GetComponent<MeshRenderer>().sharedMaterial = NewSolid(new Color(0.13f, 0.14f, 0.17f));

        BuildBuggy();

        // Apply the showroom layer AFTER everything is created - the preview camera culls
        // exclusively to this layer, so any object built outside the initial SetLayer pass
        // would be invisible to the render texture.
        SetLayer(sceneRoot.transform);

        cam = new GameObject("GaragePreviewCam").AddComponent<Camera>();
cam.gameObject.layer = previewLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.05f, 0.045f, 1f);
        cam.cullingMask = 1 << previewLayer;
        cam.targetTexture = target;
        cam.fieldOfView = 34f;
        Vector3 camDir = new Vector3(5.3f, 1.9f, -8.3f).normalized;
        cam.transform.position = camDir * camDist;
        cam.transform.LookAt(Vector3.zero);
    }

    private void BuildBuggy()
    {
        buggyRoot = new GameObject("Buggy");
        buggyRoot.transform.SetParent(sceneRoot.transform, false);
        buggyRoot.transform.localPosition = new Vector3(0f, -0.6f, 0f);

        // The showroom shows the same procedural body the game drives, for the requested vehicle
        // (null = equipped), in unlit materials (no lights in the showroom). The vehicle's own
        // skin preset is used.
        VehicleSystem.Vehicle v = previewVehicle != null ? previewVehicle : VehicleSystem.Selected;
        bool isRover = v != null && v.id == VehicleSystem.MarsRover.id;
        bool isSprint = v != null && v.id == VehicleSystem.LunarSprint.id;
        bool isSail = v != null && v.id == VehicleSystem.VenusSail.id;
        bool bikePreview = false;
        int skinIndex = VehicleSystem.SkinOfVehicle(v);
        if (isRover)
        {
            lrv = MarsRoverArt.CreateMats(MarsRoverArt.SkinOf(skinIndex), false);
            materials.AddRange(lrv.all);
            primaryOriginal = lrv.primary.color;

            MarsRoverArt.BuildChassis(buggyRoot.transform, lrv);

            const float wy = 0.52f;
            MarsRoverArt.BuildWheelVisual(buggyRoot.transform, new Vector3(1.1f, wy, 1.55f), lrv, 0.55f, 0.35f);
            MarsRoverArt.BuildWheelVisual(buggyRoot.transform, new Vector3(-1.1f, wy, 1.55f), lrv, 0.55f, 0.35f);
            MarsRoverArt.BuildWheelVisual(buggyRoot.transform, new Vector3(1.1f, wy, -1.55f), lrv, 0.55f, 0.35f);
            MarsRoverArt.BuildWheelVisual(buggyRoot.transform, new Vector3(-1.1f, wy, -1.55f), lrv, 0.55f, 0.35f);
        }
        else
        {
            lrv = LunarRoverArt.CreateMats(LunarRoverArt.SkinOf(skinIndex), false);
            materials.AddRange(lrv.all);
            primaryOriginal = lrv.primary.color;

            if (isSprint && GameEdition.IsTrials)
            {
                // LUNAR TRIALS: the Sprint slot previews the dirt-bike model itself,
                // aligned so its wheels rest on the showroom floor (the LRV wheels'
                // contact plane at -0.03). The showroom has no lights, so swap its lit
                // materials to unlit (keeping their colours) - otherwise the bike
                // renders pure black against the backdrop.
                // The asset prefab carries WheelColliders that demand a Rigidbody up the
                // chain the moment they enable (before stripping removes them) - give the
                // showroom root a kinematic one. Harmless: kinematic means nothing ever
                // moves it, and the colliders are stripped moments later.
                Rigidbody guard = buggyRoot.GetComponent<Rigidbody>();
                if (guard == null) guard = buggyRoot.gameObject.AddComponent<Rigidbody>();
                guard.isKinematic = true;

                GameObject bike = TrialsBikeBuilder.InstantiateClean(buggyRoot.transform, buggyRoot.transform.position.y - 0.03f);
                float floorWorldY = buggyRoot.transform.position.y - 0.03f;
                TrialsBikeBuilder.AlignToFloor(bike, floorWorldY);
                Shader unlit = Shader.Find("Unlit/Color");
                foreach (var rend in bike.GetComponentsInChildren<Renderer>(true))
                    foreach (var mm in rend.materials)
                        if (mm.HasProperty("_Color") && unlit != null) mm.shader = unlit;
                bikePreview = true;
            }
            else if (isSprint) LunarRoverArt.BuildSprintChassis(buggyRoot.transform, lrv);
            else if (isSail)
            {
                // The showroom has no wind, so the rig is left off and the wing sits square -
                // but it is still built, so the shop card shows the real vehicle.
                Transform unusedPivot;
                VenusSailArt.BuildChassis(buggyRoot.transform, lrv, out unusedPivot);
            }
            else LunarRoverArt.BuildChassis(buggyRoot.transform, lrv);

            // Static wire-mesh wheels tangent to the showroom floor (no physics here) —
            // skipped for the Trials bike preview (it brings its own wheels).
            if (!bikePreview)
            {
                const float wy = 0.52f;
                LunarRoverArt.BuildWheelVisual(buggyRoot.transform, new Vector3(1.1f, wy, 1.55f), lrv, 0.55f, 0.35f);
                LunarRoverArt.BuildWheelVisual(buggyRoot.transform, new Vector3(-1.1f, wy, 1.55f), lrv, 0.55f, 0.35f);
                LunarRoverArt.BuildWheelVisual(buggyRoot.transform, new Vector3(1.1f, wy, -1.55f), lrv, 0.55f, 0.35f);
                LunarRoverArt.BuildWheelVisual(buggyRoot.transform, new Vector3(-1.1f, wy, -1.55f), lrv, 0.55f, 0.35f);
            }
        }

        // Hop Rockets: two rear thruster pods (nudge these positions to sit on the LRV's tail).
        thrusterMat = NewSolid(ThrusterBase);
        thrusterL = CreatePod("ThrusterL", new Vector3(-0.55f, 0.7f, -2.0f));
        thrusterR = CreatePod("ThrusterR", new Vector3(0.55f, 0.7f, -2.0f));

        // Capture every resting colour BEFORE anything can glow.
        foreach (Material m in materials) Base(m);

        Refresh();
    }

    private GameObject CreatePod(string name, Vector3 pos)
    {
        GameObject pod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pod.name = name;
        pod.transform.SetParent(buggyRoot.transform, false);
        pod.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
        pod.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // cylinder axis along the buggy's Z
        pod.transform.localPosition = pos;
        Collider c = pod.GetComponent<Collider>();
        if (c != null) Destroy(c);
        pod.GetComponent<MeshRenderer>().sharedMaterial = thrusterMat;
        return pod;
    }

    private void Update()
    {
        if (buggyRoot != null)
        {
            buggyRoot.transform.Rotate(Vector3.up, 22f * Time.unscaledDeltaTime, Space.Self);
        }
    }

    /// <summary>Highlights the part for the hovered/selected upgrade row (null clears).</summary>
    public void SelectUpgrade(string id)
    {
        selected = id ?? "";
        UpdatePartVisibility();
        ApplyGlow();
    }

    public void ClearUpgrade()
    {
        selected = "";
        UpdatePartVisibility();
        ApplyGlow();
    }

    /// <summary>Re-reads the saved upgrade levels: call after a purchase so the tarnish and part
    /// visibility match what the player now owns.</summary>
    public void Refresh()
    {
        if (buggyRoot == null) return;

        // Hull Plate tiers tarnish the aluminum frame toward forged steel, mirroring the game.
        // 0.1 per tier so every one of the ten tiers is visibly different (0.12 saturated at 9).
        int hullLevel = UpgradeSystem.LevelOf("hull");
        baseColor[lrv.primary] = Color.Lerp(primaryOriginal, Steel, Mathf.Clamp01(0.1f * hullLevel));

        UpdatePartVisibility();
        ApplyGlow();
    }

    /// <summary>Re-skins the showroom rover (used by the Shop preview to show each vehicle's
    /// variant). Replaces the whole LunarRoverArt material set in place; the thruster trims
    /// keep their own material. The old set is retired and destroyed.</summary>
    public void SetSkin(int skinIndex)
    {
        if (buggyRoot == null) return;
        LunarRoverArt.RoverMats next = LunarRoverArt.CreateMats(LunarRoverArt.SkinOf(skinIndex), false);

        // Swap every chassis/wheel renderer that uses a material from the old set.
        Dictionary<Material, Material> map = new Dictionary<Material, Material>();
        for (int i = 0; i < lrv.all.Count && i < next.all.Count; i++) map[lrv.all[i]] = next.all[i];
        foreach (MeshRenderer mr in buggyRoot.GetComponentsInChildren<MeshRenderer>(true))
        {
            Material m = mr.sharedMaterial;
            if (m != null && map.ContainsKey(m)) mr.sharedMaterial = map[m];
        }

        // Retire the old set (un-track, un-glow, destroy); adopt the new one for cleanup + glow.
        foreach (Material m in lrv.all)
        {
            materials.Remove(m);
            baseColor.Remove(m);
            if (m != null) Destroy(m);
        }
        lrv = next;
        materials.AddRange(lrv.all);
        foreach (Material m in lrv.all) Base(m);
        primaryOriginal = lrv.primary.color;

        SelectUpgrade(selected);
        Refresh();
    }

    private void UpdatePartVisibility()
    {
        // Thruster pods: shown once Hop Rockets are owned (or while previewing them).
        bool showPods = UpgradeSystem.LevelOf("jump") > 0 || selected == "jump";
        if (thrusterL != null) thrusterL.SetActive(showPods);
        if (thrusterR != null) thrusterR.SetActive(showPods);
    }

    private void ApplyGlow()
    {
        SetGlow(lrv.primary, selected == "engine");
        SetGlow(lrv.secondary, selected == "hull");
        SetGlow(lrv.webbing, selected == "oxygen");
        SetGlow(lrv.suit, selected == "oxygen");
        SetGlow(lrv.foil, selected == "boost");
        SetGlow(thrusterMat, selected == "jump");
    }

    private Color Base(Material m)
    {
        Color c;
        if (!baseColor.TryGetValue(m, out c))
        {
            c = m.color;
            baseColor[m] = c;
        }
        return c;
    }

    private void SetGlow(Material m, bool glow)
    {
        if (m == null) return;
        Color resting = Base(m);   // always captured first, so a glowing material never loses its colour
        m.color = glow ? Glow : resting;
    }

    private Material NewSolid(Color c)
    {
        // Shader.Find only works in a build if the shader is included (Project Settings >
        // Graphics > Always Included Shaders). Fall back so the showroom never dies on a null.
        Shader sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        Material m = new Material(sh);
        m.color = c;
        materials.Add(m);   // tracked for cleanup (the stand's material used to leak)
        return m;
    }

    private void SetLayer(Transform t)
    {
        t.gameObject.layer = previewLayer;
        foreach (Transform c in t) SetLayer(c);
    }

    private void OnDestroy()
    {
        if (target != null)
        {
            target.Release();
            Destroy(target);
        }
        if (cam != null) Destroy(cam.gameObject);
        if (sceneRoot != null) Destroy(sceneRoot);
        foreach (Material m in materials)
        {
            if (m != null) Destroy(m);
        }
        materials.Clear();
        baseColor.Clear();
    }
}
