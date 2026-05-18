using System.Collections.Generic;
using UnityEngine;

namespace GalaxyGourd.Visioncast 
{
    /// <summary>
    /// Receives and resolves visioncast results into list of DataVisionSeenObject
    /// </summary>
    public static class VisioncastResultsFilter
    {
        #region RESOLVE

        public static List<DataVisionSeenObject> Resolve(DataVisioncastResult results, List<DataVisionSeenObject> previous) 
        {
            List<DataVisionSeenObject> resolved = new();
            
            // Cycle through all objects in visioncast
            for (int i = 0; i < results.Objects.Count; i++)
            {
                // Filter by object type
                if (results.Objects[i] is not Collider)
                    continue;
                
                // Was this object seen?
                if (results.VisiblePoints[i].Count > 0)
                {
                    DataVisionSeenObject? objPreviousData = GetSeenDataForObject(previous, results.Objects[i]);
                    resolved.Add(new DataVisionSeenObject()
                    {
                        ResultObject = results.Objects[i],
                        IsVisible = true,
                        JustBecameVisible = 
                            !DataSeenContainsObject(previous, results.Objects[i]) || objPreviousData is { IsVisible: false },
                        Angle = results.Angles[i],
                        Distance = results.Distances[i]
                    });
                }
                else
                {
                    // This object was not seen...
                    resolved.Add(new DataVisionSeenObject()
                    {
                        ResultObject = results.Objects[i],
                        IsVisible = false,
                        JustBecameVisible = false,
                        Angle = results.Angles[i],
                        Distance = results.Distances[i]
                    });
                }
            }
            
            //
            return resolved;
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

        private static DataVisionSeenObject? GetSeenDataForObject(List<DataVisionSeenObject> data, Collider obj)
        {
            foreach (DataVisionSeenObject item in data)
            {
                if (item.ResultObject == obj)
                    return item;
            }

            return null;
        }

        #endregion RESOLVE
    }
}