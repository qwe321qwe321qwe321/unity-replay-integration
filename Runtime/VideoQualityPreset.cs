namespace UnityReplayIntegration {
	/// <summary>
	/// Quality presets for video recording. Each preset defines resolution, frame rate, and bitrate.
	/// Select <see cref="Custom"/> to configure all parameters individually.
	/// </summary>
	public enum VideoQualityPreset {
		/// <summary>All recording parameters are set manually.</summary>
		Custom = 0,
		/// <summary>1280×720 @ 30 fps, 2.5 Mbps</summary>
		HD30,
		/// <summary>1280×720 @ 60 fps, 5 Mbps</summary>
		HD60,
		/// <summary>1920×1080 @ 30 fps, 5 Mbps</summary>
		FullHD30,
		/// <summary>1920×1080 @ 60 fps, 10 Mbps</summary>
		FullHD60,
		/// <summary>3840×2160 @ 60 fps, 40 Mbps</summary>
		UltraHD60,
	}

	/// <summary>
	/// Concrete parameter values for each <see cref="VideoQualityPreset"/>.
	/// </summary>
	public static class VideoQualityPresetSettings {
		public readonly struct PresetValues {
			public readonly int Width;
			public readonly int Height;
			public readonly int Fps;
			/// <summary>Target bitrate in kilobits per second (Kbps).</summary>
			public readonly int BitrateKbps;

			public PresetValues(int width, int height, int fps, int bitrateKbps) {
				Width = width;
				Height = height;
				Fps = fps;
				BitrateKbps = bitrateKbps;
			}
		}

		public static readonly PresetValues HD30      = new(1280, 720,  30, 2500);
		public static readonly PresetValues HD60      = new(1280, 720,  60, 5000);
		public static readonly PresetValues FullHD30  = new(1920, 1080, 30, 5000);
		public static readonly PresetValues FullHD60  = new(1920, 1080, 60, 10000);
		public static readonly PresetValues UltraHD60 = new(3840, 2160, 60, 40000);

		/// <summary>
		/// Returns the preset values for the given <paramref name="preset"/>,
		/// or <c>null</c> when <see cref="VideoQualityPreset.Custom"/> is specified.
		/// </summary>
		public static PresetValues? Get(VideoQualityPreset preset) => preset switch {
			VideoQualityPreset.HD30      => HD30,
			VideoQualityPreset.HD60      => HD60,
			VideoQualityPreset.FullHD30  => FullHD30,
			VideoQualityPreset.FullHD60  => FullHD60,
			VideoQualityPreset.UltraHD60 => UltraHD60,
			_                            => null,
		};
	}
}
