using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Barmetler.RoadSystem
{
    [CustomEditor(typeof(Intersection))]
    public class IntersectionEditor : Editor
    {
        private static readonly int HandleColor = Shader.PropertyToID("_HandleColor");
        private static readonly int HandleSize = Shader.PropertyToID("_HandleSize");

        private Intersection _intersection;
        private List<Road> _affectedRoads;

        private void OnEnable()
        {
            _intersection = (Intersection)target;
            _intersection.Invalidate();
            _affectedRoads = _intersection.AnchorPoints.Select(e => e.GetConnectedRoad()).Where(e => e).ToList();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        public void OnUndoRedo()
        {
            _affectedRoads.ForEach(e => e.OnCurveChanged(true));
        }

        private void OnSceneGUI()
        {
            if (_intersection.transform.hasChanged)
            {
                _intersection.transform.hasChanged = false;
                _intersection.Invalidate();
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                "Packages/com.barmetler.roadsystem/Assets/Models/ArrowHorizontal.fbx");

            var size = HandleUtility.GetHandleSize(_intersection.transform.position) * 1.5f;

            if (Event.current.type == EventType.Repaint)
            {
                Handles.color = new Color(0.5f, 0.7f, 1.0f);
                Shader.SetGlobalColor(HandleColor, Handles.color);
                Shader.SetGlobalFloat(HandleSize, 100 * size);
                HandleUtility.handleMaterial.SetPass(0);

                // Graphics.DrawMeshNow(mesh,
                //     Handles.matrix * Matrix4x4.TRS(_intersection.transform.position,
                //         _intersection.transform.rotation * Quaternion.Euler(0, 90, 90),
                //         Vector3.one), -1
                // );
                // Graphics.DrawMeshNow(mesh,
                //     Handles.matrix * Matrix4x4.TRS(_intersection.transform.position,
                //         _intersection.transform.rotation * Quaternion.Euler(180, 90, 90),
                //         Vector3.one), -1
                // );

                // Handles.ArrowHandleCap();
            }
        }
    }
}
