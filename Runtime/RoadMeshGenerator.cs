using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
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

                        ResultVertices.Add(pos);
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
                            ResultIndices.ElementAt(submesh).Add(Indices[submesh][3 * i] + z * vertexCount);
                            ResultIndices.ElementAt(submesh).Add(Indices[submesh][3 * i + 1] + z * vertexCount);
                            ResultIndices.ElementAt(submesh).Add(Indices[submesh][3 * i + 2] + z * vertexCount);
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
                                vertices.Add(vb);
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
                            // shuffle to make a and b outside
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
                SourceOrientation = settings.SourceOrientation,
                SourceMeshData = sourceMeshDataArray[0],
                SourceVertexAttributes = sourceAttributes,
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
            public MeshConversion.MeshOrientation SourceOrientation;

            [ReadOnly]
            public Mesh.MeshData SourceMeshData;

            [ReadOnly]
            [DeallocateOnJobCompletion]
            public NativeArray<VertexAttributeDescriptor> SourceVertexAttributes;

            [ReadOnly]
            public Bounds SourceBounds;

            public Mesh.MeshData ResultMeshData;

            [WriteOnly]
            public NativeArray<float3> ResultBounds;

            [SuppressMessage("ReSharper", "ForCanBeConvertedToForeach")]
            public void Execute()
            {
                using var sourceAttributeData = new VertexAttributeData(SourceMeshData, SourceVertexAttributes);

                // The last point is repositioned to the end of the bezier, so the length of the line is the
                // amount of segments - 1 + the length of the last segment.
                var bezierLength = Points.Length > 1
                    ? StepSize * (Points.Length - 2) +
                      length(Points[Points.Length - 2].Position - Points[Points.Length - 1].Position)
                    : 0;

                var meshLength = SourceOrientation.forward switch
                {
                    MeshConversion.MeshOrientation.AxisDirection.X_POSITIVE => SourceBounds.size.x,
                    MeshConversion.MeshOrientation.AxisDirection.X_NEGATIVE => SourceBounds.size.x,
                    MeshConversion.MeshOrientation.AxisDirection.Y_POSITIVE => SourceBounds.size.y,
                    MeshConversion.MeshOrientation.AxisDirection.Y_NEGATIVE => SourceBounds.size.y,
                    MeshConversion.MeshOrientation.AxisDirection.Z_POSITIVE => SourceBounds.size.z,
                    MeshConversion.MeshOrientation.AxisDirection.Z_NEGATIVE => SourceBounds.size.z,
                    _ => throw new ArgumentOutOfRangeException()
                };

                var meshMinZ = SourceOrientation.forward switch
                {
                    MeshConversion.MeshOrientation.AxisDirection.X_POSITIVE => SourceBounds.min.x,
                    MeshConversion.MeshOrientation.AxisDirection.X_NEGATIVE => SourceBounds.max.x,
                    MeshConversion.MeshOrientation.AxisDirection.Y_POSITIVE => SourceBounds.min.y,
                    MeshConversion.MeshOrientation.AxisDirection.Y_NEGATIVE => SourceBounds.max.y,
                    MeshConversion.MeshOrientation.AxisDirection.Z_POSITIVE => SourceBounds.min.z,
                    MeshConversion.MeshOrientation.AxisDirection.Z_NEGATIVE => SourceBounds.max.z,
                    _ => throw new ArgumentOutOfRangeException()
                };

                var copyCount = (int)ceil(bezierLength / meshLength);
                var sourceVertexCount = SourceMeshData.vertexCount;
                var subMeshCount = SourceMeshData.subMeshCount;
                var guessedResultVertexCount = copyCount * sourceVertexCount;

                var sourceIndices = new IndexLists<ushort>(subMeshCount, Allocator.Temp);
                for (var subMeshIndex = 0; subMeshIndex < subMeshCount; ++subMeshIndex)
                {
                    var subMesh = SourceMeshData.GetSubMesh(subMeshIndex);
                    ref var sourceSubMeshIndices = ref sourceIndices[subMeshIndex];
                    sourceSubMeshIndices.ResizeUninitialized(subMesh.indexCount);
                    SourceMeshData.GetIndices(sourceSubMeshIndices.AsArray(), subMeshIndex);
                    if (SourceOrientation.isRightHanded)
                    {
                        for (var i = 0; i < sourceSubMeshIndices.Length; i += 3)
                        {
                            (sourceSubMeshIndices[i], sourceSubMeshIndices[i + 2]) =
                                (sourceSubMeshIndices[i + 2], sourceSubMeshIndices[i]);
                        }
                    }
                }

                var positions = new NativeList<float3>(Allocator.Temp);
                positions.ResizeUninitialized(guessedResultVertexCount);
                var normals = new NativeList<float3>(Allocator.Temp);
                normals.ResizeUninitialized(guessedResultVertexCount);
                var tangents = new NativeList<float4>(Allocator.Temp);
                tangents.ResizeUninitialized(guessedResultVertexCount);
                var uvs = new NativeList<float2>(Allocator.Temp);
                uvs.ResizeUninitialized(guessedResultVertexCount * sourceAttributeData.UVChannelCount);

                var indices = new IndexLists<ushort>(subMeshCount, Allocator.Temp);

                var sourceForward = SourceOrientation.forward.ToFloat3();
                var sourceUp = SourceOrientation.up.ToFloat3();
                var sourceRight = SourceOrientation.isRightHanded
                    ? cross(sourceForward, sourceUp)
                    : cross(sourceUp, sourceForward);

                for (var z = 0; z < copyCount; ++z)
                {
                    var zOffset = z * meshLength;
                    for (var sourceIndex = 0; sourceIndex < sourceVertexCount; ++sourceIndex)
                    {
                        var resultIndex = z * sourceVertexCount + sourceIndex;
                        sourceAttributeData.GetFloat3(sourceIndex, VertexAttribute.Position, out var position);
                        position = float3(dot(sourceRight, position), dot(sourceUp, position),
                            dot(sourceForward, position) - meshMinZ + zOffset);
                        sourceAttributeData.GetFloat3(sourceIndex, VertexAttribute.Normal, out var normal);
                        normal = float3(dot(sourceRight, normal), dot(sourceUp, normal), dot(sourceForward, normal));
                        sourceAttributeData.GetFloat4(sourceIndex, VertexAttribute.Tangent, out var tangent);
                        tangent = float4(dot(sourceRight, tangent.xyz), dot(sourceUp, tangent.xyz),
                            dot(sourceForward, tangent.xyz), tangent.w);

                        positions[resultIndex] = position;
                        normals[resultIndex] = normal;
                        tangents[resultIndex] = tangent;

                        for (var channel = 0; channel < sourceAttributeData.UVChannelCount; ++channel)
                        {
                            sourceAttributeData.GetFloat2(sourceIndex, VertexAttribute.TexCoord0 + channel,
                                out var uv);
                            uvs[resultIndex * sourceAttributeData.UVChannelCount + channel] = uv + UVOffset * z;
                        }
                    }

                    // copy indices
                    // the last set of triangles is not copied for now, but added and potentially clipped later
                    if (z == copyCount - 1) continue;

                    for (var subMeshIndex = 0; subMeshIndex < subMeshCount; ++subMeshIndex)
                    {
                        var src = sourceIndices[subMeshIndex];
                        var dst = indices[subMeshIndex];
                        dst.ResizeUninitialized(dst.Length + src.Length);
                        for (var i = 0; i < src.Length; ++i)
                            dst[dst.Length - src.Length + i] = (ushort)(z * sourceVertexCount + src[i]);
                    }
                }

                if (copyCount >= 1)
                {
                    var intersectedIndices = new NativeHashMap<int2, ushort>(128, Allocator.Temp);

                    for (var subMeshIndex = 0; subMeshIndex < subMeshCount; ++subMeshIndex)
                    {
                        var src = sourceIndices[subMeshIndex];
                        var dst = indices[subMeshIndex];
                        var vertexOffset = (ushort)((copyCount - 1) * sourceVertexCount);
                        for (var i = 0; i + 2 < src.Length; i += 3)
                        {
                            var ia = (ushort)(src[i] + vertexOffset);
                            var ib = (ushort)(src[i + 1] + vertexOffset);
                            var ic = (ushort)(src[i + 2] + vertexOffset);
                            var a = positions[ia];
                            var b = positions[ib];
                            var c = positions[ic];
                            var insideCount = 0;
                            if (a.z <= bezierLength) ++insideCount;
                            if (b.z <= bezierLength) ++insideCount;
                            if (c.z <= bezierLength) ++insideCount;
                            switch (insideCount)
                            {
                                case 3:
                                    dst.Add(ia);
                                    dst.Add(ib);
                                    dst.Add(ic);
                                    break;
                                case 2:
                                {
                                    // shuffle to make a and b inside
                                    if (a.z > bezierLength)
                                    {
                                        (ia, ib, ic) = (ib, ic, ia);
                                        (a, b, c) = (b, c, a);
                                    }
                                    else if (b.z > bezierLength)
                                    {
                                        (ia, ib, ic) = (ic, ia, ib);
                                        (a, b, c) = (c, a, b);
                                    }

                                    // between a and c on the clipping plane
                                    AddBetween(
                                        positions, normals, tangents, uvs, sourceAttributeData.UVChannelCount,
                                        ia, ic, (bezierLength - a.z) / (c.z - a.z),
                                        intersectedIndices, out var iac
                                    );
                                    // between b and c on the clipping plane
                                    AddBetween(
                                        positions, normals, tangents, uvs, sourceAttributeData.UVChannelCount,
                                        ib, ic, (bezierLength - b.z) / (c.z - b.z),
                                        intersectedIndices, out var ibc
                                    );

                                    dst.Add(ia);
                                    dst.Add(ib);
                                    dst.Add(iac);
                                    dst.Add(iac);
                                    dst.Add(ib);
                                    dst.Add(ibc);

                                    break;
                                }
                                case 1:
                                {
                                    // shuffle to make b and c outside
                                    if (b.z <= bezierLength)
                                    {
                                        (ia, ib, ic) = (ib, ic, ia);
                                        (a, b, c) = (b, c, a);
                                    }
                                    else if (c.z <= bezierLength)
                                    {
                                        (ia, ib, ic) = (ic, ia, ib);
                                        (a, b, c) = (c, a, b);
                                    }

                                    // between a and b on the clipping plane
                                    AddBetween(
                                        positions, normals, tangents, uvs, sourceAttributeData.UVChannelCount,
                                        ia, ib, (bezierLength - a.z) / (b.z - a.z),
                                        intersectedIndices, out var iab
                                    );
                                    // between a and c on the clipping plane
                                    AddBetween(
                                        positions, normals, tangents, uvs, sourceAttributeData.UVChannelCount,
                                        ia, ic, (bezierLength - a.z) / (c.z - a.z),
                                        intersectedIndices, out var iac
                                    );

                                    dst.Add(ia);
                                    dst.Add(iab);
                                    dst.Add(iac);

                                    break;
                                }
                            }
                        }
                    }
                }

                // bend along bezier
                var resultVertexCount = positions.Length;
                for (var i = 0; i < resultVertexCount; ++i)
                {
                    var pos = positions[i];
                    var pointIndex = clamp((int)floor(pos.z / StepSize), 0, Points.Length - 2);
                    var weight = clamp(
                        pointIndex < Points.Length - 2
                            ? pos.z / StepSize - pointIndex
                            : (pos.z - StepSize * pointIndex) /
                              distance(Points[Points.Length - 1].Position, Points[Points.Length - 2].Position),
                        0, 1);
                    var centerPos = lerp(Points[pointIndex].Position, Points[pointIndex + 1].Position, weight);
                    var forward = normalize(lerp(Points[pointIndex].Forward, Points[pointIndex + 1].Forward, weight));
                    var up = normalize(lerp(Points[pointIndex].Normal, Points[pointIndex + 1].Normal, weight));
                    var right = cross(up, forward);

                    positions[i] = centerPos + right * pos.x + up * pos.y;
                    normals[i] = right * normals[i].x + up * normals[i].y + forward * normals[i].z;
                    tangents[i] = float4(
                        right * tangents[i].x + up * tangents[i].y + forward * tangents[i].z, tangents[i].w);
                }

                var vertexAttributes = new NativeArray<VertexAttributeDescriptor>(
                    3 + sourceAttributeData.UVChannelCount,
                    Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                vertexAttributes[0] = new VertexAttributeDescriptor(
                    attribute: VertexAttribute.Position, format: VertexAttributeFormat.Float32, dimension: 3,
                    stream: 0);
                vertexAttributes[1] = new VertexAttributeDescriptor(
                    attribute: VertexAttribute.Normal, format: VertexAttributeFormat.Float32, dimension: 3, stream: 1);
                vertexAttributes[2] = new VertexAttributeDescriptor(
                    attribute: VertexAttribute.Tangent, format: VertexAttributeFormat.Float32, dimension: 4, stream: 2);
                for (var i = 0; i < sourceAttributeData.UVChannelCount; i++)
                {
                    var attr = SourceVertexAttributes[(int)(VertexAttribute.TexCoord0 + i)];
                    attr.stream = 3;
                    vertexAttributes[3 + i] = attr;
                }

                ResultMeshData.SetVertexBufferParams(resultVertexCount, vertexAttributes);
                vertexAttributes.Dispose();

                var resultPositions = ResultMeshData.GetVertexData<float3>(stream: 0);
                var resultNormals = ResultMeshData.GetVertexData<float3>(stream: 1);
                var resultTangents = ResultMeshData.GetVertexData<float4>(stream: 2);
                var resultUVs = ResultMeshData.GetVertexData<float2>(stream: 3);

                resultPositions.CopyFrom(positions.AsArray());
                resultNormals.CopyFrom(normals.AsArray());
                resultTangents.CopyFrom(tangents.AsArray());
                resultUVs.CopyFrom(uvs.AsArray());

                var boundsMin = new float3(float.MaxValue);
                var boundsMax = new float3(float.MinValue);

                foreach (var position in positions)
                {
                    boundsMin = min(boundsMin, position);
                    boundsMax = max(boundsMax, position);
                }

                ResultBounds[0] = boundsMin;
                ResultBounds[1] = boundsMax;

                ResultMeshData.SetIndexBufferParams(indices.TotalIndexCount, IndexFormat.UInt16);
                ResultMeshData.subMeshCount = subMeshCount;
                var indexData = ResultMeshData.GetIndexData<ushort>();
                var indexOffset = 0;
                for (var subMeshIndex = 0; subMeshIndex < subMeshCount; ++subMeshIndex)
                {
                    var subMesh = SourceMeshData.GetSubMesh(subMeshIndex);
                    var subMeshIndices = indices[subMeshIndex];
                    indexData.GetSubArray(indexOffset, subMeshIndices.Length).CopyFrom(subMeshIndices);

                    boundsMin = new float3(float.MaxValue);
                    boundsMax = new float3(float.MinValue);
                    var minIndex = int.MaxValue;
                    using var usedIndices = new NativeHashSet<int>(subMeshIndices.Length, Allocator.Temp);

                    for (var i = 0; i < subMeshIndices.Length; ++i)
                    {
                        var index = subMeshIndices[i];
                        boundsMin = min(boundsMin, positions[index]);
                        boundsMax = max(boundsMax, positions[index]);
                        minIndex = min(minIndex, index);
                        usedIndices.Add(index);
                    }

                    ResultMeshData.SetSubMesh(subMeshIndex, new SubMeshDescriptor
                    {
                        bounds = new Bounds { min = boundsMin, max = boundsMax },
                        topology = subMesh.topology,
                        indexStart = indexOffset,
                        indexCount = subMeshIndices.Length,
                        firstVertex = minIndex,
                        vertexCount = usedIndices.Count()
                    }, MeshUpdateFlags.DontRecalculateBounds);
                    indexOffset += subMeshIndices.Length;
                }

                positions.Dispose();
                normals.Dispose();
                tangents.Dispose();
                uvs.Dispose();
                sourceIndices.Dispose();
                indices.Dispose();
            }

            private static void AddBetween(
                NativeList<float3> positions,
                NativeList<float3> normals,
                NativeList<float4> tangents,
                NativeList<float2> uvs,
                int uvChannelCount,
                ushort ia,
                ushort ib,
                float t,
                NativeHashMap<int2, ushort> intersectedIndices,
                out ushort resultIndex
            )
            {
                if (intersectedIndices.TryGetValue(new int2(ia, ib), out var index))
                {
                    resultIndex = index;
                    return;
                }

                positions.Add(lerp(positions[ia], positions[ib], t));
                normals.Add(normalize(lerp(normals[ia], normals[ib], t)));
                tangents.Add(float4(normalize(lerp(tangents[ia].xyz, tangents[ib].xyz, t)), 1));
                for (var channel = 0; channel < uvChannelCount; ++channel)
                    uvs.Add(lerp(uvs[ia * uvChannelCount + channel], uvs[ib * uvChannelCount + channel], t));
                intersectedIndices[new int2(ia, ib)] = resultIndex = (ushort)(positions.Length - 1);
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct IndexLists<T> : IDisposable where T : unmanaged
            {
                public NativeList<T>
                    SubMesh0,
                    SubMesh1,
                    SubMesh2,
                    SubMesh3,
                    SubMesh4,
                    SubMesh5,
                    SubMesh6,
                    SubMesh7,
                    SubMesh8,
                    SubMesh9,
                    SubMesh10,
                    SubMesh11,
                    SubMesh12,
                    SubMesh13,
                    SubMesh14,
                    SubMesh15,
                    SubMesh16,
                    SubMesh17,
                    SubMesh18,
                    SubMesh19,
                    SubMesh20,
                    SubMesh21,
                    SubMesh22,
                    SubMesh23,
                    SubMesh24,
                    SubMesh25,
                    SubMesh26,
                    SubMesh27,
                    SubMesh28,
                    SubMesh29,
                    SubMesh30,
                    SubMesh31;

                public Allocator Allocator;

                private int _subMeshCount;

                public IndexLists(Allocator allocator)
                {
                    this = default;
                    Allocator = allocator;
                }

                public IndexLists(int subMeshCount, Allocator allocator)
                {
                    this = default;
                    Allocator = allocator;
                    Resize(subMeshCount);
                }

                private unsafe ref NativeList<T> GetUnchecked(int index) =>
                    ref UnsafeUtility.ArrayElementAsRef<NativeList<T>>(
                        UnsafeUtility.AddressOf(ref SubMesh0), index);

                public ref NativeList<T> this[int index]
                {
                    get
                    {
                        if (index < 0 || index >= _subMeshCount)
                            throw new IndexOutOfRangeException();
                        return ref GetUnchecked(index);
                    }
                }

                public void Resize(int subMeshCount)
                {
                    if (subMeshCount < 0 || subMeshCount > 32)
                        throw new ArgumentOutOfRangeException();
                    if (subMeshCount == _subMeshCount) return;
                    if (subMeshCount < _subMeshCount)
                    {
                        for (var i = subMeshCount; i < _subMeshCount; ++i)
                            GetUnchecked(i).Dispose();
                    }
                    else
                    {
                        for (var i = _subMeshCount; i < subMeshCount; ++i)
                            GetUnchecked(i) = new NativeList<T>(Allocator);
                    }

                    _subMeshCount = subMeshCount;
                }

                public int SubMeshCount
                {
                    get => _subMeshCount;
                    set => Resize(value);
                }

                public int TotalIndexCount
                {
                    get
                    {
                        var sum = 0;
                        for (var i = 0; i < _subMeshCount; ++i)
                            sum += this[i].Length;
                        return sum;
                    }
                }

                public void Dispose()
                {
                    for (var i = 0; i < _subMeshCount; ++i)
                        this[i].Dispose();
                }
            }
        }

        /// <summary>
        /// To be called whenever the road shape changes. Will regenerate the mesh if AutoGenerate is true.
        /// </summary>
        /// <param name="update">- whether to regenerate the mesh at all.</param>
        public void Invalidate(bool update = true)
        {
            Valid = false;
            if (AutoGenerate && update) GenerateRoadMeshV2();
        }
    }
}
