using System.Collections.Generic;
using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// Data to hold raw results from Visioncaster system.
    /// </summary>
    public struct DataVisioncastResult
    {
        public List<Collider> Objects;
        public List<List<Vector3>> VisiblePoints;
        /// <summary>
        /// Total sample points tested per object (parallel to <see cref="Objects"/>). Acts as the
        /// denominator for a visibility fraction: VisiblePoints[i].Count / SampleCounts[i].
        /// </summary>
        public List<int> SampleCounts;
        public List<float> Distances;
        public List<float> Angles;
    }
}