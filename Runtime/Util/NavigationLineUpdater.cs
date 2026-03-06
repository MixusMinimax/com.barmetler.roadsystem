using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

        [SerializeField]
        private float LineWidth = 2;

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

        private void LateUpdate()
        {
            if (!navigator) return;
            var points = navigator.CurrentPoints;
            if (points == _prevPoints) return;

            lineRenderer.positionCount = points.Count;
            lineRenderer.SetPositions(points
                .Select(e => Vector3.Scale(e.position, Vector3.forward + Vector3.right) + Vector3.up * 100)
                .ToArray());
            lineRenderer.widthMultiplier = LineWidth;

            _prevPoints = points;
        }
    }
}
