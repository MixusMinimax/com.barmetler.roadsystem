using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;

namespace Barmetler.RoadSystem
{
	public class RoadMenu : MonoBehaviour
	{
		static RoadEditor ActiveEditor => RoadEditor.GetEditor(Selection.activeGameObject);

		#region Validation
		public static bool MenuRoadIsSelected() =>
				ActiveEditor;

		[MenuItem("Tools/RoadSystem/Remove Point [backspace]", validate = true)]
		public static bool MenuPointIsSelected() =>
			ActiveEditor is RoadEditor editor &&
			editor.SelectedAnchorPoint != -1;

		[MenuItem("Tools/RoadSystem/Unlink Point %u", validate = true)]
		public static bool MenuEndPointIsSelectedAndConnected() =>
			ActiveEditor is RoadEditor editor &&
			editor.IsEndPoint(editor.SelectedAnchorPoint, YesNoMaybe.YES);

		[MenuItem("Tools/RoadSystem/Extrude", validate = true)]
		[MenuItem("Tools/RoadSystem/Link Point", validate = true)]
		public static bool MenuEndPointIsSelectedAndNotConnected() =>
			ActiveEditor is RoadEditor editor &&
			editor.IsEndPoint(editor.SelectedAnchorPoint, YesNoMaybe.NO);

		#endregion Validation

		#region Menus

		/// <summary>
		/// If road is selected, create new road on the same level
		/// If intersection is selected, create road on the same level
		/// If anchorPoint is selected, and it is free, create road connected to it
		/// If RoadSystem is selected, create road under it
		/// 
		/// Use Prefab defined in RoadSystemSettings
		/// </summary>
		[MenuItem("Tools/RoadSystem/Create Road")]
		public static void CreateRoad()
		{
			var selected = Selection.activeGameObject;
			Transform parent;
			GameObject newObject;
			Road road = null;

			if (RoadSystemSettings.Instance.NewRoadPrefab)
			{
				newObject = Instantiate(RoadSystemSettings.Instance.NewRoadPrefab);
				if (road = newObject.GetComponent<Road>())
					road.start = road.end = null;
			}
			else
			{
				newObject = new GameObject();
			}

			Undo.RegisterCreatedObjectUndo(newObject, "Create new Road");

			if (!road)
				road = newObject.AddComponent<Road>();

			if (!selected)
				parent = null;
			else if (selected.GetComponent<Road>())
				parent = selected.transform.parent;
			else if (selected.GetComponent<Intersection>())
				parent = selected.transform.parent;
			else if (selected.GetComponentInParent<Intersection>())
				parent = selected.GetComponentInParent<Intersection>().transform.parent;
			else
				parent = selected.transform;

			GameObjectUtility.SetParentAndAlign(newObject, parent ? parent.gameObject : null);

			if (selected.GetComponent<RoadAnchor>() is RoadAnchor anchor && !anchor.GetConnectedRoad())
			{
				road.start = anchor;
				road.RefreshEndPoints();
				var n = (road[1] - road[0]).normalized;
				foreach (int i in new[] { 1, 3, 2 })
					road.MovePoint(i, road[0] + i * n);
				road.MoveAngle(1, road.GetAngle(0));
			}

			Selection.activeGameObject = newObject;
		}

		[MenuItem("Tools/RoadSystem/Extrude %#e")]
		public static void MenuExtrude()
		{
			if (MenuEndPointIsSelectedAndNotConnected())
			{
				ActiveEditor.ExtrudeSelected();
			}
			else
			{
				Debug.LogError("No road endpoint selected!");
			}
		}

		[MenuItem("Tools/RoadSystem/Remove Point [backspace]")]
		public static void MenuRemove()
		{
			if (MenuPointIsSelected())
			{
				ActiveEditor.RemoveSelected();
			}
			else
			{
				Debug.LogError("No Road selected!");
			}
		}

		[MenuItem("Tools/RoadSystem/Unlink Point %u")]
		public static void MenuUnlink()
		{
			if (RoadLinkTool.ActiveInstance)
			{
				RoadLinkTool.UnlinkSelected();
			}
			else if (MenuEndPointIsSelectedAndConnected())
			{
				ActiveEditor.UnlinkSelected();
			}
			else
			{
				Debug.LogError("No connected Endpoint selected!");
			}
		}

		[MenuItem("Tools/RoadSystem/Link Point %l")]
		public static void MenuLink()
		{
			if (MenuEndPointIsSelectedAndNotConnected())
			{
				var editor = ActiveEditor;
				RoadLinkTool.Select(editor.road, editor.SelectedAnchorPoint == 0);
				ToolManager.SetActiveTool<RoadLinkTool>();
			}
			else
			{
				Debug.LogError("No road endpoint selected!");
			}
		}

		#endregion Menus
	}
}