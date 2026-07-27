using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityReplayIntegration.Editor {
	class UnityReplayIntegrationSettingWindow : EditorWindow {
		const string k_BuildFoldoutKey = "UnityReplayIntegration.SettingsWindow.BuildFoldout";

		Vector2 _scrollPosition;
		bool _dependenciesFoldout = true;
		bool _dependenciesFoldoutInitialized;
		bool _buildFoldout = true;

		[MenuItem("Tools/Unity Replay Integration/Settings")]
		internal static void OpenWindow() {
			var window = GetWindow<UnityReplayIntegrationSettingWindow>();
			window.titleContent = new GUIContent("Replay Integration Settings");
			window.minSize = new Vector2(480f, 320f);
			window.Show();
		}

		void OnEnable() {
			_dependenciesFoldoutInitialized = false;
			_buildFoldout                   = EditorPrefs.GetBool(k_BuildFoldoutKey, true);
			UnityReplayIntegrationDependencyInstaller.StateChanged += Repaint;
			UnityReplayIntegrationDependencyInstaller.RefreshInstalledPackages();
		}

		// Default foldout: collapsed when required dependencies are all set up; expanded otherwise.
		// Re-evaluated once per window open, as soon as the package state is known.
		void EnsureDependenciesFoldoutDefault() {
			if (_dependenciesFoldoutInitialized) return;
			if (!UnityReplayIntegrationDependencyInstaller.HasPackageState) return;

			bool allRequiredReady =
				UnityReplayIntegrationDependencyInstaller.IsInstantReplayFullyInstalled &&
				UnityReplayIntegrationDependencyInstaller.HasUnityNuGetRegistryConfigured();

			_dependenciesFoldout = !allRequiredReady;
			_dependenciesFoldoutInitialized = true;
		}

		void OnDisable() {
			UnityReplayIntegrationDependencyInstaller.StateChanged -= Repaint;
		}

		void OnFocus() {
			UnityReplayIntegrationDependencyInstaller.RefreshInstalledPackages();
		}

		void OnGUI() {
			EnsureDependenciesFoldoutDefault();

			EditorGUILayout.LabelField("Unity Replay Integration Settings", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			_scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

			DrawDependenciesSection();
			EditorGUILayout.Space(8f);
			DrawBuildSection();

			EditorGUILayout.EndScrollView();
		}

		// ─────────────────────────────────────────────────────────────────
		// Dependencies section
		// ─────────────────────────────────────────────────────────────────
		void DrawDependenciesSection() {
			DrawSectionHeader("Dependencies", new Color(0.20f, 0.45f, 0.75f), ref _dependenciesFoldout, null);
			if (!_dependenciesFoldout) return;

			bool hasPackageState = UnityReplayIntegrationDependencyInstaller.HasPackageState;
			bool isInstalling    = UnityReplayIntegrationDependencyInstaller.IsInstalling;

			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
				if (!hasPackageState) {
					EditorGUILayout.HelpBox("Checking installed packages...", MessageType.Info);
				} else if (!UnityReplayIntegrationDependencyInstaller.IsInstantReplayFullyInstalled) {
					EditorGUILayout.HelpBox("InstantReplay is required. Install the missing required dependencies before using this package.", MessageType.Warning);
				} else {
					EditorGUILayout.HelpBox("Required dependencies are installed. Optional packages can be managed here at any time.", MessageType.Info);
				}

				if (!string.IsNullOrEmpty(UnityReplayIntegrationDependencyInstaller.LastOperationMessage)) {
					EditorGUILayout.HelpBox(
						UnityReplayIntegrationDependencyInstaller.LastOperationMessage,
						UnityReplayIntegrationDependencyInstaller.LastOperationFailed ? MessageType.Error : MessageType.None
					);
				}

				bool instantReplayUpdateAvailable = UnityReplayIntegrationDependencyInstaller.IsUpdateAvailable(UnityReplayIntegrationDependencyInstaller.InstantReplayPackageId);
				bool instantReplayDepsUpdateAvailable = UnityReplayIntegrationDependencyInstaller.IsUpdateAvailable(UnityReplayIntegrationDependencyInstaller.InstantReplayDepsPackageId);
				if (instantReplayUpdateAvailable || instantReplayDepsUpdateAvailable) {
					var names = new List<string>();
					if (instantReplayUpdateAvailable) names.Add("InstantReplay");
					if (instantReplayDepsUpdateAvailable) names.Add("InstantReplay Dependencies");
					EditorGUILayout.HelpBox(
						"Update available for: " + string.Join(", ", names) + ". See details below.",
						MessageType.Warning
					);
				}

				EditorGUILayout.HelpBox(
					"Known issue: after installing InstantReplay and its org.nuget dependencies, Unity may show missing signature or unsigned package warnings. " +
					"This is usually caused by the package source being a Git dependency and UnityNuGet/OpenUPM-based registry packages rather than a Unity-signed registry package. " +
					"If the packages resolve and compile successfully, these warnings are typically non-blocking.\n\n" +
					"Reference: https://github.com/bdovaz/UnityNuGet/issues/636",
					MessageType.Info
				);

				using (new EditorGUILayout.HorizontalScope()) {
					using (new EditorGUI.DisabledScope(isInstalling)) {
						if (GUILayout.Button("Refresh Status"))
							UnityReplayIntegrationDependencyInstaller.RefreshInstalledPackages(forceUpdateCheck: true);

						if (GUILayout.Button("Install Missing Required"))
							UnityReplayIntegrationDependencyInstaller.InstallMissingRequiredDependencies();

						if (UnityReplayIntegrationDependencyInstaller.HasUpdatesAvailable) {
							if (GUILayout.Button("Update All"))
								UnityReplayIntegrationDependencyInstaller.UpdateAllAvailable();
						}
					}
				}

				EditorGUILayout.Space();
				EditorGUILayout.LabelField("Required", EditorStyles.boldLabel);
				DrawDependencyStatus(
					"UnityNuGet Scoped Registry",
					"Required so InstantReplay can resolve its org.nuget dependencies. Unity may show missing signature warnings for packages fetched from this source.",
					UnityReplayIntegrationDependencyInstaller.HasUnityNuGetRegistryConfigured(),
					isInstalling,
					UnityReplayIntegrationDependencyInstaller.InstallMissingRequiredDependencies
				);
				DrawDependencyStatus(
					"InstantReplay Dependencies",
					"Provides the package dependencies required by InstantReplay, including org.nuget packages that may trigger known signature warnings in newer Unity versions.",
					UnityReplayIntegrationDependencyInstaller.IsPackageInstalled(UnityReplayIntegrationDependencyInstaller.InstantReplayDepsPackageId),
					isInstalling,
					UnityReplayIntegrationDependencyInstaller.InstallMissingRequiredDependencies,
					UnityReplayIntegrationDependencyInstaller.InstantReplayDepsPackageId
				);
				DrawDependencyStatus(
					"InstantReplay",
					"Core replay recording package used by Unity Replay Integration. Installed via Git URL, so Unity may report it as unsigned depending on editor version.",
					UnityReplayIntegrationDependencyInstaller.IsPackageInstalled(UnityReplayIntegrationDependencyInstaller.InstantReplayPackageId),
					isInstalling,
					UnityReplayIntegrationDependencyInstaller.InstallMissingRequiredDependencies,
					UnityReplayIntegrationDependencyInstaller.InstantReplayPackageId
				);

				EditorGUILayout.Space();
				EditorGUILayout.LabelField("Optional", EditorStyles.boldLabel);
				DrawDependencyStatus(
					"Discord Webhook",
					"Enables clip and screenshot uploads to Discord.",
					UnityReplayIntegrationDependencyInstaller.IsPackageInstalled(UnityReplayIntegrationDependencyInstaller.DiscordWebhookPackageId),
					isInstalling,
					() => UnityReplayIntegrationDependencyInstaller.InstallOptionalDependency(UnityReplayIntegrationDependencyInstaller.DiscordWebhookPackageId)
				);
				DrawDependencyStatus(
					"UniTask",
					"Adds async/await wrappers. Coroutines still work without it.",
					UnityReplayIntegrationDependencyInstaller.IsPackageInstalled(UnityReplayIntegrationDependencyInstaller.UniTaskPackageId),
					isInstalling,
					() => UnityReplayIntegrationDependencyInstaller.InstallOptionalDependency(UnityReplayIntegrationDependencyInstaller.UniTaskPackageId)
				);
			}
		}

		// ─────────────────────────────────────────────────────────────────
		// Build section
		// ─────────────────────────────────────────────────────────────────
		void DrawBuildSection() {
			bool excluded = ReplayIntegrationBuildSettings.ExcludeFromBuild;
			bool instantReplayExcluded = ReplayIntegrationBuildSettings.ExcludeInstantReplayFromBuild;
			Color accent = (excluded || instantReplayExcluded) ? new Color(0.85f, 0.55f, 0.15f) : new Color(0.30f, 0.65f, 0.35f);
			DrawSectionHeader("Build Setting", accent, ref _buildFoldout, k_BuildFoldoutKey);
			if (!_buildFoldout) return;

			BuildTargetGroup currentGroup = ReplayIntegrationBuildSettings.SelectedGroup;
			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
				EditorGUILayout.HelpBox(
					(excluded
						? "Replay Integration is currently EXCLUDED from builds for the " + currentGroup + " platform group. Editor behavior is unaffected."
						: "Replay Integration is currently INCLUDED in builds for the " + currentGroup + " platform group.") +
					"\nThis setting is per platform group (the active target in File > Build Settings). Switching the active build target may use a different value — check it again after switching platforms." +
					"\nBoth toggles below write to Project Settings > Player (ProjectSettings.asset), not to the active Build Profile.",
					excluded ? MessageType.Warning : MessageType.Info
				);

				DrawProfileOverrideWarning(ReplayIntegrationBuildSettings.ExcludeDefine, "Exclude from Build");

				bool next = EditorGUILayout.ToggleLeft(
					new GUIContent("Exclude from Build (editor-only mode) — for " + currentGroup,
						"When enabled, the " + ReplayIntegrationBuildSettings.ExcludeDefine +
						" scripting define is added to the scripting define symbols of the currently active platform group (" + currentGroup + ") only. " +
						"Built players will skip Replay Integration entirely: scene components self-destruct on Awake, " +
						"and the Discord/UniTask integration files compile to no-ops. Editor behavior is unaffected. " +
						"If you build for multiple platforms, set this separately for each platform group."),
					excluded);
				if (next != excluded) {
					ReplayIntegrationBuildSettings.ExcludeFromBuild = next;
					GUIUtility.ExitGUI();
				}
				EditorGUILayout.LabelField(
					"Define: " + ReplayIntegrationBuildSettings.ExcludeDefine + "  (platform group: " + currentGroup + ")",
					EditorStyles.miniLabel);

				EditorGUILayout.Space(6f);

				EditorGUILayout.HelpBox(
					(instantReplayExcluded
						? "InstantReplay will be STRIPPED FROM BUILDS for the " + currentGroup + " platform group: its managed assemblies are filtered out of the player and its native encoder plugins are marked incompatible for the duration of the build."
						: "InstantReplay is currently SHIPPED with builds for the " + currentGroup + " platform group.") +
					"\nThe Editor is unaffected either way — recording keeps working in Play Mode.",
					instantReplayExcluded ? MessageType.Warning : MessageType.Info
				);

				DrawProfileOverrideWarning(ReplayIntegrationBuildSettings.ExcludeInstantReplayFromBuildDefine, "Exclude InstantReplay from Build");

				bool nextInstantReplay = EditorGUILayout.ToggleLeft(
					new GUIContent("Exclude InstantReplay from Build — for " + currentGroup,
						"When enabled, the InstantReplay and UniEnc assemblies are removed from built players for the " +
						"currently active platform group (" + currentGroup + ") only, and its native encoder plugins are " +
						"excluded for the duration of the build. This is a build-time filter, not an assembly define " +
						"constraint, so InstantReplay stays compiled in the Editor and Play Mode recording is unaffected. " +
						"Requires \"Exclude from Build\" above, since Replay Integration itself would otherwise still call " +
						"into the removed assemblies."),
					instantReplayExcluded);
				if (nextInstantReplay != instantReplayExcluded) {
					ReplayIntegrationBuildSettings.ExcludeInstantReplayFromBuild = nextInstantReplay;
					GUIUtility.ExitGUI();
				}
				EditorGUILayout.LabelField(
					"Define: " + ReplayIntegrationBuildSettings.ExcludeInstantReplayFromBuildDefine + "  (platform group: " + currentGroup + ")",
					EditorStyles.miniLabel);

				if (instantReplayExcluded && !excluded) {
					EditorGUILayout.HelpBox(
						"\"Exclude from Build\" is off, so Replay Integration is still compiled against InstantReplay and would call into the removed assemblies. " +
						"Builds for " + currentGroup + " will fail with an explanatory error until you enable it (or turn this option off).",
						MessageType.Error);
				}

				if (ReplayIntegrationBuildSettings.IsInstantReplayKillSwitchSet) {
					EditorGUILayout.HelpBox(
						"The InstantReplay package's own " + ReplayIntegrationBuildSettings.InstantReplayKillSwitchDefine +
						" define is set in this project's scripting define symbols. That is a project-wide kill switch: " +
						"the InstantReplay assemblies are not compiled at all, in the Editor either, so recording is " +
						"unavailable everywhere and there is nothing left for this option to strip. " +
						"Remove it from Project Settings > Player if that was not intended.",
						MessageType.Warning);
				}
			}
		}

		// The toggles edit the project-level player settings. When the active build profile carries a
		// Player Settings override, that override is what actually reaches compilation and builds, so
		// the toggle state can disagree with reality — say so instead of letting it look broken.
		static void DrawProfileOverrideWarning(string define, string toggleName) {
			if (!ReplayIntegrationBuildSettings.IsOverriddenByActiveProfile(define)) return;

			bool effective = ReplayIntegrationBuildSettings.HasEffectiveDefine(define);
			EditorGUILayout.HelpBox(
				"The active Build Profile overrides Player Settings: " + define + " is currently " +
				(effective ? "SET" : "NOT SET") + " for actual compilation and builds, which does not match the \"" +
				toggleName + "\" toggle below (that toggle shows the project-level value). " +
				"Edit the profile's Player Settings override, or deactivate the profile, to make them agree.",
				MessageType.Warning);
		}

		// ─────────────────────────────────────────────────────────────────
		// Helpers
		// ─────────────────────────────────────────────────────────────────
		void DrawSectionHeader(string title, Color accent, ref bool foldout, string prefsKey) {
			Rect rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.foldout, GUILayout.Height(22f));

			// Accent strip on the left.
			Rect strip = new Rect(rect.x, rect.y, 3f, rect.height);
			EditorGUI.DrawRect(strip, accent);

			// Tinted background.
			Rect bg = new Rect(rect.x + 3f, rect.y, rect.width - 3f, rect.height);
			Color bgColor = accent;
			bgColor.a = 0.12f;
			EditorGUI.DrawRect(bg, bgColor);

			// Foldout + title.
			Rect labelRect = new Rect(rect.x + 10f, rect.y + 2f, rect.width - 12f, rect.height - 2f);
			var style = new GUIStyle(EditorStyles.foldout) {
				fontStyle = FontStyle.Bold,
				fontSize  = 12,
			};
			bool newFoldout = EditorGUI.Foldout(labelRect, foldout, title, true, style);
			if (newFoldout != foldout) {
				foldout = newFoldout;
				if (!string.IsNullOrEmpty(prefsKey)) EditorPrefs.SetBool(prefsKey, foldout);
			}
		}

		static void DrawDependencyStatus(string title, string description, bool installed, bool isInstalling, System.Action installAction, string updateCheckPackageId = null) {
			using (new EditorGUILayout.VerticalScope("box")) {
				using (new EditorGUILayout.HorizontalScope()) {
					EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
					GUILayout.FlexibleSpace();

					var prevColor = GUI.color;
					GUI.color = installed ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.95f, 0.65f, 0.45f);
					GUILayout.Label(installed ? "Installed" : "Missing", EditorStyles.miniBoldLabel, GUILayout.Width(60f));
					GUI.color = prevColor;

					using (new EditorGUI.DisabledScope(installed || isInstalling)) {
						if (GUILayout.Button("Install", GUILayout.Width(90f)))
							installAction();
					}
				}

				EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);

				if (installed && !string.IsNullOrEmpty(updateCheckPackageId))
					DrawUpdateStatus(updateCheckPackageId, isInstalling);
			}
		}

		static void DrawUpdateStatus(string packageId, bool isInstalling) {
			string installedVersion = UnityReplayIntegrationDependencyInstaller.GetInstalledVersion(packageId);
			string latestVersion = UnityReplayIntegrationDependencyInstaller.GetLatestKnownVersion(packageId);
			bool updateAvailable = UnityReplayIntegrationDependencyInstaller.IsUpdateAvailable(packageId);

			using (new EditorGUILayout.HorizontalScope()) {
				string versionLabel = "Version: " + (installedVersion ?? "unknown");
				if (!string.IsNullOrEmpty(latestVersion))
					versionLabel += "  (latest: " + latestVersion + ")";
				EditorGUILayout.LabelField(versionLabel, EditorStyles.miniLabel);

				if (updateAvailable) {
					GUILayout.FlexibleSpace();
					var prevColor = GUI.color;
					GUI.color = new Color(0.95f, 0.75f, 0.35f);
					GUILayout.Label("Update available", EditorStyles.miniBoldLabel);
					GUI.color = prevColor;

					using (new EditorGUI.DisabledScope(isInstalling)) {
						if (GUILayout.Button("Update", GUILayout.Width(90f)))
							UnityReplayIntegrationDependencyInstaller.UpdateDependency(packageId);
					}
				}
			}
		}
	}
}
