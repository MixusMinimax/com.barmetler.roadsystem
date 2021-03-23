using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Barmetler
{
	public static class StringUtility
	{
		public static string GetInitials(string str)
		{
			if (str == null) return null;
			MatchCollection matches;

			if (Regex.IsMatch(str, @"^([A-Z][a-z0-9_]*)+$"))
				matches = Regex.Matches(str, @"([A-Z][^A-Z]*)");
			else
				matches = Regex.Matches(str, @"([A-Za-z][^ \-_]*)");

			return string.Join(
				"",
				from g
				in matches.Cast<Match>()
				where g.Value.Length > 0
				select (g.Value.ToCharArray()[0] + "").ToUpper()
			);
		}
	}
}
