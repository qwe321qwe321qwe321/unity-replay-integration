using System;
using System.Collections;
using DiscordWebhook;
using UnityEngine;

namespace UnityReplayIntegration {
	/// <summary>
	/// Automatically registers Discord upload functionality with <see cref="UnityReplayIntegration"/>.
	/// This assembly only compiles when com.pedev.unity-discord-webhook is installed.
	/// </summary>
	static class UnityReplayIntegrationDiscordBridge {
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Register() {
			UnityReplayIntegration.DiscordUploadHandler = UploadToDiscordCoroutine;
		}

		static IEnumerator UploadToDiscordCoroutine(string filePath, bool isScreenshot) {
			var system = UnityReplayIntegration.Instance;
			if (system == null) yield break;

			WebhookResponseResult firstResult = default;
			yield return BuildWebhookBuilder(system)
				.AddFile(filePath)
				.ExecuteIEnumerator(result => firstResult = result);

			if (firstResult.isSuccess) {
				Debug.Log($"[UnityReplayIntegration] Uploaded to Discord: {filePath}");
				yield break;
			}

			string zipFileName = isScreenshot ? "screenshot.zip" : "video.zip";
			Debug.LogWarning($"[UnityReplayIntegration] Webhook upload failed ({firstResult.errorMessage}). Retrying with zip...");

			WebhookResponseResult retryResult = default;
			yield return BuildWebhookBuilder(system)
				.AddFile(filePath)
				.SetCompressAllFilesToZip(true, zipFileName)
				.ExecuteIEnumerator(result => retryResult = result);

			if (retryResult.isSuccess) {
				Debug.Log($"[UnityReplayIntegration] Uploaded to Discord (zipped): {filePath}");
			} else {
				Debug.LogError($"[UnityReplayIntegration] Failed to upload to Discord (including zip retry): {retryResult.errorMessage}");
			}
		}

		static WebhookBuilder BuildWebhookBuilder(UnityReplayIntegration system) {
			string content = FormatTemplate(system.DiscordContent);
			// ReplayDiscordChannelType values intentionally match DiscordWebhook.ChannelType.
			if ((ChannelType)(int)system.DiscordChannelType == ChannelType.Forum) {
				return WebhookBuilder.CreateForum(system.DiscordWebhookUrl)
					.SetThreadName(FormatTemplate(system.DiscordForumThreadTitle))
					.SetContent(content);
			}
			return WebhookBuilder.CreateTextChannel(system.DiscordWebhookUrl)
				.SetContent(content);
		}

		static string FormatTemplate(string template) =>
			template.Replace("{TIME}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
	}
}
