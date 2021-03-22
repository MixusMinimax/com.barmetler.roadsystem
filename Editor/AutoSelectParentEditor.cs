using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Barmetler
{
	[CustomEditor(typeof(AutoSelectParent))]
	public class AutoSelectParentEditor : Editor
	{
		private void OnEnable()
		{
			AutoSelectParent child = (AutoSelectParent)target;
			AutoSelectFromChild parent = child.GetComponentInParent<AutoSelectFromChild>();
			if (parent?.enabled ?? false)
			{
				Selection.activeObject = parent;
			}
		}
	}
}
