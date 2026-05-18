using System.Collections.Generic;
using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    internal static class VisiblePointsProcessor
    {
        #region VARIABLES

        private static readonly Vector3[] _boundsCache = new Vector3[6];

        #endregion VARIABLES


        #region INITIALIZATION



        #endregion INITIALIZATION


        #region PROCESS

        internal static Vector3[] Process(Collider collider)
        {
            switch (collider)
            {
                case BoxCollider box:
                    return VisioncastUtility.GetBoxColliderExtentsFaces(box, 0, _boundsCache);
                default:
                    return VisioncastUtility.GetColliderBoundsFaces(collider, 0, _boundsCache);
            }
        }

        #endregion PROCESS
    }
}