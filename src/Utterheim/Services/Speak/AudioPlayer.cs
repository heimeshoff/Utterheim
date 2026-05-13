using System.IO;
using Microsoft.Extensions.Logging;
using Utterheim.Services.Settings;
using Utterheim.Services.Tts;
using NAudio.Wave;

namespace Utterheim.Services.Speak;

/// <summary>
/// Thin NAudio wrapper that streams an <see cref="ITtsEngine"/>'s chunked PCM
/// output to the user's default output device. Stays alive across requests
/// (one player instance) but creates a new <see cref="WaveOutEvent"/> per
/// utterance to keep the lifecycle simple.
///
/// Why <see cref="WaveOutEvent"/> not <see cref="WasapiOut"/>: WaveOutEvent is
/// the lowest-friction NAudio output for a streaming WAV pipeline; in main-011
/// when pocket-tts's exact sample rate is wired, we may switch to WasapiOut
/// for tighter latency.
///
/// Per main-016, the active output device is read from
/// <see cref="UserSettings.OutputDeviceId"/> at <see cref="WaveOutEvent"/>
/// construction time — once per utterance. A device change therefore takes
/// effect on the next speak request; current playback continues on the
/// previous device. <c>UserSettings</c> is optional in the constructor so
/// existing tests / direct construction paths keep compiling.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly ILogger<AudioPlayer> _logger;
    private readonly UserSettings? _userSettings;
    private WaveOutEvent? _output;
    private BufferedWaveProvider? _provider;
    private bool _disposed;

    public AudioPlayer(ILogger<AudioPlayer> logger, UserSettings? userSettings = null)
    {
        _logger = logger;
        _userSettings = userSettings;
    }

    /// <summary>True while audio is actively playing (or buffered to play).</summary>
    public bool IsPlaying =>
        _output is { PlaybackState: PlaybackState.Playing } ||
        (_provider?.BufferedDuration.TotalMilliseconds > 0);

    /// <summary>
    /// Pump <paramref name="chunks"/> into the device. Returns when the stream completes
    /// naturally OR cancellation fires; throws OperationCanceledException on cancel.
    /// </summary>
    public async Task PlayAsync(
        AudioFormat format,
        IAsyncEnumerable<byte[]> chunks,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        StopInternal();

        var waveFormat = new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
        _provider = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(30),
            DiscardOnBufferOverflow = false,
        };
        // Per main-016: read the active output device id at construction time
        // (one read per utterance). null → -1 ("system default" in NAudio).
        var deviceNumber = _userSettings?.OutputDeviceId ?? -1;
        _output = new WaveOutEvent { DeviceNumber = deviceNumber, DesiredLatency = 100 };
        _output.Init(_provider);
        _output.Play();
        _logger.LogInformation("AudioPlayer started ({Hz} Hz, {Ch} ch, {Bits}-bit, device {Device})",
            format.SampleRate, format.Channels, format.BitsPerSample, deviceNumber);

        try
        {
            // Permanent first-chunk-latency instrumentation per main-023:
            // log a single line the moment the first PCM sample is handed to the
            // output device. Anything that greps for the literal substring
            // "FIRST-AUDIO-DISPATCH" can compute end-to-end latency from the
            // request's send timestamp.
            var firstSampleLogged = false;
            await foreach (var chunk in chunks.WithCancellation(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                _provider.AddSamples(chunk, 0, chunk.Length);

                if (!firstSampleLogged)
                {
                    firstSampleLogged = true;
                    _logger.LogInformation(
                        "FIRST-AUDIO-DISPATCH first PCM chunk handed to output device ({Bytes} bytes, {Hz} Hz)",
                        chunk.Length, format.SampleRate);
                }

                // Light backpressure if we're buffering more than ~1 s ahead.
                while (_provider.BufferedDuration.TotalSeconds > 1.0 && !ct.IsCancellationRequested)
                {
                    await Task.Delay(20, ct).ConfigureAwait(false);
                }
            }

            // Drain: wait until everything queued has been played.
            while (_provider.BufferedDuration.TotalMilliseconds > 0 && !ct.IsCancellationRequested)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // Whether we completed, were cancelled, or threw — release the device.
            StopInternal();
        }
    }

    /// <summary>Halt playback immediately and discard any buffered samples.</summary>
    public void Stop()
    {
        StopInternal();
    }

    private void StopInternal()
    {
        try { _output?.Stop(); } catch { /* swallow; we're tearing down */ }
        try { _output?.Dispose(); } catch { /* same */ }
        _output = null;

        try { _provider?.ClearBuffer(); } catch { /* same */ }
        _provider = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopInternal();
    }
}
