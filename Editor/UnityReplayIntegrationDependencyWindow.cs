using UnityEditor;
using UnityEngine;

namespace UnityReplayIntegration.Editor {
	class UnityReplayIntegrationDependencyWindow : EditorWindow {
		Vector2 _scrollPosition;

		[MenuItem("Tools/Unity Replay Integration/Dependencies")]
		internal static void OpenWindow() {
			var window = GetWindow<UnityReplayIntegrationDependencyWindow>();
			window.titleContent = new GUIContent("Replay Dependencies");
			window.minSize = new Vector2(480f, 320f);
			window.Show();
		}

		void OnEnable() {
			UnityReplayIntegrationDependencyInstaller.StateChanged += Repaint;
			UnityReplayIntegrationDependencyInstaller.RefreshInstalledPackages();
		}

		void OnDisable() {
			UnityReplayIntegrationDependencyInstaller.StateChanged -= Repaint;
		}

		void OnFocus() {
			UnityReplayIntegrationDependencyInstaller.RefreshInstalledPackages();
		}

		void OnGUI() {
			bool hasPackageState = UnityReplayIntegrationDependencyInstaller.HasPackageState;
			bool isInstalling = UnityReplayIntegrationDependencyInstaller.IsInstalling;

			EditorGUILayout.LabelField("Unity Replay Integration Dependencies", EditorStyles.boldLabel);
			EditorGUILayout.Space();

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

			using (new EditorGUILayout.HorizontalScope()) {
				using (new EditorGUI.DisabledScope(isInstalling)) {
					if (GUILayout.Button("Refresh Status"))
						UnityReplayIntegrationDependencyInstaller.RefreshInstalledPackages();

					if (GUILayout.Button("Install Missing Required"))
						UnityReplayIntegrationDependencyInstaller.InstallMissingRequiredDependencies();
				}
			}

			EditorGUILayout.Space();
			_scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

			EditorGUILayout.LabelField("Required", EditorStyles.boldLabel);
			DrawDependencyStatus(
				"UnityNuGet Scoped Registry",
				"Required so InstantReplay can resolve its org.nuget dependencies.",
				UnityReplayIntegrationDependencyInstaller.HasUnityNuGetRegistryConfigured(),
				isInstalling,
				UnityReplayIntegrationDependencyInstaller.InstallMissingRequiredDependencies
			);
			DrawDependencyStatus(
				"InstantReplay Dependencies",
				"Provides the package dependencies required by InstantReplay.",
				UnityReplayIntegrationDependencyInstaller.IsPackageInstalled(UnityReplayIntegrationDependencyInstaller.InstantReplayDepsPackageId),
				isInstalling,
				UnityReplayIntegrationDependencyInstaller.InstallMissingRequiredDependencies
			);
			DrawDependencyStatus(
				"InstantReplay",
				"Core replay recording package used by Unity Replay Integration.",
				UnityReplayIntegrationDependencyInstaller.IsPackageInstalled(UnityReplayIntegrationDependencyInstaller.InstantReplayPackageId),
				isInstalling,
				UnityReplayIntegrationDependencyInstaller.InstallMissingRequiredDependencies
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

			EditorGUILayout.EndScrollView();
		}

		static void DrawDependencyStatus(string title, string description, bool installed, bool isInstalling, System.Action installAction) {
			using (new EditorGUILayout.VerticalScope("box")) {
				using (new EditorGUILayout.HorizontalScope()) {
					EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
					GUILayout.FlexibleSpace();
					GUILayout.Label(installed ? "Installed" : "Missing", EditorStyles.miniBoldLabel, GUILayout.Width(55f));

					using (new EditorGUI.DisabledScope(installed || isInstalling)) {
						if (GUILayout.Button("Install", GUILayout.Width(90f)))
							installAction();
					}
				}

				EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
			}
		}
	}
}
