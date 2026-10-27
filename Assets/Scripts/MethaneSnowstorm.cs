using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Titan's Harvester (survey) blizzard: a continuous, heavy methane snow that never clears -
/// heavier than even the race course blizzard. Puffy white flakes drift and sway over the whole
/// field of view while the run is drivable, with a global flake budget so the world never spawns
/// enough to hurt the frame rate. Boarded freezing air (no roof) is handled by the survey lakes;
/// this component is the visual + ambient dwell of the storm itself.
/// </summary>
public class MethaneSnowstorm : MonoBehaviour
{
    [Header("References (found automatically if left null)")]
    public BuggyController buggy;
    public MoonTerrainGen terrain;
    public OxygenManager oxygen;

    [Header("Storm density")]
    [Tooltip("How many flakes each dump throws around the buggy. Kept modest so the live budget "
            + "never runs out mid-fall - every flake must be allowed to reach the ground.")]
    public int burstSize = 100;
    [Tooltip("Gap between dumps (seconds): lower = more continuous.)")]
    public float burstMin = 0.55f;
    public float burstMax = 0.9f;
    [Tooltip("Hard ceiling on live flakes; oldest are shunted out ONLY if the world really "
            + "overshoots, so the flakes always finish their fall under normal load.")]
    public int maxLiveFlakes = 1200;
    [Tooltip("Flake drift/sway speed (m/s vertical).")]
    public float fallMin = 2.5f;
    public float fallMax = 4.5f;

    private readonly List<Flake> flakes = new List<Flake>();
    private Material sharedMat;
    private float nextBurstAt;

    private sealed class Flake
    {
        public GameObject go;
        public float floor;
        public float fallSpeed;
        public float sway;
        public float phase;
    }

    private void Start()
    {
        nextBurstAt = Time.time + 0.4f;
        sharedMat = new Material(Shader.Find("Standard"));
        sharedMat.color = new Color(0.92f, 0.98f, 1f);
        sharedMat.SetFloat("_Metallic", 0f);
        sharedMat.SetFloat("_Glossiness", 0.85f);
        sharedMat.EnableKeyword("_EMISSION");
        sharedMat.SetColor("_EmissionColor", new Color(0.92f, 0.98f, 1f) * 0.35f);
        // GPU instancing keeps a skyful of identical flakes at one draw call, so the budget
        // can be high enough to let them ALL fall to the ground.
        sharedMat.enableInstancing = true;
    }

    private void Update()
    {
        if (buggy == null) buggy = FindAnyObjectByType<BuggyController>();
        if (buggy == null || !buggy.inputEnabled) return;
        if (oxygen == null) oxygen = OxygenManager.Instance;
        if (oxygen != null && (oxygen.gameOver || oxygen.reachedEnd || oxygen.endSequenceActive)) return;
        if (terrain == null) terrain = FindAnyObjectByType<MoonTerrainGen>();
        if (terrain == null) return;

        if (Time.time >= nextBurstAt)
        {
            nextBurstAt = Time.time + Random.Range(burstMin, burstMax);
            SpawnBurst();
        }
        MoveFlakes();
    }

    private void SpawnBurst()
    {
        for (int k = 0; k < burstSize; k++)
        {
            if (flakes.Count >= maxLiveFlakes) PruneOne();
            Vector3 p = buggy.transform.position + new Vector3(Random.Range(-24f, 24f), 0f, Random.Range(-16f, 30f));
            float groundY = terrain.SampleHeight(p);
            if (groundY <= -100f) continue;

            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "SnowFlake";
            // Drops from a LOW altitude band so the flakes hang right around the rover and
            // visibly settle on the ground instead of dissolving high up.
            s.transform.position = new Vector3(p.x, groundY + Random.Range(10f, 22f), p.z);
            float size = Random.Range(0.15f, 0.3f);
            s.transform.localScale = new Vector3(size, size, size);
            s.GetComponent<MeshRenderer>().sharedMaterial = sharedMat;
            DestroyImmediate(s.GetComponent<Collider>());

            flakes.Add(new Flake
            {
                go = s,
                floor = groundY,
                fallSpeed = Random.Range(fallMin, fallMax),
                sway = Random.Range(1.2f, 2.4f),
                phase = Random.Range(0f, Mathf.PI * 2f),
            });
        }
    }

    private void MoveFlakes()
    {
        for (int i = flakes.Count - 1; i >= 0; i--)
        {
            Flake f = flakes[i];
            if (f.go == null) { flakes.RemoveAt(i); continue; }
            Vector3 pos = f.go.transform.position;
            pos.y -= f.fallSpeed * Time.deltaTime;
            f.phase += Time.deltaTime * 3.2f;
            pos.x += Mathf.Sin(f.phase) * f.sway * Time.deltaTime;
            f.go.transform.position = pos;
            if (pos.y <= f.floor + 0.3f)
            {
                Destroy(f.go);
                flakes.RemoveAt(i);
            }
        }
    }

    private void PruneOne()
    {
        if (flakes.Count == 0) return;
        Flake f = flakes[0];
        if (f.go != null) Destroy(f.go);
        flakes.RemoveAt(0);
    }
}