using System.Collections.Generic;
using UnityEditor;

namespace UnityReplayIntegration.Editor {
	/// <summary>
	/// Manages the <see cref="ExcludeDefine"/> scripting define for the currently selected
	/// build target group. The define is stored in <c>ProjectSettings.asset</c> via
	/// <see cref="PlayerSettings"/>, so the setting is per-project and tracked by version control.
	/// </summary>
	internal static class ReplayIntegrationBuildSettings {
		public const string ExcludeDefine = "UNITY_REPLAY_INTEGRATION_EXCLUDED_IN_BUILD";

		public static bool ExcludeFromBuild {
			get => GetDefines(SelectedGroup).Contains(ExcludeDefine);
			set => SetDefineForSelectedGroup(value);
		}

		static BuildTargetGroup SelectedGroup {
			get {
				var group = EditorUserBuildSettings.selectedBuildTargetGroup;
				return group == BuildTargetGroup.Unknown ? BuildTargetGroup.Standalone : group;
			}
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

		static void SetDefineForSelectedGroup(bool enabled) {
			var group = SelectedGroup;
			var defines = GetDefines(group);
			bool has = defines.Contains(ExcludeDefine);
			if (enabled && !has) defines.Add(ExcludeDefine);
			else if (!enabled && has) defines.Remove(ExcludeDefine);
			else return;

			PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines));
		}
	}
}
