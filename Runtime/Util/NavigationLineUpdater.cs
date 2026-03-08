using System.Collections.Generic;
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

        private Vector3[] _positionBuffer = new Vector3[256];

        private void LateUpdate()
        {
            if (!navigator) return;
            var points = navigator.CurrentPoints;
            if (points == _prevPoints) return;

            _lineUpdatePerfMarker.Begin();
            if (_positionBuffer.Length < points.Count)
                _positionBuffer = new Vector3[(int)(points.Count * 1.1) + 16];

            // Linq results in allocations, so a normal for loop is better.
            // Also, we are re-using the result buffer.
            Vector3? prev = null;
            var count = 0;
            for (var i = 0; i < points.Count; ++i)
            {
                var pos = Vector3.Scale(points[i].position, Vector3.forward + Vector3.right) + Vector3.up * 100;
                if (prev == null || !((pos - prev.Value).sqrMagnitude < tolerance * tolerance))
                    _positionBuffer[count++] = pos;
                prev = pos;
            }

            _lineUpdatePerfMarker.End();
            lineRenderer.positionCount = count;
            lineRenderer.SetPositions(_positionBuffer);
            lineRenderer.widthMultiplier = lineWidth;

            _prevPoints = points;
        }
    }
}
