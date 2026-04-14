using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
#if INSTANT_REPLAY_PRESENT
using InstantReplay;
using UniEnc;
#endif
using UnityEngine;

namespace UnityReplayIntegration {
	/// <summary>
	/// Singleton system for background video recording via InstantReplay and screenshot capture,
	/// with optional Discord webhook upload. Drop the prefab into the scene to activate.
	///
	/// Optional integrations (compiled only when the respective package is installed):
	///   - com.pedev.unity-discord-webhook  → UnityReplayIntegration.Discord assembly
	///   - com.cysharp.unitask              → UnityReplayIntegration.UniTask assembly
	/// </summary>
	public class UnityReplayIntegration : MonoBehaviour {
		public static UnityReplayIntegration Instance { get; private set; }

		/// <summary>
		/// Set by the Discord extension assembly (<see cref="UnityReplayIntegration.Discord"/>).
		/// Receives (filePath, isScreenshot) and returns an IEnumerator for coroutine execution.
		/// </summary>
		public static Func<string, bool, IEnumerator> DiscordUploadHandler;

		#region Serialized Fields

		[Header("Recording")]
		[Tooltip("Select a quality preset to auto-fill resolution, frame rate, and bitrate. Use Custom to set them individually.")]
		[SerializeField] private VideoQualityPreset qualityPreset = VideoQualityPreset.HD30;
		[Tooltip("Recording width in pixels. Overridden by the quality preset unless Custom is selected.")]
		[SerializeField, Range(64, 3840)] private int recordingWidth = 1280;
		[Tooltip("Recording height in pixels. Overridden by the quality preset unless Custom is selected.")]
		[SerializeField, Range(64, 2160)] private int recordingHeight = 720;
		[Tooltip("Target frames per second for the recording. Overridden by the quality preset unless Custom is selected.")]
		[SerializeField, Range(1, 120)] private int fps = 30;
		[Tooltip("Target video bitrate in Kbps. Auto-filled by the quality preset; adjust freely when using Custom.")]
		[SerializeField, Range(500, 50000)] private int recordingBitrateKbps = 2500;
		[Tooltip("Maximum memory budget in MB for compressed in-memory frame storage.")]
		[SerializeField, Range(1, 500)] private int maxMemoryUsageMb = 10;
		[Tooltip("Number of raw (uncompressed) frame buffers held before compression. Higher values smooth bursty frame delivery at the cost of memory.")]
		[SerializeField, Range(1, 20)] private int maxNumberOfRawFrameBuffers = 4;
		[Tooltip("When enabled, recording starts automatically on Awake. Disable to start recording manually via StartRecording().")]
		[SerializeField] private bool startOnAwake = true;

		[Header("Hotkeys")]
#if ENABLE_INPUT_SYSTEM
		[Tooltip("Keyboard key that triggers video export at runtime.")]
		[SerializeField] private UnityEngine.InputSystem.Key exportVideoHotkey = UnityEngine.InputSystem.Key.F9;
		[Tooltip("Keyboard key that captures and saves a screenshot at runtime.")]
		[SerializeField] private UnityEngine.InputSystem.Key captureScreenshotHotkey = UnityEngine.InputSystem.Key.F10;
#else
		[Tooltip("Keyboard key that triggers video export at runtime.")]
		[SerializeField] private KeyCode exportVideoHotkey = KeyCode.F9;
		[Tooltip("Keyboard key that captures and saves a screenshot at runtime.")]
		[SerializeField] private KeyCode captureScreenshotHotkey = KeyCode.F10;
#endif

		[Header("Output")]
		[Tooltip("Directory where videos and screenshots are saved. Leave empty to use Application.persistentDataPath. Absolute paths are recommended for cross-platform compatibility.")]
		[SerializeField] private string outputPath = "";

