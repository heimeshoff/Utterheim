// Adapted from WhisperHeim/src/WhisperHeim/Services/Audio/AudioDeviceInfo.cs @ 911bff0
namespace Utterheim.Services.Audio;

/// <summary>
/// Represents an available audio input/output device — id, friendly name, channel count.
/// Shared by <see cref="IAudioCaptureService"/> (mic) and
/// <see cref="IHighQualityLoopbackService"/> (system render endpoint loopback).
/// </summary>
public sealed record AudioDeviceInfo(int DeviceIndex, string Name, int Channels);
