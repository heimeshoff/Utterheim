using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Utterheim.Services.Audio;
using Utterheim.Services.Voices;

namespace Utterheim.ViewModels.Pages;

/// <summary>
/// Source toggle on the cloning panel — drives which capture service is used
/// and which device-list / tip is shown.
/// </summary>
public enum CloningSource
{
    /// <summary>Microphone via <see cref="IAudioCaptureService"/>.</summary>
    Microphone,
    /// <summary>System render endpoint via <see cref="IHighQualityLoopbackService"/>.</summary>
    SystemAudio,
}

/// <summary>
/// Child view-model for the "Clone a new voice" sub-section on the Voices page
/// (main-025). Composed into <see cref="VoicesPageViewModel"/> per the task spec —
/// not a standalone page.
///
/// Hosts the recording state machine (idle → capturing → captured) and the Save
/// flow (validate → render WAV → POST /export-voice → VoiceLibraryService.AddAsync).
/// All capture-thread events are marshalled to the WPF dispatcher via
/// <see cref="DispatcherInvoke"/> mirroring the EngineStatusViewModel pattern.
/// </summary>
public sealed partial class VoiceCloningViewModel : ObservableObject
{
    /// <summary>Minimum sample length before Save enables.</summary>
    public const int MinDurationSeconds = 5;
    /// <summary>Soft cap — we surface a "you have plenty" hint at this point.</summary>
    public const int SoftCapSeconds = 30;
    /// <summary>Hard cap — capture auto-stops at this point.</summary>
    public const int HardCapSeconds = 60;

    /// <summary>Below this peak RMS (across the whole buffer) the Save flow short-circuits.</summary>
    private const float MinPeakRms = 0.01f;

