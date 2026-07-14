using System.Collections.Generic;
using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// Receives and resolves visioncast results into a list of DataVisionSeenObject
    /// </summary>
    public static class VisioncastResultsFilter
    {
        #region RESOLVE

        /// <summary>
        /// Resolves raw results into <paramref name="output"/> (cleared and refilled - no allocation).
        /// <paramref name="previousIndex"/> is a caller-owned reusable map that this method rebuilds
        /// from <paramref name="previous"/> for O(1) "was this seen/visible last update" checks.
        /// </summary>
        public static void Resolve(
            DataVisioncastResult results,
            List<DataVisionSeenObject> previous,
            Dictionary<Collider, int> previousIndex,
            List<DataVisionSeenObject> output)
        {
            output.Clear();

            // Index the previous update's targets by collider (avoids per-object linear scans)
            previousIndex.Clear();
            for (int i = 0; i < previous.Count; i++)
            {
                Collider obj = previous[i].ResultObject;
                if (!ReferenceEquals(obj, null))
                    previousIndex[obj] = i;
            }

            // Cycle through all objects in visioncast
            for (int i = 0; i < results.Objects.Count; i++)
            {
                Collider obj = results.Objects[i];
                if (ReferenceEquals(obj, null))
                    continue;

                int visiblePointCount = results.VisiblePoints[i].Count;
                int sampleCount = results.SampleCounts[i];
                bool isVisible = visiblePointCount > 0;
                float visibility = sampleCount > 0 ? Mathf.Clamp01(visiblePointCount / (float)sampleCount) : 0f;

                // Newly visible if it was absent, or present-but-not-visible, last update
                bool wasVisible = previousIndex.TryGetValue(obj, out int prevIdx) && previous[prevIdx].IsVisible;

                output.Add(new DataVisionSeenObject
                {
                    ResultObject = obj,
                    IsVisible = isVisible,
                    JustBecameVisible = isVisible && !wasVisible,
                    Angle = results.Angles[i],
                    Distance = results.Distances[i],
                    VisiblePointCount = visiblePointCount,
                    SampleCount = sampleCount,
                    Visibility = visibility
                });
            }
        }

        public static bool DataSeenContainsObject(List<DataVisionSeenObject> data, Collider visibleObject)
        {
            foreach (DataVisionSeenObject item in data)
            {
                if (item.ResultObject == visibleObject)
                    return true;
            }

            return false;
        }

        #endregion RESOLVE
    }
}
