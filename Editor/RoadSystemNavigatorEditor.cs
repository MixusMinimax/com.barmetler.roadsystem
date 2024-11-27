using UnityEngine;
using UnityEditor;
using UnityEditor.Graphs;

namespace Barmetler.RoadSystem
{
    [CustomEditor(typeof(RoadSystemNavigator))]
    public class RoadSystemNavigatorEditor : Editor
    {
        private RoadSystemNavigator navigator;
        private RoadSystemSettings settings;

        private void OnSceneGUI()
        {
            if (!Application.isPlaying && navigator.transform.hasChanged)
            {
                UpdateNavigator();
                navigator.transform.hasChanged = false;
            }

            Draw();
        }

        private void UpdateNavigator()
        {
            if (!settings.AutoCalculateNavigator) return;

            try
            {
                navigator.CalculateWayPointsSync();
            }
            catch (System.Exception e)
            {
                Debug.LogError(e);
            }

            SceneView.RepaintAll();
        }

        private void Draw()
        {
            if (!settings.DrawNavigatorDebug) return;

            var points = navigator.CurrentPoints;

            Vector3 position;
            var lastPos = navigator.transform.position;
            Handles.color = Color.yellow;
            foreach (var point in points)
            {
                position = point.position;
                Handles.DrawLine(lastPos, position);
                lastPos = position;
            }

            position = navigator.Goal;
            Handles.DrawLine(lastPos, position);

            {
                var d1 = navigator.GetMinDistance(out _, out var p1, out _);
                var d2 = navigator.GetMinDistance(out _, out _, out var p2, out _);
                var p = d1 < d2 ? p1 : p2;
                Handles.SphereHandleCap(0, p, Quaternion.identity, 0.5f, EventType.Repaint);
            }

            if (settings.DrawNavigatorDebugPoints)
            {
                foreach (var point in points)
                {
                    Handles.SphereHandleCap(0, point.position, Quaternion.identity, 0.2f, EventType.Repaint);
                }
            }
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();

            base.OnInspectorGUI();

            var drawDebug = GUILayout.Toggle(settings.DrawNavigatorDebug, "Draw Navigator Debug Info");
            if (drawDebug != settings.DrawNavigatorDebug)
            {
                Undo.RecordObject(settings, "Toggle Draw Navigator Debug Info");
                settings.DrawNavigatorDebug = drawDebug;
            }

            var drawDebugPoints = GUILayout.Toggle(settings.DrawNavigatorDebugPoints, "Draw Navigator Debug Points");
            if (drawDebugPoints != settings.DrawNavigatorDebugPoints)
            {
                Undo.RecordObject(settings, "Toggle Draw Navigator Debug Points");
                settings.DrawNavigatorDebugPoints = drawDebugPoints;
            }

            var autoCalculate = GUILayout.Toggle(settings.AutoCalculateNavigator, "Auto Calculate Navigator");
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