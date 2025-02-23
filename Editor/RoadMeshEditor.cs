using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Barmetler.RoadSystem
{
    [CustomEditor(typeof(RoadMeshGenerator))]
    public class RoadMeshEditor : Editor
    {
        private RoadMeshGenerator roadMeshGenerator;

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck() && roadMeshGenerator.AutoGenerate)
                roadMeshGenerator.GenerateRoadMesh();

            GUILayout.Space(10);
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
            GUILayout.Space(10);

            var preset = roadMeshGenerator.settings.SourceOrientation.Preset;
            var options = MeshConversion.MeshOrientation.Presets.Keys.Append("CUSTOM").ToList();
            var selected = options.IndexOf(preset);
            if (selected != options.Count - 1) options.RemoveAt(options.Count - 1);
            var index = EditorGUILayout.Popup(
                new GUIContent(
                    "Source Orientation Preset",
                    "Coordinate space of the model.\n" +
                    "\n" +
                    "If right-handed, like in blender, face orientation is automatically swapped, so that it looks correct."
                ),
                selected, options.ToArray()
            );
            if (index != selected)
            {
                Undo.RecordObject(roadMeshGenerator, "Change Source Orientation Preset");
                roadMeshGenerator.settings.SourceOrientation.Preset = options[index];
                if (roadMeshGenerator.AutoGenerate) roadMeshGenerator.GenerateRoadMesh();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(
                    "Auto Generate",
                    "Automatically generate road mesh when something changes."
                ),
                GUILayout.Width(EditorGUIUtility.labelWidth)
            );
            bool autoGenerate = GUILayout.Toggle(roadMeshGenerator.AutoGenerate, "");
            if (autoGenerate != roadMeshGenerator.AutoGenerate)
            {
                Undo.RecordObject(roadMeshGenerator, "Toggle Auto Generate");
                roadMeshGenerator.AutoGenerate = autoGenerate;
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            if (GUILayout.Button(new GUIContent("Generate Mesh", ""), GUILayout.Height(50)))
            {
                Undo.RecordObject(roadMeshGenerator.gameObject, "Generate Mesh");
                roadMeshGenerator.GenerateRoadMesh();
            }

            GUILayout.Space(10);
            if (GUILayout.Button(new GUIContent("Generate Mesh v2", ""), GUILayout.Height(50)))
            {
                Undo.RecordObject(roadMeshGenerator.gameObject, "Generate Mesh v2");
                roadMeshGenerator.GenerateRoadMeshV2();
            }
        }

        private void OnEnable()
        {
            roadMeshGenerator = (RoadMeshGenerator)target;
        }
    }
}