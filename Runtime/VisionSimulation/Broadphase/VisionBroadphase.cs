using System;
using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// Selects the broadphase implementation the Visioncaster uses. Default is the dependency-free,
    /// main-thread <see cref="PhysicsOverlapBroadphase"/>. Call <see cref="UseBatchedOverlap"/> before
    /// <see cref="VisioncastManager.Setup"/> to move the sphere queries off the main thread, or set a
    /// custom factory (in-package, e.g. the DoD spatial grid). The Visioncaster owns and disposes the
    /// instance the factory returns.
    /// </summary>
    public static class VisionBroadphase
    {
        #region VARIABLES

        internal static Func<IBroadphase> Factory = _defaultFactory;

        private static readonly Func<IBroadphase> _defaultFactory = () => new PhysicsOverlapBroadphase();

        #endregion VARIABLES


        #region API

        /// <summary>Use the batched OverlapSphereCommand broadphase (queries run across worker threads).</summary>
        public static void UseBatchedOverlap()
        {
            Factory = () => new OverlapCommandBroadphase();
        }

        /// <summary>Revert to the default main-thread physics overlap broadphase.</summary>
        public static void UsePhysicsOverlap()
        {
            Factory = _defaultFactory;
        }

        #endregion API


        #region RESET

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Factory = _defaultFactory;
        }

        #endregion RESET
    }
}
