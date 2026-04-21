#if INSTANT_REPLAY_PRESENT
using InstantReplay;
using UnityEngine;

namespace UnityReplayIntegration {
	// Placed dynamically on the active AudioListener's GameObject.
	// Relays OnAudioFilterRead callbacks to AdaptiveAudioSampleProvider.
	// Destroyed and re-created automatically when the tracked listener changes.
	[AddComponentMenu("")]  // hide from Add Component menu
	internal sealed class AudioCaptureFilter : MonoBehaviour {
		internal AdaptiveAudioSampleProvider Provider;

		private void OnAudioFilterRead(float[] data, int channels) {
			Provider?.Forward(data, channels);
		}
	}

	/// <summary>
	/// <see cref="IAudioSampleProvider"/> that automatically follows whichever
	/// <see cref="AudioListener"/> is active in the scene. Handles enable/disable,
	/// scene transitions, and listener swaps transparently — the recording session
	/// never needs to restart due to AudioListener state changes.
	///
	/// Call <see cref="Tick"/> once per frame from the main thread.
	/// </summary>
	internal sealed class AdaptiveAudioSampleProvider : IAudioSampleProvider {
		public event IAudioSampleProvider.ProvideAudioSamples OnProvideAudioSamples;

		private AudioListener _trackedListener;
		private AudioCaptureFilter _captureFilter;
		private int _sampleRate;
		private readonly bool _autoDetectOnTick;

		internal AdaptiveAudioSampleProvider(bool autoDetectOnTick = false) {
			_autoDetectOnTick = autoDetectOnTick;
		}

		// Called on the Unity audio thread by AudioCaptureFilter.OnAudioFilterRead.
		internal void Forward(float[] data, int channels) {
			OnProvideAudioSamples?.Invoke(data, channels, _sampleRate, AudioSettings.dspTime);
		}

		/// <summary>
		/// Must be called every frame on the main thread.
		/// Detects AudioListener changes and moves the capture filter accordingly.
		/// </summary>
		internal void Tick() {
			if (_trackedListener != null && _captureFilter != null) return;

			if (_trackedListener != null) {
				// Listener is known but filter was lost — reattach unconditionally.
				AttachFilter(_trackedListener);
				return;
			}

			// No tracked listener. Only scan the scene when auto-detect is enabled.
			if (!_autoDetectOnTick) return;
			RescanAndReattach();
		}

		/// <summary>
		/// Immediately switches audio capture to <paramref name="listener"/>.
		/// Pass <c>null</c> to stop capture; auto-detection resumes on the next <see cref="Tick"/> call.
		/// Must be called from the main thread.
		/// </summary>
		internal void SetAudioListener(AudioListener listener) {
			DetachFilter();
			_trackedListener = listener;
			if (_trackedListener != null) AttachFilter(_trackedListener);
		}

		/// <summary>
		/// Forces an immediate re-scan for the active <see cref="AudioListener"/> in the scene.
		/// Useful after a scene transition or whenever the listener may have changed.
		/// Must be called from the main thread.
		/// </summary>
		internal void RefreshAudioListener() => RescanAndReattach();

		private void RescanAndReattach() {
			var current = Object.FindFirstObjectByType<AudioListener>();
			if (current == _trackedListener) {
				if (current != null && _captureFilter == null) AttachFilter(current);
				return;
			}

			DetachFilter();
			_trackedListener = current;
			if (_trackedListener != null) AttachFilter(_trackedListener);
		}

		public void Dispose() {
			OnProvideAudioSamples = null;
			DetachFilter();
			_trackedListener = null;
		}

		private void AttachFilter(AudioListener listener) {
			_sampleRate = AudioSettings.outputSampleRate;
			_captureFilter = listener.gameObject.AddComponent<AudioCaptureFilter>();
			_captureFilter.Provider = this;
		}

		private void DetachFilter() {
			if (_captureFilter != null) {
				Object.Destroy(_captureFilter);
				_captureFilter = null;
			}
		}
	}
}
#endif
