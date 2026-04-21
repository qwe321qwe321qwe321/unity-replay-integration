using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityReplayIntegration.Editor {
	[CustomEditor(typeof(UnityReplayIntegration))]
	sealed class UnityReplayIntegrationEditor : UnityEditor.Editor {
		SerializedProperty _qualityPreset;
		SerializedProperty _recordingWidth;
		SerializedProperty _recordingHeight;
		SerializedProperty _fps;
		SerializedProperty _recordingBitrateKbps;
		SerializedProperty _maxMemoryUsageMb;
		SerializedProperty _maxNumberOfRawFrameBuffers;
		SerializedProperty _startOnAwake;
		SerializedProperty _recordAudio;
		SerializedProperty _exportVideoHotkey;
		SerializedProperty _captureScreenshotHotkey;
		SerializedProperty _outputPath;
		SerializedProperty _discordWebhookEnabled;
		SerializedProperty _discordWebhookUrl;
		SerializedProperty _discordChannelType;
		SerializedProperty _discordForumThreadTitle;
		SerializedProperty _discordContent;
		SerializedProperty _discordUploadLimitMb;
		SerializedProperty _discordAutoSplitVideo;
		SerializedProperty _discordFfmpegPath;

		void OnEnable() {
			_qualityPreset              = serializedObject.FindProperty("qualityPreset");
			_recordingWidth             = serializedObject.FindProperty("recordingWidth");
			_recordingHeight            = serializedObject.FindProperty("recordingHeight");
			_fps                        = serializedObject.FindProperty("fps");
			_recordingBitrateKbps       = serializedObject.FindProperty("recordingBitrateKbps");
			_maxMemoryUsageMb           = serializedObject.FindProperty("maxMemoryUsageMb");
			_maxNumberOfRawFrameBuffers = serializedObject.FindProperty("maxNumberOfRawFrameBuffers");
			_startOnAwake               = serializedObject.FindProperty("startOnAwake");
			_recordAudio                = serializedObject.FindProperty("recordAudio");
			_exportVideoHotkey          = serializedObject.FindProperty("exportVideoHotkey");
			_captureScreenshotHotkey    = serializedObject.FindProperty("captureScreenshotHotkey");
			_outputPath                 = serializedObject.FindProperty("outputPath");
			_discordWebhookEnabled      = serializedObject.FindProperty("discordWebhookEnabled");
			_discordWebhookUrl          = serializedObject.FindProperty("discordWebhookUrl");
			_discordChannelType         = serializedObject.FindProperty("discordChannelType");
			_discordForumThreadTitle    = serializedObject.FindProperty("discordForumThreadTitle");
			_discordContent             = serializedObject.FindProperty("discordContent");
			_discordUploadLimitMb       = serializedObject.FindProperty("discordUploadLimitMb");
			_discordAutoSplitVideo      = serializedObject.FindProperty("discordAutoSplitVideo");
			_discordFfmpegPath          = serializedObject.FindProperty("discordFfmpegPath");
		}

		public override void OnInspectorGUI() {
			serializedObject.Update();

			// ── Recording ────────────────────────────────────────────────
			EditorGUILayout.PropertyField(_qualityPreset);

			bool isPreset = _qualityPreset.enumValueIndex != (int)VideoQualityPreset.Custom;

			EditorGUI.BeginDisabledGroup(isPreset);
			EditorGUILayout.PropertyField(_recordingWidth);
			EditorGUILayout.PropertyField(_recordingHeight);
			EditorGUILayout.PropertyField(_fps);
			EditorGUILayout.PropertyField(_recordingBitrateKbps);
			EditorGUI.EndDisabledGroup();

			EditorGUILayout.PropertyField(_maxMemoryUsageMb);
			EditorGUILayout.PropertyField(_maxNumberOfRawFrameBuffers);
			EditorGUILayout.PropertyField(_startOnAwake);
			EditorGUILayout.PropertyField(_recordAudio);

			EditorGUILayout.Space();

			// ── Hotkeys ──────────────────────────────────────────────────
			EditorGUILayout.PropertyField(_exportVideoHotkey);
			EditorGUILayout.PropertyField(_captureScreenshotHotkey);

			EditorGUILayout.Space();

			// ── Output ───────────────────────────────────────────────────
			EditorGUILayout.PropertyField(_outputPath);
			EditorGUILayout.HelpBox(
				"Leave empty to save to Application.persistentDataPath.\n"
				+ "Absolute paths are recommended for cross-platform compatibility; "
				+ "the working directory varies by platform.",
				MessageType.Info
			);

			EditorGUILayout.Space();

			// ── Discord Webhook ───────────────────────────────────────────
			EditorGUILayout.PropertyField(_discordWebhookEnabled);
			EditorGUILayout.PropertyField(_discordWebhookUrl);
			EditorGUILayout.PropertyField(_discordChannelType);
			EditorGUILayout.PropertyField(_discordForumThreadTitle);
			EditorGUILayout.PropertyField(_discordContent);
			EditorGUILayout.HelpBox(
				"Available variables:\n"
				+ "  {TIME}    — Date and time (yyyy-MM-dd HH:mm:ss)\n"
				+ "  {SIZE}    — File size in MB (e.g. 12.34 MB)\n"
				+ "  {LENGTH}  — Video duration (e.g. 1m10s)",
				MessageType.Info
			);
			EditorGUILayout.PropertyField(_discordUploadLimitMb);
			EditorGUILayout.PropertyField(_discordAutoSplitVideo);
			EditorGUI.BeginDisabledGroup(!_discordAutoSplitVideo.boolValue);
			EditorGUI.indentLevel++;
			DrawFfmpegPathField();
			EditorGUI.indentLevel--;
			EditorGUI.EndDisabledGroup();

			serializedObject.ApplyModifiedProperties();
		}

		void DrawFfmpegPathField() {
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(_discordFfmpegPath);
			if (GUILayout.Button("Browse", GUILayout.Width(60f))) {
				string selected = EditorUtility.OpenFilePanel("Select FFmpeg Executable", "", "");
				if (!string.IsNullOrEmpty(selected)) {
					_discordFfmpegPath.stringValue = NormalizeFfmpegPath(selected);
					serializedObject.ApplyModifiedProperties();
				}
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.HelpBox(
				"Leave empty to use FFmpeg from the system PATH.\n"
				+ "Paths inside StreamingAssets are stored as StreamingAssets:/... and resolve correctly in builds.\n"
				+ "Other paths inside the project folder are stored as relative paths (Editor-only; may not work in builds).",
				MessageType.Info
			);
		}

		static string NormalizeFfmpegPath(string absolutePath) {
			string path = absolutePath.Replace('\\', '/');
			string streamingAssets = Application.streamingAssetsPath.Replace('\\', '/');
			string projectRoot = (Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath).Replace('\\', '/');

			if (path.StartsWith(streamingAssets + "/", System.StringComparison.OrdinalIgnoreCase)) {
				return "StreamingAssets:/" + path.Substring(streamingAssets.Length + 1);
			}
			if (string.Equals(path, streamingAssets, System.StringComparison.OrdinalIgnoreCase)) {
				return "StreamingAssets:/";
			}
			if (path.StartsWith(projectRoot + "/", System.StringComparison.OrdinalIgnoreCase)) {
				return path.Substring(projectRoot.Length + 1);
			}
			return absolutePath;
		}
	}
}
