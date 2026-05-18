using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    [CreateAssetMenu(
        fileName = "DAT_AIVisionSource_", 
        menuName = "RPG/AI/Vision Source Config")]
    public class DataConfigVisioncastSource : ScriptableObject
    {
        public LayerMask BroadphaseLayermask;
        public LayerMask RaycastLayermask;
        public float VisionRange;
        public float FieldOfView;
    }
}