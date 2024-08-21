using System.Linq;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Barmetler
{
    public static class Bezier
    {
        public static Vector3 EvaluateQuadratic(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            var p0 = Vector3.Lerp(a, b, t);
            var p1 = Vector3.Lerp(b, c, t);
            return Vector3.Lerp(p0, p1, t);
        }

        public static Vector3 EvaluateCubic(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            var p0 = EvaluateQuadratic(a, b, c, t);
            var p1 = EvaluateQuadratic(b, c, d, t);
            return Vector3.Lerp(p0, p1, t);
        }

        public static Vector3 DeriveQuadratic(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            return Vector3.Lerp(2 * (b - a), 2 * (c - b), t);
        }

        public static Vector3 DeriveCubic(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            return EvaluateQuadratic(3 * (b - a), 3 * (c - b), 3 * (d - c), t);
        }

        /// <summary>
        /// Position and Direction Vectors.
        /// </summary>
        public struct OrientedPoint
        {
            public Vector3 position; public Vector3 forward; public Vector3 normal;
            public OrientedPoint(Vector3 p, Vector3 f, Vector3 n) { position = p; forward = f; normal = n; }

            public OrientedPoint ToWorldSpace(Transform transform)
            {
                var p = transform.TransformPoint(position);
                var f = transform.TransformDirection(forward);
                var n = transform.TransformDirection(normal);
                return new OrientedPoint(p, f, n);
            }

            public OrientedPoint ToLocalSpace(Transform transform)
            {
                var p = transform.InverseTransformPoint(position);
                var f = transform.InverseTransformDirection(forward);
                var n = transform.InverseTransformDirection(normal);
                return new OrientedPoint(p, f, n);
            }
        }

        public static OrientedPoint[] GetEvenlySpacedPoints(
            IEnumerable<Vector3> points, IEnumerable<Vector3> normals, float spacing, float resolution = 1)
        {
            return GetEvenlySpacedPoints(points, normals, out _, null, spacing, resolution);
        }

        public static OrientedPoint[] GetEvenlySpacedPoints(
            IEnumerable<Vector3> points, IEnumerable<Vector3> normals, out Bounds bounds, List<Bounds> boundingBoxes,
            float spacing, float resolution = 1, bool burst = true)
        {
            if (burst)
                return GetEvenlySpacedPointsBurst(points, normals, out bounds, boundingBoxes, spacing, resolution);
            var _points = points.ToList();
            var _normals = normals.ToList();
            var NumPoints = _points.Count;
            var NumSegments = NumPoints / 3;
            if (_normals.Count < NumSegments + 1)
                throw new System.ArgumentException("not enough normals!");
            int LoopIndex(int i) { return (i % NumPoints + NumPoints) % NumPoints; }
            Vector3[] GetPointsInSegment(int i)
            {
                return new[] { _points[i * 3], _points[i * 3 + 1], _points[i * 3 + 2], _points[LoopIndex(i * 3 + 3)] };
            }

            bounds = new Bounds
            {
                min = Vector3.positiveInfinity,
                max = Vector3.negativeInfinity
            };
            boundingBoxes?.Clear();

            float lineLength = 0;

            var esp = new List<OrientedPoint>();

            var previousPoint = _points[0] - (_points[1] - _points[0]).normalized * spacing;
            float dstSinceLastEvenPoint = 0;

            for (var segment = 0; segment < NumSegments; ++segment)
            {
                var segmentBounds = new Bounds
                {
                    min = Vector3.positiveInfinity,
                    max = Vector3.negativeInfinity
                };

                var p = GetPointsInSegment(segment);

                var normalOnCurve = _normals[segment];

                // Initialize bounding box
                segmentBounds.Encapsulate(p[0]);
                segmentBounds.Encapsulate(p[3]);

                var previousPointOnCurve = p[0];
                float segmentLength = 0;
                Vector3 forwardOnCurve;

                var controlNetLength = Vector3.Distance(p[0], p[1]) + Vector3.Distance(p[1], p[2]) + Vector3.Distance(p[2], p[3]);
                var estimatedCurveLength = Vector3.Distance(p[0], p[3]) + 0.5f * controlNetLength;
                var divisions = Mathf.CeilToInt(estimatedCurveLength * resolution * 10);
                var startIndex = esp.Count;
                var t = startIndex == 0 ? -1f / divisions : 0;
                while (t <= 1)
                {
                    t += 1f / divisions;
                    var pointOnCurve = EvaluateCubic(p[0], p[1], p[2], p[3], t);
                    if (t > -0.5f / divisions)
                        segmentLength += Vector3.Distance(pointOnCurve, previousPointOnCurve);
                    previousPointOnCurve = pointOnCurve;
                    forwardOnCurve = DeriveCubic(p[0], p[1], p[2], p[3], Mathf.Clamp01(t)).normalized;
                    normalOnCurve = Vector3.Cross(forwardOnCurve, Vector3.Cross(normalOnCurve, forwardOnCurve)).normalized;
                    dstSinceLastEvenPoint += Vector3.Distance(previousPoint, pointOnCurve);

                    while (dstSinceLastEvenPoint >= spacing)
                    {
                        var overshootDst = dstSinceLastEvenPoint - spacing;
                        var newEvenlySpacedPoint = pointOnCurve + (previousPoint - pointOnCurve).normalized * overshootDst;

                        // Update bounding box
                        segmentBounds.Encapsulate(newEvenlySpacedPoint);

                        esp.Add(new OrientedPoint(newEvenlySpacedPoint, forwardOnCurve, normalOnCurve));

                        dstSinceLastEvenPoint = overshootDst;
                        previousPoint = newEvenlySpacedPoint;
                    }

                    previousPoint = pointOnCurve;
                }
                var endIndexExclusive = esp.Count;

                if (startIndex != endIndexExclusive)
                {
                    segmentLength += Vector3.Distance(previousPointOnCurve, p[3]);
                    lineLength += segmentLength;

                    forwardOnCurve = DeriveCubic(p[0], p[1], p[2], p[3], 1).normalized;
                    normalOnCurve = Vector3.Cross(forwardOnCurve, Vector3.Cross(normalOnCurve, forwardOnCurve)).normalized;
                    var angleError = Vector3.SignedAngle(normalOnCurve, _normals[segment + 1], forwardOnCurve);

                    // Iterate over evenly spaced points in this segment, and gradually correct angle error
                    var tStep = spacing / segmentLength;
                    var tStart = Vector3.Distance(esp[startIndex].position, p[0]) / segmentLength;
                    for (var i = startIndex; i < endIndexExclusive; ++i)
                    {
                        var t_ = (i - startIndex) * tStep + tStart;
                        // TODO: make weight non-linear, depending on handle lengths
                        var correction = t_ * angleError;
                        var element = esp[i];
                        element.normal = Quaternion.AngleAxis(correction, element.forward) * element.normal;
                        esp[i] = element;
                    }
                }

                bounds.Encapsulate(segmentBounds);
                boundingBoxes?.Add(segmentBounds);
            }

            var result = esp.ToArray();
			result[0].position = _points[0];
			result[0].normal = _normals[0];
            result[0].forward = DeriveCubic(_points[0], _points[1], _points[2], _points[3], 0).normalized;
            result[result.Length - 1].position = _points[LoopIndex(-1)];
            result[result.Length - 1].normal = _normals[_normals.Count - 1];
            result[result.Length - 1].forward = DeriveCubic(_points[LoopIndex(-4)], _points[LoopIndex(-3)], _points[LoopIndex(-2)], _points[LoopIndex(-1)], 1).normalized;

            return result;
        }

        public static OrientedPoint[] GetEvenlySpacedPointsBurst(
            IEnumerable<Vector3> points, IEnumerable<Vector3> normals, out Bounds bounds, List<Bounds> boundingBoxes,
            float spacing, float resolution = 1)
        {
            var job = new GetEvenlySpacedPointsBurstJob
            {
                Points = new NativeArray<Vector3>(points.ToArray(), Allocator.TempJob),
                Normals = new NativeArray<Vector3>(normals.ToArray(), Allocator.TempJob),
                Spacing = spacing,
                Resolution = resolution,
                Result = new NativeList<OrientedPoint>(Allocator.TempJob),
                Bounds = new Bounds(),
                BoundingBoxes = new NativeList<Bounds>(Allocator.TempJob)
            };
            job.Run();
            var result = job.Result.ToArray();
            bounds = job.Bounds;
            boundingBoxes?.Clear();
            boundingBoxes?.AddRange(job.BoundingBoxes.ToArray());
            job.Points.Dispose();
            job.Normals.Dispose();
            job.Result.Dispose();
            job.BoundingBoxes.Dispose();
            return result;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct GetEvenlySpacedPointsBurstJob : IJob
        {
            [ReadOnly] public NativeArray<Vector3> Points;
            [ReadOnly] public NativeArray<Vector3> Normals;
            [ReadOnly] public float Spacing;
            [ReadOnly] public float Resolution;
            public NativeList<OrientedPoint> Result;
            public Bounds Bounds;
            public NativeList<Bounds> BoundingBoxes;
            public float LineLength;

            private int _numPoints;
            
            int LoopIndex(int i)
            {
                return (i % _numPoints + _numPoints) % _numPoints;
            }

            private struct Segment
            {
                public Vector3 p0, p1, p2, p3;
                
                public Segment(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
                {
                    this.p0 = p0;
                    this.p1 = p1;
                    this.p2 = p2;
                    this.p3 = p3;
                }

                public Vector3 this[int i] =>
                    i switch
                    {
                        0 => p0,
                        1 => p1,
                        2 => p2,
                        3 => p3,
                        _ => throw new System.ArgumentOutOfRangeException()
                    };
            }

            Segment GetPointsInSegment(int i)
            {
                return new Segment(Points[i * 3], Points[i * 3 + 1], Points[i * 3 + 2], Points[LoopIndex(i * 3 + 3)]);
            }
            
            public void Execute()
            {
                _numPoints = Points.Length;
                var numSegments = _numPoints / 3;
                if (Normals.Length < numSegments + 1)
                    throw new System.ArgumentException("not enough normals!");
                
                Bounds.min = Vector3.positiveInfinity;
                Bounds.max = Vector3.negativeInfinity;
                BoundingBoxes.Clear();

                LineLength = 0;

                var previousPoint = Points[0] - (Points[1] - Points[0]).normalized * Spacing;
                float dstSinceLastEvenPoint = 0;

                for (var segment = 0; segment < numSegments; ++segment)
                {
                    var segmentBounds = new Bounds
                    {
                        min = Vector3.positiveInfinity,
                        max = Vector3.negativeInfinity
                    };

                    var p = GetPointsInSegment(segment);

                    var normalOnCurve = Normals[segment];

                    // Initialize bounding box
                    segmentBounds.Encapsulate(p[0]);
                    segmentBounds.Encapsulate(p[3]);

                    var previousPointOnCurve = p[0];
                    float segmentLength = 0;
                    Vector3 forwardOnCurve;

                    var controlNetLength = Vector3.Distance(p[0], p[1]) + Vector3.Distance(p[1], p[2]) +
                                           Vector3.Distance(p[2], p[3]);
                    var estimatedCurveLength = Vector3.Distance(p[0], p[3]) + 0.5f * controlNetLength;
                    var divisions = Mathf.CeilToInt(estimatedCurveLength * Resolution * 10);
                    var startIndex = Result.Length;
                    var t = startIndex == 0 ? -1f / divisions : 0;
                    while (t <= 1)
                    {
                        t += 1f / divisions;
                        var pointOnCurve = EvaluateCubic(p[0], p[1], p[2], p[3], t);
                        if (t > -0.5f / divisions)
                            segmentLength += Vector3.Distance(pointOnCurve, previousPointOnCurve);
                        previousPointOnCurve = pointOnCurve;
                        forwardOnCurve = DeriveCubic(p[0], p[1], p[2], p[3], Mathf.Clamp01(t)).normalized;
                        normalOnCurve = Vector3.Cross(forwardOnCurve, Vector3.Cross(normalOnCurve, forwardOnCurve))
                            .normalized;
                        dstSinceLastEvenPoint += Vector3.Distance(previousPoint, pointOnCurve);

                        while (dstSinceLastEvenPoint >= Spacing)
                        {
                            var overshootDst = dstSinceLastEvenPoint - Spacing;
                            var newEvenlySpacedPoint =
                                pointOnCurve + (previousPoint - pointOnCurve).normalized * overshootDst;

                            // Update bounding box
                            segmentBounds.Encapsulate(newEvenlySpacedPoint);

                            Result.Add(new OrientedPoint(newEvenlySpacedPoint, forwardOnCurve, normalOnCurve));

                            dstSinceLastEvenPoint = overshootDst;
                            previousPoint = newEvenlySpacedPoint;
                        }

                        previousPoint = pointOnCurve;
                    }

                    var endIndexExclusive = Result.Length;

                    if (startIndex != endIndexExclusive)
                    {
                        segmentLength += Vector3.Distance(previousPointOnCurve, p[3]);
                        LineLength += segmentLength;

                        forwardOnCurve = DeriveCubic(p[0], p[1], p[2], p[3], 1).normalized;
                        normalOnCurve = Vector3.Cross(forwardOnCurve, Vector3.Cross(normalOnCurve, forwardOnCurve))
                            .normalized;
                        var angleError = Vector3.SignedAngle(normalOnCurve, Normals[segment + 1], forwardOnCurve);

                        // Iterate over evenly spaced points in this segment, and gradually correct angle error
                        var tStep = Spacing / segmentLength;
                        var tStart = Vector3.Distance(Result[startIndex].position, p[0]) / segmentLength;
                        for (var i = startIndex; i < endIndexExclusive; ++i)
                        {
                            var t_ = (i - startIndex) * tStep + tStart;
                            // TODO: make weight non-linear, depending on handle lengths
                            var correction = t_ * angleError;
                            var element = Result[i];
                            element.normal = Quaternion.AngleAxis(correction, element.forward) * element.normal;
                            Result[i] = element;
                        }
                    }

                    Bounds.Encapsulate(segmentBounds);
                    BoundingBoxes.Add(segmentBounds);
                }

                if (Result.Length <= 0) return;
                var start = Result[0];
                start.position = Points[0];
                start.normal = Normals[0];
                start.forward = DeriveCubic(Points[0], Points[1], Points[2], Points[3], 0).normalized;
                Result[0] = start;
                if (Result.Length <= 1) return;
                var end = Result[Result.Length - 1];
                end.position = Points[LoopIndex(-1)];
                end.normal = Normals[Normals.Length - 1];
                end.forward = DeriveCubic(Points[LoopIndex(-4)], Points[LoopIndex(-3)],
                    Points[LoopIndex(-2)], Points[LoopIndex(-1)], 1).normalized;
                Result[Result.Length - 1] = end;
            }
        }

        public static float AngleFromNormal(Vector3 forward, Vector3 normal)
        {
            forward = forward.normalized;
            normal = normal.normalized;
            normal = (normal - Vector3.Dot(forward, normal) * forward).normalized;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var up = Vector3.Cross(forward, right).normalized;
            return Vector3.SignedAngle(normal, up, forward);
        }

        public static Vector3 NormalFromAngle(Vector3 forward, float angle)
        {
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var up = Vector3.Cross(forward, right).normalized;
            return Quaternion.AngleAxis(-angle, forward) * up;
        }
    }
}
