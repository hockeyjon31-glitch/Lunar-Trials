using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Io survey volcanoes STAY ALIVE: mostly quiet little plumes from their craters, but every
/// now and then ONE of them erupts violently, ejecting a WIDE FIELD of lava - typically 60-100
/// metres across, blown out over the flank by hard sideways flings - with far more globules
/// than the quiet plumes. A HUD warning ("VOLCANIC ERUPTION INBOUND") and storm alert telegraph
/// the big blast a few seconds before the globule plume goes wide and high, so the player has
/// time to break off the detour. Touching any globule chips the hull (same LavaGlobule as the
/// race surges). The crater that erupts is always the one NEAREST to the buggy, so the lava
/// lands at whatever detour you are actually working rather than a random volcano far away.
/// </summary>
public class IoVolcanoEruptor : MonoBehaviour
{
    [Tooltip("Crater-top launch point (world space) the globules spray from.")]
    public Vector3 vent;
    [Tooltip("Radius of the crater pool; normal plumes scatter across this disc and violent ones far beyond it.")]
    public float poolRadius = 5f;
    [Tooltip("Hot lava body colour.")]
    public Color lavaColor = new Color(1f, 0.35f, 0.05f);
    [Tooltip("Emissive overdrive colour for the molten glow.")]
    public Color lavaGlow = new Color(1f, 0.35f, 0.05f);

    [Header("Major eruption")]
    [Tooltip("Seconds the HUD warning stays up before the violent blast lands.")]
    public float warningTime = 3.4f;
    [Tooltip("How many seconds the violent burst keeps throwing wide globules (the burst count "
            + "effectively sets the real duration, this is the nominal floor).")]
    public float majorDuration = 9.5f;
    [Tooltip("Lateral scatter factor: violent eruptions spread this far beyond the pool radius.")]
    public float majorSpreadFactor = 5.0f;
    [Tooltip("How hard eruptions fling lava OUT OVER THE FLANK (lateral velocity scale): normal plumes a little past the crater, violent ones hurl molten rock clear over the ridge.")]
    public float smallSideways = 1.6f;
    public float majorSideways = 4.0f;

    private enum EruptMode { Idle, Warning, Major, Small }

    private EruptMode mode = EruptMode.Idle;
    private BuggyController buggy;
    private OxygenManager oxygen;
    private HUD hud;
    private float nextErupt;
    private float phaseEnd;
    private float nextRumbleAt;
    private int burstsLeft;
    private float burstAcc;

    // Only one volcano can be mid-major at a time (warnings + blasts never overlap), and it
    // stays a while before the whole world can blow again.
    private static bool majorActive;
    private static float nextMajorAt;

    // Every crater registers so an eruption can always pick the one nearest the buggy.
    private static readonly List<IoVolcanoEruptor> all = new List<IoVolcanoEruptor>();

    private void Awake()
    {
        all.Add(this);
    }

    private void OnDestroy()
    {
        all.Remove(this);
    }

    private void Start()
    {
        nextErupt = Time.time + Random.Range(2f, 6f);
        buggy = FindAnyObjectByType<BuggyController>();
        oxygen = OxygenManager.Instance;
        hud = FindAnyObjectByType<HUD>();
    }

    private void Update()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (buggy == null || !buggy.inputEnabled) return;
        if (oxygen == null) oxygen = OxygenManager.Instance;

        // The volcano stands down while a run stops being drivable (game over, win banner,
        // boarding cinematic) so the spray never lands on a dead run.
        if (oxygen != null && (oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive)) return;

        switch (mode)
        {
            case EruptMode.Idle:
                if (Time.time >= nextErupt)
                {
                    // The nearest ready crater takes the blast: everyone else defers briefly so
                    // the lava lands at the detour the player is actually at, never elsewhere.
                    if (!IsNearestReady())
                    {
                        nextErupt = Time.time + 0.5f;
                        break;
                    }
                    if (!majorActive && Time.time >= nextMajorAt)
                    {
                        // Roughly one in four idle volcanoes breaking -> a full-world eruption.
                        if (Random.value < 0.35f) EnterWarning();
                        else EnterSmall();
                    }
                    else
                    {
                        EnterSmall();
                    }
                }
                break;

            case EruptMode.Warning:
                if (Time.time >= phaseEnd)
                {
                    BeginMajor();
                }
                break;

            case EruptMode.Major:
                PumpBursts(0.09f, major: true);
                // Sustained rumble: every few seconds a low lava-groan rises from the vent with
                // a screen bump, so the wide eruption reads as an ongoing mountain shaking.
                if (Time.time >= nextRumbleAt)
                {
                    nextRumbleAt = Time.time + Random.Range(2.4f, 3.2f);
                    RoverSounds.PlayEruptRumble(vent, 1f);
                    if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.3f);
                }
                if (burstsLeft <= 0)
                {
                    EndMajor();
                }
                break;

