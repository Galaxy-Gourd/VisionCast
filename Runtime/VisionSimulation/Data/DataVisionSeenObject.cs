using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// Refined data describing "seen" object details
    /// </summary>
    public struct DataVisionSeenObject
    {
        public Collider ResultObject;
        public bool IsVisible;
        public bool JustBecameVisible;
        public float Distance;
        public float Angle;
        /// <summary>
        /// Number of sample points with a clear line of sight to the object this update
        /// </summary>
        public int VisiblePointCount;
        /// <summary>
        /// Total sample points tested against the object this update
        /// </summary>
        public int SampleCount;
        /// <summary>
        /// Fraction of sample points with a clear line of sight, in [0, 1]. Drives stealth exposure -
        /// denser sampling (see <see cref="VisionSampleMode"/>) yields a smoother value.
        /// </summary>
        public float Visibility;
    }
}