    /// <summary>Reserved built-in ids (case-insensitive). Mirrors VoiceLibraryService.</summary>
    private static readonly HashSet<string> ReservedBuiltInIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "alba", "marius", "javert", "jean", "fantine", "cosette", "eponine", "azelma",
    };

    private readonly IAudioCaptureService _micCapture;
    private readonly IHighQualityLoopbackService _loopbackCapture;
    private readonly VoiceLibraryService _voiceLibrary;
    private readonly VoiceCloningClient _cloningClient;
    private readonly ILogger<VoiceCloningViewModel> _logger;

    // Mic-mode buffer — accumulated float samples at 16 kHz mono.
    private readonly List<float> _micBuffer = new();
    private readonly object _micBufferLock = new();
    // Loopback-mode peak RMS tracking (samples are written by the service to disk).
    private float _peakLoopbackRms;
    // Mic-mode peak RMS — derived from _micBuffer at Save time.
    private float _peakMicRms;

    // Frequency we tick the duration / progress UI.
    private DispatcherTimer? _uiTimer;
    private DateTime _captureStartUtc;
    private bool _autoStopFiredForHardCap;
    private bool _softCapMessageShown;

    // Latched after a successful capture stop. Save() consumes these. The
    // bindable equivalent is <see cref="CaptureReady"/> — this private flag is
    // kept off the bindable surface to make stop-handler logic easier to read.
    private CloningSource _bufferSource;

    // ---- bindable properties -----------------------------------------------

    /// <summary>Mic devices, refreshed on demand from <see cref="IAudioCaptureService.GetAvailableDevices"/>.</summary>
    public ObservableCollection<AudioDeviceInfo> MicDevices { get; } = new();

    /// <summary>Loopback (render endpoint) devices.</summary>
    public ObservableCollection<AudioDeviceInfo> LoopbackDevices { get; } = new();

    /// <summary>Active source — flips between Mic and SystemAudio.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMicMode))]
    [NotifyPropertyChangedFor(nameof(IsSystemAudioMode))]
    [NotifyPropertyChangedFor(nameof(IsRainbowPassageVisible))]
    [NotifyPropertyChangedFor(nameof(TipText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private CloningSource _selectedSource = CloningSource.Microphone;

    public bool IsMicMode => SelectedSource == CloningSource.Microphone;
    public bool IsSystemAudioMode => SelectedSource == CloningSource.SystemAudio;

    /// <summary>Tip line under the device selector — varies by source.</summary>
    public string TipText => IsMicMode
        ? "Use a quiet environment. The recording captures everything the mic hears."
        : "Close other audio apps and play the voice you want to clone. Capture stops when you press Stop.";

    /// <summary>
    /// Picker options for the language selector (main-041 / ADR 0023). v1
    /// ships English + German; adding a third language is a one-entry extension
    /// here plus a <see cref="VoiceLanguage"/> enum extension. The XAML binds
    /// the picker's <c>ItemsSource</c> here.
    /// </summary>
    public IReadOnlyList<VoiceLanguage> Languages { get; } = new[]
    {
        VoiceLanguage.English,
        VoiceLanguage.German,
    };

    /// <summary>
    /// Target language for the next-saved clone (main-041 / ADR 0023). Default
    /// is <see cref="VoiceLanguage.English"/> per the product decision Marco
    /// recorded ("default will always be English"). The XAML picker is
    /// two-way bound; the value flows into
    /// <see cref="VoiceLibraryService.AddAsync"/>'s <c>language</c> parameter
    /// and the <c>X-Voice-Language</c> header on the <c>/export-voice</c>
    /// call so the sidecar's encoder uses the matching resident model.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRainbowPassageVisible))]
    private VoiceLanguage _language = VoiceLanguage.English;

    /// <summary>
    /// Drives the Rainbow Passage block's <c>Visibility</c> in XAML
    /// (main-041 task spec, point 2). The reading prompt is an English-only
    /// artefact (main-034); German + Mic hides it pending the German reading
    /// prompt landing in main-042. Visible only when (Mic mode) AND (English),
    /// matching the original main-034 gating expanded with the language
    /// dimension.
    /// </summary>
    public bool IsRainbowPassageVisible => IsMicMode && Language == VoiceLanguage.English;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedMicDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedLoopbackDevice;

    /// <summary>True while capture is active (mic or loopback).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isCapturing;

    /// <summary>Latest captured duration. Drives the mm:ss display + progress bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationLabel))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private TimeSpan _duration;

    /// <summary>Most-recent RMS from the capture event. Drives the level meter.</summary>
    [ObservableProperty]
    private float _rmsLevel;

    /// <summary>Voice name input — validated on every keystroke.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _voiceName = string.Empty;

    /// <summary>Inline validation error under the name input (null when valid).</summary>
    [ObservableProperty]
    private string? _voiceNameError;

    /// <summary>Status / error text above the Save button.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Secondary (lower-emphasis) status text — sidecar-failure detail goes here.</summary>
    [ObservableProperty]
    private string? _statusDetail;

    /// <summary>True after capture stops with a usable buffer (≥ 5 s).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _captureReady;

    public string DurationLabel
    {
        get
        {
            var d = Duration;
            return $"{(int)d.TotalMinutes:D2}:{d.Seconds:D2}";
        }
    }

    /// <summary>0..1 of the way through the minimum-duration window. Caps at 1.</summary>
    public double ProgressFraction
    {
        get
        {
            var f = Duration.TotalSeconds / MinDurationSeconds;
            return f > 1.0 ? 1.0 : f < 0 ? 0 : f;
        }
    }

    /// <summary>Percentage form for binding to a 0..100 ProgressBar.</summary>
    public double ProgressPercent => ProgressFraction * 100.0;

    /// <summary>Cancel is only visible while capturing.</summary>
    public bool CanCancel => IsCapturing;

    public VoiceCloningViewModel(
        IAudioCaptureService micCapture,
        IHighQualityLoopbackService loopbackCapture,
        VoiceLibraryService voiceLibrary,
        VoiceCloningClient cloningClient,
        ILogger<VoiceCloningViewModel> logger)
    {
        _micCapture = micCapture;
        _loopbackCapture = loopbackCapture;
        _voiceLibrary = voiceLibrary;
        _cloningClient = cloningClient;
        _logger = logger;

        _micCapture.AudioDataAvailable += OnMicAudioDataAvailable;
        _micCapture.CaptureStopped += OnMicCaptureStopped;

        _loopbackCapture.AudioDataAvailable += OnLoopbackAudioDataAvailable;
        _loopbackCapture.CaptureStopped += OnLoopbackCaptureStopped;
    }

    /// <summary>
    /// Refresh the mic + loopback device lists. Called by the page on
    /// <c>OnNavigatedTo</c>; cheap enough to call on every navigation.
    /// </summary>
    public void RefreshDevices()
    {
        try
        {
            MicDevices.Clear();
            foreach (var d in _micCapture.GetAvailableDevices()) MicDevices.Add(d);
            if (SelectedMicDevice is null && MicDevices.Count > 0)
                SelectedMicDevice = MicDevices[0];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate mic devices.");
        }

        try
        {
            LoopbackDevices.Clear();
            foreach (var d in _loopbackCapture.GetAvailableDevices()) LoopbackDevices.Add(d);
            if (SelectedLoopbackDevice is null && LoopbackDevices.Count > 0)
                SelectedLoopbackDevice = LoopbackDevices[0];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate loopback devices.");
        }
    }

    // ---- name validation ---------------------------------------------------

    partial void OnVoiceNameChanged(string value)
    {
        VoiceNameError = ValidateName(value);
    }

    private static string? ValidateName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Enter a name for the voice.";
        var trimmed = raw.Trim();
        if (trimmed.Length > 40)
            return "Name is too long (max 40).";
        var sanitised = SanitiseId(trimmed);
        if (string.IsNullOrEmpty(sanitised))
            return "Use ASCII letters or digits.";
        if (ReservedBuiltInIds.Contains(sanitised))
            return "That name is reserved. Try a different one.";
        return null;
    }

    /// <summary>
    /// Mirror of <see cref="VoiceLibraryService"/>'s id sanitiser so client-side
    /// validation matches the backend's rejection rules.
    /// </summary>
    private static string SanitiseId(string displayName)
    {
        var sb = new System.Text.StringBuilder(displayName.Length);
        bool lastWasDash = false;
        foreach (var raw in displayName.ToLowerInvariant())
        {
            char c = raw;
            bool isAllowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            bool isSeparator = char.IsWhiteSpace(c) || c == '-' || c == '_' || c == '.';
            if (isAllowed)
            {
                sb.Append(c);
                lastWasDash = false;
            }
            else if (isSeparator)
            {
                if (!lastWasDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
            }
        }
        return sb.ToString().Trim('-');
    }

    // ---- start / stop / cancel commands -----------------------------------

    private bool CanStart() => !IsCapturing;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (IsCapturing) return;

        StatusMessage = null;
        StatusDetail = null;
        CaptureReady = false;
        _autoStopFiredForHardCap = false;
        _softCapMessageShown = false;

        lock (_micBufferLock) _micBuffer.Clear();
        _peakLoopbackRms = 0f;
        _peakMicRms = 0f;

        try
        {
            if (SelectedSource == CloningSource.Microphone)
            {
                var idx = SelectedMicDevice?.DeviceIndex ?? -1;
                _micCapture.StartCapture(idx);
                _bufferSource = CloningSource.Microphone;
            }
            else
            {
                var idx = SelectedLoopbackDevice?.DeviceIndex ?? -1;
                _loopbackCapture.StartCapture(idx);
                _bufferSource = CloningSource.SystemAudio;
            }

            _captureStartUtc = DateTime.UtcNow;
            IsCapturing = true;
            Duration = TimeSpan.Zero;
            RmsLevel = 0f;

            _uiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33), // ~30 Hz
            };
            _uiTimer.Tick += OnUiTick;
            _uiTimer.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start capture failed.");
            StatusMessage = "Couldn't start capture. Check the device is available.";
            StatusDetail = ex.Message;
            CleanupAfterStop();
        }
    }

    private bool CanStop() => IsCapturing;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (!IsCapturing) return;
        try
        {
            if (_bufferSource == CloningSource.Microphone)
                _micCapture.StopCapture();
            else
                _loopbackCapture.StopCapture();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stop capture threw — capture may be partial.");
        }
        // The CaptureStopped event handler completes the state transition.
    }

    private bool CanCancelCmd() => IsCapturing;

    [RelayCommand(CanExecute = nameof(CanCancelCmd))]
    private void Cancel()
    {
        if (!IsCapturing) return;
        try
        {
            if (_bufferSource == CloningSource.Microphone)
                _micCapture.StopCapture();
            else
                _loopbackCapture.StopCapture();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cancel capture threw.");
        }
        // Mark as cancelled so the CaptureStopped handler discards regardless
        // of duration. Set BEFORE the event lands by using a flag the handler
        // checks.
        _cancellationRequested = true;
    }

    private bool _cancellationRequested;

    // ---- save command ------------------------------------------------------

    private bool CanSave() =>
        !IsCapturing
        && CaptureReady
        && Duration.TotalSeconds >= MinDurationSeconds
        && string.IsNullOrEmpty(VoiceNameError)
        && !string.IsNullOrWhiteSpace(VoiceName);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct)
    {
        if (!CaptureReady) return;
        var trimmed = VoiceName.Trim();

        // 1. Run name validation again client-side. Mirrors VoiceLibraryService.AddAsync.
        var nameErr = ValidateName(trimmed);
        if (nameErr is not null)
        {
            VoiceNameError = nameErr;
            return;
        }

        StatusMessage = null;
        StatusDetail = null;

        // 2. Render captured buffer to a temp WAV file.
        string? tempWav = null;
        byte[] wavBytes;
        try
        {
            (tempWav, wavBytes) = await Task.Run(() => RenderTempWav(), ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Render temp WAV failed.");
            StatusMessage = "Couldn't read the captured audio. Try recording again.";
            StatusDetail = ex.Message;
            return;
        }

        // 3. Quiet-buffer guard (peak RMS).
        var peak = _bufferSource == CloningSource.Microphone ? _peakMicRms : _peakLoopbackRms;
        if (peak < MinPeakRms)
        {
            StatusMessage = "Recording was very quiet. Try again closer to the mic.";
            StatusDetail = null;
            // Keep buffer + temp WAV so re-Save after re-record works.
            return;
        }

        StatusMessage = "Encoding voice profile...";
        StatusDetail = null;

        // 4. POST to /export-voice. Catch sidecar errors distinctly.
        // main-041: pass the chosen language so the sidecar swaps to the
        // matching resident TTSModel before encoding the audio prompt. Without
        // this hop a German clone would be encoded by whatever model is
        // currently swapped in (typically the default English one), which
        // pocket-tts permits but isn't what ADR 0023 promises.
        byte[] profileBytes;
        try
        {
            profileBytes = await _cloningClient
                .ExportVoiceAsync(wavBytes, voiceId: SanitiseId(trimmed), Language, ct)
                .ConfigureAwait(true);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(": ") && ex.Message.StartsWith("/export-voice returned 4"))
        {
            // Distinguish 4xx (client error — bad input) from 5xx in the error path.
            _logger.LogWarning(ex, "Sidecar rejected the cloning sample.");
            StatusMessage = "Pocket-tts couldn't read the recording. Check the sample isn't silent or corrupted.";
            StatusDetail = ex.Message;
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "/export-voice failed.");
            StatusMessage = "Voice profile encoding failed. See the engine status footer or Settings page.";
            StatusDetail = ex.Message;
            return;
        }

        // 5. Persist via VoiceLibraryService.
        ClonedVoiceMeta meta;
        try
        {
            var sampleSeconds = (int)Math.Round(Duration.TotalSeconds);
            var source = _bufferSource == CloningSource.Microphone ? VoiceSource.Mic : VoiceSource.Loopback;
            meta = await _voiceLibrary.AddAsync(
                displayName: trimmed,
                source: source,
                sampleSeconds: sampleSeconds,
                profileBytes: profileBytes,
                sampleBytes: wavBytes,
                ct: ct,
                language: Language).ConfigureAwait(true);
        }
        catch (VoiceValidationException ex)
        {
            VoiceNameError = ex.Message;
            StatusMessage = null;
            StatusDetail = null;
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VoiceLibraryService.AddAsync failed for '{Name}'.", trimmed);
            StatusMessage = "Couldn't save voice to disk. Check the data path is writable.";
            StatusDetail = ex.Message;
            return;
        }

        // 6. Success — clean up the temp WAV (best-effort), reset the form.
        try
        {
            if (tempWav is not null && File.Exists(tempWav)) File.Delete(tempWav);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Best-effort delete of temp WAV failed.");
        }

        StatusMessage = $"Voice '{meta.Name}' saved.";
        StatusDetail = null;
        VoiceName = string.Empty;
        VoiceNameError = null;
        Duration = TimeSpan.Zero;
        RmsLevel = 0f;
        CaptureReady = false;
        lock (_micBufferLock) _micBuffer.Clear();
    }

    // ---- capture event handlers (capture thread → dispatcher) -------------

    private void OnMicAudioDataAvailable(object? sender, AudioDataEventArgs e)
    {
        var samples = e.Samples;
        // Compute RMS on the capture thread — cheap. We also accumulate the
        // buffer here.
        float sumSquares = 0f;
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            var v = samples[i];
            sumSquares += v * v;
            var abs = v < 0 ? -v : v;
            if (abs > peak) peak = abs;
        }
        var rms = samples.Length > 0 ? (float)Math.Sqrt(sumSquares / samples.Length) : 0f;

        lock (_micBufferLock)
        {
            _micBuffer.AddRange(samples);
        }
        if (rms > _peakMicRms) _peakMicRms = rms;

        DispatcherInvoke(() =>
        {
            RmsLevel = rms;
        });
    }

    private void OnMicCaptureStopped(object? sender, CaptureStoppedEventArgs e)
    {
        DispatcherInvoke(() => HandleCaptureStopped(e, micPath: true));
    }

    private void OnLoopbackAudioDataAvailable(object? sender, HighQualityAudioEventArgs e)
    {
        var rms = e.RmsLevel;
        if (rms > _peakLoopbackRms) _peakLoopbackRms = rms;

        DispatcherInvoke(() =>
        {
            RmsLevel = rms;
        });
    }

    private void OnLoopbackCaptureStopped(object? sender, CaptureStoppedEventArgs e)
    {
        DispatcherInvoke(() => HandleCaptureStopped(e, micPath: false));
    }

    private void HandleCaptureStopped(CaptureStoppedEventArgs e, bool micPath)
    {
        _uiTimer?.Stop();
        _uiTimer = null;
        IsCapturing = false;

        if (e.WasDeviceDisconnected)
        {
            _logger.LogWarning(e.Exception, "Capture stopped due to device disconnection.");
            StatusMessage = "Capture device disconnected.";
            StatusDetail = e.Exception?.Message;
            DiscardBuffer(micPath);
            return;
        }

        // Compute final duration.
        var elapsed = DateTime.UtcNow - _captureStartUtc;
        Duration = elapsed;

        if (_cancellationRequested)
        {
            _cancellationRequested = false;
            StatusMessage = "Recording cancelled.";
            DiscardBuffer(micPath);
            return;
        }

        if (elapsed.TotalSeconds < MinDurationSeconds)
        {
            StatusMessage = $"Recording too short — at least {MinDurationSeconds} s needed.";
            DiscardBuffer(micPath);
            return;
        }

        // Buffer is usable.
        CaptureReady = true;
        if (_autoStopFiredForHardCap)
        {
            StatusMessage = $"Capture auto-stopped at {HardCapSeconds} s.";
            _autoStopFiredForHardCap = false;
        }
        else
        {
            StatusMessage = $"Captured {elapsed.TotalSeconds:0}s — name the voice and click Save.";
        }
    }

    private void DiscardBuffer(bool micPath)
    {
        CaptureReady = false;
        if (micPath)
        {
            lock (_micBufferLock) _micBuffer.Clear();
        }
        else
        {
            // Best-effort delete of the temp WAV the loopback service wrote.
            try
            {
                var p = _loopbackCapture.TempWavFilePath;
                if (p is not null && File.Exists(p)) File.Delete(p);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Best-effort delete of loopback temp WAV failed.");
            }
        }
    }

    private void CleanupAfterStop()
    {
        _uiTimer?.Stop();
        _uiTimer = null;
        IsCapturing = false;
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        if (!IsCapturing) return;

        var elapsed = DateTime.UtcNow - _captureStartUtc;
        Duration = elapsed;

        // Soft cap message.
        if (!_softCapMessageShown && elapsed.TotalSeconds >= SoftCapSeconds && elapsed.TotalSeconds < HardCapSeconds)
        {
            _softCapMessageShown = true;
            StatusMessage = "You have plenty of audio. You can stop now.";
        }

        // Hard cap auto-stop.
        if (elapsed.TotalSeconds >= HardCapSeconds)
        {
            _autoStopFiredForHardCap = true;
            try
            {
                if (_bufferSource == CloningSource.Microphone)
                    _micCapture.StopCapture();
                else
                    _loopbackCapture.StopCapture();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-stop at hard cap threw.");
            }
        }
    }

    // ---- WAV rendering -----------------------------------------------------

    /// <summary>
    /// Build the WAV bytes the sidecar will read. Mic mode renders from the in-memory
    /// float buffer at 16 kHz mono 16-bit PCM. Loopback mode hands back the file
    /// already written by <see cref="HighQualityLoopbackService"/>.
    /// Returns (path-to-temp-wav, bytes). Path is the file we may want to delete on success.
    /// </summary>
    private (string? tempPath, byte[] bytes) RenderTempWav()
    {
        if (_bufferSource == CloningSource.Microphone)
        {
            float[] samples;
            lock (_micBufferLock) samples = _micBuffer.ToArray();

            var tempDir = Path.Combine(Path.GetTempPath(), "Utterheim");
            Directory.CreateDirectory(tempDir);
            var path = Path.Combine(
                tempDir,
                $"voice_mic_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.wav");

            var format = new WaveFormat(AudioCaptureService.SampleRate, AudioCaptureService.BitsPerSample, AudioCaptureService.Channels);
            using (var w = new WaveFileWriter(path, format))
            {
                // float[] → 16-bit PCM
                var pcm = new byte[samples.Length * 2];
                for (int i = 0; i < samples.Length; i++)
                {
                    var v = samples[i];
                    if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                    short s = (short)Math.Round(v * 32767f);
                    pcm[i * 2] = (byte)(s & 0xff);
                    pcm[i * 2 + 1] = (byte)((s >> 8) & 0xff);
                }
                w.Write(pcm, 0, pcm.Length);
            }

            var bytes = File.ReadAllBytes(path);
            return (path, bytes);
        }
        else
        {
            var p = _loopbackCapture.TempWavFilePath
                ?? throw new InvalidOperationException("No loopback temp WAV path — capture failed?");
            if (!File.Exists(p))
                throw new FileNotFoundException("Loopback temp WAV is missing.", p);

            // HighQualityLoopbackService writes at the WASAPI native mixer format,
            // which on Windows is typically 32-bit IEEE float (format code 3) in a
            // WAVE_FORMAT_EXTENSIBLE wrapper. pocket-tts's audio reader rejects
            // that with "unknown format 3" — it only accepts PCM int16. Convert
            // in place so the upload and the persisted library sample are both PCM16.
            var converted = Path.ChangeExtension(p, ".pcm16.wav");
            using (var reader = new AudioFileReader(p))
            {
                var pcm16 = new SampleToWaveProvider16(reader);
                WaveFileWriter.CreateWaveFile(converted, pcm16);
            }
            try { File.Delete(p); } catch { /* best-effort; we still move the converted file in below */ }
            File.Move(converted, p);

            var bytes = File.ReadAllBytes(p);
            return (p, bytes);
        }
    }

    // ---- dispatcher hop ----------------------------------------------------

    private static void DispatcherInvoke(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess())
            action();
        else
            d.BeginInvoke(action);
    }
}
