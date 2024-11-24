using System.Collections.Generic;
using UnityEngine;
using Barmetler.DictExtensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Util;

namespace Barmetler
{
	namespace DictExtensions
	{
		public static class MyExtensions
		{
			/// <summary>
			/// Try to get a value from a Dictionary, but return default if the key is not in the Dictionary.
			/// </summary>
			/// <returns>this[k] if exists, defaultValue otherwise</returns>
			public static V GetWithDefault<K, V>(this Dictionary<K, V> dict, K key, V defaultValue)
			{
				return dict.TryGetValue(key, out var value) ? value : defaultValue;
			}
		}
	}

	public static class AStar
	{
		public class NodeBase
		{
			public Vector3 position;
		}

		public delegate float Heuristic<NodeType>(NodeType node, NodeType goal) where NodeType : NodeBase;

		static float DefaultHeuristic<NodeType>(NodeType node, NodeType goal) where NodeType : NodeBase
		{
			return (node.position - goal.position).magnitude;
		}

		/// <summary>
		/// Find the shortest path from one point to another in a di-graph.
		/// </summary>
		/// <typeparam name="NodeType">- Must Extend AStar.NodeBase</typeparam>
		/// <param name="nodes"></param>
		/// <param name="weights">weights[x,y] is edge cost from x to y.</param>
		/// <param name="start"></param>
		/// <param name="goal"></param>
		/// <param name="heuristic">Eulerian distance to goal per default.</param>
		/// <returns>Shortest path from start to goal.</returns>
		public static List<NodeType> FindShortestPath<NodeType>(
			List<NodeType> nodes, TwoDimensionalArray<float> weights, NodeType start, NodeType goal,
			Heuristic<NodeType> heuristic = null, int maxSteps = 10000
		)
			where NodeType : NodeBase
		{
			heuristic ??= DefaultHeuristic;
			float h(NodeType node) => heuristic(node, start);
			
			var cameFrom = new Dictionary<NodeType, NodeType>();
			var gScore = new Dictionary<NodeType, float>
			{
				[start] = 0
			};
			var fScore = new Dictionary<NodeType, float>
			{
				[start] = h(start)
			};

			var comparer = Comparer<NodeType>.Create((a, b) =>
			{
				var fCompare = fScore.GetWithDefault(a, float.PositiveInfinity)
					.CompareTo(fScore.GetWithDefault(b, float.PositiveInfinity));
				if (fCompare != 0) return fCompare;
				var gCompare = gScore.GetWithDefault(a, float.PositiveInfinity)
					.CompareTo(gScore.GetWithDefault(b, float.PositiveInfinity));
				if (gCompare != 0) return gCompare;
				return nodes.IndexOf(a).CompareTo(nodes.IndexOf(b));
			});
			
			var openSet = new SortedSet<NodeType>(new[] { start }, comparer);

			for (var steps = 0; openSet.Count > 0; ++steps)
			{
				if (steps == maxSteps)
					_ = 0; // for debugging
				if (steps > maxSteps)
					throw new System.Exception("Too many steps!");

				var current = openSet.Min;
				openSet.Remove(current);
				if (current == goal)
				{
					// Debug.Log("Steps taken: " + steps);
					return ReconstructPath(cameFrom, current);
				}

				// Pair is neighbor and the distance to that neighbor
				foreach (var neighbor in GetNeighbors(nodes, weights, current))
				{
					var tentative_gScore = gScore[current] + neighbor.Value;
					if (tentative_gScore < gScore.GetWithDefault(neighbor.Key, float.PositiveInfinity))
					{
						// remove before adding to make sure it's re-sorted
						openSet.Remove(neighbor.Key);

						cameFrom[neighbor.Key] = current;
						gScore[neighbor.Key] = tentative_gScore;
						fScore[neighbor.Key] = gScore[neighbor.Key] + h(neighbor.Key);

						openSet.Add(neighbor.Key);
					}
				}
			}

			throw new System.Exception("No Path Found!");
		}

		private static List<NodeType> ReconstructPath<NodeType>(Dictionary<NodeType, NodeType> cameFrom, NodeType current) where NodeType : NodeBase
		{
			var path = new List<NodeType>();
			while (cameFrom.ContainsKey(current))
			{
				path.Insert(0, current);
				current = cameFrom[current];
			}
			path.Insert(0, current);
			return path;
		}

		private static List<KeyValuePair<NodeType, float>> GetNeighbors<NodeType>(List<NodeType> nodes, TwoDimensionalArray<float> weights, NodeType current) where NodeType : NodeBase
		{
			var l = new List<KeyValuePair<NodeType, float>>();
			var index = nodes.IndexOf(current);
			for (var other = 0; other < nodes.Count; ++other)
			{
				if (index == other) continue;
				if (float.IsInfinity(weights[index, other])) continue;
				l.Add(new KeyValuePair<NodeType, float>(nodes[other], weights[index, other]));
			}
			return l;
		}

		private struct FindShortestPathJob : IJob
		{
			[ReadOnly] public NativeArray<float3> Nodes;
			[ReadOnly] public ExtendedTwoDimensionalNativeArray<float> Weights;
			[ReadOnly] public int Start;
			[ReadOnly] public int Goal;
			
			[WriteOnly] public NativeList<int> Path;

			public void Execute()
			{
				throw new System.NotImplementedException();
			}
		}

		public static int[] FindShortestPath(
			NativeArray<float3> nodes, ExtendedTwoDimensionalNativeArray<float> weights, int start, int goal
		)
		{
			var job = new FindShortestPathJob
			{
				Nodes = nodes,
				Weights = weights,
				Start = start,
				Goal = goal,
				Path = new NativeList<int>(Allocator.TempJob)
			};
			job.Run();
			var path = job.Path.ToArray();
			job.Path.Dispose();
			return path;
		}
	}
}
