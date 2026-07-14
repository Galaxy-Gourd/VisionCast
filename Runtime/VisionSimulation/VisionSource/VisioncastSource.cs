using System.Collections.Generic;
using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// Base class for vision source objects - inherit to add contextual functionality
    /// </summary>
    public abstract class VisioncastSource : MonoBehaviour
    {
        #region VARIABLES

        /// <summary>
        /// Layers that this source can "see"
        /// </summary>
        public abstract LayerMask BroadphaseLayers { get; }
        /// <summary>
        /// Layers of objects that will block this source's line of sight
        /// </summary>
        public abstract LayerMask RaycastLayers { get; }
        public virtual Vector3 Position => transform.position;
        public virtual Vector3 Heading => transform.forward;
        public abstract float Range { get; }
        /// <summary>
        /// Vision cone HALF-angle in degrees, measured from <see cref="Heading"/> out to the cone rim.
        /// A target is considered within the field of view when the angle between the heading and the
        /// direction to the target is less than or equal to this value. Debug visualization
        /// (<see cref="VisionCone"/>) assumes this same half-angle convention.
        /// </summary>
        public abstract float FieldOfView { get; }
        /// <summary>
        /// Strategy used to generate the per-target sample points tested for line of sight. Override
        /// to trade cost for a smoother <see cref="DataVisionSeenObject.Visibility"/> signal (stealth).
        /// </summary>
        public virtual VisionSampleMode SampleMode => VisionSampleMode.BoundsFaceGrid;
        /// <summary>
        /// Grid density per axis for the active <see cref="SampleMode"/>. 1 reproduces the legacy
        /// face-center sampling; higher values scale sample count by resolution^2 (face grid) or
        /// resolution^3 (volume grid), and thus the raycasts per target.
        /// </summary>
        public virtual int SampleResolution => 1;
        public DataVisioncastResult LastResults { get; private set; }
        /// <summary>
        /// Vision-time (see <see cref="VisioncastManager.VisionTime"/>) at which <see cref="LastResults"/>
        /// were last produced. Under LOD / time-slicing a source updates only every few ticks, so compare
        /// against VisionTime to gauge how stale its data is.
        /// </summary>
        public float LastUpdatedTime { get; private set; }
        /// <summary>Vision-time elapsed since this source last updated (0 = updated this cycle).</summary>
        public float TimeSinceUpdate => VisioncastManager.VisionTime - LastUpdatedTime;

        // Per-source double-buffered result storage, written by the Visioncaster. Ownership is
        // per-source (not a global generation) so LastResults stays valid until THIS source's next
        // distribution, independent of other sources' update cadence - required for time-slicing/LOD.
        // The back buffer is written this distribution; the front stays behind LastResults until then.
        private readonly DataVisioncastResult[] _resultBuffers =
        {
            NewResultBuffer(),
            NewResultBuffer()
        };
        private int _resultBack;

        /// <summary>The buffer the Visioncaster writes this distribution (not the one behind LastResults).</summary>
        internal DataVisioncastResult ResultBackBuffer => _resultBuffers[_resultBack];

        // LOD scheduling state, owned by the scheduler (see Visioncaster.BuildSchedule).
        // Phase spreads same-cadence sources across ticks to flatten load; tier is cached for hysteresis.
        internal int SchedulePhase;
        internal int ScheduleTier = -1;

        private VisioncastSourceDebug _debug;

        #endregion VARIABLES


        #region INITIALIZATION

        private void OnEnable()
        {
            VisioncastManager.RegisterVisionSource(this);
        }

        private void OnDisable()
        {
            VisioncastManager.UnregisterVisionSource(this);
        }
        
        #endregion INITIALIZATION


        #region CAST

        /// <summary>
        /// Called immediately before the broadphase of the visioncast is started
        /// </summary>
        public virtual void OnBeforeVisioncast()
        {
            
        }
        
        /// <summary>
        /// Promotes the freshly written back buffer to <see cref="LastResults"/> and flips, so the next
        /// distribution writes the other buffer and this one stays valid for consumers until then.
        /// </summary>
        internal void DeliverResults(float updateTime)
        {
            LastUpdatedTime = updateTime;
            LastResults = _resultBuffers[_resultBack];
            _resultBack ^= 1;
            OnReceiveResults(LastResults);
        }

        protected abstract void OnReceiveResults(DataVisioncastResult data);

        private static DataVisioncastResult NewResultBuffer()
        {
            return new DataVisioncastResult
            {
                Objects = new List<Collider>(),
                VisiblePoints = new List<List<Vector3>>(),
                SampleCounts = new List<int>(),
                Distances = new List<float>(),
                Angles = new List<float>()
            };
        }

        #endregion CAST


        #region DEBUG

        internal void TickDebug(float delta)
        {
            if (!_debug)
                return;
            
            _debug.Tick(delta);
        }

        internal void AttachDebug(VisioncastSourceDebug debug)
        {
            _debug = debug;
        }
        
        internal void DetachDebug(VisioncastSourceDebug debug)
        {
            _debug = null;
        }

        #endregion DEBUG
    }
}