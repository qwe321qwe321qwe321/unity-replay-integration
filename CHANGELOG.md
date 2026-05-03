# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.4] - 2026-05-03
### Added
- `Tools → Unity Replay Integration → Settings` 視窗：合併原先的 Dependencies 視窗，並新增 Build 設定區塊。
- Build 設定：可透過 `UNITY_REPLAY_INTEGRATION_EXCLUDED_IN_BUILD` scripting define 將整個 Replay Integration（含 Discord bridge）從 build 中排除。
- `ReplayFfmpegPlatformPath` 與 `discordFfmpegPlatformPaths`：可針對不同平台（RuntimePlatform）指定對應的 FFmpeg 執行檔路徑，於執行期自動選用。
- 錄影預估時間上限顯示（estimated recording time upper bound）。

### Changed
- 取代舊的 `Tools → Unity Replay Integration → Dependencies` 視窗為新的 Settings 視窗。
- Dependency installer 改用 `EditorApplication.delayCall` 延後執行，避免 domain reload 時觸發 `ScriptableSingleton` 警告。

### Fixed
- 分段上傳時 `{FPS}` 與 `{RES}` placeholder 在 Discord 訊息中顯示異常的問題。

## [0.1.3] - 2026-04-21
### Added
- `AdaptiveAudioSampleProvider`：音訊擷取現在會自動追蹤場景中的活躍 `AudioListener`，在 scene transition 或 listener 切換時無需重啟錄影 session。
- `autoDetectAudioListenerOnTick` 欄位（預設 `false`）：啟用時每幀自動掃描場景尋找 `AudioListener`；停用時改由手動呼叫 `RefreshAudioListener()` 或 `SetAudioListener()`，可避免每幀 `FindFirstObjectByType` 的效能開銷。
- 公開 API `SetAudioListener(AudioListener listener)`：立即切換音訊擷取目標（例如切換相機時）。
- 公開 API `RefreshAudioListener()`：強制立即重新掃描並更新 AudioListener，適合 scene transition 後手動觸發。

### Changed
- 錄影啟動時不再因場景中無 `AudioListener` 而中止；`AdaptiveAudioSampleProvider` 會在 listener 出現時自動接管擷取。

## [0.1.2] - 2026-04-21
### Added
- FPS and resolution placeholders (`{FPS}`, `{RES}`) in Discord webhook content and thread title.

## [0.1.1] - 2026-04-21
### Added
- Option to disable audio recording (`recordAudio` toggle in the editor and runtime API).

## [0.1.0] - 2026-04-15
### Added
- Auto-split video into chunks to bypass the Discord file size limit.
- `EditorWindow` for monitoring dependency status.
- UniTask async API support.
- Initial Discord webhook upload integration.
- Background video recording via InstantReplay and screenshot capture.
