using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// A combined vision target produced by <see cref="VisioncastSourceCompound"/>. Represents one
    /// actor (or a standalone collider) with its visibility aggregated across every child source and,
    /// when actor grouping is enabled, across every collider the actor owns.
    /// </summary>
    public struct DataVisionSeenTarget
    {
        /// <summary>
        /// The owning actor when the underlying colliders are grouped (see VisionTargetsManifest);
        /// null when this target is a standalone collider.
        /// </summary>
        public Component Actor;
        /// <summary>
        /// A representative collider for the target - the most-visible collider this update.
        /// </summary>
        public Collider Collider;
        public bool IsVisible;
        public bool JustBecameVisible;
        /// <summary>Distance of the closest contributing observation.</summary>
        public float Distance;
        /// <summary>Angle of the most-direct contributing observation.</summary>
        public float Angle;
        /// <summary>Sample points with a clear line of sight, summed across contributions.</summary>
        public int VisiblePointCount;
        /// <summary>Total sample points tested, summed across contributions.</summary>
        public int SampleCount;
        /// <summary>Aggregated visibility in [0, 1] (see <see cref="VisibilityAggregation"/>).</summary>
        public float Visibility;
        /// <summary>
        /// Vision-time of the most recent contributing source update (see
        /// <see cref="VisioncastManager.VisionTime"/>). Children update on different cadences under LOD,
        /// so this is the freshest observation folded into the target.
        /// </summary>
        public float LastUpdatedTime;
    }
}
