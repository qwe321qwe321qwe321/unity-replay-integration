using System;

namespace UnityReplayIntegration {
	public enum ReplayFfmpegPlatform {
		WindowsEditor,
		WindowsBuild,
		MacEditor,
		MacBuild,
		LinuxEditor,
		LinuxBuild,
	}

	[Serializable]
	public struct ReplayFfmpegPlatformPath {
		public ReplayFfmpegPlatform platform;
		public string path;
	}
}
