using System;
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
		internal const string InstantReplayPackageId     = "jp.co.cyberagent.instant-replay";
		internal const string InstantReplayDepsPackageId = "jp.co.cyberagent.instant-replay.dependencies";
		const string InstantReplayGitUrl                 = "https://github.com/CyberAgentGameEntertainment/InstantReplay.git?path=Packages/jp.co.cyberagent.instant-replay#release";
		const string InstantReplayDepsGitUrl             = "https://github.com/CyberAgentGameEntertainment/InstantReplay.git?path=/Packages/jp.co.cyberagent.instant-replay.dependencies#release";

		// UnityNuGet scoped registry – required so UPM can resolve org.nuget.* packages
		// that jp.co.cyberagent.instant-replay.dependencies depends on.
		const string UnityNuGetRegistryName = "UnityNuGet";
		internal const string UnityNuGetRegistryUrl  = "https://unitynuget-registry.openupm.com";
		const string UnityNuGetScope        = "org.nuget";

		// ── Optional ─────────────────────────────────────────────────────────
		internal const string DiscordWebhookPackageId = "com.pedev.unity-discord-webhook";
		const string DiscordWebhookGitUrl             = "https://github.com/qwe321qwe321qwe321/Unity-DiscordWebhook.git?path=src/DiscordWebhook/Assets/DiscordWebhook";

		internal const string UniTaskPackageId = "com.cysharp.unitask";
		const string UniTaskGitUrl             = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask";

		static ListRequest _listRequest;
		static Queue<string> _installQueue;
		static AddRequest _currentAddRequest;
		static string _currentInstallUrl;
		static HashSet<string> _installedIds = new HashSet<string>();
		static bool _packageStateKnown;
		static bool _autoOpenWindowAfterNextRefresh;

		internal static event Action StateChanged;
		internal static string LastOperationMessage { get; private set; }
		internal static bool LastOperationFailed { get; private set; }
		internal static bool HasPackageState => _packageStateKnown;
		internal static bool IsInstalling => _currentAddRequest != null || (_installQueue != null && _installQueue.Count > 0);
		internal static bool IsInstantReplayInstalled => IsPackageInstalled(InstantReplayPackageId);
		internal static bool IsInstantReplayFullyInstalled =>
			IsPackageInstalled(InstantReplayPackageId) &&
			IsPackageInstalled(InstantReplayDepsPackageId);

		static UnityReplayIntegrationDependencyInstaller() {
			RefreshInstalledPackages(autoOpenWindowWhenRequiredMissing: true);
		}

		internal static bool IsPackageInstalled(string packageId) {
			return _packageStateKnown && _installedIds.Contains(packageId);
		}

		internal static bool HasUnityNuGetRegistryConfigured() {
			string manifestPath = GetManifestPath();
			if (!File.Exists(manifestPath)) return false;
			return File.ReadAllText(manifestPath).Contains(UnityNuGetRegistryUrl);
		}

		internal static void RefreshInstalledPackages(bool autoOpenWindowWhenRequiredMissing = false) {
			if (_listRequest != null && !_listRequest.IsCompleted)
				return;

			_autoOpenWindowAfterNextRefresh = autoOpenWindowWhenRequiredMissing;
			_listRequest = Client.List(offlineMode: false, includeIndirectDependencies: true);
			EditorApplication.update -= WaitForPackageList;
			EditorApplication.update += WaitForPackageList;
			NotifyStateChanged();
		}

		internal static void InstallMissingRequiredDependencies() {
			if (!EnsureUnityNuGetRegistry())
				return;

			EnqueueInstalls(GetMissingRequiredInstallUrls());
		}

		internal static void InstallOptionalDependency(string packageId) {
			switch (packageId) {
				case DiscordWebhookPackageId:
					if (!IsPackageInstalled(DiscordWebhookPackageId))
						EnqueueInstalls(new[] { DiscordWebhookGitUrl });
					break;

				case UniTaskPackageId:
					if (!IsPackageInstalled(UniTaskPackageId))
						EnqueueInstalls(new[] { UniTaskGitUrl });
					break;
			}
		}

		static void WaitForPackageList() {
			if (!_listRequest.IsCompleted) return;
			EditorApplication.update -= WaitForPackageList;

			if (_listRequest.Status == StatusCode.Failure) {
				SetOperationMessage($"Failed to list packages: {_listRequest.Error.message}", failed: true);
				Debug.LogError($"[UnityReplayIntegration] {LastOperationMessage}");
				_autoOpenWindowAfterNextRefresh = false;
				return;
			}

			_installedIds = new HashSet<string>(_listRequest.Result.Select(p => p.name));
			_packageStateKnown = true;
			NotifyStateChanged();

			if (_autoOpenWindowAfterNextRefresh && !IsInstalling && !IsInstantReplayInstalled)
				UnityReplayIntegrationDependencyWindow.OpenWindow();

			_autoOpenWindowAfterNextRefresh = false;
		}

		static IEnumerable<string> GetMissingRequiredInstallUrls() {
			if (!IsPackageInstalled(InstantReplayDepsPackageId))
				yield return InstantReplayDepsGitUrl;

			if (!IsPackageInstalled(InstantReplayPackageId))
				yield return InstantReplayGitUrl;
		}

		static void EnqueueInstalls(IEnumerable<string> packageUrls) {
			var newUrls = packageUrls.Where(url => !string.IsNullOrWhiteSpace(url)).ToArray();
			if (newUrls.Length == 0) {
				RefreshInstalledPackages();
				return;
			}

			if (_installQueue == null)
				_installQueue = new Queue<string>();

			var queuedUrls = new HashSet<string>(_installQueue);
			if (!string.IsNullOrEmpty(_currentInstallUrl))
				queuedUrls.Add(_currentInstallUrl);

			foreach (string url in newUrls) {
				if (queuedUrls.Add(url))
					_installQueue.Enqueue(url);
			}

			if (_installQueue.Count == 0)
				return;

			SetOperationMessage("Installing dependencies...");
			EditorApplication.update -= ProcessInstallQueue;
			EditorApplication.update += ProcessInstallQueue;
		}

		/// <summary>
		/// Directly edits Packages/manifest.json to add the UnityNuGet scoped registry
		/// if it is not already present. This must happen before UPM resolves packages
		/// that depend on org.nuget.* (such as jp.co.cyberagent.instant-replay.dependencies).
		/// </summary>
		static bool EnsureUnityNuGetRegistry() {
			string manifestPath = GetManifestPath();
			if (!File.Exists(manifestPath)) {
				SetOperationMessage("manifest.json not found.", failed: true);
				Debug.LogError($"[UnityReplayIntegration] {LastOperationMessage}");
				return false;
			}

			string json = File.ReadAllText(manifestPath);

			// Quick check – if the registry URL is already present, nothing to do.
			if (json.Contains(UnityNuGetRegistryUrl)) {
				return true;
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
					SetOperationMessage("Failed to parse scopedRegistries in manifest.json.", failed: true);
					Debug.LogError($"[UnityReplayIntegration] {LastOperationMessage}");
					return false;
				}
				// Check if array already has entries (need a comma separator).
				string before = json.Substring(0, closeIdx).TrimEnd();
				string separator = before.EndsWith("[") ? "\n    " : ",\n    ";
				json = json.Substring(0, closeIdx) + separator + registryBlock + "\n  " + json.Substring(closeIdx);
			} else {
				// No scopedRegistries key at all – inject it before "dependencies".
				int depsIdx = json.IndexOf("\"dependencies\"");
				if (depsIdx < 0) {
					SetOperationMessage("Failed to find \"dependencies\" key in manifest.json.", failed: true);
					Debug.LogError($"[UnityReplayIntegration] {LastOperationMessage}");
					return false;
				}
				string scopedBlock =
					"\"scopedRegistries\": [\n" +
					"    " + registryBlock + "\n" +
					"  ],\n  ";
				json = json.Substring(0, depsIdx) + scopedBlock + json.Substring(depsIdx);
			}

			File.WriteAllText(manifestPath, json);
			SetOperationMessage("UnityNuGet scoped registry added to manifest.json.");
			Debug.Log($"[UnityReplayIntegration] {LastOperationMessage}");

			// Trigger UPM to re-read the manifest.
			RequestEditorRefresh();
			Client.Resolve();
			return true;
		}

		static string GetManifestPath() {
			return Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/manifest.json"));
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
				if (_currentAddRequest.Status == StatusCode.Failure) {
					SetOperationMessage($"Failed to install package: {_currentAddRequest.Error.message}", failed: true);
					Debug.LogError($"[UnityReplayIntegration] {LastOperationMessage}");
				} else {
					SetOperationMessage($"Package installed: {_currentAddRequest.Result?.name}");
					Debug.Log($"[UnityReplayIntegration] {LastOperationMessage}");
					RequestEditorRefresh();
				}

				_currentAddRequest = null;
				_currentInstallUrl = null;
				NotifyStateChanged();
			}

			if (_installQueue == null || _installQueue.Count == 0) {
				EditorApplication.update -= ProcessInstallQueue;
				_installQueue = null;
				RequestEditorRefresh();
				RefreshInstalledPackages();
				Debug.Log("[UnityReplayIntegration] Dependency installation complete. Requested editor refresh.");
				return;
			}

			_currentInstallUrl = _installQueue.Dequeue();
			SetOperationMessage($"Installing: {_currentInstallUrl}");
			Debug.Log($"[UnityReplayIntegration] {LastOperationMessage}");
			_currentAddRequest = Client.Add(_currentInstallUrl);
			NotifyStateChanged();
		}

		static void RequestEditorRefresh() {
			AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
			EditorApplication.QueuePlayerLoopUpdate();
			UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
		}

		static void SetOperationMessage(string message, bool failed = false) {
			LastOperationMessage = message;
			LastOperationFailed = failed;
			NotifyStateChanged();
		}

		static void NotifyStateChanged() {
			StateChanged?.Invoke();
		}
	}
}
