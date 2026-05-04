using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Voices;
using Wpf.Ui.Controls;

namespace Mockingbird.ViewModels.Dialogs;

/// <summary>
/// Backs <c>DeleteVoiceDialog</c> (main-026) — the Fluent
/// <see cref="ContentDialog"/> shown when the user clicks Delete on a cloned
/// voice row. Holds the voice id + display name for the modal copy, drives
/// the in-progress state during the delete call, and surfaces inline errors
/// when <see cref="VoiceLibraryService.DeleteAsync"/> fails (file lock,
/// permission, etc.).
///
/// Per the task spec the dialog **stays open on failure** so the user can
/// retry or cancel; only success dismisses it. The row vanishes via
/// <c>VoiceCatalog.VoicesChanged</c> — this VM has no knowledge of the
/// list-side refresh.
/// </summary>
public sealed partial class DeleteVoiceDialogViewModel : ObservableObject
{
    private readonly string _voiceId;
    private readonly VoiceLibraryService _voiceLibrary;
    private readonly ILogger _logger;

    private ContentDialog? _dialog;

    public DeleteVoiceDialogViewModel(
        string voiceId,
        string displayName,
        VoiceLibraryService voiceLibrary,
        ILogger logger)
    {
        _voiceId = voiceId;
        VoiceName = displayName;
        _voiceLibrary = voiceLibrary;
        _logger = logger;
    }

    /// <summary>Display name of the voice — bound to the modal's "name card".</summary>
    public string VoiceName { get; }

    /// <summary>True while <see cref="VoiceLibraryService.DeleteAsync"/> is in flight — drives the inline ProgressRing + button-disable.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isDeleting;

    /// <summary>Inline error message shown beneath the buttons if delete fails. Empty/null = no error.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Hand the VM a reference to the dialog so it can dismiss on success.
    /// Avoids passing the dialog through the ctor (which would force the page
    /// VM to construct it before the dialog instance exists).
    /// </summary>
    public void AttachDialog(ContentDialog dialog) => _dialog = dialog;

    /// <summary>
    /// Confirm the delete. Re-entry is guarded by <see cref="IsDeleting"/>;
    /// on success the dialog hides itself; on IO failure the dialog stays
    /// open with an inline error (the user can retry or cancel).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (IsDeleting) return;
        IsDeleting = true;
        ErrorMessage = null;
        try
        {
            await _voiceLibrary.DeleteAsync(_voiceId, CancellationToken.None).ConfigureAwait(true);
            // Success — dismiss the dialog; the row vanishes via VoicesChanged.
            _dialog?.Hide(ContentDialogResult.Primary);
        }
        catch (System.IO.IOException ex)
        {
            _logger.LogWarning(ex, "Delete of voice '{VoiceId}' failed (IO).", _voiceId);
            ErrorMessage = "File is locked. Stop playback and try again.";
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Delete of voice '{VoiceId}' failed (permission).", _voiceId);
            ErrorMessage = "Permission denied. Close any tool that has the voice folder open and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete of voice '{VoiceId}' failed.", _voiceId);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsDeleting = false;
        }
    }

    private bool CanDelete() => !IsDeleting;

    /// <summary>Close the dialog with no action.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (IsDeleting) return;
        _dialog?.Hide(ContentDialogResult.None);
    }

    private bool CanCancel() => !IsDeleting;
}
