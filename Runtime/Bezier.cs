using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Barmetler
{
	public static class Bezier
	{
		public static Vector3 EvaluateQuadratic(Vector3 a, Vector3 b, Vector3 c, float t)
		{
			Vector3 p0 = Vector3.Lerp(a, b, t);
			Vector3 p1 = Vector3.Lerp(b, c, t);
			return Vector3.Lerp(p0, p1, t);
		}

		public static Vector3 EvaluateCubic(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
		{
			Vector3 p0 = EvaluateQuadratic(a, b, c, t);
			Vector3 p1 = EvaluateQuadratic(b, c, d, t);
			return Vector3.Lerp(p0, p1, t);
		}

		public struct EvenlySpacedPoint
		{
			public Vector3 position; public Vector3 forward; public Vector3 normal;
			public EvenlySpacedPoint(Vector3 p, Vector3 f, Vector3 n) { position = p; forward = f; normal = n; }

			public EvenlySpacedPoint ToWorldSpace(Transform transform)
			{
				var p = transform.TransformPoint(position);
				var f = transform.TransformDirection(forward);
				var n = transform.TransformDirection(normal);
				return new EvenlySpacedPoint(p, f, n);
			}

			public EvenlySpacedPoint ToLocalSpace(Transform transform)
			{
				var p = transform.InverseTransformPoint(position);
				var f = transform.InverseTransformDirection(forward);
				var n = transform.InverseTransformDirection(normal);
				return new EvenlySpacedPoint(p, f, n);
			}
		}

		public static EvenlySpacedPoint[] GetEvenlySpacedPoints(
			IEnumerable<Vector3> points, List<float> angles, float spacing, float resolution = 1)
		{
			var b = new Bounds();
			return GetEvenlySpacedPoints(points, angles, ref b, null, spacing, resolution);
		}

		public static EvenlySpacedPoint[] GetEvenlySpacedPoints(
			IEnumerable<Vector3> points, List<float> angles, ref Bounds bounds, List<Bounds> boundingBoxes,
			float spacing, float resolution = 1)
		{
			var _points = points.ToList();
			var NumPoints = _points.Count;
			var NumSegments = NumPoints / 3;
			int LoopIndex(int i) { return ((i % NumPoints) + NumPoints) % NumPoints; }
			Vector3[] GetPointsInSegment(int i)
			{
				return new Vector3[] { _points[i * 3], _points[i * 3 + 1], _points[i * 3 + 2], _points[LoopIndex(i * 3 + 3)] };
			}

			bounds.min = Vector3.positiveInfinity;
			bounds.max = Vector3.negativeInfinity;
			boundingBoxes?.Clear();

			var esp = new List<EvenlySpacedPoint>();
			esp.Add(new EvenlySpacedPoint(_points[0], Vector3.zero, Vector3.zero));
			var esAngles = new List<float>();
			esAngles.Add(angles[0]);

			Vector3 previousPoint = _points[0];
			float dstSinceLastEvenPoint = 0;

			for (int segment = 0; segment < NumSegments; ++segment)
			{
				Bounds segmentBounds = new Bounds
				{
					min = Vector3.positiveInfinity,
					max = Vector3.negativeInfinity
				};

				Vector3[] p = GetPointsInSegment(segment);

				// Initialize bounding box (in case no point gets created for this segment)
				segmentBounds.Encapsulate(p[0]);
				segmentBounds.Encapsulate(p[3]);

				float controlNetLength = Vector3.Distance(p[0], p[1]) + Vector3.Distance(p[1], p[2]) + Vector3.Distance(p[2], p[3]);
				float estimatedCurveLength = Vector3.Distance(p[0], p[3]) + 0.5f * controlNetLength;
				int divisions = Mathf.CeilToInt(estimatedCurveLength * resolution * 10);
				float t = 0;
				while (t <= 1)
				{
					t += 1f / divisions;
					Vector3 pointOnCurve = EvaluateCubic(p[0], p[1], p[2], p[3], t);
					dstSinceLastEvenPoint += Vector3.Distance(previousPoint, pointOnCurve);

					while (dstSinceLastEvenPoint >= spacing)
					{
						float overshootDst = dstSinceLastEvenPoint - spacing;
						Vector3 newEvenlySpacedPoint = pointOnCurve + (previousPoint - pointOnCurve).normalized * overshootDst;

						// Update bounding box
						segmentBounds.Encapsulate(newEvenlySpacedPoint);

						esp.Add(new EvenlySpacedPoint(newEvenlySpacedPoint, Vector3.zero, Vector3.zero));
						esAngles.Add(Mathf.Lerp(angles[segment], angles[segment + 1], t));

						dstSinceLastEvenPoint = overshootDst;
						previousPoint = newEvenlySpacedPoint;
					}

					previousPoint = pointOnCurve;
				}

				bounds.Encapsulate(segmentBounds);
				boundingBoxes?.Add(segmentBounds);
			}

			EvenlySpacedPoint[] result = esp.ToArray();
			result[result.Length - 1].position = _points[LoopIndex(-1)];

			for (int i = 0; i < result.Length; ++i)
			{
				if (i == 0) result[i].forward = (_points[1] - _points[0]).normalized;
				else if (i == result.Length - 1) result[i].forward = (_points[LoopIndex(-1)] - _points[LoopIndex(-2)]).normalized;
				else
					result[i].forward = (result[i + 1].position - result[i - 1].position).normalized;

				result[i].normal = NormalFromAngle(result[i].forward, esAngles[i]);
			}

			return result;
		}

		public static float AngleFromNormal(Vector3 forward, Vector3 normal)
		{
			forward = forward.normalized;
			normal = normal.normalized;
			normal = (normal - Vector3.Dot(forward, normal) * forward).normalized;
			Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
			Vector3 up = Vector3.Cross(forward, right).normalized;
			return Vector3.SignedAngle(normal, up, forward);
		}

		public static Vector3 NormalFromAngle(Vector3 forward, float angle)
		{
			Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
			Vector3 up = Vector3.Cross(forward, right).normalized;
			return Quaternion.AngleAxis(-angle, forward) * up;
		}
	}
}
