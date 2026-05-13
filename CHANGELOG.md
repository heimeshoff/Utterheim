# Changelog

All notable WhisperHeim provenance + significant cross-cutting changes are recorded here.
Per ADR 0006, every WhisperHeim-derived source file carries a
`// Adapted from WhisperHeim/<path> @ <commit>` header and gets a one-line entry below.

## Unreleased

### Added
- `Services/Audio/IAudioCaptureService.cs` — adapted from WhisperHeim
  `Services/Audio/IAudioCaptureService.cs` @ 911bff0 (main-025).
- `Services/Audio/AudioCaptureService.cs` — adapted from WhisperHeim
  `Services/Audio/AudioCaptureService.cs` @ 911bff0 (main-025). Same 16 kHz mono
  16-bit PCM mic capture path; pocket-tts resamples internally.
- `Services/Audio/IHighQualityLoopbackService.cs` — adapted from WhisperHeim
  `Services/Audio/IHighQualityLoopbackService.cs` @ 911bff0 (main-025).
  `SaveAsVoice` removed: persistence routes through `VoiceLibraryService` per ADR 0005.
- `Services/Audio/HighQualityLoopbackService.cs` — adapted from WhisperHeim
  `Services/Audio/HighQualityLoopbackService.cs` @ 911bff0 (main-025).
  `SaveAsVoice` deleted; `Initialize(DataPathService)` and `CustomVoicesDir` static
  removed (utterheim only uses the temp WAV path; persistent location belongs to
  the voice library).
- `Services/Audio/AudioDeviceInfo.cs` — adapted from WhisperHeim
  `Services/Audio/AudioDeviceInfo.cs` @ 911bff0 (main-025).
- `Services/Audio/AudioDeviceResolver.cs` — adapted from WhisperHeim
  `Services/Audio/AudioDeviceResolver.cs` @ 911bff0 (main-025).
- `Services/Audio/AudioRingBuffer.cs` — adapted from WhisperHeim
  `Services/Audio/AudioRingBuffer.cs` @ 911bff0 (main-025).

### Notes
- WhisperHeim's `LoopbackCaptureService` (the 16 kHz-downsampled variant for ASR)
  is intentionally **not copied** — voice cloning needs native quality, so only
  `HighQualityLoopbackService` is brought across.
