using UnityEngine;
using UnityEditor;

namespace Barmetler.RoadSystem
{
	[CustomEditor(typeof(RoadSystemNavigator))]
	public class RoadSystemNavigatorEditor : Editor
	{
		RoadSystemNavigator navigator;
		RoadSystemSettings settings;

		private void OnSceneGUI()
		{
			if (!Application.isPlaying && navigator.transform.hasChanged)
			{
				UpdateNavigator();
				navigator.transform.hasChanged = false;
			}
			Draw();
		}

		void UpdateNavigator()
		{
			if (settings.AutoCalculateNavigator)
			{
				try
				{
					navigator.CalculateWayPointsSync();
				} catch (System.Exception e)
				{
					Debug.LogError(e);
				}

				SceneView.RepaintAll();
			}
		}

		void Draw()
		{
			if (settings.DrawNavigatorDebug)
			{
				var points = navigator.CurrentPoints;

				Vector3 position;
				Vector3 lastPos = navigator.transform.position;
				Handles.color = Color.yellow;
				foreach (var point in points.Points)
				{
					position = point.position;
					Handles.DrawLine(lastPos, position);
					lastPos = position;
				}
				position = navigator.Goal;
				Handles.DrawLine(lastPos, position);

				{
					float d1 = navigator.GetMinDistance(out Road _, out var p1, out _);
					float d2 = navigator.GetMinDistance(out Intersection _, out RoadAnchor _, out var p2, out _);
					var p = d1 < d2 ? p1 : p2;
					Handles.SphereHandleCap(0, p, Quaternion.identity, 0.5f, EventType.Repaint);
				}

				if (settings.DrawNavigatorDebugPoints)
				{
					foreach (var point in points.Points)
					{
						Handles.SphereHandleCap(0, point.position, Quaternion.identity, 0.2f, EventType.Repaint);
					}
				}

				if (points.Nodes.Count > 0 && navigator.currentRoadSystem)
				{
					foreach (var node in points.Nodes)
					{
						var pos = navigator.currentRoadSystem.transform.TransformPoint(node.position);
						Handles.Label(pos, node.nodeType.ToString(),
							new GUIStyle { normal = new GUIStyleState { textColor = Color.red } });
					}
				}
			}
		}

		public override void OnInspectorGUI()
		{
			EditorGUI.BeginChangeCheck();

			base.OnInspectorGUI();

			bool drawDebug = GUILayout.Toggle(settings.DrawNavigatorDebug, "Draw Navigator Debug Info");
			if (drawDebug != settings.DrawNavigatorDebug)
			{
				Undo.RecordObject(settings, "Toggle Draw Navigator Debug Info");
				settings.DrawNavigatorDebug = drawDebug;
			}

			bool drawDebugPoints = GUILayout.Toggle(settings.DrawNavigatorDebugPoints, "Draw Navigator Debug Points");
			if (drawDebugPoints != settings.DrawNavigatorDebugPoints)
			{
				Undo.RecordObject(settings, "Toggle Draw Navigator Debug Points");
				settings.DrawNavigatorDebugPoints = drawDebugPoints;
			}

			bool autoCalculate = GUILayout.Toggle(settings.AutoCalculateNavigator, "Auto Calculate Navigator");
			if (autoCalculate != settings.AutoCalculateNavigator)
			{
				Undo.RecordObject(settings, "Toggle Auto Calculate Navigator");
				settings.AutoCalculateNavigator = autoCalculate;
			}

			if (GUILayout.Button("Calculate WayPoints"))
			{
				navigator.CalculateWayPointsSync();
				SceneView.RepaintAll();
			}

			if (EditorGUI.EndChangeCheck())
			{
				UpdateNavigator();
			}
		}


		private void OnEnable()
		{
			navigator = (RoadSystemNavigator)target;
			settings = RoadSystemSettings.Instance;
			UpdateNavigator();
		}
	}
}
