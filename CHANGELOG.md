# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