		[Header("Discord Webhook (requires com.pedev.unity-discord-webhook)")]
		[Tooltip("When enabled, exported videos and screenshots are automatically uploaded to the configured Discord webhook.")]
		[SerializeField] private bool discordWebhookEnabled = false;
		[Tooltip("Full Discord webhook URL. Create one in Discord under Server Settings → Integrations → Webhooks.")]
		[SerializeField] private string discordWebhookUrl = "";
		[Tooltip("Type of the target Discord channel. Use Forum for forum channels that require a thread title.")]
		[SerializeField] private ReplayDiscordChannelType discordChannelType = ReplayDiscordChannelType.TextChannel;
		[Tooltip("Thread title for Forum channel type. Supports {TIME}, {SIZE}, and {LENGTH} placeholders.")]
		[SerializeField] private string discordForumThreadTitle = "Game Clip - {TIME} ({SIZE} / {LENGTH})";
		[Tooltip("Message content. Supports {TIME} (yyyy-MM-dd HH:mm:ss), {SIZE} (file size in MB), and {LENGTH} (video duration) placeholders.")]
		[SerializeField] private string discordContent = "Captured at {TIME} ({SIZE} / {LENGTH})";
		[Tooltip("Maximum file size in MB for a single Discord upload. Files larger than this will be handled according to the Auto Split Video setting. Set to 0 to disable the size check.")]
		[SerializeField, Range(0, 500)] private int discordUploadLimitMb = 10;
		[Tooltip("When enabled, videos that exceed the upload limit are automatically split into playable segments via FFmpeg. When disabled (or when FFmpeg is unavailable), the file is uploaded as a zip archive instead. If the zip is also too large the upload fails.")]
		[SerializeField] private bool discordAutoSplitVideo = false;
		[Tooltip("Path to the ffmpeg executable. Leave empty to use FFmpeg from the system PATH. Use StreamingAssets:/ffmpeg.exe for a path inside StreamingAssets (build-safe). Paths inside the project folder are stored as relative paths (Editor-only).")]
		[SerializeField] private string discordFfmpegPath = "";

		#endregion

#if INSTANT_REPLAY_PRESENT
		private RealtimeInstantReplaySession _currentSession;
		private volatile bool _pendingSessionRestart;
		private bool _isExporting;
#endif

		#region Discord Config (read by Discord extension assembly)

		public bool DiscordWebhookEnabled => discordWebhookEnabled;
		public string DiscordWebhookUrl => discordWebhookUrl;
		public ReplayDiscordChannelType DiscordChannelType => discordChannelType;
		public string DiscordForumThreadTitle => discordForumThreadTitle;
		public string DiscordContent => discordContent;
		/// <summary>Upload size limit in bytes. Returns 0 when the size check is disabled.</summary>
		public long DiscordUploadLimitBytes => discordUploadLimitMb > 0 ? (long)discordUploadLimitMb * 1024 * 1024 : 0;
		public bool DiscordAutoSplitVideo => discordAutoSplitVideo;
		public string DiscordFfmpegPath {
			get {
				const string k_Prefix = "StreamingAssets:/";
				if (!string.IsNullOrEmpty(discordFfmpegPath) && discordFfmpegPath.StartsWith(k_Prefix)) {
					return Path.Combine(Application.streamingAssetsPath,
						discordFfmpegPath.Substring(k_Prefix.Length).Replace('/', Path.DirectorySeparatorChar));
				}
				return discordFfmpegPath;
			}
		}

		public bool ShouldSendToDiscord() =>
			discordWebhookEnabled
			&& !string.IsNullOrEmpty(discordWebhookUrl)
			&& DiscordUploadHandler != null;

		#endregion

		#region Unity Lifecycle

		private void OnValidate() {
			var presetValues = VideoQualityPresetSettings.Get(qualityPreset);
			if (presetValues == null) return;
			recordingWidth       = presetValues.Value.Width;
			recordingHeight      = presetValues.Value.Height;
			fps                  = presetValues.Value.Fps;
			recordingBitrateKbps = presetValues.Value.BitrateKbps;
		}

		private void Awake() {
			if (Instance != null && Instance != this) {
				Destroy(gameObject);
				return;
			}
			Instance = this;
			DontDestroyOnLoad(gameObject);

			if (startOnAwake) {
				StartRecording();
			}
		}

		private void OnDestroy() {
			if (Instance == this) {
				Instance = null;
			}
			DisposeCurrentSession();
		}

		private void Update() {
			HandlePendingSessionRestart();
			HandleHotkeys();
		}

		private void HandlePendingSessionRestart() {
#if INSTANT_REPLAY_PRESENT
			if (!_pendingSessionRestart) return;
			// ExportVideoCoroutine calls StartRecording on completion; skip if export is in progress.
			if (_isExporting) return;
			_pendingSessionRestart = false;
			DisposeCurrentSession();
			StartRecording();
#endif
		}

