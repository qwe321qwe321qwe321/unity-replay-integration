using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace UnityReplayIntegration.Editor {
	/// <summary>
	/// Manages the <see cref="ExcludeDefine"/> scripting define across all build target groups.
	/// The define is stored directly in <c>ProjectSettings.asset</c> via <see cref="PlayerSettings"/>,
	/// so the setting is per-project and tracked by version control.
	/// </summary>
	internal static class ReplayIntegrationBuildSettings {
		public const string ExcludeDefine = "UNITY_REPLAY_INTEGRATION_EXCLUDED_IN_BUILD";

		public static bool ExcludeFromBuild {
			get {
				var group = EditorUserBuildSettings.selectedBuildTargetGroup;
				if (group == BuildTargetGroup.Unknown) group = BuildTargetGroup.Standalone;
				return GetDefines(group).Contains(ExcludeDefine);
			}
			set => SetDefineAllGroups(value);
		}

		static List<string> GetDefines(BuildTargetGroup group) {
			string raw = PlayerSettings.GetScriptingDefineSymbolsForGroup(group) ?? string.Empty;
			var list = new List<string>();
			foreach (string token in raw.Split(';')) {
				string trimmed = token.Trim();
				if (trimmed.Length > 0) list.Add(trimmed);
			}
			return list;
		}

		static void SetDefineAllGroups(bool enabled) {
			foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup))) {
				if (group == BuildTargetGroup.Unknown) continue;
				if (IsObsolete(group)) continue;

				var defines = GetDefines(group);
				bool has = defines.Contains(ExcludeDefine);
				if (enabled && !has) defines.Add(ExcludeDefine);
				else if (!enabled && has) defines.Remove(ExcludeDefine);
				else continue;

				PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines));
			}
		}

		static bool IsObsolete(BuildTargetGroup group) {
			FieldInfo field = typeof(BuildTargetGroup).GetField(group.ToString());
			if (field == null) return true;
			return Attribute.IsDefined(field, typeof(ObsoleteAttribute));
		}
	}
}
