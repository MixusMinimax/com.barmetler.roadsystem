using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Serialization;

namespace Barmetler.RoadSystem.Util
{
    /// <summary>
    /// Updates a LineRenderer based on a RoadSystemNavigator
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(LineRenderer))]
    public class NavigationLineUpdater : MonoBehaviour
    {
        [SerializeField]
        private RoadSystemNavigator navigator;

        [FormerlySerializedAs("Tolerance")]
        [SerializeField]
        private float tolerance = 0.1f;

        [FormerlySerializedAs("LineWidth")]
        [SerializeField]
        private float lineWidth = 2;

        [SerializeField, HideInInspector]
        private LineRenderer lineRenderer;

        private List<Bezier.OrientedPoint> _prevPoints;

        private void OnValidate()
        {
            lineRenderer = GetComponent<LineRenderer>();
        }

        private void Awake()
        {
            OnValidate();
        }

        private static ProfilerMarker _lineUpdatePerfMarker = new ProfilerMarker("NavigationLineUpdater simplify");

        private void LateUpdate()
        {
            if (!navigator) return;
            var points = navigator.CurrentPoints;
            if (points == _prevPoints) return;

            Vector3? prev = null;
            _lineUpdatePerfMarker.Begin();
            var positions = points
                .Select(e => Vector3.Scale(e.position, Vector3.forward + Vector3.right) + Vector3.up * 100)
                .Where(e =>
                {
                    var p = prev;
                    prev = e;
                    return p == null || !((e - p.Value).sqrMagnitude < tolerance * tolerance);
                })
                .ToArray();
            _lineUpdatePerfMarker.End();
            lineRenderer.positionCount = positions.Length;
            lineRenderer.SetPositions(positions);
            lineRenderer.widthMultiplier = lineWidth;

            _prevPoints = points;
        }
    }
}
