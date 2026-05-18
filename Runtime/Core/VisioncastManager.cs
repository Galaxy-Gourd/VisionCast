using System;
using System.Collections.Generic;
using GalaxyGourd.Raycast;
using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// Manages high-level visioncast system flow.
    /// </summary>
    [DefaultExecutionOrder(-499)]
    public static class VisioncastManager
    {
        #region VARIABLES

        public static Action<List<VisioncastSource>> OnSourceComponentsModified;
        public static Action PreVisioncastTick { get; set; }
        public static Action PostVisioncastTick { get; set; }

        private static Visioncaster _visioncaster;
        private static readonly List<VisioncastSource> _addQueue = new();

        #endregion VARIABLES


        #region INIT

        public static void Setup()
        {
            OnSourceComponentsModified = null;
            _visioncaster = new Visioncaster(RaycastManager.ScheduledRaycaster);
            
            //
            foreach (VisioncastSource source in _addQueue)
            {
                RegisterVisionSource(source);
            }
            _addQueue.Clear();
        }

        public static void Dispose()
        {
            _visioncaster?.Dispose();
            _addQueue.Clear();
            _visioncaster = null;
        }

        #endregion INIT
        
        
        #region API

        public static void TickVisioncasts(float delta)
        {
            PreVisioncastTick?.Invoke();
            _visioncaster?.Tick(delta);
            PostVisioncastTick?.Invoke();
        }

        /// <summary>
        /// Updates the debug visualization for the visioncast sources
        /// </summary>
        public static void TickVisioncastSourceDebug(float delta)
        {
            foreach (VisioncastSource component in _visioncaster.Components)
            {
                component.TickDebug(delta);
            }
        }

        public static void RegisterVisionSource(VisioncastSource source)
        {
            // Unreliable init ordering can cause components to be added before the visioncaster is initialized, this
            // queue will enable that to still work
            if (_visioncaster == null)
                _addQueue.Add(source);
            else
                _visioncaster?.RegisterComponent(source);
        }
        
        public static void UnregisterVisionSource(VisioncastSource source)
        {
            _visioncaster?.RemoveComponent(source);
        }

        public static void VisionSourceComponentsModified(List<VisioncastSource> sources)
        {
            OnSourceComponentsModified?.Invoke(sources);
        }

        #endregion API


        #region UTILITY

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _visioncaster?.Dispose();
            _addQueue.Clear();
            _visioncaster = null;
        }

        #endregion UTILITY
    }
}