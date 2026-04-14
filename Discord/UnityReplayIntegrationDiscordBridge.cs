using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using DiscordWebhook;
using UnityEngine;
using Debug = UnityEngine.Debug;

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

			// Check upload limit for videos.
			long uploadLimitBytes = system.DiscordUploadLimitBytes;
			if (!isScreenshot && uploadLimitBytes > 0) {
				long fileSize;
				try {
					fileSize = new FileInfo(filePath).Length;
				} catch (Exception ex) {
					Debug.LogError($"[UnityReplayIntegration] Failed to read file info: {ex.Message}");
					yield break;
				}

				if (fileSize > uploadLimitBytes) {
					if (system.DiscordAutoSplitVideo) {
						// Try to split into playable segments via FFmpeg.
						// SplitAndUploadCoroutine falls back to DirectUploadWithZipFallback if FFmpeg is unavailable or fails.
						yield return SplitAndUploadCoroutine(system, filePath, fileSize, uploadLimitBytes);
					} else {
						// Auto-split disabled: skip FFmpeg and upload as zip (falls back to error if zip is also too large).
						Debug.LogWarning($"[UnityReplayIntegration] File exceeds the {uploadLimitBytes / (1024f * 1024f):F0} MB limit. Auto Split Video is disabled; attempting zip upload instead.");
						yield return DirectUploadWithZipFallback(system, filePath, false);
					}
					yield break;
				}
			}

			yield return DirectUploadWithZipFallback(system, filePath, isScreenshot);
		}

		// ── Split upload ──────────────────────────────────────────────────────

		/// <summary>
		/// Splits <paramref name="filePath"/> into playable MP4 segments at keyframe boundaries
		/// using FFmpeg (<c>-c copy</c>), then uploads each segment as a separate Discord message.
		/// Falls back to a direct upload attempt when FFmpeg is unavailable or fails.
		/// </summary>
		static IEnumerator SplitAndUploadCoroutine(UnityReplayIntegration system, string filePath, long fileSize, long chunkSize) {
			// 1. Parse video duration from the MP4 container.
			double? durationSec = GetMp4DurationSeconds(filePath);
			if (durationSec == null || durationSec.Value <= 0) {
				Debug.LogWarning("[UnityReplayIntegration] Could not read video duration for smart splitting. Attempting direct upload.");
				yield return DirectUploadWithZipFallback(system, filePath, false);
				yield break;
			}

			// 2. Calculate target segment duration.
			//    Use 85 % of the chunk size as margin to account for keyframe boundary rounding.
			double safeChunk = chunkSize * 0.85;
			double segmentSec = durationSec.Value * safeChunk / fileSize;
			int estimatedParts = (int)Math.Ceiling(durationSec.Value / segmentSec);
			Debug.Log($"[UnityReplayIntegration] Video ({fileSize / (1024f * 1024f):F1} MB, {durationSec.Value:F1}s) exceeds the {chunkSize / (1024f * 1024f):F0} MB limit. Splitting into ~{estimatedParts} parts via FFmpeg.");

			// 3. Prepare FFmpeg arguments.
			string directory = Path.GetDirectoryName(filePath) ?? "";
			string baseName = Path.GetFileNameWithoutExtension(filePath);
			string outputPattern = Path.Combine(directory, $"{baseName}.part%03d.mp4");
			string ffmpegExe = string.IsNullOrEmpty(system.DiscordFfmpegPath) ? "ffmpeg" : system.DiscordFfmpegPath;
			// -c copy: remux without re-encoding (fast, lossless)
			// -f segment: split into numbered files at keyframe boundaries
			// -reset_timestamps 1: each segment starts from t=0 so it plays correctly
			// -avoid_negative_ts make_zero: fix negative timestamps that can occur at segment start
			string args = $"-y -loglevel error -i \"{filePath}\" -c copy -map 0 -f segment"
			            + $" -segment_time {segmentSec:F3} -reset_timestamps 1 -avoid_negative_ts make_zero"
			            + $" \"{outputPattern}\"";

			// 4. Launch FFmpeg as a background process.
			Process process = null;
			string ffmpegStartError = null;
			try {
				var psi = new ProcessStartInfo {
					FileName        = ffmpegExe,
					Arguments       = args,
					UseShellExecute = false,
					CreateNoWindow  = true,
				};
				process = Process.Start(psi);
				if (process == null) ffmpegStartError = "Process.Start returned null.";
			} catch (Exception ex) {
				ffmpegStartError = ex.Message;
			}

			if (ffmpegStartError != null) {
				Debug.LogError(
					$"[UnityReplayIntegration] Failed to start FFmpeg ('{ffmpegExe}'): {ffmpegStartError}.\n"
					+ "Ensure FFmpeg is installed and available in the system PATH, or set the path in the Inspector.\n"
					+ "Attempting direct upload instead.");
				yield return DirectUploadWithZipFallback(system, filePath, false);
				yield break;
			}

			// 5. Poll until FFmpeg finishes (non-blocking coroutine wait, with timeout).
			const float k_TimeoutSeconds = 120f;
			float elapsed = 0f;
			while (!process.HasExited && elapsed < k_TimeoutSeconds) {
				yield return null;
				elapsed += Time.unscaledDeltaTime;
			}

			if (!process.HasExited) {
				process.Kill();
				process.Dispose();
				Debug.LogError("[UnityReplayIntegration] FFmpeg timed out. Attempting direct upload instead.");
				yield return DirectUploadWithZipFallback(system, filePath, false);
				yield break;
			}

			int exitCode = process.ExitCode;
			process.Dispose();

			if (exitCode != 0) {
				Debug.LogError($"[UnityReplayIntegration] FFmpeg exited with code {exitCode}. Attempting direct upload instead.");
				yield return DirectUploadWithZipFallback(system, filePath, false);
				yield break;
			}

			// 6. Collect the generated part files (FFmpeg uses 0-based index).
			var partFiles = new List<string>();
			for (int i = 0; ; i++) {
				string partPath = Path.Combine(directory, $"{baseName}.part{i:D3}.mp4");
				if (!File.Exists(partPath)) break;
				partFiles.Add(partPath);
			}

			if (partFiles.Count == 0) {
				Debug.LogError("[UnityReplayIntegration] FFmpeg produced no output files. Attempting direct upload instead.");
				yield return DirectUploadWithZipFallback(system, filePath, false);
				yield break;
			}

			// 7. Upload each part, then clean it up.
			int total = partFiles.Count;
			for (int i = 0; i < total; i++) {
				string partPath = partFiles[i];
				string partLabel = $"[{i + 1}/{total}]";
				string content = i == 0
					? $"{partLabel} {FormatTemplate(system.DiscordContent, filePath, durationSec)}"
					: partLabel;
				string threadTitle = $"{FormatTemplate(system.DiscordForumThreadTitle, filePath, durationSec)} {partLabel}";

				WebhookResponseResult result = default;
				yield return BuildWebhookBuilder(system, contentOverride: content, threadTitleOverride: threadTitle)
					.AddFile(partPath)
					.ExecuteIEnumerator(r => result = r);

				if (result.isSuccess) {
					Debug.Log($"[UnityReplayIntegration] Uploaded part {i + 1}/{total} to Discord.");
				} else {
					Debug.LogError($"[UnityReplayIntegration] Failed to upload part {i + 1}/{total}: {result.errorMessage}");
				}

				try { File.Delete(partPath); } catch { }
			}
		}

		// ── Normal upload ─────────────────────────────────────────────────────

		static IEnumerator DirectUploadWithZipFallback(UnityReplayIntegration system, string filePath, bool isScreenshot) {
			WebhookResponseResult firstResult = default;
			yield return BuildWebhookBuilder(system, filePath: filePath)
				.AddFile(filePath)
				.ExecuteIEnumerator(result => firstResult = result);

			if (firstResult.isSuccess) {
				Debug.Log($"[UnityReplayIntegration] Uploaded to Discord: {filePath}");
				yield break;
			}

			string zipFileName = isScreenshot ? "screenshot.zip" : "video.zip";
			Debug.LogWarning($"[UnityReplayIntegration] Webhook upload failed ({firstResult.errorMessage}). Retrying with zip...");

			WebhookResponseResult retryResult = default;
			yield return BuildWebhookBuilder(system, filePath: filePath)
				.AddFile(filePath)
				.SetCompressAllFilesToZip(true, zipFileName)
				.ExecuteIEnumerator(result => retryResult = result);

			if (retryResult.isSuccess) {
				Debug.Log($"[UnityReplayIntegration] Uploaded to Discord (zipped): {filePath}");
			} else {
				Debug.LogError($"[UnityReplayIntegration] Failed to upload to Discord (including zip retry): {retryResult.errorMessage}");
			}
		}

		// ── Helpers ───────────────────────────────────────────────────────────

		static WebhookBuilder BuildWebhookBuilder(UnityReplayIntegration system, string filePath = null, double? durationSec = null, string contentOverride = null, string threadTitleOverride = null) {
			string content = contentOverride ?? FormatTemplate(system.DiscordContent, filePath, durationSec);
			string threadTitle = threadTitleOverride ?? FormatTemplate(system.DiscordForumThreadTitle, filePath, durationSec);
			// ReplayDiscordChannelType values intentionally match DiscordWebhook.ChannelType.
			if ((ChannelType)(int)system.DiscordChannelType == ChannelType.Forum) {
				return WebhookBuilder.CreateForum(system.DiscordWebhookUrl)
					.SetThreadName(threadTitle)
					.SetContent(content);
			}
			return WebhookBuilder.CreateTextChannel(system.DiscordWebhookUrl)
				.SetContent(content);
		}

		static string FormatTemplate(string template, string filePath = null, double? durationSec = null) {
			string result = template.Replace("{TIME}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

			if (result.Contains("{SIZE}")) {
				string sizeStr = "?";
				if (filePath != null) {
					try {
						long bytes = new FileInfo(filePath).Length;
						sizeStr = (bytes / (1024.0 * 1024.0)).ToString("F2") + " MB";
					} catch { /* ignore */ }
				}
				result = result.Replace("{SIZE}", sizeStr);
			}

			if (result.Contains("{LENGTH}")) {
				double? dur = durationSec ?? (filePath != null ? GetMp4DurationSeconds(filePath) : null);
				result = result.Replace("{LENGTH}", dur.HasValue ? FormatDuration(dur.Value) : "?");
			}

			return result;
		}

		static string FormatDuration(double seconds) {
			int total = (int)Math.Round(seconds);
			int h = total / 3600;
			int m = (total % 3600) / 60;
			int s = total % 60;
			if (h > 0) return $"{h}h{m}m{s}s";
			if (m > 0) return $"{m}m{s}s";
			return $"{s}s";
		}

		// ── MP4 duration parser ───────────────────────────────────────────────

		/// <summary>
		/// Reads the <c>mvhd</c> box inside an MP4 file to extract the total duration in seconds.
		/// Returns <c>null</c> when the box cannot be found or the file is not a valid MP4.
		/// </summary>
		static double? GetMp4DurationSeconds(string filePath) {
			try {
				using (var fs = File.OpenRead(filePath))
				using (var r = new BinaryReader(fs)) {
					long fileLen = fs.Length;

					// Walk top-level boxes.
					while (fs.Position + 8 <= fileLen) {
						long boxStart = fs.Position;
						uint size32 = ReadU32BE(r);
						string boxType = new string(r.ReadChars(4));

						long boxSize;
						if (size32 == 1) {
							if (fs.Position + 8 > fileLen) return null;
							boxSize = (long)ReadU64BE(r);
						} else if (size32 == 0) {
							boxSize = fileLen - boxStart;
						} else {
							boxSize = size32;
						}

						long boxEnd = boxStart + boxSize;
						if (boxEnd > fileLen || boxSize < 8) return null;

						if (boxType == "moov") {
							// Walk boxes inside moov.
							while (fs.Position + 8 <= boxEnd) {
								long innerStart = fs.Position;
								uint innerSize32 = ReadU32BE(r);
								string innerType = new string(r.ReadChars(4));

								long innerSize;
								if (innerSize32 == 1) {
									innerSize = (long)ReadU64BE(r);
								} else if (innerSize32 == 0) {
									innerSize = boxEnd - innerStart;
								} else {
									innerSize = innerSize32;
								}

								long innerEnd = innerStart + innerSize;

								if (innerType == "mvhd") {
									byte version = r.ReadByte();
									r.ReadBytes(3); // flags
									uint timescale;
									ulong duration;
									if (version == 1) {
										r.ReadBytes(8);  // creation_time
										r.ReadBytes(8);  // modification_time
										timescale = ReadU32BE(r);
										duration = ReadU64BE(r);
									} else {
										r.ReadBytes(4);  // creation_time
										r.ReadBytes(4);  // modification_time
										timescale = ReadU32BE(r);
										duration = ReadU32BE(r);
									}
									if (timescale == 0) return null;
									return (double)duration / timescale;
								}

								if (innerEnd <= innerStart) break;
								fs.Seek(innerEnd, SeekOrigin.Begin);
							}
							return null; // mvhd not found
						}

						if (boxEnd <= boxStart) break;
						fs.Seek(boxEnd, SeekOrigin.Begin);
					}
				}
			} catch {
				// Ignore parse errors; caller treats null as "unknown".
			}
			return null;
		}

		static uint ReadU32BE(BinaryReader r) {
			byte[] b = r.ReadBytes(4);
			return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
		}

		static ulong ReadU64BE(BinaryReader r) {
			byte[] b = r.ReadBytes(8);
			return ((ulong)b[0] << 56) | ((ulong)b[1] << 48) | ((ulong)b[2] << 40) | ((ulong)b[3] << 32)
			     | ((ulong)b[4] << 24) | ((ulong)b[5] << 16) | ((ulong)b[6] <<  8) | b[7];
		}
	}
}