            case EruptMode.Small:
                PumpBursts(0.12f, major: false);
                if (burstsLeft <= 0)
                {
                    mode = EruptMode.Idle;
                    burstAcc = 0f;
                    nextErupt = Time.time + Random.Range(4f, 9f);
                }
                break;
        }
    }

    /// <summary>True when THIS volcano is the closest READY one to the buggy. Every crater
    /// defers its eruption to whichever is nearest, so lava always lands at the detour the
    /// player is actually working instead of a random volcano half a world away.</summary>
    private bool IsNearestReady()
    {
        if (all.Count <= 1 || buggy == null) return true;
        float myDist = (transform.position - buggy.transform.position).sqrMagnitude;
        for (int i = 0; i < all.Count; i++)
        {
            IoVolcanoEruptor other = all[i];
            if (other == null || other == this) continue;
            if (other.mode != EruptMode.Idle) continue; // busy craters don't compete
            float d = (other.transform.position - buggy.transform.position).sqrMagnitude;
            if (d < myDist) return false;
        }
        return true;
    }

    private void EnterWarning()
    {
        mode = EruptMode.Warning;
        majorActive = true; // reserve the world's blast slot during the telegraph too
        phaseEnd = Time.time + warningTime;
        if (hud != null) hud.ShowWarning("VOLCANIC ERUPTION INBOUND");
        RoverSounds.PlayStormAlert();
        if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.5f);
    }

    private void BeginMajor()
    {
        mode = EruptMode.Major;
        burstAcc = 0f;
        // A violent eruption dumps roughly DOUBLE the globules of the old burst so the wide
        // 60-100m lava field stays convincingly loaded with molten rock to avoid.
        burstsLeft = Random.Range(88, 118);
        phaseEnd = Time.time + majorDuration;
        nextRumbleAt = Time.time + Random.Range(1.6f, 2.4f);
        if (hud != null) hud.HideWarning();
        RoverSounds.PlayVolcanoExplosion(vent, 1f);   // the opening blast
        RoverSounds.PlayEruptRumble(vent, 1f);    // and the rumble that follows it
        if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(1f);
    }

    private void EndMajor()
    {
        mode = EruptMode.Idle;
        burstAcc = 0f;
        majorActive = false;
        nextMajorAt = Time.time + Random.Range(18f, 30f); // the world catches its breath
        nextErupt = Time.time + Random.Range(3f, 6f);
    }

    private void EnterSmall()
    {
        mode = EruptMode.Small;
        burstAcc = 0f;
        burstsLeft = Random.Range(6, 11);
        // Small plumes still rumble, just softer than the violent blast.
        RoverSounds.PlayEruptRumble(vent, 0.45f);
        if (BuggyCamera.Instance != null) BuggyCamera.Instance.AddShake(0.3f);
    }

    private void PumpBursts(float interval, bool major)
    {
        burstAcc += Time.deltaTime;
        while (burstsLeft > 0 && burstAcc >= interval)
        {
            LaunchGlobule(major);
            burstsLeft--;
            burstAcc -= interval;
        }
    }

    private void LaunchGlobule(bool major)
    {
        float spread = major ? poolRadius * majorSpreadFactor : poolRadius * 0.55f;
        float upMin = major ? 10f : 7f;
        float upMax = major ? 20f : 13f;
        float sizeMin = major ? 0.7f : 0.5f;
        float sizeMax = major ? 2.0f : 1.1f;
        float dmgMin = major ? 3f : 2f;
        float dmgMax = major ? 6f : 4f;
        float sideways = major ? majorSideways : smallSideways;

        Vector3 p = vent + new Vector3(
            Random.Range(-spread, spread),
            Random.Range(0f, 0.5f),
            Random.Range(-spread, spread));
        LavaGlobule.Spawn(p, sizeMin, sizeMax, upMin, upMax, lavaColor, lavaGlow,
            Random.Range(dmgMin, dmgMax), sideways, LavaArt.MoltenFx());
        SpawnSmokePuff(major);
    }

    /// <summary>A short-lived dark ash puff that rises and fades near the crater vent so
    /// eruptions billow volcanic smoke as well as throw lava.</summary>
    private void SpawnSmokePuff(bool major)
    {
        Vector3 pos = vent + new Vector3(
            Random.Range(-poolRadius * 0.4f, poolRadius * 0.4f),
            Random.Range(1f, 3f),
            Random.Range(-poolRadius * 0.4f, poolRadius * 0.4f));

        GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        puff.name = "VolcanicSmoke";
        puff.transform.position = pos;
        float scale = Random.Range(major ? 5f : 3f, major ? 8f : 5f);
        puff.transform.localScale = new Vector3(scale, scale * 0.6f, scale);
        DestroyImmediate(puff.GetComponent<Collider>());
        Material mat = new Material(Shader.Find("Standard"));
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.color = new Color(0.14f, 0.12f, 0.1f, 0.32f);
        puff.GetComponent<MeshRenderer>().sharedMaterial = mat;
        SmokePuffLift lift = puff.AddComponent<SmokePuffLift>();
        lift.mat = mat;
        lift.baseAlpha = 0.32f;
        lift.lifetime = Random.Range(1.8f, 2.6f);
    }
}

/// <summary>Rises slowly and fades out so the volcano vent billows ash rather than popping.</summary>
public class SmokePuffLift : MonoBehaviour
{
    public Material mat;
    public float baseAlpha = 0.32f;
    public float lifetime = 2.2f;
    public float liftSpeed = 2.4f;
    private float age;

    private void Update()
    {
        age += Time.deltaTime;
        transform.position += Vector3.up * liftSpeed * Time.deltaTime;
        if (mat != null)
        {
            float k = Mathf.Clamp01(1f - age / lifetime);
            Color c = mat.color;
            c.a = baseAlpha * k;
            mat.color = c;
        }
        if (age >= lifetime) Destroy(gameObject);
    }
}