		private void HandleHotkeys() {
#if ENABLE_INPUT_SYSTEM
			var keyboard = UnityEngine.InputSystem.Keyboard.current;
			if (keyboard == null) return;
			if (keyboard[exportVideoHotkey].wasPressedThisFrame) TriggerExportVideo();
			if (keyboard[captureScreenshotHotkey].wasPressedThisFrame) TriggerCaptureScreenshot();
#else
			if (Input.GetKeyDown(exportVideoHotkey)) TriggerExportVideo();
			if (Input.GetKeyDown(captureScreenshotHotkey)) TriggerCaptureScreenshot();
#endif
		}

		#endregion

		#region Public API

		public bool IsRecording {
			get {
#if INSTANT_REPLAY_PRESENT
				return _currentSession != null;
#else
				return false;
#endif
			}
		}

		public bool IsPaused {
			get {
#if INSTANT_REPLAY_PRESENT
				return _currentSession?.IsPaused ?? false;
#else
				return false;
#endif
			}
		}

		/// <summary>Starts a new background recording session. No-op if already recording.</summary>
		public void StartRecording() {
#if INSTANT_REPLAY_PRESENT
			if (_currentSession != null) return;

			var sessionBox = new SessionBox();
			sessionBox.Session = _currentSession = new RealtimeInstantReplaySession(
				new RealtimeEncodingOptions {
					VideoOptions = new VideoEncoderOptions {
						Width = (uint)recordingWidth,
						Height = (uint)recordingHeight,
						FpsHint = (uint)fps,
						Bitrate = (uint)recordingBitrateKbps * 1000u,
					},
					AudioOptions = new AudioEncoderOptions {
						Channels = 2,
						SampleRate = (uint)AudioSettings.outputSampleRate,
						Bitrate = 128000,
					},
					MaxNumberOfRawFrameBuffers = maxNumberOfRawFrameBuffers,
					MaxMemoryUsageBytesForCompressedFrames = (long)maxMemoryUsageMb * 1024 * 1024,
					FixedFrameRate = fps,
					VideoInputQueueSize = 5,
					AudioInputQueueSizeSeconds = 1.0,
				},
				onException: exception => {
					Debug.LogException(exception);
					// Signal main thread to restart. Handled in Update via _pendingSessionRestart.
					if (sessionBox.Session == _currentSession) {
						_pendingSessionRestart = true;
					}
				}
			);
			Debug.Log("[UnityReplayIntegration] Recording started.");
#else
			Debug.LogWarning("[UnityReplayIntegration] InstantReplay is not installed. Please install jp.co.cyberagent.instant-replay via the Package Manager.");
#endif
		}

		/// <summary>Stops and discards the current recording session without exporting.</summary>
		public void StopRecording() {
#if INSTANT_REPLAY_PRESENT
			DisposeCurrentSession();
			Debug.Log("[UnityReplayIntegration] Recording stopped.");
#endif
		}

		/// <summary>Pauses the current recording session.</summary>
		public void PauseRecording() {
#if INSTANT_REPLAY_PRESENT
			_currentSession?.Pause();
#endif
		}

		/// <summary>Resumes the current recording session.</summary>
		public void ResumeRecording() {
#if INSTANT_REPLAY_PRESENT
			_currentSession?.Resume();
#endif
		}

		/// <summary>
		/// Triggers video export as a coroutine. Automatically restarts recording after export.
		/// When the UniTask extension (com.cysharp.unitask) is installed, prefer
		/// <c>ExportVideoAsync()</c> from <c>UnityReplayIntegrationExtensions</c> instead.
		/// </summary>
		/// <param name="onComplete">Called with the exported file path, or null on failure.</param>
		public void TriggerExportVideo(Action<string> onComplete = null) {
#if INSTANT_REPLAY_PRESENT
			if (_isExporting) {
				Debug.LogWarning("[UnityReplayIntegration] Export already in progress.");
				onComplete?.Invoke(null);
				return;
			}
			StartCoroutine(ExportVideoCoroutine(onComplete));
#else
			onComplete?.Invoke(null);
#endif
		}

