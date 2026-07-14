using System.Collections.Generic;
using UnityEngine;

namespace GalaxyGourd.Visioncast
{
    /// <summary>
    /// Combines the refined vision of several child sources into a single de-duplicated output. Each
    /// child <see cref="VisioncastSourceFiltered"/> casts independently; the compound merges their
    /// per-collider results so a target resolved by ANY child is reported once, with its visibility
    /// aggregated across the children that see it (see <see cref="VisibilityAggregation"/>).
    ///
    /// When actor grouping is enabled, colliders registered against the same actor in
    /// <see cref="VisionTargetsManifest"/> collapse into a single target - so a multi-collider actor
    /// yields one visibility score. Colliders with no registered actor stand alone.
    ///
    /// Typical use is stealth exposure from multiple lights: parent several light-mounted sources
    /// under one compound and query the combined visibility per actor. The compound does NOT cast
    /// itself - it only aggregates its children.
    /// </summary>
    public class VisioncastSourceCompound : MonoBehaviour
    {
        #region VARIABLES

        [Header("Sources")]
        [SerializeField] private List<VisioncastSourceFiltered> _sources = new();

        [Header("Aggregation")]
        [Tooltip("How a target's visibility is combined across the child sources that resolve it.")]
        [SerializeField] private VisibilityAggregation _aggregation = VisibilityAggregation.Max;
        [Tooltip("Collapse colliders owned by the same actor (VisionTargetsManifest) into one target.")]
        [SerializeField] private bool _groupByActor = true;
        [Tooltip("Combine automatically each LateUpdate. Disable to drive Combine() from your own tick.")]
        [SerializeField] private bool _autoCombine = true;

        /// <summary>Combined, de-duplicated vision targets across all active child sources.</summary>
        public IReadOnlyList<DataVisionSeenTarget> VisionTargets => _combined;
        /// <summary>Representative colliders of targets that became visible since the previous combine.</summary>
        public IReadOnlyList<Collider> NewlySeenObjects => _newlySeen;
        /// <summary>Representative colliders of targets that stopped being visible since the previous combine.</summary>
        public IReadOnlyList<Collider> NewlyLostObjects => _newlyLost;

        private readonly List<DataVisionSeenTarget> _combined = new();
        private readonly List<DataVisionSeenTarget> _previous = new();
        private readonly HashSet<Component> _previousVisible = new();
        // aggregation key (actor when grouped, else the collider) -> index into _combined
        private readonly Dictionary<Component, int> _index = new();
        // Per-combined-target aggregation scratch (aligned with _combined)
        private readonly List<float> _maxVis = new();
        private readonly List<float> _sumVis = new();
        private readonly List<int> _visibleCount = new();
        private readonly List<Collider> _newlySeen = new();
        private readonly List<Collider> _newlyLost = new();

        #endregion VARIABLES


        #region SOURCES

        public void AddSource(VisioncastSourceFiltered source)
        {
            if (source && !_sources.Contains(source))
                _sources.Add(source);
        }

        public void RemoveSource(VisioncastSourceFiltered source)
        {
            _sources.Remove(source);
        }

        #endregion SOURCES


        #region TICK

        private void LateUpdate()
        {
            if (_autoCombine)
                Combine();
        }

        #endregion TICK


        #region COMBINE

        /// <summary>
        /// Merges the current results of all active child sources into the combined output and
        /// recomputes the newly-seen / newly-lost sets relative to the previous combine.
        /// </summary>
        public void Combine()
        {
            // Snapshot the previous result for transition diffing
            _previous.Clear();
            _previous.AddRange(_combined);
            _previousVisible.Clear();
            for (int i = 0; i < _previous.Count; i++)
            {
                if (_previous[i].IsVisible)
                    _previousVisible.Add(KeyOf(_previous[i]));
            }

            // Reset the working buffers
            _combined.Clear();
            _index.Clear();
            _maxVis.Clear();
            _sumVis.Clear();
            _visibleCount.Clear();

            // Fold every active child's targets into the combined set
            for (int s = 0; s < _sources.Count; s++)
            {
                VisioncastSourceFiltered source = _sources[s];

                // A disabled source is no longer casting; its data is stale, so skip it
                if (!source || !source.isActiveAndEnabled)
                    continue;

                IReadOnlyList<DataVisionSeenObject> targets = source.VisionTargets;
                for (int t = 0; t < targets.Count; t++)
                {
                    MergeTarget(targets[t]);
                }
            }

            // Resolve aggregated visibility now that all contributions are folded
            for (int i = 0; i < _combined.Count; i++)
            {
                DataVisionSeenTarget entry = _combined[i];
                entry.Visibility = AggregateVisibility(i);
                _combined[i] = entry;
            }

            ResolveTransitions();
        }

