using System.Collections.Generic;
using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// Source that filters visioncast results into more detailed data. 
    /// </summary>
    public class VisioncastSourceFiltered : VisioncastSource
    {
        #region VARIABLES

        [Header("Data")]
        [SerializeField] protected DataConfigVisioncastSource _dataInteraction;

        public override LayerMask BroadphaseLayers => _dataInteraction.BroadphaseLayermask;
        public override LayerMask RaycastLayers => _dataInteraction.RaycastLayermask;
        public override float Range => _dataInteraction.VisionRange;
        public override float FieldOfView => _dataInteraction.FieldOfView;

        /// <summary>
        /// All objects currently seen by this source
        /// </summary>
        protected List<DataVisionSeenObject> _filteredVisionTargets = new ();
        /// <summary>
        /// The object most directly in the center of the source's field of view
        /// </summary>
        protected Collider _targetedObject;
        /// <summary>
        /// A list of objects that were newly seen since the most recent visioncast update 
        /// </summary>
        protected readonly List<Collider> _newlySeenObjects = new();
        /// <summary>
        /// A list of objects that were newly un-seen since the most recent visioncast update
        /// </summary>
        protected readonly List<Collider> _newlyLostObjects = new();

        private readonly List<DataVisionSeenObject> _objCache = new();

        #endregion VARIABLES
        

        #region VISION

        protected override void OnReceiveResults(DataVisioncastResult data)
        {
            FilterVisionTargets(data);
            PostVisionFilter();
        }

        protected virtual void FilterVisionTargets(DataVisioncastResult data)
        {
            // If there are no objects visible we can get out
            if (data.Objects == null)
            {
                // Copy previously seen objects
                _objCache.Clear();
                _objCache.AddRange(_filteredVisionTargets);
                
                // Clear out vision data since there's nothing visible
                ClearVisionData();

                // If there WERE visible items last update, they now count as newly lost
                foreach (DataVisionSeenObject lastSeen in _objCache)
                {
                    _newlyLostObjects.Add(lastSeen.ResultObject);
                }
                
                return;
            }
            
            // Resolve seen objects into workable data
            List<DataVisionSeenObject> newResults = VisioncastResultsFilter.Resolve(data, _filteredVisionTargets);
            _newlySeenObjects.Clear();
            _newlyLostObjects.Clear();

            // We need to act on the objects that were seen previously, but no more
            for (int i = 0; i < _filteredVisionTargets.Count; i++)
            {
                // If we HAD seen an item but NO LONGER have it in the results array
                if (_filteredVisionTargets[i].IsVisible &&
                    !VisioncastResultsFilter.DataSeenContainsObject(newResults, _filteredVisionTargets[i].ResultObject))
                {
                    _newlyLostObjects.Add(_filteredVisionTargets[i].ResultObject);
                }
            }

            // Collect new objects
            foreach (DataVisionSeenObject visionObject in newResults)
            {
                if (visionObject.JustBecameVisible)
                    _newlySeenObjects.Add(visionObject.ResultObject);
                else if (!visionObject.IsVisible && !_newlyLostObjects.Contains(visionObject.ResultObject))
                    _newlyLostObjects.Add(visionObject.ResultObject);
            }

            _filteredVisionTargets = newResults;
            
            // The object closest to the view center is our key object
            float closestAngle = float.MaxValue;
            _targetedObject = null;
            foreach (DataVisionSeenObject obj in _filteredVisionTargets)
            {
                // if (obj.IsVisible) 
                //     obj.ResultObject.Seen(this);
                
                if (obj.IsVisible && obj.Angle < closestAngle)
                {
                    closestAngle = obj.Angle;
                    _targetedObject = obj.ResultObject;
                }
            }
        }
        
        protected virtual void PostVisionFilter() { }

        protected virtual void ClearVisionData()
        {
            _filteredVisionTargets.Clear();
            _newlySeenObjects.Clear();
            _newlyLostObjects.Clear();
            _targetedObject = null;
        }

        #endregion VISION
    }
}