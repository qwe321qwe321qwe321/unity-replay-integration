# Unity Replay Integration

> 註：此 package 為 vibe coded 玩具，主要提供個人專案快速應用用途，設計目標偏向實用與快速整合，不特別追求通用化、完整產品化或長期穩定 API 承諾。

> 本文件由 AI agent（Claude Sonnet 4.6）撰寫與維護。

`Unity Replay Integration` 是一個 Unity package，將 **[InstantReplay](https://github.com/CyberAgentGameEntertainment/InstantReplay)**（背景錄影引擎）與 **[Discord Webhook](https://github.com/qwe321qwe321qwe321/Unity-DiscordWebhook)**（影片與截圖輸出端）整合為單一易用的系統。只需在場景中放入一個元件，即可在遊戲執行中持續進行背景錄影、匯出即時回放片段、擷取截圖，並可選擇自動上傳至 Discord Webhook。

此套件同時處理了 InstantReplay 與 Discord Webhook 的相依性問題，並透過內建的 Editor 安裝檢查機制，自動偵測並提示安裝缺少的套件，大幅降低接入成本。

## 功能特色

- 整合 **[InstantReplay](https://github.com/CyberAgentGameEntertainment/InstantReplay)** 作為背景錄影引擎，無縫提供即時回放能力
- 整合 **[Discord Webhook](https://github.com/qwe321qwe321qwe321/Unity-DiscordWebhook)** 作為影片與截圖的輸出端，一鍵上傳至 Discord
- 支援匯出回放影片 `mp4`
- 支援擷取螢幕截圖 `png`
- 可綁定熱鍵快速匯出影片與截圖
- 可指定輸出資料夾，未指定時使用 `Application.persistentDataPath`
- 內建 Editor 相依性檢查，首次載入時自動提示安裝必要與選用套件
- 若專案有安裝 `UniTask`，可搭配額外 async API 使用
- Discord 上傳大小限制檢查，可依 Discord 方案設定
- 超過大小限制時，可啟用 **自動 FFmpeg 分段上傳**：影片以 `-c copy` 無損分割為多個可獨立播放的 MP4，依序分訊息上傳
- FFmpeg 不可用或分段失敗時，自動 fallback 為 zip 壓縮上傳；zip 也超過限制則回報錯誤
- 支援自訂 FFmpeg 執行檔路徑，支援 `StreamingAssets:/` 前綴（build-safe）或系統 PATH

## 相依套件

### 必要

- `jp.co.cyberagent.instant-replay`
- `jp.co.cyberagent.instant-replay.dependencies`

此 package 內建 editor 安裝檢查，Unity 載入後若發現缺少必要套件，會提示是否自動安裝。

### 選用

- `com.pedev.unity-discord-webhook`
  用於 Discord Webhook 上傳
- `com.cysharp.unitask`
  用於 async/await API

## 安裝方式

透過 **Unity Package Manager（UPM）** 以 Git URL 安裝：

1. 開啟 Unity Editor，進入 **Window > Package Manager**
2. 點擊左上角 **+** 按鈕，選擇 **Add package from git URL...**
3. 輸入以下 URL 後按 **Add**：

```text
https://github.com/qwe321qwe321qwe321/unity-media-collecting-solution.git
```

第一次載入 editor 時，若缺少必要或選用依賴，package 會主動跳出安裝提示，引導一鍵完成相依套件安裝。

### 已知警告：missing signature / unsigned package

安裝 `InstantReplay` 與其必要依賴後，在部分較新的 Unity 版本中，Console 可能會出現 `missing signature`、`no signature` 或 `unsigned package` 類型的警告。

這通常不是此 package 安裝失敗，而是因為目前相依來源包含：

- 透過 Git URL 安裝的 `jp.co.cyberagent.instant-replay`
- 透過 UnityNuGet / OpenUPM registry 解析的 `org.nuget.*` 套件

只要 package 能正常 resolve、編譯且在 Package Manager 中顯示為已安裝，這些警告通常可視為非阻塞的已知現象。

相關討論可參考：

- `UnityNuGet issue #636`: <https://github.com/bdovaz/UnityNuGet/issues/636>

## 快速開始

1. 在場景中放入一個掛有 `UnityReplayIntegration` 的物件。
2. 視需求設定錄影品質、熱鍵、輸出路徑與 Discord Webhook。
3. 若 `Start On Awake` 開啟，遊戲啟動後會自動開始背景錄影。
4. 執行時可透過熱鍵匯出影片或擷取截圖。

預設熱鍵：

- 匯出影片：`F9`
- 擷取截圖：`F10`

## Inspector 設定說明

### Recording

- `Quality Preset`
  使用預設畫質快速套用解析度、FPS、Bitrate
- `Recording Width` / `Recording Height`
  錄影解析度，僅在 `Custom` 時手動調整
- `Fps`
  錄影幀率
- `Recording Bitrate Kbps`
  錄影 bitrate（Kbps）
- `Max Memory Usage Mb`
  壓縮影格最大記憶體使用量
- `Max Number Of Raw Frame Buffers`
  Raw frame buffer 上限，數值越高越能平滑突發影格，但會消耗更多記憶體
- `Start On Awake`
  是否在啟動時自動開始錄影

### Hotkeys

- `Export Video Hotkey`
  匯出回放影片熱鍵
- `Capture Screenshot Hotkey`
  擷取截圖熱鍵

### Output

- `Output Path`
  輸出資料夾，空字串時使用 `Application.persistentDataPath`；建議使用絕對路徑以確保跨平台相容性

### Discord Webhook

- `Discord Webhook Enabled`
  是否啟用 Discord 上傳
- `Discord Webhook Url`
  Discord Webhook 位址（在 Discord 伺服器設定 → 整合 → Webhooks 建立）
- `Discord Channel Type`
  支援 `TextChannel` 與 `Forum`
- `Discord Forum Thread Title`
  Forum 模式下使用的 thread title，支援 `{TIME}`、`{SIZE}`、`{LENGTH}` 佔位符
- `Discord Content`
  訊息內容，支援 `{TIME}`、`{SIZE}`、`{LENGTH}` 佔位符
- `Discord Upload Limit Mb`
  單次上傳的大小上限（MB）；超過此限制時依 Auto Split Video 設定處理。設為 `0` 停用大小檢查。
- `Discord Auto Split Video`
  啟用時，超過大小限制的影片會透過 FFmpeg 自動分段（`-c copy` 無損切割），每段作為獨立訊息上傳。停用時跳過 FFmpeg，直接嘗試 zip 壓縮上傳
- `Discord Ffmpeg Path`
  FFmpeg 執行檔路徑，空白時使用系統 PATH。`StreamingAssets:/ffmpeg.exe` 會在執行時解析為正確的 StreamingAssets 路徑（build-safe）。專案資料夾內的路徑以相對路徑儲存（僅限 Editor）。僅在 Auto Split Video 啟用時生效

佔位符說明：

- `{TIME}` — 格式化為 `yyyy-MM-dd HH:mm:ss`
- `{SIZE}` — 檔案大小（例如 `12.34 MB`）
- `{LENGTH}` — 影片長度（例如 `1m10s`）

## 執行流程

- 系統啟動後建立背景錄影 session
- 匯出影片時會停止目前 session，輸出完成後自動重新開始錄影
- 截圖會在 `WaitForEndOfFrame` 後擷取，儲存成 `png`
- 若 Discord 上傳啟用，輸出完成後會自動呼叫 webhook
- **大小限制流程（影片）**：
  1. 檔案大小 ≤ 上傳限制：直接上傳
  2. 檔案大小 > 上傳限制，且 Auto Split Video 啟用：以 FFmpeg 分段後依序上傳各段；FFmpeg 失敗或不可用時 fallback 到 zip 上傳
  3. 檔案大小 > 上傳限制，且 Auto Split Video 停用：嘗試 zip 壓縮後上傳
  4. zip 也超過限制：回報錯誤

## 程式 API

主要入口為 `UnityReplayIntegration.Instance`。

常用 API：

- `StartRecording()`
- `StopRecording()`
- `PauseRecording()`
- `ResumeRecording()`
- `TriggerExportVideo(Action<string> onComplete = null)`
- `TriggerCaptureScreenshot(Action<string> onComplete = null)`

狀態查詢：

- `IsRecording`
- `IsPaused`

`onComplete` 會收到輸出檔案路徑；若失敗則為 `null`。

## 輸出檔名

- 影片：`replay_yyyyMMdd_HHmmss.mp4`
- 截圖：`screenshot_yyyyMMdd_HHmmss.png`

## 注意事項

- 此 package 採 Singleton 設計，場景中應只保留一個 `UnityReplayIntegration`
- 物件會在執行時 `DontDestroyOnLoad`
- 若尚未錄到有效內容，匯出影片可能沒有輸出檔案
- Discord 功能只有在有安裝 `com.pedev.unity-discord-webhook` 時才會生效
- async API 只有在有安裝 `com.cysharp.unitask` 時才可使用
- `InstantReplay` 與其 `org.nuget.*` 依賴在部分 Unity 版本中可能顯示 `missing signature` / `unsigned package` 警告；若套件能正常 resolve 與編譯，通常不影響使用
- FFmpeg 分段上傳需要 FFmpeg 已安裝並可從系統 PATH 存取，或在 Inspector 中指定執行檔路徑

## License

本 package 使用 MIT License，詳見 `LICENSE`。