        private void MergeTarget(DataVisionSeenObject incoming)
        {
            Collider col = incoming.ResultObject;
            if (col == null)
                return;

            Component key = ResolveKey(col, out Component actor);

            if (_index.TryGetValue(key, out int i))
            {
                DataVisionSeenTarget entry = _combined[i];
                entry.IsVisible |= incoming.IsVisible;
                entry.Distance = Mathf.Min(entry.Distance, incoming.Distance);
                entry.Angle = Mathf.Min(entry.Angle, incoming.Angle);
                entry.VisiblePointCount += incoming.VisiblePointCount;
                entry.SampleCount += incoming.SampleCount;

                // The representative collider is the most-visible contribution
                if (incoming.Visibility > _maxVis[i])
                    entry.Collider = col;

                _combined[i] = entry;

                _maxVis[i] = Mathf.Max(_maxVis[i], incoming.Visibility);
                _sumVis[i] += incoming.Visibility;
                if (incoming.IsVisible)
                    _visibleCount[i]++;
            }
            else
            {
                _index[key] = _combined.Count;
                _combined.Add(new DataVisionSeenTarget
                {
                    Actor = actor,
                    Collider = col,
                    IsVisible = incoming.IsVisible,
                    JustBecameVisible = false, // resolved in ResolveTransitions
                    Distance = incoming.Distance,
                    Angle = incoming.Angle,
                    VisiblePointCount = incoming.VisiblePointCount,
                    SampleCount = incoming.SampleCount,
                    Visibility = 0f // resolved in the finalize pass
                });
                _maxVis.Add(incoming.Visibility);
                _sumVis.Add(incoming.Visibility);
                _visibleCount.Add(incoming.IsVisible ? 1 : 0);
            }
        }

        private float AggregateVisibility(int i)
        {
            switch (_aggregation)
            {
                case VisibilityAggregation.Sum:
                    return Mathf.Clamp01(_sumVis[i]);
                case VisibilityAggregation.Average:
                    return _visibleCount[i] > 0 ? _sumVis[i] / _visibleCount[i] : 0f;
                default: // Max
                    return _maxVis[i];
            }
        }

        private void ResolveTransitions()
        {
            _newlySeen.Clear();
            _newlyLost.Clear();

            // Newly seen: visible now, but not visible in the previous combine
            for (int i = 0; i < _combined.Count; i++)
            {
                DataVisionSeenTarget entry = _combined[i];
                if (entry.IsVisible && !_previousVisible.Contains(KeyOf(entry)))
                {
                    entry.JustBecameVisible = true;
                    _combined[i] = entry;
                    _newlySeen.Add(entry.Collider);
                }
            }

            // Newly lost: visible previously, but not visible now
            for (int i = 0; i < _previous.Count; i++)
            {
                DataVisionSeenTarget prev = _previous[i];
                if (prev.IsVisible && !IsVisibleNow(KeyOf(prev)))
                    _newlyLost.Add(prev.Collider);
            }
        }

        #endregion COMBINE


        #region QUERY

        /// <summary>
        /// Combined visibility in [0, 1] of the target owning <paramref name="col"/>, or 0 if no
        /// active child resolves it.
        /// </summary>
        public bool TryGetVisibility(Collider col, out float visibility)
        {
            if (col != null)
                return TryGetVisibility(ResolveKey(col), out visibility);

            visibility = 0f;
            return false;
        }

        /// <summary>
        /// Combined visibility in [0, 1] of an actor grouped in the combined output, or 0 if no active
        /// child resolves any of its colliders.
        /// </summary>
        public bool TryGetVisibility(Component actor, out float visibility)
        {
            if (actor && _index.TryGetValue(actor, out int i))
            {
                visibility = _combined[i].Visibility;
                return true;
            }

            visibility = 0f;
            return false;
        }

        /// <summary>True if any active child source currently sees the target owning the collider.</summary>
        public bool IsVisible(Collider col)
        {
            return col != null && IsVisibleNow(ResolveKey(col));
        }

        /// <summary>True if any active child source currently sees the actor.</summary>
        public bool IsVisible(Component actor)
        {
            return actor && IsVisibleNow(actor);
        }

        #endregion QUERY


        #region UTILITY

        /// <summary>
        /// Aggregation key for a collider: its registered actor when grouping is enabled, otherwise
        /// the collider itself (a Collider is a Component, so both share the key type).
        /// </summary>
        private Component ResolveKey(Collider col, out Component actor)
        {
            if (_groupByActor && VisionTargetsManifest.TryGetActor(col, out actor))
                return actor;

            actor = null;
            return col;
        }

        private Component ResolveKey(Collider col)
        {
            return ResolveKey(col, out _);
        }

        private static Component KeyOf(DataVisionSeenTarget target)
        {
            return target.Actor ? target.Actor : (Component)target.Collider;
        }

        private bool IsVisibleNow(Component key)
        {
            return key && _index.TryGetValue(key, out int i) && _combined[i].IsVisible;
        }

        #endregion UTILITY
    }
}
