using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Utterheim.Services.Speak;
using Utterheim.Services.Tts;
using Utterheim.Services.Voices;
using Utterheim.ViewModels.Dialogs;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Utterheim.ViewModels.Pages;

/// <summary>
/// View-model for the Voices page (main-014). Backs the two-section voice
/// library list (Built-in + Cloned) with per-row Preview buttons.
///
/// Per ADR 0014, Preview routes through <see cref="SpeakService.Enqueue"/> —
/// the same in-process seam the Speak page Play button and <c>POST /speak</c>
/// use. The queue is the single arbiter of "what plays next" (ADR 0007), so
/// previews FIFO behind any active Claude utterance and the stop hotkey
/// drains them just like any other request (ADR 0004).
///
/// Loading / error states bind to <see cref="SidecarHost.StateChanged"/>:
/// <c>starting</c> / <c>restarting</c> show a centred progress ring,
/// <c>failed</c> shows an inline error banner, <c>running</c> shows the
/// list. The catalog refreshes on <c>OnNavigatedTo()</c> and on
/// <see cref="VoiceCatalog.VoicesChanged"/>; main-015 will fire that event
/// when cloned voices are saved/deleted.
/// </summary>
public sealed partial class VoicesPageViewModel : ObservableObject
{
    private readonly SpeakService _speakService;
    private readonly VoiceCatalog _voiceCatalog;
    private readonly VoiceLibraryService _voiceLibrary;
    private readonly IContentDialogService _dialogService;
    private readonly ILogger<VoicesPageViewModel> _logger;

    private bool _refreshing;
    private string? _activeRequestVoiceId;

    /// <summary>
    /// Cloning sub-flow VM (main-025). Composed here per the task spec — the
    /// "Clone a new voice" panel is a third row beneath the list, not a
    /// separate page.
    /// </summary>
    public VoiceCloningViewModel Cloning { get; }

    /// <summary>Built-in voices (those shipped with pocket-tts). Always populated when engine is running.</summary>
    public ObservableCollection<VoiceRowViewModel> BuiltInVoices { get; } = new();

    /// <summary>Cloned voices (created by the user via main-015). Empty in v1.</summary>
    public ObservableCollection<VoiceRowViewModel> ClonedVoices { get; } = new();

    /// <summary>Total catalog count — drives the "8 voices" sub-header.</summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>Coarse engine state. Drives the loading / error / running visibility flags.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private SidecarState _engineState = SidecarState.NotStarted;

    /// <summary>True when the page should render the "Voice engine is starting…" placeholder.</summary>
    public bool IsLoading => EngineState is SidecarState.Starting or SidecarState.Restarting or SidecarState.NotStarted;

    /// <summary>True when the page should render the inline error banner.</summary>
    public bool IsFailed => EngineState == SidecarState.Failed;

    /// <summary>True when the list should render normally (engine reports voices).</summary>
    public bool IsRunning => EngineState == SidecarState.Running;

    /// <summary>True when the cloned section is empty — drives the inline empty-state message.</summary>
    public bool ClonedSectionIsEmpty => ClonedVoices.Count == 0;

    public VoicesPageViewModel(
        SpeakService speakService,
        VoiceCatalog voiceCatalog,
        VoiceLibraryService voiceLibrary,
        IContentDialogService dialogService,
        VoiceCloningViewModel cloning,
        ILogger<VoicesPageViewModel> logger)
    {
        _speakService = speakService;
        _voiceCatalog = voiceCatalog;
        _voiceLibrary = voiceLibrary;
        _dialogService = dialogService;
        _logger = logger;
        Cloning = cloning;
    }

    /// <summary>
    /// Refresh the two voice sections from <see cref="VoiceCatalog.ListAsync"/>.
    /// Partitions on <see cref="VoiceDescriptor.IsBuiltIn"/>. Guarded against
    /// re-entrant fire (e.g. <c>VoicesChanged</c> arriving mid-<c>OnNavigatedTo</c>).
    /// </summary>
    public async Task RefreshVoicesAsync(CancellationToken ct = default)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var voices = await _voiceCatalog.ListAsync(ct).ConfigureAwait(true);

