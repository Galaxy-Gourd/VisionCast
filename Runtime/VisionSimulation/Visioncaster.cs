using System;
using System.Collections.Generic;
using GalaxyGourd.Raycast;
using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// Class responsible for executing visioncast requests
    /// </summary>
    internal class Visioncaster : IScheduledRaycastListener, IDisposable
    {
        #region VARIABLES
        
        internal List<VisioncastSource> Components => _components;
        public List<RaycastHit> RaycasterResults { get; set; }
        public List<DataScheduledRaycastRequest> RaycasterRequests { get; set; }
        
        // Aligned buffers
        private static readonly List<List<Collider>> _visibleObjects = new();
        // item1 = index of _visibleObjects object to which points belong
        private static readonly List<List<List<Vector3>>> _visibleObjectPoints = new();
        private static readonly List<List<float>> _visibleObjectDistances = new();
        private static readonly List<List<float>> _visibleObjectAngles = new();
        // We use this to map our raycast requests to the correct source/target
        // 1 = index of component(source), 2 = index of _visibleObjects, 3 = index of _visibleObjectPoints
        private static readonly List<Tuple<int, int, int>> _raycastRequestMap = new();
        // Results for each of the components
        private static readonly List<DataVisioncastResult> _visioncastResults = new();
        
        // Hitbuffer for each source
        private readonly List<Collider[]> _hitsBuffer = new();
        private readonly SystemScheduledRaycaster _raycaster;
        private bool _waitingForRaycasts;
        private bool _destroyed;
        private readonly List<VisioncastSource> _components = new();
        private readonly List<VisioncastSource> _queuedAdd = new();
        private readonly List<VisioncastSource> _queuedRemove = new();
        
        private const int CONST_VisionCastColliderBuffer = 64;

        #endregion VARIABLES
        
        
        #region INITIALIZATION

        internal Visioncaster(SystemScheduledRaycaster raycaster)
        {
            _raycaster = raycaster;
            RaycasterRequests = new List<DataScheduledRaycastRequest>();
        }

        #endregion INITIALIZATION


        #region REGISTRATION

        public void RegisterComponent(VisioncastSource component)
        {
            if (_components.Count == 0)
            {
                _components.Add(component);
                _hitsBuffer.Add(new Collider[CONST_VisionCastColliderBuffer]);
            }
            else
            {
                _queuedAdd.Add(component);
            }
        }

        public void RemoveComponent(VisioncastSource component)
        {
            _queuedRemove.Add(component);
        }

        /// <summary>
        /// Syncs the addition and removal of components until after raycasting has been completed
        /// </summary>
        private void ResolveComponentQueues()
        {
            if (_destroyed)
                return;
            
            foreach (VisioncastSource source in _queuedAdd)
            {
                _components.Add(source);
                _hitsBuffer.Add(new Collider[CONST_VisionCastColliderBuffer]);
            }
            
            foreach (VisioncastSource source in _queuedRemove)
            {
                _hitsBuffer.RemoveAt(_components.IndexOf(source));
                _components.Remove(source);
            }
            
            if (_queuedAdd.Count > 0 || _queuedRemove.Count > 0)
                VisioncastManager.VisionSourceComponentsModified(_components);

            _queuedAdd.Clear();
            _queuedRemove.Clear();
        }

        #endregion REGISTRATION


        #region TICK

        internal void Tick(float delta)
        {
            if (_destroyed || _waitingForRaycasts)
                return;
            
            CalculateVisionBroadphase();
            ExecuteVisionNarrowphase();
            _raycaster.Schedule(this, RaycasterRequests);
        }

        #endregion TICK


        #region LOGIC

        /// <summary>
        /// Gathers targets to be raycasted against, eliminating objects that are too far or not in front of the source
        /// </summary>
        private void CalculateVisionBroadphase()
        {
            // Clear buffers for new calculation
            ClearBuffers();
            
            for (int i = 0; i < _components.Count; i++)
            {
                VisioncastSource source = _components[i];
                if (source.transform == null)
                    continue;
                
                source.OnBeforeVisioncast();
                
                // Reset caches
                _visibleObjects.Add(new List<Collider>());
                _visibleObjectPoints.Add(new List<List<Vector3>>());
                _visibleObjectDistances.Add(new List<float>());
                _visibleObjectAngles.Add(new List<float>());
                _visioncastResults.Add(new DataVisioncastResult
                {
                    Objects = new List<Collider>(),
                    VisiblePoints = new List<List<Vector3>>(),
                    Distances = new List<float>(),
                    Angles = new List<float>()
                });

                Vector3 position = source.Position;
                if (Physics.OverlapSphereNonAlloc(
                        position,
                        source.Range,
                        _hitsBuffer[i],
                        source.BroadphaseLayers,
                        QueryTriggerInteraction.UseGlobal) > 0)
                {
                    Vector3 heading = source.Heading;
                    float fov = source.FieldOfView;
                    foreach (Collider hit in _hitsBuffer[i])
                    {
                        if (!hit) 
                            break;
                        
                        if (!VisionTargetsManifest.Manifest.Contains(hit)) // manifest must contain collider
                            continue;
                        
                        // We know the object is in range, but we also need to filter by angle
                        Vector3 closestPoint = hit.ClosestPoint(position);
                        Vector3 dirToTarget = closestPoint - position;
                        float angle = Vector3.Angle(heading, dirToTarget);
                        float objDistance = Vector3.Distance(closestPoint, position);
                        
                        if (angle <= fov)
                        {
                            _visibleObjects[i].Add(hit);
                            List<Vector3> objectPoints = new List<Vector3>(7) { closestPoint };
                            objectPoints.AddRange(VisiblePointsProcessor.Process(hit));
                            _visibleObjectPoints[i].Add(objectPoints);
                            _visibleObjectDistances[i].Add(objDistance);
                            _visibleObjectAngles[i].Add(angle);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Takes resulting broadphase objects and schedules raycasts against them
        /// </summary>
        private void ExecuteVisionNarrowphase()
        {
            RaycasterRequests.Clear();
            _raycastRequestMap.Clear();

            for (int i = 0; i < _components.Count; i++)
            {
                VisioncastSource source = _components[i];
                RaycasterRequests.Add(new DataScheduledRaycastRequest
                {
                    SourcePosition = source.Position,
                    Direction = source.Heading.normalized,
                    MaxDistance = source.Range,
                    LayerMask = source.RaycastLayers
                });
            }
            
            for (int i = 0; i < _components.Count; i++)
            {
                VisioncastSource source = _components[i];
                Vector3 position = source.Position;
                float range = source.Range;
                int layerMask = source.RaycastLayers;
                
                for (int e = 0; e < _visibleObjects[i].Count; e++)
                {
                    for (int y = 0; y < _visibleObjectPoints[i][e].Count; y++)
                    {
                        Vector3 objectPoint = _visibleObjectPoints[i][e][y];
                        RaycasterRequests.Add(new DataScheduledRaycastRequest
                        {
                            SourcePosition = position,
                            Direction = objectPoint - position,
                            MaxDistance = range,
                            LayerMask = layerMask
                        });
                        
                        // Add matching key map
                        _raycastRequestMap.Add(new Tuple<int, int, int>(i, e, y));
                    }
                }
            }
        }

        void IScheduledRaycastListener.ReceiveScheduledRaycasterResults()
        {
            // Sanity check, sometimes this is not true which throws an exception
            // TODO fix ^^
            if (RaycasterResults.Count == _raycastRequestMap.Count + _components.Count)
            {
                (this as IScheduledRaycastListener).DistributeRaycasterResults();
            }
            else
            {
                Debug.Log("Visioncast Raycast count mismatch!");
            }
            
            // Now that we've recieved raycasts, we can restart visioncast
            _waitingForRaycasts = false;
            ResolveComponentQueues();
        }

        void IScheduledRaycastListener.DistributeRaycasterResults()
        {
            // Distribute straight-ahead raycasts
            DistributeStraightAheadCasts();
            
            // Distribute object-specific raycast hits
            DistributeMappedCasts();

            // Distribute results
            SendResultsToComponents();
        }

        void IScheduledRaycastListener.OnEmptyRaycastRequestResult()
        {
            ResolveComponentQueues();
        }

        #endregion LOGIC


        #region DISTRIBUTION

        private void DistributeStraightAheadCasts()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                RaycastHit hit = RaycasterResults[i];
                if (hit.collider != null && VisionTargetsManifest.Manifest.Contains(hit.collider))
                {
                    DataVisioncastResult result = _visioncastResults[i];
                    result.VisiblePoints.Add(new List<Vector3>(7));
                    result.VisiblePoints[0].Add(hit.point);
                    result.Objects.Add(hit.collider);
                    result.Angles.Add(0);
                    result.Distances.Add(hit.distance);
                }
            }
        }

        private void DistributeMappedCasts()
        {
            for (int i = _components.Count; i < RaycasterResults.Count; i++)
            {
                RaycastHit hit = RaycasterResults[i];
                Tuple<int, int, int> map = _raycastRequestMap[i - _components.Count];
                DataVisioncastResult result = _visioncastResults[map.Item1];
                Collider thisObject = _visibleObjects[map.Item1][map.Item2];
                Vector3 thisPoint = _visibleObjectPoints[map.Item1][map.Item2][map.Item3];

                // Add object to source results if not present
                if (!result.Objects.Contains(thisObject))
                {
                    result.Objects.Add(thisObject);
                    result.Angles.Add(_visibleObjectAngles[map.Item1][map.Item2]);
                    result.Distances.Add(_visibleObjectDistances[map.Item1][map.Item2]);
                    result.VisiblePoints.Add(new List<Vector3>(7));
                }
                
                // Did we hit the object itself?
                if (hit.collider && hit.collider == thisObject)
                {
                    result.VisiblePoints[result.Objects.IndexOf(thisObject)].Add(thisPoint);
                }
            }
        }

        private void SendResultsToComponents()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].ReceiveResults(_visioncastResults[i]);
            }
        }

        #endregion DISTRIBUTION


        #region UTILITY

        private void ClearBuffers()
        {
            _visibleObjects.Clear();
            _visibleObjectPoints.Clear();
            _visibleObjectDistances.Clear();
            _visibleObjectAngles.Clear();
            _visioncastResults.Clear();
            
            // Hits buffers
            for (int i = 0; i < _hitsBuffer.Count; i++)
            {
                for (int e = 0; e < _hitsBuffer[i].Length; e++)
                {
                    _hitsBuffer[i][e] = null;
                } 
            }
        }

        #endregion UTILITY


        #region RESET

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            _visibleObjects.Clear();
            _visibleObjectPoints.Clear();
            _visibleObjectDistances.Clear();
            _visibleObjectAngles.Clear();
            _raycastRequestMap.Clear();
            _visioncastResults.Clear();
        }
        
        public void Dispose()
        {
            _destroyed = true;
            _components.Clear();
        }

        #endregion RESET
    }
}