using UnityEngine;

namespace Gadd420
{
    public enum RenderPipelineType { None = 0, BRP = 1, URP = 2, HDRP = 3 }
    
    public class VehiclePipeline : MonoBehaviour
    {
        [Tooltip("Render pipeline this vehicle's materials are set up for. Leave None on base rigs that should not be spawned directly.")]
        public RenderPipelineType pipeline = RenderPipelineType.None;
    }
}
