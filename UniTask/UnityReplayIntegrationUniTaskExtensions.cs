using Cysharp.Threading.Tasks;

namespace UnityReplayIntegration {
	/// <summary>
	/// UniTask async extension methods for <see cref="UnityReplayIntegration"/>.
	/// This assembly only compiles when com.cysharp.unitask is installed.
	/// </summary>
	public static class UnityReplayIntegrationUniTaskExtensions {
		/// <summary>
		/// Exports the current replay as a video file and returns the saved file path.
		/// Returns null if export fails or no footage has been recorded yet.
		/// Recording automatically restarts after export completes.
		/// </summary>
		public static UniTask<string> ExportVideoAsync(this UnityReplayIntegration system) {
			var completionSource = new UniTaskCompletionSource<string>();
			system.TriggerExportVideo(filePath => completionSource.TrySetResult(filePath));
			return completionSource.Task;
		}

		/// <summary>
		/// Captures a screenshot, saves it to disk, and optionally uploads to Discord.
		/// Returns the saved file path, or null on failure.
		/// </summary>
		public static UniTask<string> CaptureScreenshotAsync(this UnityReplayIntegration system) {
			var completionSource = new UniTaskCompletionSource<string>();
			system.TriggerCaptureScreenshot(filePath => completionSource.TrySetResult(filePath));
			return completionSource.Task;
		}
	}
}
