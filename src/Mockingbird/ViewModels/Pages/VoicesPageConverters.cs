using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Mockingbird.ViewModels.Pages;

/// <summary>
/// Maps a nullable / possibly-empty string to <see cref="Visibility"/> —
/// non-empty → Visible, null/empty/whitespace → Collapsed. Used by the
/// cloning panel's inline error + status surfaces (main-025).
/// </summary>
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        return string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Two-way bridge between a <see cref="CloningSource"/> property and a
/// <see cref="System.Windows.Controls.RadioButton"/>'s <c>IsChecked</c> bool.
/// ConverterParameter is the source name ("Microphone" / "SystemAudio") this
/// radio represents — the converter compares against it.
/// </summary>
public sealed class CloningSourceToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CloningSource src) return false;
        var name = parameter as string;
        if (string.IsNullOrEmpty(name)) return false;
        return string.Equals(src.ToString(), name, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // RadioButton fires ConvertBack with both true (the one being checked)
        // and false (the one being unchecked). Only the "true" hop should
        // update the source property; ignore "false" so we don't briefly clear
        // the selection.
        if (value is not bool b || !b) return Binding.DoNothing;
        var name = parameter as string ?? "Microphone";
        return Enum.TryParse<CloningSource>(name, ignoreCase: true, out var parsed)
            ? parsed
            : CloningSource.Microphone;
    }
}
