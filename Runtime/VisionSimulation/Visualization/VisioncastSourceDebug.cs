using System.Collections.Generic;
using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// Provides debug visuals for a visioncast source
    /// </summary>
    [RequireComponent(typeof(VisioncastSource))]
    public class VisioncastSourceDebug : MonoBehaviour
    {
        #region VARIABLES

        [Header("References")]
        [SerializeField] private LineOfSightRay _prefabLineDebug;
        [SerializeField] private GameObject _prefabVisionCone;
        
        [Header("Draw Options")]
        [SerializeField] private bool _gizmos = true;
        [SerializeField] private bool _lineRenderers;
        [SerializeField] private bool _pauseDrawing;
        [SerializeField] private bool _drawCone;
        
        private VisionCone _visionCone;
        private VisioncastSource _source;
        private readonly List<LineOfSightRay> _debugLines = new();
        
        #endregion VARIABLES


        #region INITIALIZATION

        private void Awake()
        {
            if (_drawCone)
                _visionCone = Instantiate(_prefabVisionCone, transform).GetComponent<VisionCone>();
            _source = GetComponent<VisioncastSource>();
            _source.AttachDebug(this);
        }

        private void OnDestroy()
        {
            _source.DetachDebug(this);
        }

        #endregion INITIALIZATION


        #region TICK

        internal void Tick(float delta)
        {
            if (_pauseDrawing)
                return;
            
            ClearLines();

            // Vision cone
            if (_visionCone)
            {
                _visionCone.CalculateCone(_source);
            }
            
            // LIne of sight rays
            DrawLineOfSightRays();
        }

        #endregion TICK


        #region LINE OF SIGHT

        private void DrawLineOfSightRays()
        {
            if (_source.LastResults.Objects == null)
                return;
            
            Vector3 sourcePosition = _source.Position;
            for (int i = 0; i < _source.LastResults.Objects.Count; i++)
            {
                if (_source.LastResults.Objects[i] == null)
                    continue;
                
                if (_source.LastResults.VisiblePoints[i].Count == 0 && _source.LastResults.Objects[i] != null)
                {
                    DrawLine(sourcePosition, _source.LastResults.Objects[i].transform.position, Color.red);
                }
                else
                {
                    for (int e = 0; e < _source.LastResults.VisiblePoints[i].Count; e++)
                    {
                        DrawLine(sourcePosition, _source.LastResults.VisiblePoints[i][e], Color.green);
                    }
                }
            }
        }
        
        private void DrawLine(Vector3 start, Vector3? end, Color color, float thickness = 1)
        {
            if (end is not { } v)
                return;
            
            if (_gizmos)
            {
                Debug.DrawLine(start, v, color);
            }

            if (_lineRenderers)
            {
                LineOfSightRay line = GetNextAvailableLine();
                if (line)
                {
                    line.SetParameters(start, v, color, thickness);
                    line.gameObject.SetActive(true);
                }
            }
        }

        #endregion LINE OF SIGHT


        #region UTILITY

        public void Toggle(bool on)
        {
            _gizmos = on;
            _lineRenderers = on;
        }
        
        private LineOfSightRay GetNextAvailableLine()
        {
            // Find first inactive line
            LineOfSightRay next = null;
            foreach (LineOfSightRay line in _debugLines)
            {
                if (!line.gameObject.activeSelf)
                {
                    next = line;
                    break;
                }
            }
            
            // If needed, create new line
            if (!next && _prefabLineDebug)
            {
                next = Instantiate(_prefabLineDebug.gameObject, transform).GetComponent<LineOfSightRay>();
                _debugLines.Add(next);
            }

            return next;
        }

        private void ClearLines()
        {
            foreach (LineOfSightRay line in _debugLines)
            {
                line.gameObject.SetActive(false);
            }
        }

        #endregion UTILITY
    }
}