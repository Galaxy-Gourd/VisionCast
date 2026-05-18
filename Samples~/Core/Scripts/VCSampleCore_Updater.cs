using System.Collections;
using GalaxyGourd.Raycast;
using GalaxyGourd.Visioncast;
using UnityEngine;

namespace VisioncastSamples.Core
{
    [DefaultExecutionOrder(-500)]
    public class VCSampleCore_Updater : MonoBehaviour
    {
        #region INITIALIZATION

        private void Awake()
        {
            VisioncastManager.Setup();
        }
        
        private void OnDestroy()
        {
            VisioncastManager.Dispose();
        }

        #endregion INITIALIZATION


        #region TICK

        private void FixedUpdate()
        {
            float delta = Time.fixedDeltaTime;
            RaycastManager.TickRaycasts(delta);
            VisioncastManager.TickVisioncasts(delta);
        }

        private void LateUpdate()
        {
            VisioncastManager.TickVisioncastSourceDebug(Time.deltaTime);
        }

        #endregion
    }
}