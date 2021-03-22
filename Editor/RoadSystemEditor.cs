using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Barmetler.RoadSystem
{
	[CustomEditor(typeof(RoadSystem))]
	public class RoadSystemEditor : Editor
	{
		RoadSystem roadSystem;

		private void OnSceneGUI()
		{
			Draw();
		}

		void Draw()
		{
			if (roadSystem.ShowDebugInfo)
			{
				var edges = roadSystem.GetGraphEdges();
				Handles.color = Color.blue;
				GUIStyle style = new GUIStyle();
				style.normal.textColor = Color.magenta;
				foreach (var edge in edges)
				{
					Handles.DrawLine(edge.start, edge.end);
					if (roadSystem.ShowEdgeWeights)
						Handles.Label((edge.start + edge.end) / 2, "Cost: " + edge.cost, style);
				}
			}
		}

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			if (GUILayout.Button("Construct Graph"))
			{
				roadSystem.ConstructGraph();
				EditorUtility.SetDirty(roadSystem);
				SceneView.RepaintAll();
			}

			if (GUILayout.Button("Rebuild All Roads"))
			{
				roadSystem.RebuildAllRoads();
			}
		}



		private void OnEnable()
		{
			roadSystem = (RoadSystem)target;
		}
	}
}
