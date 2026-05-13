using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Utterheim.Services.Settings;
using Utterheim.Services.Speak;
using Utterheim.Services.Tts;

namespace Utterheim.ViewModels.Pages;

/// <summary>
/// View-model for the Speak page (main-013). Backs the multi-line textbox, the
/// voice picker, the Play / Stop / Save commands, and the status line. Per
/// ADR 0010 the bindable surface is generated from <c>[ObservableProperty]</c>
/// and <c>[RelayCommand]</c>; <c>[NotifyCanExecuteChangedFor]</c> recomputes
/// Play's CanExecute as the user types or picks a voice.
/// </summary>
public sealed partial class SpeakPageViewModel : ObservableObject
{
    private readonly SpeakService _speakService;
    private readonly VoiceCatalog _voiceCatalog;
    private readonly UserSettings _userSettings;
    private readonly ILogger<SpeakPageViewModel> _logger;

    /// <summary>The textbox content. Source of truth for both Play and Save.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _text = string.Empty;

    /// <summary>The voice picked in the dropdown.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private VoiceDescriptor? _selectedVoice;

    /// <summary>Voices shown in the picker. Refreshed on navigation and on VoicesChanged.</summary>
    public ObservableCollection<VoiceDescriptor> Voices { get; } = new();

    /// <summary>Status-line label, e.g. "idle", "synthesising — alba".</summary>
    [ObservableProperty]
    private string _statusLabel = "idle";

    public SpeakPageViewModel(
        SpeakService speakService,
        VoiceCatalog voiceCatalog,
        UserSettings userSettings,
        ILogger<SpeakPageViewModel> logger)
    {
        _speakService = speakService;
        _voiceCatalog = voiceCatalog;
        _userSettings = userSettings;
        _logger = logger;

        StatusLabel = FormatStatus(_speakService.CurrentStatus);
    }

    /// <summary>
    /// Refresh the picker from the catalog. Preserves the prior selection when
    /// the voice still exists, otherwise applies the resolution order
    /// (UserSettings.DefaultVoiceId → alphabetical-first).
    /// </summary>
    public async Task RefreshVoicesAsync(CancellationToken ct = default)
    {
        try
        {
            var fresh = await _voiceCatalog.ListAsync(ct).ConfigureAwait(true);
            var previousId = SelectedVoice?.Id;

            Voices.Clear();
            foreach (var v in fresh) Voices.Add(v);

            VoiceDescriptor? next = null;
            if (!string.IsNullOrEmpty(previousId))
            {
                foreach (var v in Voices)
                    if (string.Equals(v.Id, previousId, StringComparison.Ordinal)) { next = v; break; }
            }
            if (next is null && !string.IsNullOrEmpty(_userSettings.DefaultVoiceId))
            {
                foreach (var v in Voices)
                    if (string.Equals(v.Id, _userSettings.DefaultVoiceId, StringComparison.Ordinal)) { next = v; break; }
            }
            if (next is null && Voices.Count > 0)
            {
                // Alphabetical-first fallback.
                VoiceDescriptor? first = null;
                foreach (var v in Voices)
                {
                    if (first is null || string.Compare(v.Id, first.Id, StringComparison.Ordinal) < 0)
                        first = v;
                }
                next = first;
            }
            SelectedVoice = next;
        }
        catch (OperationCanceledException) { /* navigated away mid-refresh */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RefreshVoicesAsync failed.");
        }
    }

    /// <summary>Apply a SpeakStatus snapshot to the bindable label.</summary>
    public void ApplyStatus(SpeakStatus status)
    {
        StatusLabel = FormatStatus(status);
    }

    private static string FormatStatus(SpeakStatus status) => status.Kind switch
    {
        SpeakStatusKind.Idle => "idle",
        SpeakStatusKind.Synthesising => string.IsNullOrEmpty(status.VoiceId)
            ? "synthesising"
            : $"synthesising — {status.VoiceId}",
        SpeakStatusKind.Playing => string.IsNullOrEmpty(status.VoiceId)
            ? "playing"
            : $"playing — {status.VoiceId}",
        SpeakStatusKind.Stopped => "stopped",
        _ => status.Kind.ToString().ToLowerInvariant(),
    };

    private bool CanPlay() => !string.IsNullOrWhiteSpace(Text) && SelectedVoice is not null;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        if (SelectedVoice is null) return;
        try
        {
            _speakService.Enqueue(Text, SelectedVoice.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speak page Play failed.");
        }
    }

    [RelayCommand]
    private void Stop()
    {
        try
        {
            _speakService.StopAndDrain();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speak page Stop failed.");
        }
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(Text) && SelectedVoice is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct)
    {
        if (SelectedVoice is null) return;
        var voiceId = SelectedVoice.Id;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dialog = new SaveFileDialog
        {
            Filter = "WAV audio (*.wav)|*.wav",
            DefaultExt = ".wav",
            FileName = $"utterheim-{voiceId}-{stamp}.wav",
            AddExtension = true,
        };
        var owner = Application.Current?.MainWindow;
        var ok = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        if (ok != true) return;

        var target = dialog.FileName;
        try
        {
            await _speakService.RenderToFileAsync(Text, voiceId, target, ct).ConfigureAwait(true);
            StatusLabel = $"Saved to {Path.GetFileName(target)}";
        }
        catch (OperationCanceledException)
        {
            // Page-level cancel — no status change beyond returning to idle.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save to {Path} failed.", target);
            StatusLabel = "Save failed — see logs";
        }
    }
}
