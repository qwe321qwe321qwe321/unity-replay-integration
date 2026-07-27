using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace UnityReplayIntegration.Editor {
	/// <summary>
	/// Manages the scripting defines that strip Replay Integration (<see cref="ExcludeDefine"/>) and
	/// InstantReplay itself (<see cref="ExcludeInstantReplayDefine"/>) for the currently selected
	/// build target group.
	///
	/// Reads and writes go to the GLOBAL player settings in <c>ProjectSettings/ProjectSettings.asset</c>,
	/// deliberately bypassing the active Build Profile. The <see cref="PlayerSettings"/> static API is
	/// not used for this: since Unity 6, when the active build profile carries a Player Settings
	/// override, those statics resolve to the profile's own PlayerSettings copy, so writes silently
	/// land in the profile asset instead of the project setting.
	///
	/// Because the profile override still wins at build/compile time, <see cref="IsOverriddenByActiveProfile"/>
	/// reports when the effective value differs from the global one so the UI can warn about it.
	/// </summary>
	internal static class ReplayIntegrationBuildSettings {
		public const string ExcludeDefine = "UNITY_REPLAY_INTEGRATION_EXCLUDED_IN_BUILD";

		/// <summary>
		/// Define recognized by the InstantReplay package itself: its assembly definitions carry a
		/// <c>!EXCLUDE_INSTANTREPLAY</c> constraint, so setting this drops the InstantReplay assemblies
		/// and native plugins entirely.
		/// </summary>
		public const string ExcludeInstantReplayDefine = "EXCLUDE_INSTANTREPLAY";

		const string k_ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";
		const string k_DefinesProperty     = "scriptingDefineSymbols";

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

		public static bool HasDefine(string define) => GetGlobalDefines(SelectedGroup).Contains(define);

		/// <summary>
		/// The value that actually applies to compilation and builds right now, which is the active
		/// build profile's override when it has one, and the global setting otherwise.
		/// </summary>
		public static bool HasEffectiveDefine(string define) =>
			SplitDefines(PlayerSettings.GetScriptingDefineSymbolsForGroup(SelectedGroup)).Contains(define);

		/// <summary>
		/// True when the active build profile overrides player settings in a way that disagrees with
		/// the global value for <paramref name="define"/> — i.e. the toggle state and what gets built differ.
		/// </summary>
		public static bool IsOverriddenByActiveProfile(string define) =>
			HasDefine(define) != HasEffectiveDefine(define);

		// ─────────────────────────────────────────────────────────────────
		// Global (ProjectSettings.asset) access
		// ─────────────────────────────────────────────────────────────────

		/// <summary>
		/// The global PlayerSettings object. Loading it by path returns the project-level instance even
		/// when a build profile override is active, unlike the <see cref="PlayerSettings"/> statics.
		/// </summary>
		static SerializedObject LoadGlobalPlayerSettings() {
			var assets = AssetDatabase.LoadAllAssetsAtPath(k_ProjectSettingsPath);
			if (assets == null) return null;
			foreach (var asset in assets) {
				if (asset is PlayerSettings) return new SerializedObject(asset);
			}
			return null;
		}

		/// <summary>
		/// Locates the entry for <paramref name="group"/> inside the serialized
		/// <c>map&lt;string, string&gt;</c> of per-platform defines. Returns null when the platform has
		/// no entry yet and <paramref name="createIfMissing"/> is false.
		/// </summary>
		static SerializedProperty FindDefinesProperty(SerializedObject settings, BuildTargetGroup group, bool createIfMissing) {
			var map = settings.FindProperty(k_DefinesProperty);
			if (map == null || !map.isArray) return null;

			string key = GetPlatformKey(group);
			if (string.IsNullOrEmpty(key)) return null;

			for (int i = 0; i < map.arraySize; i++) {
				var pair = map.GetArrayElementAtIndex(i);
				var first = pair.FindPropertyRelative("first");
				if (first != null && first.stringValue == key)
					return pair.FindPropertyRelative("second");
			}

			if (!createIfMissing) return null;

			int index = map.arraySize;
			map.InsertArrayElementAtIndex(index);
			var newPair = map.GetArrayElementAtIndex(index);
			var newKey = newPair.FindPropertyRelative("first");
			var newValue = newPair.FindPropertyRelative("second");
			if (newKey == null || newValue == null) return null;
			newKey.stringValue = key;
			newValue.stringValue = string.Empty;
			return newValue;
		}

		/// <summary>
		/// The key used in ProjectSettings.asset, which is the NamedBuildTarget name
		/// ("Standalone", "WebGL", "Windows Store Apps", …) rather than the enum's ToString().
		/// </summary>
		static string GetPlatformKey(BuildTargetGroup group) {
			try { return NamedBuildTarget.FromBuildTargetGroup(group).TargetName; }
			catch { return null; }
		}

		static List<string> GetGlobalDefines(BuildTargetGroup group) {
			var settings = LoadGlobalPlayerSettings();
			var map = settings?.FindProperty(k_DefinesProperty);
			if (map != null && map.isArray) {
				var property = FindDefinesProperty(settings, group, createIfMissing: false);
				// A missing entry legitimately means "no defines for this platform".
				return property == null ? new List<string>() : SplitDefines(property.stringValue);
			}

			// Fallback for editor versions where the serialized layout does not match.
			return SplitDefines(PlayerSettings.GetScriptingDefineSymbolsForGroup(group));
		}

		static void SetDefine(string define, bool enabled) {
			var group = SelectedGroup;
			bool effectiveBefore = HasEffectiveDefine(define);

			var settings = LoadGlobalPlayerSettings();
			var property = settings == null ? null : FindDefinesProperty(settings, group, createIfMissing: enabled);
			if (settings == null || property == null) {
				Debug.LogWarning(
					"[UnityReplayIntegration] Could not access the global scripting define symbols in " +
					k_ProjectSettingsPath + "; falling back to the PlayerSettings API, which writes to the " +
					"active build profile's player settings override when one exists.");
				SetDefineViaPlayerSettings(group, define, enabled);
				return;
			}

			var defines = SplitDefines(property.stringValue);
			bool has = defines.Contains(define);
			if (enabled && !has) defines.Add(define);
			else if (!enabled && has) defines.Remove(define);
			else return;

			property.stringValue = string.Join(";", defines);
			settings.ApplyModifiedProperties();
			AssetDatabase.SaveAssets();

			// Only the effective define set drives compilation; if a profile override masks this
			// change there is nothing to recompile.
			if (HasEffectiveDefine(define) != effectiveBefore)
				UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
		}

		static void SetDefineViaPlayerSettings(BuildTargetGroup group, string define, bool enabled) {
			var defines = SplitDefines(PlayerSettings.GetScriptingDefineSymbolsForGroup(group));
			bool has = defines.Contains(define);
			if (enabled && !has) defines.Add(define);
			else if (!enabled && has) defines.Remove(define);
			else return;

			PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines));
		}

		static List<string> SplitDefines(string raw) {
			var list = new List<string>();
			foreach (string token in (raw ?? string.Empty).Split(';')) {
				string trimmed = token.Trim();
				if (trimmed.Length > 0) list.Add(trimmed);
			}
			return list;
		}
	}
}
