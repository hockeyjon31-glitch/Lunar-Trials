using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gadd420
{
    /// <summary>
    /// Demo-scene vehicle carousel: spawns one vehicle at a time and cycles
    /// through the available prefabs with the mouse scroll wheel.
    ///
    /// A vehicle is any prefab carrying a <see cref="VehiclePipeline"/>
    /// component. The list is gathered on Awake by scanning the project, so
    /// vehicles shipped in separate packages are picked up automatically,
    /// then filtered to the prefabs whose declared pipeline matches the
    /// <see cref="pipeline"/> this scene instance is set to. Prefabs declared
    /// as None are never spawned.
    ///
    /// Prefab references are intentionally never serialized: storing them in
    /// the scene would make every vehicle of every pipeline a dependency of
    /// the scene, dragging foreign-pipeline assets into package exports.
    ///
    /// The scene's <see cref="ThirdPersonCamera"/> is retargeted to every
    /// vehicle that gets spawned.
    /// </summary>
    public class VehicleSelector : MonoBehaviour
    {
        [Header("Pipeline")]
        [Tooltip("Render pipeline this demo scene is built for. Only vehicles declaring the same pipeline are spawned.")]
        public RenderPipelineType pipeline = RenderPipelineType.None;

        [Header("Spawning")]
        [Tooltip("Where the first vehicle appears. Defaults to this transform.")]
        public Transform spawnPoint;
        [Tooltip("Extra height above the ground hit point so wheel colliders settle cleanly.")]
        public float spawnHeightOffset = 0.35f;

        [Header("Camera")]
        [Tooltip("Camera that follows the spawned vehicle. Auto-found if left empty.")]
        public ThirdPersonCamera followCamera;

        [Header("Input")]
        [Tooltip("Minimum time between two scroll switches.")]
        public float switchCooldown = 0.3f;

        [Header("HUD")]
        public bool showVehicleName = true;

        // Deliberately not serialized — see class summary.
        private readonly List<VehiclePipeline> vehiclePrefabs = new List<VehiclePipeline>();

        private readonly List<GameObject> activePrefabs = new List<GameObject>();
        private GameObject currentInstance;
        private int currentIndex;
        private float lastSwitchTime = float.NegativeInfinity;

        private void Awake()
        {
            FindVehiclePrefabs();
            if (!spawnPoint) spawnPoint = transform;
            if (!followCamera) followCamera = FindFirstObjectByType<ThirdPersonCamera>();

            BuildActivePrefabList();
        }

        private void Start()
        {
            if (activePrefabs.Count > 0)
                Spawn(0);
        }

        private void Update()
        {
            float scroll = Input_Compat.GetScrollDelta();
            if (scroll == 0f)
                return;

            if (scroll > 0f) SelectNext();
            else SelectPrevious();
        }

        public void SelectNext() => SwitchBy(1);
        public void SelectPrevious() => SwitchBy(-1);

        private void SwitchBy(int direction)
        {
            if (activePrefabs.Count < 2)
                return;

            if (Time.unscaledTime - lastSwitchTime < switchCooldown)
                return;

            lastSwitchTime = Time.unscaledTime;
            Spawn((currentIndex + direction + activePrefabs.Count) % activePrefabs.Count);
        }

        private void Spawn(int index)
        {
            currentIndex = index;

            Vector3 position = spawnPoint ? spawnPoint.position : transform.position;
            float yaw = spawnPoint ? spawnPoint.eulerAngles.y : 0f;

            if (currentInstance)
            {
                // Keep the spot the player drove to, but reset roll/pitch so a
                // crashed bike is replaced by an upright one.
                position = currentInstance.transform.position;
                yaw = currentInstance.transform.eulerAngles.y;

                // Deactivate before the ground raycast so it can't hit the old
                // vehicle's colliders (Destroy is deferred to end of frame).
                currentInstance.SetActive(false);
                Destroy(currentInstance);
            }

            if (Physics.Raycast(position + Vector3.up * 100f, Vector3.down, out RaycastHit hit,
                    500f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                position = hit.point;
            }

            GameObject prefab = activePrefabs[currentIndex];
            currentInstance = Instantiate(prefab,
                position + Vector3.up * spawnHeightOffset,
                Quaternion.Euler(0f, yaw, 0f));
            currentInstance.name = prefab.name;

            if (followCamera)
                followCamera.lookAt = currentInstance.transform;
        }

        /// <summary>
        /// Filters <see cref="vehiclePrefabs"/> down to the prefabs whose
        /// declared pipeline matches this scene's <see cref="pipeline"/>.
        /// </summary>
        private void BuildActivePrefabList()
        {
            activePrefabs.Clear();

            if (pipeline == RenderPipelineType.None)
            {
                Debug.LogWarning("[VehicleSelector] Pipeline is set to None — set the pipeline this scene targets in the inspector.", this);
                return;
            }

            foreach (VehiclePipeline vehicle in vehiclePrefabs)
            {
                if (vehicle && vehicle.pipeline == pipeline)
                    activePrefabs.Add(vehicle.gameObject);
            }

            activePrefabs.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            if (activePrefabs.Count == 0)
                Debug.LogWarning($"[VehicleSelector] No vehicle prefabs declare the {pipeline} pipeline. " +
                                 "Check the VehiclePipeline component on your vehicle prefabs.", this);
        }

        private void OnGUI()
        {
            if (!showVehicleName || !currentInstance || activePrefabs.Count == 0)
                return;

            string text = activePrefabs.Count > 1
                ? $"< {currentInstance.name} >  ({currentIndex + 1}/{activePrefabs.Count})  —  scroll to change vehicle"
                : currentInstance.name;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerCenter,
                fontSize = 16
            };

            var rect = new Rect(0f, Screen.height - 40f, Screen.width, 30f);

            GUI.color = Color.black;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
            GUI.color = Color.white;
            GUI.Label(rect, text, style);
        }

        /// <summary>
        /// Fills <see cref="vehiclePrefabs"/> with every prefab in the
        /// project carrying a <see cref="VehiclePipeline"/> component.
        /// Discovery goes through the asset database, so it is editor-only;
        /// this demo tool stays inert in builds.
        /// </summary>
        private void FindVehiclePrefabs()
        {
            vehiclePrefabs.Clear();

#if UNITY_EDITOR
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                VehiclePipeline vehicle = prefab.GetComponent<VehiclePipeline>();
                if (vehicle != null)
                    vehiclePrefabs.Add(vehicle);
            }
#endif
        }
    }
}
