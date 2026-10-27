using UnityEngine;

/// <summary>
/// Asks for a world's Milky Way night sky. Safe to call before the camera exists: the request is
/// parked on a <see cref="NightSkyDome"/> that builds itself on the following frame. No-op unless
/// the world declares a sky panorama (see <see cref="LevelDefinition.skyTexture"/>).
/// </summary>
public static class NightSky
{
    /// <summary>Parks a starscape dome for <paramref name="def"/>. No-op unless the world has one.</summary>
    public static void Build(LevelDefinition def)
    {
        if (def == null || string.IsNullOrEmpty(def.skyTexture)) return;
        if (GameObject.Find("NightSkyDome") != null) return; // already built for this scene

        Texture2D tex = Resources.Load<Texture2D>(def.skyTexture);
        if (tex == null)
        {
            Debug.LogWarning("NightSky: panorama '" + def.skyTexture
                             + "' not found in Assets/Resources; black night sky kept.");
            return;
        }

        GameObject go = new GameObject("NightSkyDome");
        go.AddComponent<NightSkyDome>().Configure(tex, def.skyYaw, def.skyPitch);
    }
}

/// <summary>
/// The world's night-star backdrop (the Milky Way panorama on the Moon). One huge inverted sphere
/// that follows the camera's POSITION but keeps its own fixed world heading, so the stars stay
/// put as the rover turns. The sphere is Unlit, so the level's distance fog cannot wash it out,
/// and it sits inside the camera's far plane but far beyond any terrain or horizon backdrop.
/// The texture is latitude-longitude: the image's top edge is the zenith and its centre row is
/// the horizon, which is why the material flips V - plain Unlit/Texture mapping on the built-in
/// sphere runs image-top to the bottom pole. The dome may then be PITCHED (see
/// <see cref="LevelDefinition.skyPitch"/>) so the panorama's own centre band - often the
/// photographic subject, like the Milky Way's galactic plane - lifts off the terrain horizon. A
/// NEGATIVE pitch tips that band up into the low forward sky the chase camera frames; a large
/// positive pitch pushes it behind and overhead, out of the forward view entirely.
/// </summary>
public class NightSkyDome : MonoBehaviour
{
    // Dome diameter in world units: radius 6400 comfortably inside the 6500m camera far plane
    // and, moved onto the camera each frame, always wider than the reachable terrain, so the
    // horizon is starfield, never a dome edge.
    private const float DomeDiameter = 12800f;

    private Texture2D sky;
    private float yaw;
    private float pitch;
    private Transform cam;

    /// <summary>Hands over the panorama texture and the fixed world heading and tilt of the sky.</summary>
    public void Configure(Texture2D t, float skyYaw, float skyPitch)
    {
        sky = t;
        yaw = skyYaw;
        pitch = skyPitch;
    }

    private void Start()
    {
        // Deferred to here on purpose: BuildSkyAndLighting runs before BuildCamera in the Survival
        // path, so a camera looked up on the previous line can still be null.
        Camera camCamera = Camera.main;
        if (sky == null || camCamera == null)
        {
            Debug.LogWarning("NightSkyDome: no texture or camera; sky left as-is. tex="
                             + (sky == null ? "null" : sky.name) + " cam=" + (camCamera == null ? "null" : camCamera.name));
            Destroy(gameObject);
            return;
        }
        cam = camCamera.transform;
        transform.position = cam.position;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        MeshFilter mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = SphereMesh();

        // Unlit/Texture is force-included in the WebGL build for the horizon backdrops, so the
        // dome needs no build-script change to survive shader stripping.
        Material mat = new Material(Shader.Find("Unlit/Texture"));
        mat.mainTexture = sky;
        // Latitude-longitude panorama: image top = zenith, image centre row = horizon. The built-in
        // sphere maps v=0 to the bottom pole, so flip V to stand the sky the right way up. Draw
        // both faces as belt-and-braces with the mirrored scale below.
        mat.mainTextureScale = new Vector2(1f, -1f);
        mat.mainTextureOffset = new Vector2(0f, 1f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

        MeshRenderer mr = gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // One line of self-diagnosis, mirroring MistySkyDome: if the sky is still black, this
        // says which piece is at fault (texture, shader, frustum or the camera far plane).
        Debug.Log("NightSkyDome: yaw=" + yaw + " pitch=" + pitch
                  + " cam=" + camCamera.name + " far=" + camCamera.farClipPlane
                  + " shader=" + (mat.shader != null ? mat.shader.name : "NULL")
                  + " tex=" + sky.width + "x" + sky.height
                  + " topPixel=" + (sky.isReadable ? sky.GetPixel(sky.width / 2, 2).ToString() : "notReadable")
                  + " boundsR=" + mr.bounds.extents.magnitude
                  + " inFrustum=" + GeometryUtility.TestPlanesAABB(
                      GeometryUtility.CalculateFrustumPlanes(camCamera), mr.bounds));

        // Mirrored on X so the sphere's inward-facing side is the side that renders: a negative
        // scale flips the triangle winding outright, which is more dependable than relying on a
        // built-in shader exposing _Cull.
        transform.localScale = new Vector3(-DomeDiameter, DomeDiameter, DomeDiameter);
    }

    private void LateUpdate()
    {
        if (cam != null) transform.position = cam.position; // slide under the rover, keep the world heading
    }

    /// <summary>Unity's built-in sphere primitive, built here rather than via CreatePrimitive so
    /// the dome carries no collider and no default Standard material to clean up.</summary>
    private static Mesh SphereMesh()
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(temp);
        return mesh;
    }
}