		/// <summary>
		/// Captures a screenshot, saves it, and optionally uploads to Discord.
		/// When the UniTask extension is installed, prefer <c>CaptureAndUploadScreenshotAsync()</c> instead.
		/// </summary>
		/// <param name="onComplete">Called with the saved file path, or null on failure.</param>
		public void TriggerCaptureScreenshot(Action<string> onComplete = null) {
			StartCoroutine(CaptureScreenshotCoroutine(onComplete));
		}

		#endregion

		#region Coroutine Implementations

		private IEnumerator ExportVideoCoroutine(Action<string> onComplete) {
#if INSTANT_REPLAY_PRESENT
			_isExporting = true;

			var session = _currentSession;
			if (session == null) {
				Debug.LogWarning("[UnityReplayIntegration] No active recording session to export.");
				_isExporting = false;
				onComplete?.Invoke(null);
				yield break;
			}
			_currentSession = null;

			string directory = GetOutputDirectory();
			string exportedVideoPath = null;

			ValueTask<string> exportTask;
			try {
				Directory.CreateDirectory(directory);
				string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
				string videoPath = Path.Combine(directory, $"replay_{timestamp}.mp4");
				exportTask = session.StopAndExportAsync(outputPath: videoPath);
			} catch (Exception exception) {
				Debug.LogException(exception);
				session.Dispose();
				StartRecording();
				_isExporting = false;
				onComplete?.Invoke(null);
				yield break;
			}

			// Poll on main thread until the background task completes.
			while (!exportTask.IsCompleted) {
				yield return null;
			}
			session.Dispose();

			if (exportTask.IsFaulted) {
				var aggregateException = exportTask.AsTask().Exception;
				Debug.LogException(aggregateException?.InnerException ?? aggregateException);
			} else {
				exportedVideoPath = exportTask.Result;
				if (string.IsNullOrEmpty(exportedVideoPath)) {
					Debug.LogWarning("[UnityReplayIntegration] Export produced no output (no data recorded yet).");
					exportedVideoPath = null;
				} else {
					Debug.Log($"[UnityReplayIntegration] Video exported: {exportedVideoPath}");
				}
			}

			StartRecording();
			_isExporting = false;

			if (exportedVideoPath != null && ShouldSendToDiscord()) {
				yield return StartCoroutine(DiscordUploadHandler(exportedVideoPath, false));
			}

			onComplete?.Invoke(exportedVideoPath);
#else
			onComplete?.Invoke(null);
			yield break;
#endif
		}

		private IEnumerator CaptureScreenshotCoroutine(Action<string> onComplete) {
			yield return new WaitForEndOfFrame();

			string screenshotPath = null;
			Texture2D screenshot = null;

			try {
				screenshot = ScreenCapture.CaptureScreenshotAsTexture();
				if (screenshot == null) {
					Debug.LogWarning("[UnityReplayIntegration] Failed to capture screenshot.");
					onComplete?.Invoke(null);
					yield break;
				}

				string directory = GetOutputDirectory();
				Directory.CreateDirectory(directory);
				string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
				screenshotPath = Path.Combine(directory, $"screenshot_{timestamp}.png");
				File.WriteAllBytes(screenshotPath, screenshot.EncodeToPNG());
				Debug.Log($"[UnityReplayIntegration] Screenshot saved: {screenshotPath}");
			} catch (Exception exception) {
				Debug.LogException(exception);
				screenshotPath = null;
			} finally {
				if (screenshot != null) Destroy(screenshot);
			}

			if (screenshotPath != null && ShouldSendToDiscord()) {
				yield return StartCoroutine(DiscordUploadHandler(screenshotPath, true));
			}

			onComplete?.Invoke(screenshotPath);
		}

		#endregion

		#region Private Helpers

		private void DisposeCurrentSession() {
#if INSTANT_REPLAY_PRESENT
			if (_currentSession == null) return;
			_currentSession.Dispose();
			_currentSession = null;
#endif
		}

		private string GetOutputDirectory() =>
			string.IsNullOrEmpty(outputPath) ? Application.persistentDataPath : outputPath;

		#endregion

#if INSTANT_REPLAY_PRESENT
		private sealed class SessionBox {
			public RealtimeInstantReplaySession Session;
		}
#endif
	}
}
