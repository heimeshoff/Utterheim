using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Audio;
using Mockingbird.Services.Http;
using Mockingbird.Services.Settings;
using Mockingbird.Services.Speak;
using Mockingbird.Services.Tts;

namespace Mockingbird.ViewModels.Pages;

/// <summary>
/// View-model for the Settings page (main-016). Three sections:
///
/// - <b>Audio</b>: Default voice (writes <see cref="UserSettings.DefaultVoiceId"/>)
///   and Output device (writes <see cref="UserSettings.OutputDeviceId"/>;
///   <see cref="AudioPlayer"/> reads on the next utterance).
/// - <b>App</b>: Start minimised (writes <see cref="UserSettings.StartMinimised"/>)
///   and Launch at startup (writes <c>HKCU\…\Run\Mockingbird</c> via
///   <see cref="StartupRegistration"/> — registry IS the source of truth, so the
///   toggle re-reads on every <c>OnNavigatedTo</c>).
/// - <b>Diagnostics</b> (read-only in v1): HTTP port, Stop hotkey, Data path
///   (with an "Open in Explorer" button).
///
/// Per ADR 0010 the bindable surface is generated from <c>[ObservableProperty]</c>
/// and <c>[RelayCommand]</c>.
/// </summary>
public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly UserSettings _userSettings;
    private readonly VoiceCatalog _voiceCatalog;
    private readonly StartupRegistration _startupRegistration;
    private readonly DataPathService _dataPathService;
    private readonly SpeakServer _speakServer;
    private readonly ILogger<SettingsPageViewModel> _logger;

    /// <summary>Sentinel "system default" entry shown at the top of the output-device picker.</summary>
    public static AudioDeviceInfo SystemDefaultDevice { get; } = new(-1, "System default", 0);

    private bool _suspendPersist;

    public SettingsPageViewModel(
        UserSettings userSettings,
        VoiceCatalog voiceCatalog,
        StartupRegistration startupRegistration,
        DataPathService dataPathService,
        SpeakServer speakServer,
        ILogger<SettingsPageViewModel> logger)
    {
        _userSettings = userSettings;
        _voiceCatalog = voiceCatalog;
        _startupRegistration = startupRegistration;
        _dataPathService = dataPathService;
        _speakServer = speakServer;
        _logger = logger;

        // Diagnostics labels are stable for the lifetime of the host (v1 makes
        // them read-only). Compute them once at construction.
        HttpEndpoint = $"{_speakServer.Host}:{_speakServer.Port}";
        StopHotkeyLabel = "Double-tap Left Ctrl";
        DataPath = _dataPathService.DataPath;
    }

    // ─── Audio ──────────────────────────────────────────────────────────────

    /// <summary>Voices the user can pick as the default. Refreshed on navigation.</summary>
    public ObservableCollection<VoiceDescriptor> Voices { get; } = new();

    /// <summary>The currently-selected default voice. <c>null</c> = "no preference".</summary>
    [ObservableProperty]
    private VoiceDescriptor? _selectedDefaultVoice;

    /// <summary>WaveOut output devices, with a "System default" entry at index 0.</summary>
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();

    /// <summary>Selected output device — never null after <see cref="LoadAsync"/>.</summary>
    [ObservableProperty]
    private AudioDeviceInfo? _selectedOutputDevice;

    // ─── App ────────────────────────────────────────────────────────────────

    /// <summary>Backed by <see cref="UserSettings.StartMinimised"/>.</summary>
    [ObservableProperty]
    private bool _startMinimised;

    /// <summary>Backed by the registry — re-read on every <see cref="LoadAsync"/>.</summary>
    [ObservableProperty]
    private bool _launchAtStartup;

    // ─── Diagnostics (read-only in v1) ──────────────────────────────────────

    /// <summary>e.g. <c>127.0.0.1:7223</c>.</summary>
    [ObservableProperty]
    private string _httpEndpoint = string.Empty;

    /// <summary>Static label per ADR 0006 (read-only display, no rebinding UI in v1).</summary>
    [ObservableProperty]
    private string _stopHotkeyLabel = "Double-tap Left Ctrl";

    /// <summary>Active data path from <c>bootstrap.json</c> per ADR 0005.</summary>
    [ObservableProperty]
    private string _dataPath = string.Empty;

    /// <summary>
    /// Refresh every dynamic field from its source of truth — the catalog, the
    /// registry, and the persisted settings file. Called by <c>OnNavigatedTo</c>
    /// so the registry-backed Launch-at-startup toggle stays accurate after
    /// external mutations between page visits.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        _suspendPersist = true;
        try
        {
            await RefreshVoicesAsync(ct).ConfigureAwait(true);
            RefreshOutputDevices();
            StartMinimised = _userSettings.StartMinimised;
            LaunchAtStartup = _startupRegistration.IsRegistered;
        }
        finally
        {
            _suspendPersist = false;
        }
    }

    private async Task RefreshVoicesAsync(CancellationToken ct)
    {
        try
        {
            var fresh = await _voiceCatalog.ListAsync(ct).ConfigureAwait(true);

            Voices.Clear();
            foreach (var v in fresh) Voices.Add(v);

            // Pre-select the persisted default voice when present and still in
            // the catalog; otherwise leave selection null ("no preference").
            VoiceDescriptor? next = null;
            var defaultId = _userSettings.DefaultVoiceId;
            if (!string.IsNullOrEmpty(defaultId))
            {
                foreach (var v in Voices)
                    if (string.Equals(v.Id, defaultId, StringComparison.Ordinal)) { next = v; break; }
            }
            SelectedDefaultVoice = next;
        }
        catch (OperationCanceledException) { /* navigated away mid-refresh */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: RefreshVoicesAsync failed.");
        }
    }

    private void RefreshOutputDevices()
    {
        try
        {
            OutputDevices.Clear();
            // "System default" always sits at the top of the list and maps to
            // WaveOutEvent.DeviceNumber = -1 in AudioPlayer.
            OutputDevices.Add(SystemDefaultDevice);
            foreach (var d in AudioDeviceResolver.EnumerateOutputDevices())
                OutputDevices.Add(d);

            // Resolve current selection from UserSettings.OutputDeviceId.
            var savedId = _userSettings.OutputDeviceId;
            AudioDeviceInfo? next = null;
            if (savedId is null)
            {
                next = SystemDefaultDevice;
            }
            else
            {
                foreach (var d in OutputDevices)
                    if (d.DeviceIndex == savedId.Value) { next = d; break; }
                // Saved device no longer present — fall back to system default.
                next ??= SystemDefaultDevice;
            }
            SelectedOutputDevice = next;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: RefreshOutputDevices failed.");
        }
    }

    // ─── Persistence wiring (auto-generated partial methods) ────────────────

    partial void OnSelectedDefaultVoiceChanged(VoiceDescriptor? value)
    {
        if (_suspendPersist) return;
        try
        {
            _userSettings.DefaultVoiceId = value?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: failed to persist DefaultVoiceId.");
        }
    }

    partial void OnSelectedOutputDeviceChanged(AudioDeviceInfo? value)
    {
        if (_suspendPersist) return;
        try
        {
            // SystemDefaultDevice (DeviceIndex = -1) → null ("no preference").
            _userSettings.OutputDeviceId = value is null || value.DeviceIndex < 0
                ? null
                : value.DeviceIndex;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: failed to persist OutputDeviceId.");
        }
    }

    partial void OnStartMinimisedChanged(bool value)
    {
        if (_suspendPersist) return;
        try
        {
            _userSettings.StartMinimised = value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: failed to persist StartMinimised.");
        }
    }

    partial void OnLaunchAtStartupChanged(bool value)
    {
        if (_suspendPersist) return;
        try
        {
            // Registry IS the source of truth — write/delete on every change so an
            // external uninstaller dropping the Run entry stays reflected on the
            // next page load (we re-read on OnNavigatedTo).
            if (value) _startupRegistration.Register();
            else _startupRegistration.Unregister();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: failed to toggle LaunchAtStartup to {Value}.", value);
        }
    }

    /// <summary>Open the active data path in Explorer (read-only diagnostic action).</summary>
    [RelayCommand]
    private void OpenDataPath()
    {
        try
        {
            var path = DataPath;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                _logger.LogWarning("Settings: data path does not exist on disk: {Path}", path);
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: OpenDataPath failed.");
        }
    }
}
