using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Utterheim.Views.Dialogs;

/// <summary>
/// Fluent <see cref="ContentDialog"/> for the per-row Delete affordance on
/// cloned voices (main-026). Visual composition mirrors WhisperHeim's
/// <c>DeleteConfirmationDialog</c> (red-tinted icon block, "Delete voice?"
/// title, "This action cannot be undone." subtitle, voice-name card, Cancel
/// + red Delete buttons), but the host control is the wpfui-native
/// <c>ContentDialog</c> rather than a separate <c>Window</c> — main-020's
/// nav-shell pattern prefers in-window content dialogs.
/// </summary>
public partial class DeleteVoiceDialog : ContentDialog
{
    public DeleteVoiceDialog(ContentPresenter? dialogHost) : base(dialogHost)
    {
        InitializeComponent();
    }
}
