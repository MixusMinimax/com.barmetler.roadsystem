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
			public enum ESymbol
			{
				NONE = 0, PLUS = 1, MINUS = 2,
			}

			public string Name;
			public string DisplayName;
			public string ToolTip;
			public ESymbol Symbol;
			public System.Action OnClick;
			public System.Action OnClickAlt;
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
					Name = "new_road_system",
					DisplayName = "New Road System",
					ToolTip = "Create a new Road System",
					Symbol = Button.ESymbol.PLUS,
					OnClick = RoadMenu.CreateRoadSystem,
				},
				new Button
				{
					Name = "new_road",
					DisplayName = "New Road",
					ToolTip = "Create a new Road",
					Symbol = Button.ESymbol.PLUS,
					OnClick = RoadMenu.CreateRoad,
					OnClickAlt = NewRoadWizard.CreateWizard,
					icon = EditorGUIUtility.Load("Packages/com.barmetler.roadsystem/Assets/Resources/Icons/Road.png") as Texture,
				},
				new Button
				{
					Name = "remove_point",
					DisplayName = "Remove Point",
					ToolTip = "Remove selected point from the Road [Backspace]",
					Symbol = Button.ESymbol.MINUS,
					OnClick = RoadMenu.MenuRemove,
					IsEnabled = RoadMenu.MenuPointIsSelected,
					icon = EditorGUIUtility.Load("Packages/com.barmetler.roadsystem/Assets/Resources/Icons/RemovePoint.png") as Texture,
				},
				new Button
				{
					Name = "extrude",
					DisplayName = "Extrude",
					ToolTip = "Extrude Selected Endpoint [Ctrl+E]",
					Symbol = Button.ESymbol.PLUS,
					OnClick = RoadMenu.MenuExtrude,
					IsEnabled = RoadMenu.MenuEndPointIsSelectedAndNotConnected,
					icon = EditorGUIUtility.Load("Packages/com.barmetler.roadsystem/Assets/Resources/Icons/Extrude.png") as Texture,
				},
			};
		}

		private void OnInspectorUpdate()
		{
			Repaint();
		}

		private void OnGUI()
		{
			var popupStyle = new GUIStyle();
			popupStyle.padding = new RectOffset(2, 2, 2, 2);
			popupStyle.alignment = TextAnchor.UpperRight;
			var symbolStyle = new GUIStyle(popupStyle);
			symbolStyle.alignment = TextAnchor.LowerRight;
			var popupIcon = EditorGUIUtility.IconContent("_Popup");
			var plusIcon = EditorGUIUtility.IconContent("d_Toolbar Plus");
			var minusIcon = EditorGUIUtility.IconContent("d_Toolbar Minus");

			GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
			buttonStyle.fontSize = 24;
			buttonStyle.fontStyle = FontStyle.Bold;

			int rowWidth = Mathf.Max(1, (int)((EditorGUIUtility.currentViewWidth - BUTTON_GAP) / (BUTTON_SIZE + BUTTON_GAP)));

			int x = 0;
			foreach (var action in Actions)
			{
				if (x == 0)
					GUILayout.BeginHorizontal();

				var ToolTip = action.ToolTip;
				if (action.OnClickAlt != null)
					ToolTip += "\n\n(ALT-Click for more Settings)";

				var content = action.icon ?
					new GUIContent(action.icon, $"[{action.DisplayName}]: {ToolTip}") :
					new GUIContent(GetInitials(action.DisplayName), ToolTip);

				GUI.enabled = action.OnClick != null && (action.IsEnabled?.Invoke() ?? true);
				if (GUILayout.Button(content, buttonStyle,
					GUILayout.Width(50), GUILayout.Height(50)))
				{
					if (action.OnClickAlt != null && Event.current.alt)
						action.OnClickAlt();
					else
						action.OnClick?.Invoke();
				}

				var rect = GUILayoutUtility.GetLastRect();

				switch (action.Symbol)
				{
					case Button.ESymbol.PLUS:
						GUI.Label(rect, plusIcon, symbolStyle);
						break;
					case Button.ESymbol.MINUS:
						GUI.Label(rect, minusIcon, symbolStyle);
						break;
				}

				if (action.OnClickAlt != null)
				{
					GUI.Label(rect, popupIcon, popupStyle);
				}

				if (x == rowWidth - 1)
					GUILayout.EndHorizontal();
				x = (x + 1) % rowWidth;
			}
			GUI.enabled = true;
			if (x != 0)
				GUILayout.EndHorizontal();
		}

		static string GetInitials(string str)
		{
			if (str == null) return "";
			str = str.ToLower();
			if (str.StartsWith("new"))
				str = str.Substring(3);
			return StringUtility.GetInitials(str);
		}
	}
}
