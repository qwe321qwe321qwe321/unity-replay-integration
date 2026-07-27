using System.Collections.Generic;
using UnityEditor;

namespace UnityReplayIntegration.Editor {
	/// <summary>
	/// Manages the scripting defines that strip Replay Integration (<see cref="ExcludeDefine"/>) and
	/// InstantReplay itself (<see cref="ExcludeInstantReplayDefine"/>) for the currently selected
	/// build target group. The defines are stored in <c>ProjectSettings.asset</c> via
	/// <see cref="PlayerSettings"/>, so the settings are per-project and tracked by version control.
	/// </summary>
	internal static class ReplayIntegrationBuildSettings {
		public const string ExcludeDefine = "UNITY_REPLAY_INTEGRATION_EXCLUDED_IN_BUILD";

		/// <summary>
		/// Define recognized by the InstantReplay package itself: its assembly definitions carry a
		/// <c>!EXCLUDE_INSTANTREPLAY</c> constraint, so setting this drops the InstantReplay assemblies
		/// and native plugins entirely.
		/// </summary>
		public const string ExcludeInstantReplayDefine = "EXCLUDE_INSTANTREPLAY";

		public static bool ExcludeFromBuild {
			get => HasDefine(ExcludeDefine);
			set => SetDefine(ExcludeDefine, value);
		}

		public static bool ExcludeInstantReplay {
			get => HasDefine(ExcludeInstantReplayDefine);
			set => SetDefine(ExcludeInstantReplayDefine, value);
		}

		public static BuildTargetGroup SelectedGroup {
			get {
				var group = EditorUserBuildSettings.selectedBuildTargetGroup;
				return group == BuildTargetGroup.Unknown ? BuildTargetGroup.Standalone : group;
			}
		}

		static bool HasDefine(string define) => GetDefines(SelectedGroup).Contains(define);

		static List<string> GetDefines(BuildTargetGroup group) {
			string raw = PlayerSettings.GetScriptingDefineSymbolsForGroup(group) ?? string.Empty;
			var list = new List<string>();
			foreach (string token in raw.Split(';')) {
				string trimmed = token.Trim();
				if (trimmed.Length > 0) list.Add(trimmed);
			}
			return list;
		}

		static void SetDefine(string define, bool enabled) {
			var group = SelectedGroup;
			var defines = GetDefines(group);
			bool has = defines.Contains(define);
			if (enabled && !has) defines.Add(define);
			else if (!enabled && has) defines.Remove(define);
			else return;

			PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines));
		}
	}
}
