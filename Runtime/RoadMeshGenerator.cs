using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using Util;
using static Unity.Mathematics.math;
using float3 = Unity.Mathematics.float3;
using static Barmetler.Util.Functional;

namespace Barmetler.RoadSystem
{
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

		/// <summary>
		/// Generate the mesh based on the curve described in the Road component.
		/// </summary>
		public void GenerateRoadMesh(bool burst = true)
		{
			if (burst)
			{
				GenerateRoadMeshBurst();
				return;
			}
			
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
			var bezierLength = stepSize * (points.Length - 2) +
			                   (points[points.Length - 2].position - points[points.Length - 1].position).magnitude;

			var completeCopies = Mathf.FloorToInt(bezierLength / meshLength);

			var submeshCount = oldMesh.subMeshCount;

			var oldVertices = new List<Vector3>();
			oldMesh.GetVertices(oldVertices);
			var oldIndices = Enumerable.Range(0, submeshCount).Select(i => new List<int>(oldMesh.GetIndices(i))).ToArray();
			var oldUVs = Enumerable.Range(0, 8)
				.Select(channel =>
				{
					var x = new List<Vector2>();
					oldMesh.GetUVs(channel, x);
					return x;
				})
				.ToArray();

			var newVertices = new List<Vector3>();
			var newIndices = Enumerable.Range(0, submeshCount).Select(_ => new List<int>()).ToArray();
			var newUVs = Enumerable.Range(0, 8).Select(_ => new List<Vector2>()).ToArray();

			var vertexCount = oldVertices.Count;
			var indexCounts = oldIndices.Select(e => e.Count).ToArray();

			for (var z = 0; z < completeCopies; ++z)
			{
				var yOffset = z * meshLength;

				for (var v = 0; v < vertexCount; ++v)
				{
					var pos = oldVertices[v] + Vector3.forward * yOffset;
					// transform from blender to unity coordinate system
					pos = new Vector3(pos.x, pos.y, pos.z);

					newVertices.Add(pos);
				}

				for (var submesh = 0; submesh < submeshCount; ++submesh)
				{
					for (var i = 0; i < indexCounts[submesh] / 3; ++i)
					{
						// transform from blender to unity coordinate system
						newIndices[submesh].Add(oldIndices[submesh][3 * i] + z * vertexCount);
						newIndices[submesh].Add(oldIndices[submesh][3 * i + 1] + z * vertexCount);
						newIndices[submesh].Add(oldIndices[submesh][3 * i + 2] + z * vertexCount);
					}
				}

				for (var channel = 0; channel < 8; ++channel)
					for (var uv = 0; uv < oldUVs[channel].Count; ++uv)
						newUVs[channel].Add(oldUVs[channel][uv] + Vector2.up * settings.uvOffset * z);
			}

			var remainder = bezierLength - completeCopies * meshLength;
			var remainderVertices = oldVertices.ToList();
			var remainderIndices = oldIndices.Select(e => e.ToList()).ToArray();
			var remainderUVs = oldUVs.Select(e => e.ToList()).ToArray();
			for (var i = 0; i < submeshCount; ++i)
				ClipMeshZ(ref remainderVertices, ref remainderIndices[i], ref remainderUVs, remainder);

			remainderVertices = remainderVertices.Select(p =>
			{
				var pos = p + Vector3.forward * (meshLength * completeCopies);
				pos = new Vector3(pos.x, pos.y, pos.z);
				return pos;
			}).ToList();

			remainderIndices = remainderIndices.Select(e => e.Select(i => i + newVertices.Count).ToList()).ToArray();

			remainderUVs = remainderUVs.Select(e => e.Select(uv => uv + settings.uvOffset * completeCopies).ToList()).ToArray();

			newVertices.AddRange(remainderVertices);
			for (var i = 0; i < submeshCount; ++i)
				newIndices[i].AddRange(remainderIndices[i]);
			for (var i = 0; i < 8; ++i)
				newUVs[i].AddRange(remainderUVs[i]);

			// bend along bezier
			for (var v = 0; v < newVertices.Count; ++v)
			{
				var pos = newVertices[v];

				var pointIndex = Mathf.Clamp(Mathf.FloorToInt(pos.z / stepSize), 0, points.Length - 2);
				var weight = pos.z / stepSize - pointIndex;
				if (pointIndex == points.Length - 2)
				{
					weight = (pos.z - stepSize * pointIndex) /
						(points[points.Length - 1].position - points[points.Length - 2].position).magnitude;
				}
				Vector3 centerPos;
				Vector3 forward;
				Vector3 normal;
				if (pointIndex < points.Length - 1)
				{
					centerPos = Vector3.Lerp(points[pointIndex].position, points[pointIndex + 1].position, weight);
					forward = Vector3.Lerp(points[pointIndex].forward, points[pointIndex + 1].forward, weight).normalized;
					if (weight < 1e-6)
						normal = points[pointIndex].normal;
					else if (weight > 1 - 1e-6)
						normal = points[pointIndex + 1].normal;
					else
						normal = Vector3.Lerp(points[pointIndex].normal, points[pointIndex + 1].normal, weight);
				}
				else // Should not happen, except if the z coordinate is EXACTLY at the end of the bezier
				{
					centerPos = points[pointIndex].position;
					forward = points[pointIndex].forward;
					normal = points[pointIndex].normal;
				}
				var right = Vector3.Cross(normal, forward).normalized;

				pos = centerPos + right * pos.x + normal * pos.y;

				newVertices[v] = pos;
			}

			newMesh.subMeshCount = submeshCount;
			newMesh.SetVertices(newVertices);
			for (var i = 0; i < submeshCount; ++i)
				newMesh.SetIndices(newIndices[i].ToArray(), oldMesh.GetTopology(i), i);
			for (var i = 0; i < 8; ++i)
				newMesh.SetUVs(i, newUVs[i].ToArray());
			newMesh.RecalculateNormals();
			newMesh.RecalculateTangents();
			newMesh.RecalculateBounds();

			mf.mesh = newMesh;
			if (GetComponent<MeshCollider>() != null)
				GetComponent<MeshCollider>().sharedMesh = newMesh;

			Valid = true;
		}

