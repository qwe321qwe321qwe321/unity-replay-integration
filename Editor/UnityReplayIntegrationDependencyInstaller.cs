using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UnityReplayIntegration.Editor {
	/// <summary>
	/// Runs on editor load to verify and install dependencies for Unity Replay Integration.
	/// Required: InstantReplay (jp.co.cyberagent.instant-replay)
	/// Optional: DiscordWebhook (com.pedev.unity-discord-webhook), UniTask (com.cysharp.unitask)
	/// </summary>
	[InitializeOnLoad]
	static class UnityReplayIntegrationDependencyInstaller {
		const string InstantReplayPackageId = "jp.co.cyberagent.instant-replay";
		const string InstantReplayDepsPackageId = "jp.co.cyberagent.instant-replay.dependencies";
		const string InstantReplayGitUrl = "https://github.com/CyberAgentGameEntertainment/InstantReplay.git?path=Packages/jp.co.cyberagent.instant-replay#release";
		const string InstantReplayDepsGitUrl = "https://github.com/CyberAgentGameEntertainment/InstantReplay.git?path=/Packages/jp.co.cyberagent.instant-replay.dependencies#release";

		const string DiscordWebhookPackageId = "com.pedev.unity-discord-webhook";
		const string DiscordWebhookGitUrl = "https://github.com/qwe321qwe321qwe321/Unity-DiscordWebhook.git?path=src/DiscordWebhook/Assets/DiscordWebhook";

		const string UniTaskPackageId = "com.cysharp.unitask";
		const string UniTaskGitUrl = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask";

		static ListRequest _listRequest;
		static Queue<string> _installQueue;
		static AddRequest _currentAddRequest;

		static UnityReplayIntegrationDependencyInstaller() {
			_listRequest = Client.List(offlineMode: false, includeIndirectDependencies: true);
			EditorApplication.update += WaitForPackageList;
		}

		static void WaitForPackageList() {
			if (!_listRequest.IsCompleted) return;
			EditorApplication.update -= WaitForPackageList;

			if (_listRequest.Status == StatusCode.Failure) {
				Debug.LogError($"[UnityReplayIntegration] Failed to list packages: {_listRequest.Error.message}");
				return;
			}

			var installedIds = new HashSet<string>(_listRequest.Result.Select(p => p.name));
			PromptMissingDependencies(installedIds);
		}

		static void PromptMissingDependencies(HashSet<string> installedIds) {
			var requiredInstalls = new Queue<string>();
			var optionalInstalls = new Queue<string>();

			// --- Required ---
			bool instantReplayMissing =
				!installedIds.Contains(InstantReplayPackageId) ||
				!installedIds.Contains(InstantReplayDepsPackageId);

			if (instantReplayMissing) {
				bool confirm = EditorUtility.DisplayDialog(
					"Unity Replay Integration — Required Dependency",
					"InstantReplay (jp.co.cyberagent.instant-replay) is required by Unity Replay Integration but is not installed.\n\nInstall it now?",
					"Install",
					"Cancel"
				);
				if (confirm) {
					// Dependencies package must be added first.
					requiredInstalls.Enqueue(InstantReplayDepsGitUrl);
					requiredInstalls.Enqueue(InstantReplayGitUrl);
				}
			}

			// --- Optional ---
			var optionalMissing = new List<(string label, string gitUrl)>();
			if (!installedIds.Contains(DiscordWebhookPackageId))
				optionalMissing.Add(("Discord Webhook (com.pedev.unity-discord-webhook)\n  Enables uploading clips and screenshots to Discord", DiscordWebhookGitUrl));
			if (!installedIds.Contains(UniTaskPackageId))
				optionalMissing.Add(("UniTask (com.cysharp.unitask)\n  Provides async/await API (falls back to coroutines without it)", UniTaskGitUrl));

			if (optionalMissing.Count > 0) {
				string packageList = string.Join("\n\n", optionalMissing.Select(p => $"• {p.label}"));
				bool confirm = EditorUtility.DisplayDialog(
					"Unity Replay Integration — Optional Packages",
					$"The following optional packages are not installed:\n\n{packageList}\n\nInstall them now?",
					"Install",
					"Skip"
				);
				if (confirm) {
					foreach (var (_, gitUrl) in optionalMissing) {
						optionalInstalls.Enqueue(gitUrl);
					}
				}
			}

			// Merge: required first, then optional.
			var combined = new Queue<string>(requiredInstalls.Concat(optionalInstalls));
			if (combined.Count > 0) {
				_installQueue = combined;
				EditorApplication.update += ProcessInstallQueue;
			}
		}

		static void ProcessInstallQueue() {
			if (_currentAddRequest != null) {
				if (!_currentAddRequest.IsCompleted) return;
				if (_currentAddRequest.Status == StatusCode.Failure) {
					Debug.LogError($"[UnityReplayIntegration] Failed to install package: {_currentAddRequest.Error.message}");
				} else {
					Debug.Log($"[UnityReplayIntegration] Package installed successfully: {_currentAddRequest.Result?.name}");
				}
				_currentAddRequest = null;
			}

			if (_installQueue == null || _installQueue.Count == 0) {
				EditorApplication.update -= ProcessInstallQueue;
				_installQueue = null;
				Debug.Log("[UnityReplayIntegration] Dependency installation complete. Unity will recompile.");
				return;
			}

			string nextUrl = _installQueue.Dequeue();
			Debug.Log($"[UnityReplayIntegration] Installing: {nextUrl}");
			_currentAddRequest = Client.Add(nextUrl);
		}
	}
}
