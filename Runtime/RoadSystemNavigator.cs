using System;
using System.Collections;
using System.Collections.Generic;
using Barmetler.RoadSystem.Util;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Serialization;

namespace Barmetler.RoadSystem
{
    using PointList = List<Bezier.OrientedPoint>;

    public class RoadSystemNavigator : MonoBehaviour
    {
        public RoadSystem currentRoadSystem;

        [FormerlySerializedAs("Goal")]
        public Vector3 goal = Vector3.zero;

        [FormerlySerializedAs("GraphStepSize")]
        public float graphStepSize = 1;

        [FormerlySerializedAs("MinDistanceYScale")]
        public float minDistanceYScale = 1;

        [FormerlySerializedAs("MinDistanceToRoadToConnect")]
        public float minDistanceToRoadToConnect = 10;

        [Obsolete("Use goal instead")]
        public Vector3 Goal
        {
            get => goal;
            set => goal = value;
        }

        [Obsolete("Use graphStepSize instead")]
        public float GraphStepSize
        {
            get => graphStepSize;
            set => graphStepSize = value;
        }

        [Obsolete("Use minDistanceYScale instead")]
        public float MinDistanceYScale
        {
            get => minDistanceYScale;
            set => minDistanceYScale = value;
        }

        [Obsolete("Use minDistanceToRoadToConnect instead")]
        public float MinDistanceToRoadToConnect
        {
            get => minDistanceToRoadToConnect;
            set => minDistanceToRoadToConnect = value;
        }

        public PointList CurrentPoints { private set; get; } = new PointList();
        private AsyncUpdater<PointList> _currentPoints;

        [SerializeField]
        [Tooltip("If true, runs the path finding in another thread.\n" +
                 "The path will become available in the next frame at the earliest.\n" +
                 "This can't be changed at run-time.")]
        private bool async = true;

        [SerializeField]
        [Tooltip("Use this to stop the navigator from updating if async is enabled.\n" +
                 "It serves as a soft shutdown, as it allows the current update to finish without triggering more.\n" +
                 "After the last update is finished, you can disable the entire navigator.")]
        private bool updateEnabled;

        private bool _updateRunning;

        public IEnumerator SetUpdateEnabledAsync(bool value)
        {
            updateEnabled = value;
            if (!value) yield return new WaitWhile(() => _updateRunning);
        }

        private void Update()
        {
            _currentPoints ??= async
                ? new AsyncUpdater<PointList>(this, GetNewWayPointsAsync, new PointList(), 1f / 144)
                : new AsyncUpdater<PointList>(this, GetNewWayPoints, new PointList(), 1f / 144);
            if (!updateEnabled) return;
            _currentPoints.Update();
            var points = _currentPoints.GetData();
            if (points != CurrentPoints)
            {
                CurrentPoints = points;
                RemovePointsAhead();
            }

            RemovePointsBehind();
        }

        public float GetMinDistance(out Road road, out Vector3 closestPoint, out float distanceAlongRoad)
        {
            if (!currentRoadSystem)
            {
                road = null;
                closestPoint = Vector3.zero;
                distanceAlongRoad = 0;
                return float.PositiveInfinity;
            }

            return currentRoadSystem.GetMinDistance(transform.position, Mathf.Max(0.1f, graphStepSize),
                minDistanceYScale, out road, out closestPoint, out distanceAlongRoad);
        }

        public float GetMinDistance(
            out Intersection intersection, out RoadAnchor anchor, out Vector3 closestPoint, out float distanceAlongRoad)
        {
            if (!currentRoadSystem)
            {
                intersection = null;
                anchor = null;
                closestPoint = Vector3.zero;
                distanceAlongRoad = 0;
                return float.PositiveInfinity;
            }

            return currentRoadSystem.GetMinDistance(
                transform.position, minDistanceYScale, out intersection, out anchor, out closestPoint,
                out distanceAlongRoad);
        }

        private void RemovePointsBehind()
        {
            var pos = transform.position;
            var count = 0;
            for (; count < CurrentPoints.Count - 1; ++count)
            {
                // if next point is further away, stop (but don't stop if current point is really close)
                var sqrDst = (CurrentPoints[count].position - pos).sqrMagnitude;
                if (
                    sqrDst < (CurrentPoints[count + 1].position - pos).sqrMagnitude &&
                    sqrDst > graphStepSize / 2 * graphStepSize / 2
                ) break;
            }

            if (count > 0)
            {
                CurrentPoints.RemoveRange(0, count);
            }
        }

        private void RemovePointsAhead()
        {
            var pos = goal;
            var count = 0;
            for (; count < CurrentPoints.Count - 1; ++count)
            {
                // if next point is further away, stop (but don't stop if current point is really close)
                var sqrDst = (CurrentPoints[CurrentPoints.Count - 1 - count].position - pos).sqrMagnitude;
                if (
                    sqrDst < (CurrentPoints[CurrentPoints.Count - 1 - count - 1].position - pos).sqrMagnitude &&
                    sqrDst > graphStepSize / 2 * graphStepSize / 2
                ) break;
            }

            if (count > 0)
            {
                CurrentPoints.RemoveRange(CurrentPoints.Count - count, count);
            }
        }

        public void CalculateWayPointsSync()
        {
            CurrentPoints = GetNewWayPoints();
        }

        private static readonly ProfilerMarker GetNewWayPointsPerfMarker =
            new ProfilerMarker("RoadSystemNavigator.cs GetNewWayPoints");

        private PointList GetNewWayPoints()
        {
            using var marker = GetNewWayPointsPerfMarker.Auto();
            return currentRoadSystem.FindPath(
                transform.position, goal,
                yScale: minDistanceYScale,
                stepSize: Mathf.Max(0.1f, graphStepSize),
                minDstToRoadToConnect: minDistanceToRoadToConnect
            );
        }

        private static readonly ProfilerMarker CancelPerfMarker =
            new ProfilerMarker("RoadSystemNavigator.cs Cancel");

        private event Action Cancel;

        private void OnDisable()
        {
            StopAllCoroutines();
            using var marker = CancelPerfMarker.Auto();
            Cancel?.Invoke(); // this may block.
            Cancel = null;
            _updateRunning = false;
        }

        private IEnumerator GetNewWayPointsAsync(Consumer<PointList> resultConsumer)
        {
            _updateRunning = true;
            GetNewWayPointsPerfMarker.Begin();
            var findPathEn = currentRoadSystem.FindPathAsync(
                action => Cancel += action,
                resultConsumer,
                transform.position, goal,
                yScale: minDistanceYScale,
                stepSize: Mathf.Max(0.1f, graphStepSize),
                minDstToRoadToConnect: minDistanceToRoadToConnect
            );
            GetNewWayPointsPerfMarker.End();
            yield return findPathEn;
            Cancel = null;
            _updateRunning = false;
        }
    }
}
