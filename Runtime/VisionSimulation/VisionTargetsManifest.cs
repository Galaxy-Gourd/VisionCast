using System.Collections.Generic;
using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// Source of truth for all active interactable objects. Maps colliders to key objects
    /// </summary>
    public static class VisionTargetsManifest
    {
        #region VARIABLES

        internal static HashSet<Collider> Manifest { get; private set; } = new();

        #endregion VARIABLES


        #region INITIALIZATION

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Manifest.Clear();
        }

        #endregion INITIALIZATION


        #region REGISTRATION

        public static void Register(Collider col)
        {
            Manifest.Add(col);       
        }
        
        public static void Unregister(Collider col)
        {
            Manifest.Remove(col);
        }

        #endregion REGISTRATION
    }
}