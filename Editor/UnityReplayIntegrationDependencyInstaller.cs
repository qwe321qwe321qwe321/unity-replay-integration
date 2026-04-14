using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UnityReplayIntegration.Editor {
	/// <summary>
	/// Runs on editor load to verify and install dependencies for Unity Replay Integration.
	///
	/// Required: InstantReplay (jp.co.cyberagent.instant-replay)
	///   - Needs UnityNuGet scoped registry for its NuGet sub-dependencies
	///   - Needs jp.co.cyberagent.instant-replay.dependencies (provides System.IO.Pipelines etc.)
	///
	/// Optional: DiscordWebhook (com.pedev.unity-discord-webhook), UniTask (com.cysharp.unitask)
	/// </summary>
	[InitializeOnLoad]
	static class UnityReplayIntegrationDependencyInstaller {
		// ── Required ─────────────────────────────────────────────────────────
		const string InstantReplayPackageId     = "jp.co.cyberagent.instant-replay";
		const string InstantReplayDepsPackageId = "jp.co.cyberagent.instant-replay.dependencies";
		const string InstantReplayGitUrl        = "https://github.com/CyberAgentGameEntertainment/InstantReplay.git?path=Packages/jp.co.cyberagent.instant-replay#release";
		const string InstantReplayDepsGitUrl    = "https://github.com/CyberAgentGameEntertainment/InstantReplay.git?path=/Packages/jp.co.cyberagent.instant-replay.dependencies#release";

		// UnityNuGet scoped registry – required so UPM can resolve org.nuget.* packages
		// that jp.co.cyberagent.instant-replay.dependencies depends on.
		const string UnityNuGetRegistryName = "UnityNuGet";
		const string UnityNuGetRegistryUrl  = "https://unitynuget-registry.openupm.com";
		const string UnityNuGetScope        = "org.nuget";

		// ── Optional ─────────────────────────────────────────────────────────
		const string DiscordWebhookPackageId = "com.pedev.unity-discord-webhook";
		const string DiscordWebhookGitUrl    = "https://github.com/qwe321qwe321qwe321/Unity-DiscordWebhook.git?path=src/DiscordWebhook/Assets/DiscordWebhook";

		const string UniTaskPackageId = "com.cysharp.unitask";
		const string UniTaskGitUrl    = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask";

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
			bool instantReplayMissing =
				!installedIds.Contains(InstantReplayPackageId) ||
				!installedIds.Contains(InstantReplayDepsPackageId);

			var requiredInstalls = new Queue<string>();
			var optionalInstalls = new Queue<string>();

			// ── Required ─────────────────────────────────────────────────────
			if (instantReplayMissing) {
				bool confirm = EditorUtility.DisplayDialog(
					"Unity Replay Integration — Required Dependency",
					"InstantReplay (jp.co.cyberagent.instant-replay) is required but is not fully installed.\n\n" +
					"This will:\n" +
					"  1. Add the UnityNuGet scoped registry to manifest.json\n" +
					"  2. Install jp.co.cyberagent.instant-replay.dependencies\n" +
					"  3. Install jp.co.cyberagent.instant-replay\n\n" +
					"Install now?",
					"Install",
					"Cancel"
				);

				if (confirm) {
					// Step 1: ensure the scoped registry is present BEFORE installing packages,
					// because the .dependencies package resolves org.nuget.* through it.
					EnsureUnityNuGetRegistry();

					// Step 2+3: queue package installs (deps first, then main).
					if (!installedIds.Contains(InstantReplayDepsPackageId))
						requiredInstalls.Enqueue(InstantReplayDepsGitUrl);
					if (!installedIds.Contains(InstantReplayPackageId))
						requiredInstalls.Enqueue(InstantReplayGitUrl);
				}
			}

			// ── Optional ─────────────────────────────────────────────────────
			if (!installedIds.Contains(DiscordWebhookPackageId)) {
				bool confirm = EditorUtility.DisplayDialog(
					"Unity Replay Integration — Optional Package",
					"Discord Webhook (com.pedev.unity-discord-webhook) is not installed.\n\n" +
					"Enables uploading clips and screenshots to Discord.\n\nInstall now?",
					"Install",
					"Skip"
				);
				if (confirm)
					optionalInstalls.Enqueue(DiscordWebhookGitUrl);
			}

			if (!installedIds.Contains(UniTaskPackageId)) {
				bool confirm = EditorUtility.DisplayDialog(
					"Unity Replay Integration — Optional Package",
					"UniTask (com.cysharp.unitask) is not installed.\n\n" +
					"Provides async/await API (falls back to coroutines without it).\n\nInstall now?",
					"Install",
					"Skip"
				);
				if (confirm)
					optionalInstalls.Enqueue(UniTaskGitUrl);
			}

			// Merge: required first, then optional.
			var combined = new Queue<string>(requiredInstalls.Concat(optionalInstalls));
			if (combined.Count > 0) {
				_installQueue = combined;
				EditorApplication.update += ProcessInstallQueue;
			}
		}

		/// <summary>
		/// Directly edits Packages/manifest.json to add the UnityNuGet scoped registry
		/// if it is not already present. This must happen before UPM resolves packages
		/// that depend on org.nuget.* (such as jp.co.cyberagent.instant-replay.dependencies).
		/// </summary>
		static void EnsureUnityNuGetRegistry() {
			string manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/manifest.json"));
			if (!File.Exists(manifestPath)) {
				Debug.LogError("[UnityReplayIntegration] manifest.json not found.");
				return;
			}

			string json = File.ReadAllText(manifestPath);

			// Quick check – if the registry URL is already present, nothing to do.
			if (json.Contains(UnityNuGetRegistryUrl)) {
				Debug.Log("[UnityReplayIntegration] UnityNuGet scoped registry already present.");
				return;
			}

			string registryBlock =
				"{\n" +
				$"      \"name\": \"{UnityNuGetRegistryName}\",\n" +
				$"      \"url\": \"{UnityNuGetRegistryUrl}\",\n" +
				"      \"scopes\": [\n" +
				$"        \"{UnityNuGetScope}\"\n" +
				"      ]\n" +
				"    }";

			if (json.Contains("\"scopedRegistries\"")) {
				// Append to existing scopedRegistries array.
				// Insert the new entry before the closing ] of the array.
				int closeIdx = FindScopedRegistriesArrayClose(json);
				if (closeIdx < 0) {
					Debug.LogError("[UnityReplayIntegration] Failed to parse scopedRegistries in manifest.json.");
					return;
				}
				// Check if array already has entries (need a comma separator).
				string before = json.Substring(0, closeIdx).TrimEnd();
				string separator = before.EndsWith("[") ? "\n    " : ",\n    ";
				json = json.Substring(0, closeIdx) + separator + registryBlock + "\n  " + json.Substring(closeIdx);
			} else {
				// No scopedRegistries key at all – inject it before "dependencies".
				int depsIdx = json.IndexOf("\"dependencies\"");
				if (depsIdx < 0) {
					Debug.LogError("[UnityReplayIntegration] Failed to find \"dependencies\" key in manifest.json.");
					return;
				}
				string scopedBlock =
					"\"scopedRegistries\": [\n" +
					"    " + registryBlock + "\n" +
					"  ],\n  ";
				json = json.Substring(0, depsIdx) + scopedBlock + json.Substring(depsIdx);
			}

			File.WriteAllText(manifestPath, json);
			Debug.Log("[UnityReplayIntegration] UnityNuGet scoped registry added to manifest.json.");

			// Trigger UPM to re-read the manifest.
			Client.Resolve();
		}

		/// <summary>Returns the index of the closing ] of the scopedRegistries array, or -1 on failure.</summary>
		static int FindScopedRegistriesArrayClose(string json) {
			int keyIdx = json.IndexOf("\"scopedRegistries\"");
			if (keyIdx < 0) return -1;
			int openIdx = json.IndexOf('[', keyIdx);
			if (openIdx < 0) return -1;

			int depth = 0;
			for (int i = openIdx; i < json.Length; i++) {
				if (json[i] == '[') depth++;
				else if (json[i] == ']') {
					depth--;
					if (depth == 0) return i;
				}
			}
			return -1;
		}

		static void ProcessInstallQueue() {
			if (_currentAddRequest != null) {
				if (!_currentAddRequest.IsCompleted) return;
				if (_currentAddRequest.Status == StatusCode.Failure)
					Debug.LogError($"[UnityReplayIntegration] Failed to install package: {_currentAddRequest.Error.message}");
				else
					Debug.Log($"[UnityReplayIntegration] Package installed: {_currentAddRequest.Result?.name}");
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
