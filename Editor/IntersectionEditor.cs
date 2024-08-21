using System.Linq;
using System.Collections.Generic;
using UnityEditor;

namespace Barmetler.RoadSystem
{
    [CustomEditor(typeof(Intersection))]
    public class IntersectionEditor : Editor
    {
        Intersection intersection;
        List<Road> affectedRoads;

        private void OnEnable()
        {
            intersection = (Intersection)target;
            intersection.Invalidate();
            affectedRoads = intersection.AnchorPoints.Select(e => e.GetConnectedRoad()).Where(e => e).ToList();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        public void OnUndoRedo()
        {
            affectedRoads.ForEach(e => e.OnCurveChanged(true));
        }

        private void OnSceneGUI()
        {
            if (intersection.transform.hasChanged)
            {
                intersection.transform.hasChanged = false;
                intersection.Invalidate();
            }
        }
    }
}