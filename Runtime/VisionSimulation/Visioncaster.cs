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

        // Aligned intermediate buffers - every list is indexed 1:1 with _components, so a source's
        // slot is ALWAYS present even when the source is skipped (see CalculateVisionBroadphase).
        // Losing that alignment shifts every downstream index in narrowphase/distribution.
        // These are single-buffered: written in the broadphase, consumed in the following frame's
        // distribution, then reset by PrepareBuffers (which runs after that distribution).
        private readonly List<List<Collider>> _visibleObjects = new();
        // item1 = index of _visibleObjects object to which points belong
        private readonly List<List<List<Vector3>>> _visibleObjectPoints = new();
        private readonly List<List<float>> _visibleObjectDistances = new();
        private readonly List<List<float>> _visibleObjectAngles = new();
        // We use this to map our raycast requests back to the correct source/target/point
        private readonly List<RaycastRequestMap> _raycastRequestMap = new();

        // Results are DOUBLE-buffered: each source's LastResults hands these lists to external
        // consumers (gameplay, debug visualization in LateUpdate), so the generation just handed
        // out must stay intact until the next distribution. We ping-pong between two generations
        // and only ever recycle the one no consumer is still holding.
        private readonly List<DataVisioncastResult>[] _resultGenerations =
        {
            new List<DataVisioncastResult>(),
            new List<DataVisioncastResult>()
        };
        private int _activeGeneration;
        private List<DataVisioncastResult> ActiveResults => _resultGenerations[_activeGeneration];

        // Reused Vector3 lists to keep the narrowphase off the per-frame allocation path
        private readonly Stack<List<Vector3>> _pointListPool = new();

        // Per-source reusable collider -> result-object-index maps, so distribution avoids a linear
        // List.IndexOf per sample point. Grown to component count and cleared each distribution.
        private readonly List<Dictionary<Collider, int>> _resultObjectIndex = new();

        // Single shared broadphase hit buffer - sources are processed sequentially on the main
        // thread, so one buffer serves all of them (no per-source array needed).
        private readonly Collider[] _broadphaseHits = new Collider[CONST_VisionCastColliderBuffer];
        private readonly SystemScheduledRaycaster _raycaster;
        private bool _waitingForRaycasts;
        private bool _destroyed;
        private readonly List<VisioncastSource> _components = new();
        private readonly List<VisioncastSource> _queuedAdd = new();
        private readonly List<VisioncastSource> _queuedRemove = new();

        private const int CONST_VisionCastColliderBuffer = 64;
        private const int CONST_PointListCapacity = 7;

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
            // Uniformly queued - registration never mutates _components directly, so the component
            // set stays stable between a schedule and its matching result distribution
            _queuedAdd.Add(component);
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
                if (_components.Contains(source))
                    continue;

                _components.Add(source);
            }

            foreach (VisioncastSource source in _queuedRemove)
            {
                int index = _components.IndexOf(source);
                if (index < 0)
                    continue;

                _components.RemoveAt(index);
            }

            bool modified = _queuedAdd.Count > 0 || _queuedRemove.Count > 0;
            _queuedAdd.Clear();
            _queuedRemove.Clear();

            if (modified)
                VisioncastManager.VisionSourceComponentsModified(_components);
        }

        #endregion REGISTRATION


        #region TICK

        internal void Tick(float delta)
        {
            if (_destroyed || _waitingForRaycasts)
                return;

            CalculateVisionBroadphase();
            ExecuteVisionNarrowphase();

            // Guard against scheduling again before the previous batch has been received
            _waitingForRaycasts = true;
            _raycaster.Schedule(this, RaycasterRequests);
        }

        #endregion TICK


        #region LOGIC

        /// <summary>
        /// Gathers targets to be raycasted against, eliminating objects that are too far or not in front of the source
        /// </summary>
        private void CalculateVisionBroadphase()
        {
            // Reset the intermediate buffers for the new calculation; slots are reused, not reallocated
            PrepareBuffers(_components.Count);

            for (int i = 0; i < _components.Count; i++)
            {
                VisioncastSource source = _components[i];

                // Skipping a source must NOT skip its slot - PrepareBuffers already added an empty
                // one at index i, so alignment with _components is preserved
                if (source.transform == null)
                    continue;

                source.OnBeforeVisioncast();

                Vector3 position = source.Position;
                int hitCount = Physics.OverlapSphereNonAlloc(
                    position,
                    source.Range,
                    _broadphaseHits,
                    source.BroadphaseLayers,
                    QueryTriggerInteraction.UseGlobal);

                // A saturated buffer silently drops targets - surface it rather than hide it
                if (hitCount >= _broadphaseHits.Length)
                {
                    Debug.LogWarning($"Visioncast broadphase buffer ({CONST_VisionCastColliderBuffer}) saturated " +
                                     $"for '{source.name}'; some targets may be ignored this tick.");
                }

                if (hitCount == 0)
                    continue;

                Vector3 heading = source.Heading;
                float fov = source.FieldOfView;
                VisionSampleMode sampleMode = source.SampleMode;
                int sampleResolution = source.SampleResolution;
                for (int h = 0; h < hitCount; h++)
                {
                    Collider hit = _broadphaseHits[h];
                    if (!hit)
                        continue;

                    if (!VisionTargetsManifest.Manifest.Contains(hit)) // manifest must contain collider
                        continue;

                    // We know the object is in range, but we also need to filter by angle
                    Vector3 closestPoint = GetClosestPoint(hit, position);
                    Vector3 dirToTarget = closestPoint - position;
                    float angle = Vector3.Angle(heading, dirToTarget);
                    if (angle > fov)
                        continue;

                    float objDistance = Vector3.Distance(closestPoint, position);

                    _visibleObjects[i].Add(hit);

                    List<Vector3> objectPoints = RentPointList();
                    objectPoints.Add(closestPoint);
                    VisiblePointsProcessor.Process(hit, sampleMode, sampleResolution, objectPoints);
                    _visibleObjectPoints[i].Add(objectPoints);

                    _visibleObjectDistances[i].Add(objDistance);
                    _visibleObjectAngles[i].Add(angle);
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

            // One straight-ahead cast per source, kept 1:1 with _components
            for (int i = 0; i < _components.Count; i++)
            {
                VisioncastSource source = _components[i];
                if (source.transform == null)
                {
                    // Placeholder keeps the request list aligned with _components; a zero-layer
                    // cast is a guaranteed miss and its slot is ignored on distribution
                    RaycasterRequests.Add(new DataScheduledRaycastRequest
                    {
                        SourcePosition = Vector3.zero,
                        Direction = Vector3.forward,
                        MaxDistance = 0,
                        LayerMask = 0
                    });
                    continue;
                }

                RaycasterRequests.Add(new DataScheduledRaycastRequest
                {
                    SourcePosition = source.Position,
                    Direction = source.Heading.normalized,
                    MaxDistance = source.Range,
                    LayerMask = source.RaycastLayers
                });
            }

            // One cast per visible sample point, mapped back to its source/object/point
            for (int i = 0; i < _components.Count; i++)
            {
                VisioncastSource source = _components[i];
                if (source.transform == null)
                    continue;

                Vector3 position = source.Position;
                float range = source.Range;
                int layerMask = source.RaycastLayers;

                for (int e = 0; e < _visibleObjects[i].Count; e++)
                {
                    List<Vector3> points = _visibleObjectPoints[i][e];
                    for (int y = 0; y < points.Count; y++)
                    {
                        Vector3 objectPoint = points[y];
                        RaycasterRequests.Add(new DataScheduledRaycastRequest
                        {
                            SourcePosition = position,
                            Direction = objectPoint - position,
                            MaxDistance = range,
                            LayerMask = layerMask
                        });

                        // Add matching key map
                        _raycastRequestMap.Add(new RaycastRequestMap
                        {
                            Source = i,
                            Object = e,
                            Point = y
                        });
                    }
                }
            }
        }

        void IScheduledRaycastListener.ReceiveScheduledRaycasterResults()
        {
            // Requests are 1:1 with (straight-ahead per source) + (mapped sample points). With
            // uniform queued registration the component set no longer changes mid-flight, so this
            // is a defensive guard rather than an expected path.
            if (RaycasterResults.Count == _raycastRequestMap.Count + _components.Count)
            {
                DistributeRaycasterResults();
            }
            else
            {
                Debug.LogWarning("Visioncast raycast count mismatch; skipping distribution this tick.");
            }

            // Now that we've received raycasts, we can restart visioncast
            _waitingForRaycasts = false;
            ResolveComponentQueues();
        }

        private void DistributeRaycasterResults()
        {
            // Flip to the generation no consumer is still holding and prepare it fresh. The
            // previous generation stays intact behind each source's LastResults.
            _activeGeneration ^= 1;
            PrepareResultGeneration(_activeGeneration, _components.Count);
            PrepareResultObjectIndex(_components.Count);

            // Distribute straight-ahead raycasts
            DistributeStraightAheadCasts();

            // Distribute object-specific raycast hits
            DistributeMappedCasts();

            // Distribute results
            SendResultsToComponents();
        }

        void IScheduledRaycastListener.OnEmptyRaycastRequestResult()
        {
            _waitingForRaycasts = false;
            ResolveComponentQueues();
        }

        #endregion LOGIC


        #region DISTRIBUTION

        private void DistributeStraightAheadCasts()
        {
            List<DataVisioncastResult> results = ActiveResults;
            for (int i = 0; i < _components.Count; i++)
            {
                RaycastHit hit = RaycasterResults[i];
                if (hit.collider != null && VisionTargetsManifest.Manifest.Contains(hit.collider))
                {
                    DataVisioncastResult result = results[i];

                    // Denominator is the object's dedicated sample count; the straight-ahead ray is
                    // an extra confirmed point on top of it (visibility is clamped on read)
                    int broadIndex = _visibleObjects[i].IndexOf(hit.collider);
                    int sampleCount = broadIndex >= 0 ? _visibleObjectPoints[i][broadIndex].Count : 1;

                    List<Vector3> points = RentPointList();
                    points.Add(hit.point);
                    result.VisiblePoints.Add(points);
                    result.Objects.Add(hit.collider);
                    result.SampleCounts.Add(sampleCount);
                    result.Angles.Add(0);
                    result.Distances.Add(hit.distance);

                    // Record so DistributeMappedCasts finds this object in O(1)
                    _resultObjectIndex[i][hit.collider] = result.Objects.Count - 1;
                }
            }
        }

        private void DistributeMappedCasts()
        {
            List<DataVisioncastResult> results = ActiveResults;
            for (int i = _components.Count; i < RaycasterResults.Count; i++)
            {
                RaycastHit hit = RaycasterResults[i];
                RaycastRequestMap map = _raycastRequestMap[i - _components.Count];
                DataVisioncastResult result = results[map.Source];
                Dictionary<Collider, int> objectIndexMap = _resultObjectIndex[map.Source];
                Collider thisObject = _visibleObjects[map.Source][map.Object];
                Vector3 thisPoint = _visibleObjectPoints[map.Source][map.Object][map.Point];

                // Add object to source results if not present (O(1) via the per-source index map)
                if (!objectIndexMap.TryGetValue(thisObject, out int objectIndex))
                {
                    result.Objects.Add(thisObject);
                    result.SampleCounts.Add(_visibleObjectPoints[map.Source][map.Object].Count);
                    result.Angles.Add(_visibleObjectAngles[map.Source][map.Object]);
                    result.Distances.Add(_visibleObjectDistances[map.Source][map.Object]);
                    result.VisiblePoints.Add(RentPointList());
                    objectIndex = result.Objects.Count - 1;
                    objectIndexMap[thisObject] = objectIndex;
                }

                // Did we hit the object itself?
                if (hit.collider && hit.collider == thisObject)
                {
                    result.VisiblePoints[objectIndex].Add(thisPoint);
                }
            }
        }

        private void SendResultsToComponents()
        {
            List<DataVisioncastResult> results = ActiveResults;
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].ReceiveResults(results[i]);
            }
        }

        #endregion DISTRIBUTION


        #region BUFFERS

        /// <summary>
        /// Resets the aligned intermediate buffers for a tick over <paramref name="count"/> sources,
        /// reusing existing container/point lists so the narrowphase does not allocate per frame.
        /// </summary>
        private void PrepareBuffers(int count)
        {
            // Return last tick's broadphase point-lists before the buffers are reused
            for (int i = 0; i < _visibleObjectPoints.Count; i++)
            {
                List<List<Vector3>> objectPoints = _visibleObjectPoints[i];
                for (int e = 0; e < objectPoints.Count; e++)
                    ReturnPointList(objectPoints[e]);
                objectPoints.Clear();
            }

            // Grow outer buffers to match the component count, reusing containers
            while (_visibleObjects.Count < count)
            {
                _visibleObjects.Add(new List<Collider>());
                _visibleObjectPoints.Add(new List<List<Vector3>>());
                _visibleObjectDistances.Add(new List<float>());
                _visibleObjectAngles.Add(new List<float>());
            }

            // Reset the active range; capacity is retained so no per-tick allocation occurs
            for (int i = 0; i < count; i++)
            {
                _visibleObjects[i].Clear();
                _visibleObjectDistances[i].Clear();
                _visibleObjectAngles[i].Clear();
            }
        }

        /// <summary>
        /// Grows and clears the per-source result-object index maps for a distribution over
        /// <paramref name="count"/> sources. Reused each tick so distribution allocates no maps.
        /// </summary>
        private void PrepareResultObjectIndex(int count)
        {
            while (_resultObjectIndex.Count < count)
                _resultObjectIndex.Add(new Dictionary<Collider, int>());

            for (int i = 0; i < count; i++)
                _resultObjectIndex[i].Clear();
        }

        /// <summary>
        /// Recycles the given result generation and sizes it to <paramref name="count"/> sources.
        /// Only ever called on the generation no consumer is holding (see DistributeRaycasterResults).
        /// </summary>
        private void PrepareResultGeneration(int generation, int count)
        {
            List<DataVisioncastResult> results = _resultGenerations[generation];

            // Return this generation's point-lists to the pool before reuse
            for (int i = 0; i < results.Count; i++)
            {
                List<List<Vector3>> visiblePoints = results[i].VisiblePoints;
                for (int e = 0; e < visiblePoints.Count; e++)
                    ReturnPointList(visiblePoints[e]);
                visiblePoints.Clear();
            }

            // Grow to match the component count, reusing containers
            while (results.Count < count)
            {
                results.Add(new DataVisioncastResult
                {
                    Objects = new List<Collider>(),
                    VisiblePoints = new List<List<Vector3>>(),
                    SampleCounts = new List<int>(),
                    Distances = new List<float>(),
                    Angles = new List<float>()
                });
            }

            // Reset the active range; capacity is retained
            for (int i = 0; i < count; i++)
            {
                DataVisioncastResult result = results[i];
                result.Objects.Clear();
                result.SampleCounts.Clear();
                result.Distances.Clear();
                result.Angles.Clear();
                // VisiblePoints already cleared above
            }
        }

        private List<Vector3> RentPointList()
        {
            List<Vector3> list = _pointListPool.Count > 0
                ? _pointListPool.Pop()
                : new List<Vector3>(CONST_PointListCapacity);
            list.Clear();
            return list;
        }

        private void ReturnPointList(List<Vector3> list)
        {
            if (list == null)
                return;

            _pointListPool.Push(list);
        }

        #endregion BUFFERS


        #region UTILITY

        /// <summary>
        /// Collider.ClosestPoint throws on non-convex mesh colliders; those fall back to the
        /// (looser) bounds approximation so a mesh target cannot bring down the whole tick.
        /// </summary>
        private static Vector3 GetClosestPoint(Collider col, Vector3 position)
        {
            if (col is MeshCollider { convex: false })
                return col.bounds.ClosestPoint(position);

            return col.ClosestPoint(position);
        }

        #endregion UTILITY


        #region RESET

        public void Dispose()
        {
            _destroyed = true;
            _components.Clear();
            _queuedAdd.Clear();
            _queuedRemove.Clear();
        }

        #endregion RESET


        #region DATA

        /// <summary>
        /// Maps a scheduled raycast back to the source/object/point it was cast for
        /// </summary>
        private struct RaycastRequestMap
        {
            public int Source;
            public int Object;
            public int Point;
        }

        #endregion DATA
    }
}
