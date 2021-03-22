using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;

public abstract class MBEditorTool : EditorTool
{
	void OnEnable()
	{
		ToolManager.activeToolChanging += OnActiveToolWillChange;
		ToolManager.activeToolChanged += OnActiveToolDidChange;
		if (ToolManager.IsActiveTool(this))
			OnActivateTool();
		else
			OnDeactivateTool();
	}

	void OnDisable()
	{
		ToolManager.activeToolChanging -= OnActiveToolWillChange;
		ToolManager.activeToolChanged -= OnActiveToolDidChange;
	}

	void OnActiveToolWillChange()
	{
		if (ToolManager.IsActiveTool(this))
			OnDeactivateTool();
	}

	void OnActiveToolDidChange()
	{
		if (ToolManager.IsActiveTool(this))
			OnActivateTool();
	}

	protected virtual void OnActivateTool() { }
	protected virtual void OnDeactivateTool() { }
}
