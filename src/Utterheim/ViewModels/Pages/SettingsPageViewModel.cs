using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Utterheim.Services.Audio;
using Utterheim.Services.Http;
using Utterheim.Services.Settings;
using Utterheim.Services.Speak;
using Utterheim.Services.Tts;

namespace Utterheim.ViewModels.Pages;

/// <summary>
/// View-model for the Settings page (main-016 / main-029 / main-031). Sections:
///
/// - <b>Audio</b>: Default voice (writes <see cref="UserSettings.DefaultVoiceId"/>)
///   and Output device (writes <see cref="UserSettings.OutputDeviceId"/>;
///   <see cref="AudioPlayer"/> reads on the next utterance).
/// - <b>App</b>: Start minimised (writes <see cref="UserSettings.StartMinimised"/>)
///   and Launch at startup (writes <c>HKCU\…\Run\Utterheim</c> via
///   <see cref="StartupRegistration"/> — registry IS the source of truth, so the
///   toggle re-reads on every <c>OnNavigatedTo</c>).
/// - <b>Diagnostics</b>: HTTP port and Stop hotkey are read-only in v1. Data path
///   is editable via Browse… + Reset (main-031) — a folder-picker dialog with
///   writability validation; persists through <see cref="DataPathService.SetDataPath"/>
///   (pointer-swap, no migration of existing voices), refreshes the displayed
///   path live via <see cref="DataPathService.DataPathChanged"/>, and surfaces a
///   restart-required MessageBox on a successful swap.
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

    /// <summary>
    /// Engine-status card (state pip / port / healthy / last error / Restart
    /// Engine / View logs) surfaced at the end of Settings → Diagnostics.
    /// Composed sub-VM mirroring <c>VoicesPageViewModel.Cloning</c>; the page's
    /// <c>OnNavigatedTo</c> / <c>OnNavigatedFrom</c> drive its <see cref="EngineStatusCardViewModel.Attach"/>
    /// / <see cref="EngineStatusCardViewModel.Detach"/> lifecycle so the
    /// <see cref="SidecarHost.StateChanged"/> subscription cannot leak.
    /// Relocated from About in main-032.
    /// </summary>
    public EngineStatusCardViewModel EngineStatus { get; }

    public SettingsPageViewModel(
        UserSettings userSettings,
        VoiceCatalog voiceCatalog,
        StartupRegistration startupRegistration,
        DataPathService dataPathService,
        SpeakServer speakServer,
        EngineStatusCardViewModel engineStatus,
        ILogger<SettingsPageViewModel> logger)
    {
        _userSettings = userSettings;
        _voiceCatalog = voiceCatalog;
        _startupRegistration = startupRegistration;
        _dataPathService = dataPathService;
        _speakServer = speakServer;
        _logger = logger;

        EngineStatus = engineStatus;

        // Diagnostics labels are stable for the lifetime of the host (HTTP port +
        // stop hotkey are read-only in v1). Data path is a live mirror — see
        // OnDataPathChanged for the runtime-swap path.
        HttpEndpoint = $"{_speakServer.Host}:{_speakServer.Port}";
        StopHotkeyLabel = "Double-tap Right Ctrl";
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
    private string _stopHotkeyLabel = "Double-tap Right Ctrl";

    /// <summary>
    /// Active data path from <c>bootstrap.json</c> per ADR 0005. Editable via
    /// the Browse… and Reset commands (main-031) — re-read live from
    /// <see cref="DataPathService.DataPathChanged"/>.
    /// </summary>
    [ObservableProperty]
    private string _dataPath = string.Empty;

    /// <summary>
    /// Refresh every dynamic field from its source of truth — the catalog, the
    /// registry, and the persisted settings file. Called by <c>OnNavigatedTo</c>
    /// so the registry-backed Launch-at-startup toggle stays accurate after
    /// external mutations between page visits. Also wires the
    /// <see cref="DataPathService.DataPathChanged"/> subscription so
    /// <see cref="DataPath"/> stays in sync if any other surface ever calls
    /// <see cref="DataPathService.SetDataPath"/>.
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
            DataPath = _dataPathService.DataPath;
        }
        finally
        {
            _suspendPersist = false;
        }
    }

    /// <summary>
    /// Subscribe to <see cref="DataPathService.DataPathChanged"/> and forward
    /// the lifecycle to the composed <see cref="EngineStatus"/> card so its
    /// <see cref="SidecarHost.StateChanged"/> subscription is bounded by the
    /// page's lifetime. Idempotent — both subscriptions detach-then-attach so
    /// duplicate calls are safe. Called by the page's <c>OnNavigatedTo</c>.
    /// </summary>
    public void Attach()
    {
        _dataPathService.DataPathChanged -= OnDataPathChanged;
        _dataPathService.DataPathChanged += OnDataPathChanged;
        EngineStatus.Attach();
    }

    /// <summary>Detach the <see cref="DataPathService.DataPathChanged"/> handler and the engine-status subscription.</summary>
    public void Detach()
    {
        _dataPathService.DataPathChanged -= OnDataPathChanged;
        EngineStatus.Detach();
    }

    private void OnDataPathChanged(object? sender, string newResolvedPath)
    {
        // The event may be raised from any thread; SetDataPath is invoked from
        // the UI dispatcher in v1 but the contract is "thread-agnostic". Hop
        // back to the dispatcher so the bound TextBlock updates safely.
        var app = Application.Current;
        if (app?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => DataPath = newResolvedPath));
            return;
        }
        DataPath = newResolvedPath;
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

    /// <summary>
    /// Open <see cref="Microsoft.Win32.OpenFolderDialog"/> seeded at the current
    /// data path. On a writable selection persist via
    /// <see cref="DataPathService.SetDataPath"/> and surface a restart-required
    /// info dialog; on an unwritable selection surface a warning and leave
    /// bootstrap.json unchanged. Mirrors WhisperHeim's <c>BrowseDataPath_Click</c>.
    /// </summary>
    [RelayCommand]
    private void BrowseDataPath()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select data folder for Utterheim",
                InitialDirectory = _dataPathService.DataPath,
            };

            if (dialog.ShowDialog() != true) return;

            var newPath = dialog.FolderName;
            if (!DataPathService.ValidatePath(newPath))
            {
                MessageBox.Show(
                    $"The selected folder is not writable:\n\n{newPath}\n\nPlease choose a different folder.",
                    "Invalid folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_dataPathService.SetDataPath(newPath))
            {
                MessageBox.Show(
                    "Data folder changed. Please restart Utterheim for the change to take full effect.",
                    "Restart required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: BrowseDataPath failed.");
        }
    }

    /// <summary>
    /// Clear the data-path override and fall back to <see cref="DataPathService.RoamingRoot"/>.
    /// No confirmation, no MessageBox — the on-page display refresh is the visible
    /// signal (matches WhisperHeim).
    /// </summary>
    [RelayCommand]
    private void ResetDataPath()
    {
        try
        {
            _dataPathService.SetDataPath(null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: ResetDataPath failed.");
        }
    }
}