            BuiltInVoices.Clear();
            ClonedVoices.Clear();

            foreach (var v in voices)
            {
                var row = new VoiceRowViewModel(
                    v,
                    EnqueuePreview,
                    deleteAction: v.IsBuiltIn ? null : RequestDelete);
                row.CanPreview = EngineState == SidecarState.Running;
                if (v.IsBuiltIn) BuiltInVoices.Add(row);
                else ClonedVoices.Add(row);
            }

            TotalCount = BuiltInVoices.Count + ClonedVoices.Count;
            OnPropertyChanged(nameof(ClonedSectionIsEmpty));

            // Reapply the active-request indicator if a preview was already in flight
            // when the page mounted (e.g. user navigated mid-playback).
            ApplyActiveVoice(_activeRequestVoiceId);
        }
        catch (OperationCanceledException) { /* navigated away mid-refresh */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RefreshVoicesAsync failed.");
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Apply a <see cref="SpeakStatus"/> snapshot — flips per-row IsActiveRequest indicators.</summary>
    public void ApplyStatus(SpeakStatus status)
    {
        // Synthesising / Playing → that row's voice id is the "active" one.
        // Idle / Stopped → no row is active.
        var active = status.Kind is SpeakStatusKind.Synthesising or SpeakStatusKind.Playing
            ? status.VoiceId
            : null;
        _activeRequestVoiceId = active;
        ApplyActiveVoice(active);
    }

    /// <summary>Apply a sidecar state change — flips the loading / failed / running flags + per-row CanPreview.</summary>
    public void ApplyEngineState(SidecarState state)
    {
        EngineState = state;
        var canPreview = state == SidecarState.Running;
        foreach (var row in BuiltInVoices) row.CanPreview = canPreview;
        foreach (var row in ClonedVoices) row.CanPreview = canPreview;
    }

    private void ApplyActiveVoice(string? voiceId)
    {
        foreach (var row in BuiltInVoices)
            row.IsActiveRequest = voiceId is not null && string.Equals(row.VoiceId, voiceId, StringComparison.Ordinal);
        foreach (var row in ClonedVoices)
            row.IsActiveRequest = voiceId is not null && string.Equals(row.VoiceId, voiceId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Per ADR 0014, route the preview through the speak queue. The phrase
    /// is hard-coded for v1; if i18n lands later it moves to a resource string.
    /// </summary>
    private void EnqueuePreview(VoiceRowViewModel row)
    {
        try
        {
            var phrase = $"Hello, this is {row.DisplayName}.";
            _speakService.Enqueue(phrase, row.VoiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voices page Preview failed for {VoiceId}.", row.VoiceId);
        }
    }

    /// <summary>
    /// Open the delete-confirmation dialog for a cloned voice (main-026).
    /// On Primary (Delete) the dialog VM calls
    /// <see cref="VoiceLibraryService.DeleteAsync"/>; on success the dialog
    /// closes and the row vanishes via <see cref="VoiceCatalog.VoicesChanged"/>.
    /// On IO failure the dialog stays open with an inline error message
    /// (per task spec) — the user can retry or cancel.
    /// </summary>
    private async void RequestDelete(VoiceRowViewModel row)
    {
        try
        {
            var dialogVm = new DeleteVoiceDialogViewModel(row.VoiceId, row.DisplayName, _voiceLibrary, _logger);
            var dialog = new Views.Dialogs.DeleteVoiceDialog(_dialogService.GetDialogHost())
            {
                DataContext = dialogVm,
            };
            dialogVm.AttachDialog(dialog);

            await _dialogService.ShowAsync(dialog, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open delete confirmation dialog for {VoiceId}.", row.VoiceId);
        }
    }
}

/// <summary>
/// One row in the voices list. Trim view-model — display name, meta line,
/// preview command, and the active-request indicator. Lives here rather than
/// in its own file per the worker tip in main-014's spec.
///
/// main-026 adds the Delete affordance for cloned voices. Built-in rows
/// expose <see cref="IsBuiltIn"/> = true and the page template binds the
/// Delete column's visibility to <c>!IsBuiltIn</c>; <see cref="DeleteCommand"/>
/// is wired regardless but never invoked on a built-in row from the UI.
/// </summary>
public sealed partial class VoiceRowViewModel : ObservableObject
{
    private readonly Action<VoiceRowViewModel> _previewAction;
    private readonly Action<VoiceRowViewModel>? _deleteAction;

    public VoiceRowViewModel(
        VoiceDescriptor descriptor,
        Action<VoiceRowViewModel> previewAction,
        Action<VoiceRowViewModel>? deleteAction = null)
    {
        _previewAction = previewAction;
        _deleteAction = deleteAction;
        VoiceId = descriptor.Id;
        DisplayName = string.IsNullOrEmpty(descriptor.Name) ? descriptor.Id : descriptor.Name;
        IsBuiltIn = descriptor.IsBuiltIn;
        // Built-ins show just the engine name in the meta line. Cloned voices
        // will get a richer "engine • source • createdAt" form once main-015
        // lands and feeds the catalog with cloned-voice metadata. For now the
        // catalog only returns built-ins so a single-field meta line is correct.
        Meta = descriptor.Engine;
        Language = descriptor.Language;
        LanguageLabel = ToLanguageLabel(descriptor.Language);
    }

    /// <summary>
    /// Short two-letter label for the language chip rendered on each row
    /// (main-041, ADR 0023). Matches the ISO-639-1 codes Marco picked in the
    /// task brief: <c>EN</c> for English, <c>DE</c> for German. Centralised
    /// here so the XAML template binds a single <c>TextBlock</c> rather than
    /// chaining a converter.
    /// </summary>
    private static string ToLanguageLabel(VoiceLanguage language) => language switch
    {
        VoiceLanguage.English => "EN",
        VoiceLanguage.German => "DE",
        _ => language.ToString().ToUpperInvariant(),
    };

    /// <summary>Opaque voice identifier the engine resolves (e.g. "alba").</summary>
    public string VoiceId { get; }

    /// <summary>Human-friendly name shown in the row header. Falls back to the id.</summary>
    public string DisplayName { get; }

    /// <summary>Engine + (cloned only) source + createdAt; built-ins show only the engine name.</summary>
    public string Meta { get; }

    /// <summary>
    /// Language this voice speaks (main-041 / ADR 0023). Built-ins source it
    /// from <c>PocketTtsEngine.BuiltInVoices</c>; cloned voices source it from
    /// <see cref="ClonedVoiceIndexEntry.Language"/> via
    /// <see cref="VoiceCatalog"/>. Exposed as a strongly-typed property so a
    /// future filter / per-language section can bind without a string parse.
    /// </summary>
    public VoiceLanguage Language { get; }

    /// <summary>
    /// Short label for the language chip rendered on each row
    /// (main-041 / ADR 0023). Two letters — <c>"EN"</c> or <c>"DE"</c>
    /// in v1 — matching ISO 639-1. The XAML row template binds a chip
    /// <c>TextBlock</c> directly to this string.
    /// </summary>
    public string LanguageLabel { get; }

    /// <summary>True for engine built-ins (alba, marius, …); false for cloned voices.</summary>
    public bool IsBuiltIn { get; }

    /// <summary>Inverse of <see cref="IsBuiltIn"/>. Drives the Delete column's visibility on cloned-row templates.</summary>
    public bool IsCloned => !IsBuiltIn;

    /// <summary>True when this row's preview is the active speak request — drives the speaker indicator.</summary>
    [ObservableProperty]
    private bool _isActiveRequest;

    /// <summary>False while the engine is not running — disables the Preview button.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    private bool _canPreview = true;

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private void Preview() => _previewAction(this);

    /// <summary>
    /// Open the delete confirmation dialog for this row (main-026). No-op for
    /// built-in rows (the Delete column is collapsed via <see cref="IsCloned"/>
    /// binding, but the command is wired uniformly so the row template can be
    /// shared if needed).
    /// </summary>
    [RelayCommand]
    private void Delete()
    {
        if (IsBuiltIn) return;
        _deleteAction?.Invoke(this);
    }
}
