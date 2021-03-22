using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Barmetler.RoadSystem
{
	[CustomEditor(typeof(RoadMeshGenerator))]
	public class RoadMeshEditor : Editor
	{
		RoadMeshGenerator roadMeshGenerator;

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			GUILayout.Space(10);
			Rect rect = EditorGUILayout.GetControlRect(false, 1);
			rect.height = 1;
			EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
			GUILayout.Space(10);

			GUILayout.BeginHorizontal();
			GUILayout.Label("Auto Generate", GUILayout.Width(EditorGUIUtility.labelWidth));
			bool autoGenerate = GUILayout.Toggle(roadMeshGenerator.AutoGenerate, "");
			if (autoGenerate != roadMeshGenerator.AutoGenerate)
			{
				Undo.RecordObject(roadMeshGenerator, "Toggle Auto Generate");
				roadMeshGenerator.AutoGenerate = autoGenerate;
			}
			GUILayout.EndHorizontal();

			GUILayout.Space(10);
			if (GUILayout.Button("Generate Mesh", GUILayout.Height(50)))
			{
				Undo.RecordObject(roadMeshGenerator, "Generate Mesh");
				roadMeshGenerator.GenerateRoadMesh();
			}
		}

		private void OnEnable()
		{
			roadMeshGenerator = (RoadMeshGenerator)target;
		}
	}
}
