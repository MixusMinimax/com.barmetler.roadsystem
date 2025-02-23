using System;
using System.Collections.Generic;
using System.Linq;
using Barmetler.RoadSystem.Util;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Barmetler.RoadSystem
{
    using static math;


    [RequireComponent(typeof(Road)), RequireComponent(typeof(MeshFilter))]
    public class RoadMeshGenerator : MonoBehaviour
    {
        [Serializable]
        public class RoadMeshSettings
        {
            [Tooltip("Orientation of the Source Mesh")]
            public MeshConversion.MeshOrientation SourceOrientation = MeshConversion.MeshOrientation.Presets["BLENDER"];

            [Tooltip("By how much to displace uvs every time the mesh tiles")]
            public Vector2 uvOffset = Vector2.up;
        }

        [Tooltip("Settings regarding mesh generation")]
        public RoadMeshSettings settings;

        public bool AutoGenerate
        {
            get => autoGenerate;
            set
            {
                if (value)
                    GenerateRoadMesh();
                autoGenerate = value;
            }
        }

        [SerializeField, HideInInspector]
        private bool autoGenerate;

        [Tooltip("Drag the model to be used for mesh generation into this slot")]
        public MeshFilter SourceMesh;

        public bool Valid { private set; get; }

        private Road road;
        private MeshFilter mf;

        private void OnValidate()
        {
            road = GetComponent<Road>();
            mf = GetComponent<MeshFilter>();
        }

        private static ProfilerMarker _extractResultsMarker = new ProfilerMarker("Extract Results");
        private static ProfilerMarker _disposeMarker = new ProfilerMarker("Dispose");
        private static ProfilerMarker _setVerticesMarker = new ProfilerMarker("Set Vertices");
        private static ProfilerMarker _setIndicesMarker = new ProfilerMarker("Set Indices");
        private static ProfilerMarker _setUVsMarker = new ProfilerMarker("Set UVs");
        private static ProfilerMarker _recalculateNormalsMarker = new ProfilerMarker("Recalculate Normals");
        private static ProfilerMarker _recalculateTangentsMarker = new ProfilerMarker("Recalculate Tangents");
        private static ProfilerMarker _recalculateBoundsMarker = new ProfilerMarker("Recalculate Bounds");

        /// <summary>
        /// Generate the mesh based on the curve described in the Road component.
        /// </summary>
        public void GenerateRoadMesh()
        {
            OnValidate();

            if (!road) road = GetComponent<Road>();
            if (!road) return;
            if (!SourceMesh) return;

            float stepSize = 1;

            var points = road.GetEvenlySpacedPoints(stepSize, 1);

            var oldMesh = MeshConversion.CopyMesh(SourceMesh.sharedMesh);
            MeshConversion.TransformMesh(oldMesh, settings.SourceOrientation);
            var newMesh = new Mesh();

            var meshLength = oldMesh.bounds.size.z;
            {
                var meshOffset = -oldMesh.bounds.min.z;
                oldMesh.SetVertices(oldMesh.vertices.Select(v => v + meshOffset * Vector3.forward).ToArray());
            }

            // The last point is repositioned to the end of the bezier
            var bezierLength = points.Length > 1
                ? stepSize * (points.Length - 2) +
                  (points[points.Length - 2].position - points[points.Length - 1].position).magnitude
                : 0;

            var completeCopies = Mathf.FloorToInt(bezierLength / meshLength);

            var submeshCount = oldMesh.subMeshCount;

            var oldVertices = new List<Vector3>();
            oldMesh.GetVertices(oldVertices);
            var oldIndices = Enumerable.Range(0, submeshCount).Select(i => new List<int>(oldMesh.GetIndices(i)))
                .ToArray();
            var oldUVs = Enumerable.Range(0, 8)
                .Select(channel =>
                {
                    var x = new List<Vector2>();
                    oldMesh.GetUVs(channel, x);
                    return x;
                })
                .ToArray();

            var job = new GenerateRoadMeshJob
            {
                Points = new NativeArray<Bezier.OrientedPoint>(
                    points.Select(p => new Bezier.OrientedPoint(p.position, p.forward, p.normal)).ToArray(),
                    Allocator.TempJob),
                Vertices = new NativeArray<float3>(oldVertices.ToArray().Select(e => (float3)e).ToArray(),
                    Allocator.TempJob),
                Indices = new UnsafeList<UnsafeList<int>>(
                    oldIndices.Length,
                    Allocator.TempJob),
                UVs = new UnsafeList<UnsafeList<float2>>(oldUVs.Length, Allocator.TempJob),
                CompleteCopies = completeCopies,
                MeshLength = meshLength,
                BezierLength = bezierLength,
                StepSize = stepSize,
                UVOffset = settings.uvOffset,
                ResultVertices = new NativeList<float3>(Allocator.TempJob),
                ResultIndices = new UnsafeList<UnsafeList<int>>(submeshCount, Allocator.TempJob),
                ResultUVs = new UnsafeList<UnsafeList<float2>>(8, Allocator.TempJob),
                IntersectedIndices = new NativeHashMap<int2, int>(128, Allocator.TempJob)
            };

            foreach (var oldList in oldIndices)
            {
                var arr = oldList.ToArray();
                var l = new UnsafeList<int>(oldList.Count, Allocator.TempJob);
                foreach (var element in arr)
                    l.Add(element);
                job.Indices.Add(l);

                job.ResultIndices.Add(new UnsafeList<int>(1, Allocator.TempJob));
            }

            foreach (var oldList in oldUVs)
            {
                var arr = oldList.ToArray();
                var l = new UnsafeList<float2>(oldList.Count, Allocator.TempJob);
                foreach (var element in arr)
                    l.Add(element);
                job.UVs.Add(l);

                job.ResultUVs.Add(new UnsafeList<float2>(oldList.Count, Allocator.TempJob));
            }

            job.Run();

            _extractResultsMarker.Begin();
            // extract results with no allocations
            var newVertices = job.ResultVertices.AsArray().ToArray();
            var newIndices = new int[job.ResultIndices.Length][];
            for (var i = 0; i < newIndices.Length; ++i)
            {
                ref var x = ref job.ResultIndices.ElementAt(i);
                var y = newIndices[i] = new int[x.Length];
                for (var j = 0; j < x.Length; ++j)
                    y[j] = x[j];
            }

            var newUVs = new Vector2[8][];
            for (var i = 0; i < 8; ++i)
            {
                ref var x = ref job.ResultUVs.ElementAt(i);
                var y = newUVs[i] = new Vector2[x.Length];
                for (var j = 0; j < x.Length; ++j)
                    y[j] = x[j];
            }

            _extractResultsMarker.End();

            _disposeMarker.Begin();
            job.Points.Dispose();
            job.Vertices.Dispose();
            foreach (var i in job.Indices) i.Dispose();
            job.Indices.Dispose();
            foreach (var i in job.UVs) i.Dispose();
            job.UVs.Dispose();
            job.ResultVertices.Dispose();
            foreach (var i in job.ResultIndices) i.Dispose();
            job.ResultIndices.Dispose();
            foreach (var i in job.ResultUVs) i.Dispose();
            job.ResultUVs.Dispose();
            job.IntersectedIndices.Dispose();
            _disposeMarker.End();

            newMesh.subMeshCount = submeshCount;
            using (_setVerticesMarker.Auto())
                newMesh.SetVertices(newVertices.Select(e => (Vector3)e).ToList());
            using (_setIndicesMarker.Auto())
                for (var i = 0; i < submeshCount; ++i)
                    newMesh.SetIndices(newIndices[i], oldMesh.GetTopology(i), i);
            using (_setUVsMarker.Auto())
                for (var i = 0; i < 8; ++i)
                    newMesh.SetUVs(i, newUVs.ElementAt(i).Select(e => (Vector2)e).ToList());
            using (_recalculateNormalsMarker.Auto())
                newMesh.RecalculateNormals();
            using (_recalculateTangentsMarker.Auto())
                newMesh.RecalculateTangents();
            using (_recalculateBoundsMarker.Auto())
                newMesh.RecalculateBounds();

            mf.sharedMesh = newMesh;
            if (GetComponent<MeshCollider>().Let(out var coll))
                coll.sharedMesh = mf.sharedMesh;

            Valid = true;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct GenerateRoadMeshJob : IJob
        {
            [ReadOnly]
            public NativeArray<Bezier.OrientedPoint> Points;

            [ReadOnly]
            public NativeArray<float3> Vertices;

            [ReadOnly]
            public UnsafeList<UnsafeList<int>> Indices;

            [ReadOnly]
            public UnsafeList<UnsafeList<float2>> UVs;

            [ReadOnly]
            public int CompleteCopies;

            [ReadOnly]
            public float MeshLength;

            [ReadOnly]
            public float BezierLength;

            [ReadOnly]
            public float StepSize;

            [ReadOnly]
            public float2 UVOffset;

            public NativeList<float3> ResultVertices;
            public UnsafeList<UnsafeList<int>> ResultIndices;
            public UnsafeList<UnsafeList<float2>> ResultUVs;

            /// <summary>
            /// Cache for the indices of the vertices that were intersected by the clipping plane.
            /// </summary>
            public NativeHashMap<int2, int> IntersectedIndices;

            public void Execute()
            {
                var vertexCount = Vertices.Length;
                var indexCounts = new NativeArray<int>(Indices.Length, Allocator.Temp);
                for (var i = 0; i < Indices.Length; ++i)
                    indexCounts[i] = Indices[i].Length;
                var submeshCount = Indices.Length;

                for (var z = 0; z < CompleteCopies + 1; ++z)
                {
                    var yOffset = z * MeshLength;

                    for (var v = 0; v < vertexCount; ++v)
                    {
                        var pos = Vertices[v] + float3(0, 0, yOffset);

                        ResultVertices.AddGrowth(pos);
                    }

                    for (var channel = 0; channel < 8; ++channel)
                    for (var uv = 0; uv < UVs[channel].Length; ++uv)
                        ResultUVs.ElementAt(channel).Add(UVs[channel][uv] + UVOffset * z);

                    // the last set of triangles is not copied for now, but added and potentially clipped
                    if (z == CompleteCopies) break;

                    for (var submesh = 0; submesh < submeshCount; ++submesh)
                    {
                        for (var i = 0; i < indexCounts[submesh] / 3; ++i)
                        {
                            ResultIndices.ElementAt(submesh).AddGrowth(Indices[submesh][3 * i] + z * vertexCount);
                            ResultIndices.ElementAt(submesh).AddGrowth(Indices[submesh][3 * i + 1] + z * vertexCount);
                            ResultIndices.ElementAt(submesh).AddGrowth(Indices[submesh][3 * i + 2] + z * vertexCount);
                        }
                    }
                }

                for (var i = 0; i < submeshCount; ++i)
                {
                    // ClipMeshZ(ref ResultVertices, ref indices, ref ResultUVs, BezierLength);
                    AddRemainderTriangles(
                        ref ResultVertices,
                        ref Indices.ElementAt(i),
                        ref ResultIndices.ElementAt(i),
                        ref UVs,
                        ref ResultUVs,
                        Vertices.Length * CompleteCopies,
                        BezierLength,
                        ref IntersectedIndices,
                        UVOffset * CompleteCopies
                    );
                }

                // bend along bezier
                for (var v = 0; v < ResultVertices.Length && Points.Length > 1; ++v)
                {
                    var pos = ResultVertices[v];

                    var pointIndex = Mathf.Clamp(Mathf.FloorToInt(pos.z / StepSize), 0, Points.Length - 2);
                    var weight = pos.z / StepSize - pointIndex;
                    if (pointIndex == Points.Length - 2)
                    {
                        weight = (pos.z - StepSize * pointIndex) /
                                 (Points[Points.Length - 1].position - Points[Points.Length - 2].position)
                                 .magnitude;
                    }

                    Vector3 centerPos;
                    Vector3 forward;
                    Vector3 normal;
                    if (pointIndex < Points.Length - 1)
                    {
                        centerPos = Vector3.Lerp(Points[pointIndex].position, Points[pointIndex + 1].position,
                            weight);
                        forward = Vector3.Lerp(Points[pointIndex].forward, Points[pointIndex + 1].forward, weight)
                            .normalized;
                        if (weight < 1e-6)
                            normal = Points[pointIndex].normal;
                        else if (weight > 1 - 1e-6)
                            normal = Points[pointIndex + 1].normal;
                        else
                            normal = Vector3.Lerp(Points[pointIndex].normal, Points[pointIndex + 1].normal, weight);
                    }
                    else // Should not happen, except if the z coordinate is EXACTLY at the end of the bezier
                    {
                        centerPos = Points[pointIndex].position;
                        forward = Points[pointIndex].forward;
                        normal = Points[pointIndex].normal;
                    }

                    var right = Vector3.Cross(normal, forward).normalized;

                    pos = centerPos + right * pos.x + normal * pos.y;

                    ResultVertices[v] = pos;
                }

                indexCounts.Dispose();
            }

            /// <summary>
            /// Add remaining triangles, and clip the ones at the end, potentially adding new vertices.
            /// </summary>
            private static void AddRemainderTriangles(
                ref NativeList<float3> vertices,
                ref UnsafeList<int> sourceIndices,
                ref UnsafeList<int> resultIndices,
                ref UnsafeList<UnsafeList<float2>> sourceUVs,
                ref UnsafeList<UnsafeList<float2>> resultUVs,
                int vertexStart,
                float maxZ,
                ref NativeHashMap<int2, int> intersectedIndices,
                float2 uvOffset
            )
            {
                for (var tri = 0; tri + 3 <= sourceIndices.Length; tri += 3)
                {
                    var count = 0;
                    for (var i = 0; i < 3; ++i)
                        if (vertices[vertexStart + sourceIndices[tri + i]].z <= maxZ)
                            ++count;
                    switch (count)
                    {
                        case 3:
                        {
                            resultIndices.Add(vertexStart + sourceIndices[tri]);
                            resultIndices.Add(vertexStart + sourceIndices[tri + 1]);
                            resultIndices.Add(vertexStart + sourceIndices[tri + 2]);
                            break;
                        }
                        case 2:
                        {
                            var a = vertexStart + sourceIndices[tri];
                            var b = vertexStart + sourceIndices[tri + 1];
                            var c = vertexStart + sourceIndices[tri + 2];
                            // shuffle to make a and b inside
                            if (vertices[a].z > maxZ)
                            {
                                var t = a;
                                a = b;
                                b = c;
                                c = t;
                            }
                            else if (vertices[b].z > maxZ)
                            {
                                var t = b;
                                b = a;
                                a = c;
                                c = t;
                            }

                            var ac = vertices[c] - vertices[a];
                            var bc = vertices[c] - vertices[b];
                            var va = vertices[a] + ac * (maxZ - vertices[a].z) / (vertices[c].z - vertices[a].z);
                            var vb = vertices[b] + bc * (maxZ - vertices[b].z) / (vertices[c].z - vertices[b].z);

                            var insertedA = false;
                            int ia;
                            if (!intersectedIndices.ContainsKey(int2(a, c)))
                            {
                                vertices.Add(va);
                                ia = vertices.Length - 1;
                                intersectedIndices[int2(a, c)] = ia;
                                insertedA = true;
                            }
                            else ia = intersectedIndices[int2(a, c)];

                            var insertedB = false;
                            int ib;
                            if (!intersectedIndices.ContainsKey(int2(b, c)))
                            {
                                vertices.AddGrowth(vb);
                                ib = vertices.Length - 1;
                                intersectedIndices[int2(b, c)] = ib;
                                insertedB = true;
                            }
                            else ib = intersectedIndices[int2(b, c)];

                            var weightA = length(va - vertices[c]) / length(ac);
                            var weightB = length(vb - vertices[c]) / length(bc);
                            for (var channel = 0; channel < 8; ++channel)
                            {
                                if (sourceUVs.ElementAt(channel).Length == 0) continue;
                                if (insertedA)
                                    resultUVs.ElementAt(channel).Add(
                                        weightA * sourceUVs[channel][a - vertexStart] +
                                        (1 - weightA) * sourceUVs[channel][c - vertexStart] +
                                        uvOffset
                                    );
                                if (insertedB)
                                    resultUVs.ElementAt(channel).Add(
                                        weightB * sourceUVs[channel][b - vertexStart] +
                                        (1 - weightB) * sourceUVs[channel][c - vertexStart] +
                                        uvOffset
                                    );
                            }

                            resultIndices.Add(a);
                            resultIndices.Add(b);
                            resultIndices.Add(ib);
                            resultIndices.Add(a);
                            resultIndices.Add(ib);
                            resultIndices.Add(ia);
                            break;
                        }
                        case 1:
                        {
                            var a = vertexStart + sourceIndices[tri];
                            var b = vertexStart + sourceIndices[tri + 1];
                            var c = vertexStart + sourceIndices[tri + 2];
                            // shuffle to make a and b inside
                            if (vertices[a].z <= maxZ)
                            {
                                var t = a;
                                a = b;
                                b = c;
                                c = t;
                            }
                            else if (vertices[b].z <= maxZ)
                            {
                                var t = b;
                                b = a;
                                a = c;
                                c = t;
                            }

                            var ca = vertices[a] - vertices[c];
                            var cb = vertices[b] - vertices[c];
                            if (vertices[a].z - vertices[c].z < 1e-6 || vertices[b].z - vertices[c].z < 1e-6) break;
                            var va = vertices[c] + ca * (maxZ - vertices[c].z) / (vertices[a].z - vertices[c].z);
                            var vb = vertices[c] + cb * (maxZ - vertices[c].z) / (vertices[b].z - vertices[c].z);

                            var insertedA = false;
                            int ia;
                            if (!intersectedIndices.ContainsKey(int2(c, a)))
                            {
                                vertices.Add(va);
                                ia = vertices.Length - 1;
                                intersectedIndices[int2(c, a)] = ia;
                                insertedA = true;
                            }
                            else ia = intersectedIndices[int2(c, a)];

                            var insertedB = false;
                            int ib;
                            if (!intersectedIndices.ContainsKey(int2(c, b)))
                            {
                                vertices.Add(vb);
                                ib = vertices.Length - 1;
                                intersectedIndices[int2(c, b)] = ib;
                                insertedB = true;
                            }
                            else ib = intersectedIndices[int2(c, b)];

                            var weightA = length(va - vertices[c]) / length(ca);
                            var weightB = length(vb - vertices[c]) / length(cb);
                            for (var channel = 0; channel < 8; ++channel)
                            {
                                if (sourceUVs.ElementAt(channel).Length == 0) continue;
                                if (insertedA)
                                    resultUVs.ElementAt(channel).Add(
                                        weightA * sourceUVs[channel][a - vertexStart] +
                                        (1 - weightA) * sourceUVs[channel][c - vertexStart] +
                                        uvOffset
                                    );
                                if (insertedB)
                                    resultUVs.ElementAt(channel).Add(
                                        weightB * sourceUVs[channel][b - vertexStart] +
                                        (1 - weightB) * sourceUVs[channel][c - vertexStart] +
                                        uvOffset
                                    );
                            }

                            resultIndices.Add(ia);
                            resultIndices.Add(ib);
                            resultIndices.Add(c);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="stepSize">
        /// distance between evenly spaced points for the spline.
        /// A bigger value results in straight sections of road.
        /// </param>
        public void GenerateRoadMeshV2(float stepSize = 1)
        {
            if (!road) road = GetComponent<Road>();
            if (!mf) mf = GetComponent<MeshFilter>();
            if (!road || !mf) return;
            if (!SourceMesh) return;

            var points = road
                .GetEvenlySpacedPoints(stepSize)
                .Select(p => new GenerateRoadMeshV2Job.OrientedPoint
                {
                    Position = p.position,
                    Forward = p.forward,
                    Normal = p.normal
                })
                .ToArray();

            var sourceMesh = SourceMesh.sharedMesh;
            var vertexAttributeEnumValues = (VertexAttribute[])Enum.GetValues(typeof(VertexAttribute));
            // contains a mapping from VertexAttribute to VertexAttributeDescriptor.
            // because the amount of VertexAttributes is small, we can use a NativeArray, functioning as a map.
            var sourceAttributes =
                new NativeArray<VertexAttributeDescriptor>(vertexAttributeEnumValues.Length, Allocator.TempJob);
            foreach (var attributeDescriptor in sourceMesh.GetVertexAttributes())
                sourceAttributes[(int)attributeDescriptor.attribute] = attributeDescriptor;
            using var sourceMeshDataArray = Mesh.AcquireReadOnlyMeshData(sourceMesh);
            var sourceBounds = sourceMesh.bounds;

            var resultMeshData = Mesh.AllocateWritableMeshData(1);
            using var resultBounds = new NativeArray<float3>(2, Allocator.TempJob);

            new GenerateRoadMeshV2Job
            {
                StepSize = stepSize,
                UVOffset = settings.uvOffset,
                Points = new NativeArray<GenerateRoadMeshV2Job.OrientedPoint>(points, Allocator.TempJob),
                SourceMeshData = sourceMeshDataArray[0],
                SourceAttributes = sourceAttributes,
                SourceBounds = sourceBounds,
                ResultMeshData = resultMeshData[0],
                ResultBounds = resultBounds
            }.Run();

            var resultMesh = new Mesh
            {
                name = "Road Mesh"
            };
            Mesh.ApplyAndDisposeWritableMeshData(resultMeshData, resultMesh);
            resultMesh.bounds = new Bounds { min = resultBounds[0], max = resultBounds[1] };
            mf.mesh = resultMesh;
            if (GetComponent<MeshCollider>().Let(out var coll))
                coll.sharedMesh = mf.sharedMesh;

            Valid = true;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct GenerateRoadMeshV2Job : IJob
        {
            public struct OrientedPoint
            {
                public float3 Position, Forward, Normal;
            }

            [ReadOnly]
            public float StepSize;

            [ReadOnly]
            public float2 UVOffset;

            [ReadOnly]
            [DeallocateOnJobCompletion]
            public NativeArray<OrientedPoint> Points;

            [ReadOnly]
            public Mesh.MeshData SourceMeshData;

            [ReadOnly]
            [DeallocateOnJobCompletion]
            public NativeArray<VertexAttributeDescriptor> SourceAttributes;

            [ReadOnly]
            public Bounds SourceBounds;

            public Mesh.MeshData ResultMeshData;

            [WriteOnly]
            public NativeArray<float3> ResultBounds;

            public void Execute()
            {
                using var sourceAttributeData = new VertexAttributeData(SourceMeshData, SourceAttributes);

                // The last point is repositioned to the end of the bezier, so the length of the line is the
                // amount of segments - 1 + the length of the last segment.
                var bezierLength = Points.Length > 1
                    ? StepSize * (Points.Length - 2) +
                      length(Points[Points.Length - 2].Position - Points[Points.Length - 1].Position)
                    : 0;
            }
        }

        /// <summary>
        /// To be called whenever the road shape changes. Will regenerate the mesh if AutoGenerate is true.
        /// </summary>
        /// <param name="update">- whether to regenerate the mesh at all.</param>
        public void Invalidate(bool update = true)
        {
            Valid = false;
            if (AutoGenerate && update) GenerateRoadMesh();
        }
    }
}
