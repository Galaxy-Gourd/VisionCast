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
        
        internal void ReceiveResults(DataVisioncastResult data)
        {
            LastResults = data;
            OnReceiveResults(data);
        }

        protected abstract void OnReceiveResults(DataVisioncastResult data);

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