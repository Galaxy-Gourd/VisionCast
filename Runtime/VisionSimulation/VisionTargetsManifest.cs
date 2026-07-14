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
        // Optional collider -> owning actor mapping, used to group a multi-collider actor's results
        // into a single target (see VisioncastSourceCompound). Colliders with no entry stand alone.
        private static readonly Dictionary<Collider, Component> _actors = new();

        #endregion VARIABLES


        #region INITIALIZATION

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Manifest.Clear();
            _actors.Clear();
        }

        #endregion INITIALIZATION


        #region REGISTRATION

        public static void Register(Collider col)
        {
            Register(col, null);
        }

        /// <summary>
        /// Registers a collider as a vision target, optionally owned by <paramref name="actor"/>.
        /// Register every collider of a multi-collider actor against the same actor so line-of-sight
        /// results collapse to one target per actor.
        /// </summary>
        public static void Register(Collider col, Component actor)
        {
            Manifest.Add(col);

            if (actor)
                _actors[col] = actor;
            else
                _actors.Remove(col);
        }

        public static void Unregister(Collider col)
        {
            Manifest.Remove(col);
            _actors.Remove(col);
        }

        /// <summary>
        /// Resolves the actor that owns a collider. Returns false (and null) when the collider has no
        /// registered actor or that actor has been destroyed - callers then treat the collider as its
        /// own standalone target.
        /// </summary>
        public static bool TryGetActor(Collider col, out Component actor)
        {
            if (_actors.TryGetValue(col, out actor) && actor)
                return true;

            actor = null;
            return false;
        }

        #endregion REGISTRATION
    }
}
