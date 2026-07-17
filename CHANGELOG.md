# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.10] - 2026-07-17
### Fixed
- `DisposeCurrentSession` 改為在主執行緒同步呼叫 `session.Dispose()`，不再透過 `Task.Run` 丟到背景執行緒執行。Session 釋放內部會觸碰 Unity API（例如 `ScreenshotFrameProvider.Dispose` 存取 `Application.isPlaying`），在背景執行緒呼叫是不合法的，過去曾靜默造成 engine/Mono 狀態損毀（詳見 v0.1.6）；此變更會讓長時間 session 的釋放可能造成主執行緒短暫卡頓，但避免了更嚴重的狀態損毀問題。

## [0.1.9] - 2026-07-17
### Added
- Settings 視窗新增 InstantReplay / InstantReplay Dependencies 的更新檢查：從 `release` 分支讀取遠端 `package.json` 版本，若已安裝版本落後會顯示「Update available」提示與版本號，並提供 `Update` / `Update All` 按鈕一鍵更新對應的 Git dependency。

## [0.1.8] - 2026-07-01
### Fixed
- 停用第三方音效引擎（FMOD / WWISE）情境下 `Record Audio` 仍啟用時，`StartRecording()` 因無法查詢 `AudioSettings.outputSampleRate` 而拋出 `ArgumentException` 的問題；`recordAudio` 關閉時改用固定的 44100 Hz 取代查詢結果。

### Changed
- README 新增「使用第三方音效引擎（FMOD / WWISE）」章節，說明上述例外的成因與因應方式。

## [0.1.7] - 2026-06-26
### Fixed
- 修正 `UNITY_REPLAY_INTEGRATION_EXCLUDED_IN_BUILD` build stub 呼叫 `Instance.StartRecording()` 等 API 時拋出 `NullReferenceException` 的問題：build stub 過去直接 `Destroy(this)` 且 `Instance` 恆為 `null`，現改為維護正常的 singleton（與完整版本相同模式），所有 public API 呼叫維持 no-op 但不再 NPE。

## [0.1.6] - 2026-06-26
### Fixed
- `StopRecording` 阻塞主執行緒的問題：`DisposeCurrentSession()` 改為立即將 `_currentSession` 設為 `null`，並將實際的 `Dispose()` 丟到 thread pool 執行，避免捨棄長時間錄影 session 時造成 editor/game 卡頓。

### Changed
- `ReplayIntegrationBuildSettings.SelectedGroup` 改為 `public`。
- Settings 視窗的 Build 設定區塊會顯示目前的 platform group 名稱，並註明該 exclude 設定是以 platform group 為單位分別生效。

## [0.1.5] - 2026-05-19
### Fixed
- Discord 影片分段上傳偶爾因 FFmpeg 分段後單一片段仍超過檔案大小限制而拋出例外的問題：目標片段時長的安全邊際從原本的 85% 調降為 60%，以因應稀疏 GOP（例如 30 秒關鍵影格間隔）導致 `-c copy` 分割超出預期時長；分段後若單一片段仍超過限制，會自動改用 zip 壓縮重新上傳該片段。

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
