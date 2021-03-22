using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Barmetler.RoadSystem
{
	public class RoadEditorWindow : EditorWindow
	{
		[MenuItem("Window/RoadSystem")]
		static void ShowWindow()
		{
			GetWindow(typeof(RoadEditorWindow));
		}

		Road SelectedRoad =>
			Selection.activeGameObject ? Selection.activeGameObject.GetComponent<Road>() : null;

		RoadSystem ActiveRoadSystem =>
			Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<RoadSystem>() : null;

		RoadEditor ActiveRoadEditor =>
			RoadEditor.GetEditor(Selection.activeGameObject);

		struct Button
		{
			public string Name;
			public string DisplayName;
			public string ToolTip;
			public System.Action OnClick;
			public System.Func<bool> IsEnabled;
			public Texture icon;
		}

		List<Button> Actions = new List<Button>();

		const float BUTTON_SIZE = 48;
		const float BUTTON_GAP = 4;

		private void OnEnable()
		{
			Actions = new List<Button>
			{
				new Button
				{
					Name = "new_road",
					DisplayName = "New Road",
					ToolTip = "Create a new Road",
					OnClick = RoadMenu.CreateRoad,
					IsEnabled = () => true,
					icon = EditorGUIUtility.Load("Packages/com.barmetler.roadsystem/Assets/Resources/Icons/Road.png") as Texture,
				},
				new Button
				{
					Name = "remove_point",
					DisplayName = "Remove Point",
					ToolTip = "Remove selected point from the Road [Backspace]",
					OnClick = RoadMenu.MenuRemove,
					IsEnabled = RoadMenu.MenuPointIsSelected,
					icon = EditorGUIUtility.Load("Packages/com.barmetler.roadsystem/Assets/Resources/Icons/RemovePoint.png") as Texture,
				},
				new Button
				{
					Name = "extrude",
					DisplayName = "Extrude",
					ToolTip = "Extrude Selected Endpoint [Ctrl+E]",
					OnClick = RoadMenu.MenuExtrude,
					IsEnabled = RoadMenu.MenuEndPointIsSelectedAndNotConnected,
					icon = EditorGUIUtility.Load("Packages/com.barmetler.roadsystem/Assets/Resources/Icons/Extrude.png") as Texture,
				},
				new Button
				{

				}
			};
		}

		private void OnInspectorUpdate()
		{
			Repaint();
		}

		private void OnGUI()
		{
			int rowWidth = Mathf.Max(1, (int)((EditorGUIUtility.currentViewWidth - BUTTON_GAP) / (BUTTON_SIZE + BUTTON_GAP)));

			int x = 0;
			foreach (var action in Actions)
			{
				if (x == 0)
					GUILayout.BeginHorizontal();

				var content = action.icon ?
					new GUIContent(action.icon, $"[{action.DisplayName}]: {action.ToolTip}") :
					new GUIContent(action.DisplayName, action.ToolTip);

				GUI.enabled = action.IsEnabled?.Invoke() ?? false;
				if (GUILayout.Button(content,
					GUILayout.Width(50), GUILayout.Height(50)))
				{
					action.OnClick?.Invoke();
				}

				else if (x == rowWidth - 1)
					GUILayout.EndHorizontal();
				x = (x + 1) % rowWidth;
			}
			GUI.enabled = true;
			if (x != 0)
				GUILayout.EndHorizontal();
		}
	}
}
