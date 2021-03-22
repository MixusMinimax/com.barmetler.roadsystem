using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Barmetler.RoadSystem
{
	[RequireComponent(typeof(Road))]
	[RequireComponent(typeof(MeshFilter))]
	public class RoadMeshGenerator : MonoBehaviour
	{
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
		bool autoGenerate;

		public MeshFilter SourceMesh;
		public float vOffset = 1;

		public bool valid { private set; get; }

		private Road road;
		private MeshFilter mf;

		private void OnValidate()
		{
			road = GetComponent<Road>();
			mf = GetComponent<MeshFilter>();
		}

		public void GenerateRoadMesh()
		{
			if (!road) road = GetComponent<Road>();
			if (!road) return;
			if (!SourceMesh) return;

			float stepSize = 1;

			var points = road.GetEvenlySpacedPoints(stepSize, 1);

			Mesh oldMesh = SourceMesh.sharedMesh;
			Mesh newMesh = new Mesh();

			float meshLength = oldMesh.bounds.size.y;
			float meshOffset = -oldMesh.bounds.min.y;

			// The last point is repositioned to the end of the bezier
			float bezierLength = stepSize * points.Length - stepSize +
				Vector3.Distance(points[points.Length - 2].position, points[points.Length - 1].position);

			int completeCopies = Mathf.FloorToInt(bezierLength / meshLength);

			int submeshCount = oldMesh.subMeshCount;

			var oldVertices = new List<Vector3>();
			oldMesh.GetVertices(oldVertices);
			var oldIndices = Enumerable.Range(0, submeshCount).Select(i => new List<int>(oldMesh.GetIndices(i))).ToArray();
			var oldUVs = Enumerable.Range(0, submeshCount)
				.Select(delegate (int i)
				{
					var x = new List<Vector2>();
					oldMesh.GetUVs(i, x);
					return x;
				})
				.ToArray();

			var newVertices = new List<Vector3>();
			var newIndices = Enumerable.Range(0, submeshCount).Select(_ => new List<int>()).ToArray();
			var newUVs = Enumerable.Range(0, submeshCount).Select(_ => new List<Vector2>()).ToArray();

			int vertexCount = oldVertices.Count;
			int[] indexCounts = oldIndices.Select(e => e.Count).ToArray();

			for (int z = 0; z < completeCopies; ++z)
			{
				float yOffset = z * meshLength;

				for (int v = 0; v < vertexCount; ++v)
				{
					Vector3 pos = oldVertices[v] + Vector3.up * (yOffset + meshOffset);
					// transform from blender to unity coordinate system
					pos = new Vector3(pos.x, pos.z, pos.y);

					newVertices.Add(pos);
				}

				for (int submesh = 0; submesh < submeshCount; ++submesh)
				{
					for (int i = 0; i < indexCounts[submesh] / 3; ++i)
					{
						// transform from blender to unity coordinate system
						newIndices[submesh].Add(oldIndices[submesh][3 * i] + z * vertexCount);
						newIndices[submesh].Add(oldIndices[submesh][3 * i + 2] + z * vertexCount);
						newIndices[submesh].Add(oldIndices[submesh][3 * i + 1] + z * vertexCount);
					}

					for (int uv = 0; uv < oldUVs[submesh].Count; ++uv)
						newUVs[submesh].Add(oldUVs[submesh][uv] + Vector2.up * vOffset * z);
				}
			}

			float remainder = bezierLength - completeCopies * meshLength;
			var remainderVertices = oldVertices.ToList();
			var remainderIndices = oldIndices.Select(e => e.ToList()).ToArray();
			var remainderUVs = oldUVs.Select(e => e.ToList()).ToArray();
			for (int i = 0; i < submeshCount; ++i)
				ClipMeshZ(ref remainderVertices, ref remainderIndices[i], ref remainderUVs[i], remainder);

			remainderVertices = remainderVertices.Select(delegate (Vector3 p)
			{
				Vector3 pos = p + Vector3.up * (meshLength * completeCopies + meshOffset);
				// transform from blender to unity coordinate system
				pos = new Vector3(pos.x, pos.z, pos.y);
				return pos;
			}).ToList();

			remainderIndices = remainderIndices.Select(e => e.Select(i => i + newVertices.Count).ToList()).ToArray();

			remainderUVs = remainderUVs.Select(e => e.Select(uv => uv + Vector2.up * vOffset * completeCopies).ToList()).ToArray();

			newVertices.AddRange(remainderVertices);
			for (int i = 0; i < submeshCount; ++i)
			{
				newIndices[i].AddRange(remainderIndices[i]);
				newUVs[i].AddRange(remainderUVs[i]);
			}

			// bend along bezier
			for (int v = 0; v < newVertices.Count; ++v)
			{
				Vector3 pos = newVertices[v];

				int pointIndex = Mathf.Clamp(Mathf.FloorToInt(pos.z / stepSize), 0, points.Length - 1);
				float weight = pos.z / stepSize - pointIndex;
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
						normal = Bezier.NormalFromAngle(forward, Mathf.LerpAngle(
							Bezier.AngleFromNormal(points[pointIndex].forward, points[pointIndex].normal),
							Bezier.AngleFromNormal(points[pointIndex + 1].forward, points[pointIndex + 1].normal),
							weight));
				}
				else // Should not happen, except if the z coordinate is EXACTLY at the end of the bezier
				{
					centerPos = points[pointIndex].position;
					forward = points[pointIndex].forward;
					normal = points[pointIndex].normal;
				}
				Vector3 right = Vector3.Cross(normal, forward).normalized;

				pos = centerPos + right * pos.x + normal * pos.y;

				newVertices[v] = pos;
			}

			newMesh.subMeshCount = submeshCount;
			newMesh.SetVertices(newVertices);
			for (int i = 0; i < submeshCount; ++i)
			{
				newMesh.SetIndices(newIndices[i].ToArray(), oldMesh.GetTopology(i), i);
				newMesh.SetUVs(i, newUVs[i].ToArray());
			}
			newMesh.RecalculateNormals();
			newMesh.RecalculateBounds();

			mf.mesh = newMesh;
			if (GetComponent<MeshCollider>() != null)
				GetComponent<MeshCollider>().sharedMesh = newMesh;

			valid = true;
		}

		void ClipMeshZ(ref List<Vector3> verticesRef, ref List<int> indicesRef, ref List<Vector2> uvsRef, float maxZ)
		{
			var vertices = verticesRef;
			var indices = indicesRef;
			var uvs = uvsRef;

			var newVertices = vertices.ToList();
			var newIndices = new List<int>();
			var newUVs = uvs.ToList();

			for (int tri = 0; tri < indices.Count / 3; ++tri)
			{
				switch (new int[] { tri * 3, tri * 3 + 1, tri * 3 + 2 }.Where(i => vertices[indices[i]].y <= maxZ).Count())
				{
					case 3:
						newIndices.Add(indices[tri * 3]);
						newIndices.Add(indices[tri * 3 + 2]);
						newIndices.Add(indices[tri * 3 + 1]);
						break;
				}
			}

			verticesRef = newVertices;
			indicesRef = newIndices;
			uvsRef = newUVs;
		}

		public void Invalidate(bool update = true)
		{
			valid = false;
			if (AutoGenerate && update) GenerateRoadMesh();
		}
	}
}
