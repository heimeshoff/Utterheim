// Adapted from WhisperHeim/src/WhisperHeim/Services/Audio/HighQualityLoopbackService.cs @ 911bff0
//
// Utterheim-specific divergences vs WhisperHeim:
//   - SaveAsVoice(string) removed: persistence routes through VoiceLibraryService
//     (ADR 0005), not this capture service.
//   - Initialize(DataPathService) + the static CustomVoicesDir field removed:
//     utterheim only uses the temp WAV path; the persistent location belongs
//     to the voice library, not the capture service.
using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Utterheim.Services.Audio;

/// <summary>
/// Captures system audio (loopback) via WASAPI at native quality (typically 48kHz 32-bit float stereo)
/// and writes to a temporary WAV. Designed for voice cloning reference audio where quality matters —
/// the temp WAV path is then handed to <see cref="Voices.VoiceLibraryService.AddAsync"/> for
/// persistence per ADR 0005.
/// </summary>
public sealed class HighQualityLoopbackService : IHighQualityLoopbackService
{
    private WasapiLoopbackCapture? _capture;
    private WaveFileWriter? _waveFileWriter;
    private string? _tempWavFilePath;
    private bool _disposed;
    private volatile bool _isCapturing;
    private DateTime _captureStartTime;
    private readonly object _lock = new();

    /// <inheritdoc />
    public event EventHandler<HighQualityAudioEventArgs>? AudioDataAvailable;

    /// <inheritdoc />
    public event EventHandler? CaptureStarted;

    /// <inheritdoc />
    public event EventHandler<CaptureStoppedEventArgs>? CaptureStopped;

    /// <inheritdoc />
    public bool IsCapturing => _isCapturing;

    /// <inheritdoc />
    public TimeSpan Duration =>
        _isCapturing ? DateTime.UtcNow - _captureStartTime : TimeSpan.Zero;

    /// <inheritdoc />
    public string? TempWavFilePath => _tempWavFilePath;

    /// <inheritdoc />
    public IReadOnlyList<AudioDeviceInfo> GetAvailableDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            for (int i = 0; i < endpoints.Count; i++)
            {
                var endpoint = endpoints[i];
                devices.Add(new AudioDeviceInfo(
                    i,
                    endpoint.FriendlyName,
                    endpoint.AudioClient.MixFormat.Channels));
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("[HQLoopback] Failed to enumerate devices: {0}", ex.Message);
        }

        return devices;
    }

    /// <inheritdoc />
    public void StartCapture(int deviceIndex = -1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isCapturing)
            return;

        // Verify that a render device is available
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "No audio output device is available for loopback capture.", ex);
        }

        // Create temp WAV file
        var tempDir = Path.Combine(Path.GetTempPath(), "Utterheim");
        Directory.CreateDirectory(tempDir);
        _tempWavFilePath = Path.Combine(
            tempDir,
            $"voice_loopback_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.wav");

        _capture = new WasapiLoopbackCapture();

        // The WasapiLoopbackCapture.WaveFormat gives us the native system mixer format
        var nativeFormat = _capture.WaveFormat;
        Trace.TraceInformation(
            "[HQLoopback] Native format: {0}Hz, {1}-bit, {2}ch, {3}",
            nativeFormat.SampleRate, nativeFormat.BitsPerSample,
            nativeFormat.Channels, nativeFormat.Encoding);

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        // Write at the native format -- no resampling
        _waveFileWriter = new WaveFileWriter(_tempWavFilePath, nativeFormat);

        try
        {
            _capture.StartRecording();
            _isCapturing = true;
            _captureStartTime = DateTime.UtcNow;
            CaptureStarted?.Invoke(this, EventArgs.Empty);
            Trace.TraceInformation("[HQLoopback] Capture started. Output: {0}", _tempWavFilePath);
        }
        catch (Exception)
        {
            CleanupCapture();
            throw;
        }
    }

    /// <inheritdoc />
    public void StopCapture()
    {
        if (!_isCapturing)
            return;

        _isCapturing = false;

        try
        {
            _capture?.StopRecording();
        }
        catch (Exception)
        {
            CleanupCapture();
        }
    }

    /// <summary>
    /// Called on the WASAPI capture thread when audio data is available.
    /// Writes raw audio at native quality and computes RMS for level metering.
    /// </summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            int bytesRecorded = e.BytesRecorded;
            if (bytesRecorded == 0 || _capture is null)
                return;

            // Write raw bytes directly to WAV -- no conversion, no resampling
            lock (_lock)
            {
                _waveFileWriter?.Write(e.Buffer, 0, bytesRecorded);
            }

            // Compute RMS from float samples for level metering
            float rmsLevel = ComputeRms(e.Buffer, bytesRecorded, _capture.WaveFormat);

            // Convert to float samples for the event (mono downmix for simplicity)
            var floatSamples = ConvertToFloatMono(e.Buffer, bytesRecorded, _capture.WaveFormat);

            AudioDataAvailable?.Invoke(this, new HighQualityAudioEventArgs(floatSamples, rmsLevel));
        }
        catch (Exception ex)
        {
            Trace.TraceError("[HQLoopback] OnDataAvailable error: {0}", ex);
        }
    }

    /// <summary>
    /// Called when WASAPI stops recording.
    /// </summary>
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            _isCapturing = false;

            bool deviceDisconnected = e.Exception is not null;

            CleanupCapture();

            CaptureStopped?.Invoke(this, new CaptureStoppedEventArgs(
                wasDeviceDisconnected: deviceDisconnected,
                exception: e.Exception));
        }
        catch (Exception ex)
        {
            Trace.TraceError("[HQLoopback] OnRecordingStopped error: {0}", ex);
        }
    }

    /// <summary>
    /// Computes the RMS level from raw audio bytes.
    /// </summary>
    private static float ComputeRms(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat ||
            (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
        {
            int sampleCount = bytesRecorded / 4;
            if (sampleCount == 0) return 0f;

            double sumSquares = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                float sample = BitConverter.ToSingle(buffer, i * 4);
                sumSquares += sample * sample;
            }
            return (float)Math.Sqrt(sumSquares / sampleCount);
        }

        return 0f;
    }

    /// <summary>
    /// Converts raw audio to float mono samples (for event consumers).
    /// </summary>
    private static float[] ConvertToFloatMono(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.IeeeFloat &&
            !(format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
        {
            return [];
        }

        int sampleCount = bytesRecorded / 4;
        var floats = new float[sampleCount];
        Buffer.BlockCopy(buffer, 0, floats, 0, bytesRecorded);

        if (format.Channels <= 1)
            return floats;

        // Downmix to mono
        int channels = format.Channels;
        int frameCount = sampleCount / channels;
        var mono = new float[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            float sum = 0f;
            int offset = i * channels;
            for (int ch = 0; ch < channels; ch++)
                sum += floats[offset + ch];
            mono[i] = sum / channels;
        }

        return mono;
    }

    private void CleanupCapture()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }

        lock (_lock)
        {
            if (_waveFileWriter is not null)
            {
                try
                {
                    _waveFileWriter.Dispose();
                }
                catch (Exception)
                {
                    // Ignore disposal errors for file writer.
                }
                _waveFileWriter = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopCapture();
        CleanupCapture();
    }
}
