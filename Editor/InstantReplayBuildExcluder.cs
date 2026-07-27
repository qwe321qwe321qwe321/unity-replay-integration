using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnityReplayIntegration.Editor {
	/// <summary>
	/// Drops InstantReplay from built players while leaving the Editor untouched, so recording keeps
	/// working in Play Mode.
	///
	/// A scripting define cannot express this: InstantReplay's assembly definitions constrain on
	/// <c>!EXCLUDE_INSTANTREPLAY</c>, and define symbols apply to Editor compilation as well, so that
	/// switch always removes the package from the Editor too. Instead this runs at build time:
	/// <see cref="IFilterBuildAssemblies"/> removes the managed assemblies from the player, and the
	/// native encoder plugins are marked incompatible for the duration of the build (the same approach
	/// InstantReplay's own PluginsExcluder uses), then restored.
	///
	/// Enabled per platform group via <see cref="ReplayIntegrationBuildSettings.ExcludeInstantReplayFromBuild"/>.
	/// </summary>
	class InstantReplayBuildExcluder : IFilterBuildAssemblies, IPreprocessBuildWithReport, IPostprocessBuildWithReport {
		// Must run late enough that assemblies are known, but the value is not order sensitive.
		public int callbackOrder => 0;

		const string k_PackageId    = "jp.co.cyberagent.instant-replay";
		const string k_PluginFolder = "/UniEnc/Plugins/";

		// Paths whose PluginImporter compatibility we turned off for this build, so a crashed or
		// cancelled build does not leave the project with permanently disabled native plugins.
		const string k_PendingRestoreKey = "UnityReplayIntegration.InstantReplayBuildExcluder.PendingRestore";

		static bool IsEnabledFor(BuildTarget target) {
			var group = BuildPipeline.GetBuildTargetGroup(target);
			if (group == BuildTargetGroup.Unknown) return false;
			return ReplayIntegrationBuildSettings.HasEffectiveDefine(
				ReplayIntegrationBuildSettings.ExcludeInstantReplayFromBuildDefine, group);
		}

		// ─────────────────────────────────────────────────────────────────
		// Managed assemblies
		// ─────────────────────────────────────────────────────────────────

		/// <summary>
		/// Matches InstantReplay's own assemblies (InstantReplay, InstantReplay.Cri,
		/// InstantReplay.UniversalRP, InstantReplay.Wwise, UniEnc, UniEnc.Unity). The NuGet dependencies
		/// it pulls in (System.Threading.Channels and friends) are deliberately left alone, since other
		/// packages may use them too.
		/// </summary>
		static bool IsInstantReplayAssembly(string assemblyPath) {
			string name = Path.GetFileNameWithoutExtension(assemblyPath);
			return name == "InstantReplay" || name.StartsWith("InstantReplay.") ||
			       name == "UniEnc" || name.StartsWith("UniEnc.");
		}

		string[] IFilterBuildAssemblies.OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies) {
			if (!IsEnabledFor(EditorUserBuildSettings.activeBuildTarget)) return assemblies;

			var kept = new List<string>(assemblies.Length);
			var removed = new List<string>();
			foreach (string assembly in assemblies) {
				if (IsInstantReplayAssembly(assembly)) removed.Add(Path.GetFileName(assembly));
				else kept.Add(assembly);
			}

			if (removed.Count > 0)
				Debug.Log("[UnityReplayIntegration] Excluded from build: " + string.Join(", ", removed));

			return kept.ToArray();
		}

		// ─────────────────────────────────────────────────────────────────
		// Native plugins
		// ─────────────────────────────────────────────────────────────────

		void IPreprocessBuildWithReport.OnPreprocessBuild(BuildReport report) {
			if (!IsEnabledFor(report.summary.platform)) return;

			// Removing the managed assemblies while Replay Integration still calls into them would only
			// surface as a link-time or runtime failure, so refuse the build with an actionable message.
			if (!ReplayIntegrationBuildSettings.HasEffectiveDefine(
					ReplayIntegrationBuildSettings.ExcludeDefine,
					BuildPipeline.GetBuildTargetGroup(report.summary.platform))) {
				throw new BuildFailedException(
					"[UnityReplayIntegration] \"Exclude InstantReplay from Build\" is enabled, but \"Exclude from Build\" is not. " +
					"Replay Integration is still compiled against InstantReplay, so removing the InstantReplay assemblies would " +
					"produce a broken player. Enable \"Exclude from Build\" as well (Tools > Unity Replay Integration > Settings), " +
					"or turn this option off.");
			}

			var excluded = new List<string>();
			foreach (var plugin in PluginImporter.GetAllImporters()) {
				string path = plugin.assetPath;
				if (path == null || !path.Contains(k_PackageId) || !path.Contains(k_PluginFolder)) continue;
				if (!plugin.GetCompatibleWithPlatform(report.summary.platform)) continue;

				excluded.Add(path);
				plugin.SetCompatibleWithPlatform(report.summary.platform, false);
				plugin.SaveAndReimport();
			}

			if (excluded.Count == 0) return;

			SessionState.SetString(k_PendingRestoreKey, JoinRestoreEntry(report.summary.platform, excluded));
			Debug.Log("[UnityReplayIntegration] Excluded " + excluded.Count + " InstantReplay native plugin(s) from this build.");
		}

		void IPostprocessBuildWithReport.OnPostprocessBuild(BuildReport report) {
			RestorePendingPlugins();
		}

		/// <summary>
		/// A failed or cancelled build never reaches OnPostprocessBuild, which would leave the native
		/// plugins disabled in the project. Restore them on the next domain reload instead.
		/// </summary>
		[InitializeOnLoadMethod]
		static void RestoreAfterInterruptedBuild() {
			if (BuildPipeline.isBuildingPlayer) return;
			RestorePendingPlugins();
		}

		static void RestorePendingPlugins() {
			string pending = SessionState.GetString(k_PendingRestoreKey, string.Empty);
			if (string.IsNullOrEmpty(pending)) return;
			SessionState.EraseString(k_PendingRestoreKey);

			if (!TrySplitRestoreEntry(pending, out BuildTarget platform, out var paths)) return;

			foreach (string path in paths) {
				if (AssetImporter.GetAtPath(path) is not PluginImporter plugin) continue;
				plugin.SetCompatibleWithPlatform(platform, true);
				plugin.SaveAndReimport();
			}
		}

		static string JoinRestoreEntry(BuildTarget platform, List<string> paths) =>
			(int)platform + "\n" + string.Join("\n", paths);

		static bool TrySplitRestoreEntry(string entry, out BuildTarget platform, out string[] paths) {
			platform = BuildTarget.NoTarget;
			paths    = null;

			var lines = entry.Split('\n');
			if (lines.Length < 2 || !int.TryParse(lines[0], out int platformValue)) return false;

			platform = (BuildTarget)platformValue;
			paths    = new string[lines.Length - 1];
			System.Array.Copy(lines, 1, paths, 0, paths.Length);
			return true;
		}
	}
}
