using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Barmetler.RoadSystem
{
	public class RoadSystemNavigator : MonoBehaviour
	{
		public RoadSystem currentRoadSystem;

		public Vector3 Goal = Vector3.zero;

		public float GraphStepSize = 1;
		public float CornerSharpness = 0.6f;
		public float MinDistanceYScale = 1;

		public float GetMinDistance(out Road road, out Vector3 closestPoint, out float distanceAlongRoad)
		{
			if (currentRoadSystem == null)
			{
				road = null;
				closestPoint = Vector3.zero;
				distanceAlongRoad = 0;
				return float.PositiveInfinity;
			}
			return currentRoadSystem.GetMinDistance(transform.position, Mathf.Max(0.1f, GraphStepSize), MinDistanceYScale, out road, out closestPoint, out distanceAlongRoad);
		}

		public float GetMinDistance(
			out Intersection intersection, out RoadAnchor anchor, out Vector3 closestPoint, out float distanceAlongRoad)
		{
			if (currentRoadSystem == null)
			{
				intersection = null;
				anchor = null;
				closestPoint = Vector3.zero;
				distanceAlongRoad = 0;
				return float.PositiveInfinity;
			}
			return currentRoadSystem.GetMinDistance(
				transform.position, MinDistanceYScale, out intersection, out anchor, out closestPoint, out distanceAlongRoad);
		}

		private void FixedUpdate()
		{
			lock (lockObject)
			{
				if (newPoints != null)
				{
					CurrentPoints = newPoints;
					RemovePointsAhead();
					newPoints = null;
				}
			}


			if (!coroutineRunning && Goal != null && currentRoadSystem != null)
			{
				coroutineRunning = true;
				StartCoroutine(CalculateWayPointsAsync());
			}

			RemovePointsBehind();
		}

		private object lockObject = new object();
		private bool coroutineRunning = false;
		private List<Bezier.EvenlySpacedPoint> newPoints = null;
		public List<Bezier.EvenlySpacedPoint> CurrentPoints { private set; get; } = new List<Bezier.EvenlySpacedPoint>();

		void RemovePointsBehind()
		{
			var pos = transform.position;
			int count = 0;
			for (; count < CurrentPoints.Count - 1; ++count)
			{
				// if next point is further away, stop (but don't stop if current point is really close)
				float sqrDst = (CurrentPoints[count].position - pos).sqrMagnitude;
				if (
					sqrDst < (CurrentPoints[count + 1].position - pos).sqrMagnitude &&
					sqrDst > GraphStepSize / 2 * GraphStepSize / 2
					) break;
			}

			if (count > 0)
			{
				CurrentPoints.RemoveRange(0, count);
			}
		}

		void RemovePointsAhead()
		{
			var pos = Goal;
			int count = 0;
			for (; count < CurrentPoints.Count - 1; ++count)
			{
				// if next point is further away, stop (but don't stop if current point is really close)
				float sqrDst = (CurrentPoints[CurrentPoints.Count - 1 - count].position - pos).sqrMagnitude;
				if (
					sqrDst < (CurrentPoints[CurrentPoints.Count - 1 - count - 1].position - pos).sqrMagnitude &&
					sqrDst > GraphStepSize / 2 * GraphStepSize / 2
					) break;
			}

			if (count > 0)
			{
				CurrentPoints.RemoveRange(CurrentPoints.Count - count, count);
			}
		}

		IEnumerator CalculateWayPointsAsync()
		{
			if (currentRoadSystem != null)
			{
				try
				{
					CalculateNewWayPoints();
				}
				catch (System.Exception e)
				{
					Debug.LogError(e);
				}
			}
			coroutineRunning = false;
			yield return null;
		}

		public void CalculateWayPointsSync()
		{
			CalculateNewWayPoints();
			CurrentPoints = newPoints;
			//RemovePointsAhead();
			//RemovePointsBehind();
		}

		void CalculateNewWayPoints()
		{
			if (!currentRoadSystem) return;
			var points = currentRoadSystem.FindPath(transform.position, Goal, MinDistanceYScale, Mathf.Max(0.1f, GraphStepSize));

			lock (lockObject)
			{
				newPoints = points;
			}
		}
	}
}