		void ClipMeshZ(ref List<Vector3> verticesRef, ref List<int> indicesRef, ref List<Vector2>[] uvsRef, float maxZ)
		{
			var reuseVertices = true;

			var vertices = verticesRef;
			var indices = indicesRef;
			var uvs = uvsRef;

			var newVertices = vertices.ToList();
			var newIndices = new List<int>();
			var newUVs = uvs.Select(e => e.ToList()).ToArray();

			var intersectedIndices = new Dictionary<(int a, int b), int>();

			for (var tri = 0; tri + 3 <= indices.Count; tri += 3)
			{
				switch (new int[] { tri, tri + 1, tri + 2 }.Where(i => vertices[indices[i]].z <= maxZ).Count())
				{
					case 3:
						{
							newIndices.Add(indices[tri]);
							newIndices.Add(indices[tri + 1]);
							newIndices.Add(indices[tri + 2]);
							break;
						}
					case 2:
						{
							var a = indices[tri];
							var b = indices[tri + 1];
							var c = indices[tri + 2];
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
							if (vertices[c].z - vertices[a].z < 1e-6 || vertices[c].z - vertices[b].z < 1e-6) break;
							var va = vertices[a] + ac * (maxZ - vertices[a].z) / (vertices[c].z - vertices[a].z);
							var vb = vertices[b] + bc * (maxZ - vertices[b].z) / (vertices[c].z - vertices[b].z);

							var insertedA = false;
							int ia;
							if (!reuseVertices || !intersectedIndices.ContainsKey((a, c)))
							{
								newVertices.Add(va);
								ia = newVertices.Count - 1;
								intersectedIndices[(a, c)] = ia;
								insertedA = true;
							}
							else ia = intersectedIndices[(a, c)];
							var insertedB = false;
							int ib;
							if (!reuseVertices || !intersectedIndices.ContainsKey((b, c)))
							{
								newVertices.Add(vb);
								ib = newVertices.Count - 1;
								intersectedIndices[(b, c)] = ib;
								insertedB = true;
							}
							else ib = intersectedIndices[(b, c)];

							var weightA = (va - vertices[c]).magnitude / ac.magnitude;
							var weightB = (vb - vertices[c]).magnitude / bc.magnitude;
							for (var channel = 0; channel < 8; ++channel)
							{
								if (newUVs[channel].Count > 0)
								{
									if (insertedA)
										newUVs[channel].Add(
											weightA * uvs[channel][a] + (1 - weightA) * uvs[channel][c]);
									if (insertedB)
										newUVs[channel].Add(
											weightB * uvs[channel][b] + (1 - weightB) * uvs[channel][c]);
								}
							}
							newIndices.AddRange(new[] {
								a, b, ib,
								a, ib, ia
							});
							break;
						}
					case 1:
						{
							var a = indices[tri];
							var b = indices[tri + 1];
							var c = indices[tri + 2];
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
							if (!reuseVertices || !intersectedIndices.ContainsKey((c, a)))
							{
								newVertices.Add(va);
								ia = newVertices.Count - 1;
								intersectedIndices[(c, a)] = ia;
								insertedA = true;
							}
							else ia = intersectedIndices[(c, a)];
							var insertedB = false;
							int ib;
							if (!reuseVertices || !intersectedIndices.ContainsKey((c, b)))
							{
								newVertices.Add(vb);
								ib = newVertices.Count - 1;
								intersectedIndices[(c, b)] = ib;
								insertedB = true;
							}
							else ib = intersectedIndices[(c, b)];

							var weightA = (va - vertices[c]).magnitude / ca.magnitude;
							var weightB = (vb - vertices[c]).magnitude / cb.magnitude;
							for (var channel = 0; channel < 8; ++channel)
							{
								if (newUVs[channel].Count > 0)
								{
									if (insertedA)
										newUVs[channel].Add(
											weightA * uvs[channel][a] + (1 - weightA) * uvs[channel][c]);
									if (insertedB)
										newUVs[channel].Add(
											weightB * uvs[channel][b] + (1 - weightB) * uvs[channel][c]);
								}
							}
							newIndices.AddRange(new[] {
								ia, ib, c
							});
							break;
						}
				}
			}

			verticesRef = newVertices;
			indicesRef = newIndices;
			uvsRef = newUVs;
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
		public void GenerateRoadMeshBurst()
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
			var oldIndices = Enumerable.Range(0, submeshCount).Select(i => new List<int>(oldMesh.GetIndices(i))).ToArray();
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
			[ReadOnly] public NativeArray<Bezier.OrientedPoint> Points;
			[ReadOnly] public NativeArray<float3> Vertices;
			[ReadOnly] public UnsafeList<UnsafeList<int>> Indices;
			[ReadOnly] public UnsafeList<UnsafeList<float2>> UVs;
			[ReadOnly] public int CompleteCopies;
			[ReadOnly] public float MeshLength;
			[ReadOnly] public float BezierLength;
			[ReadOnly] public float StepSize;
			[ReadOnly] public float2 UVOffset;
			
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
						var pos = Vertices[v] + float3(0,0, yOffset);

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
		/// To be called whenever the road shape changes. Will regenerate the mesh if AutoGenerate is true.
		/// </summary>
		/// <param name="update">- whether to regenerate the mesh at all.</param>
		public void Invalidate(bool update = true)
		{
			Valid = false;
			// if (AutoGenerate && update) GenerateRoadMesh();
			if (AutoGenerate && update) GenerateRoadMeshBurst();
		}
	}
}
