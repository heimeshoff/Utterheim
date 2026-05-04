using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Mockingbird.Services.Tts;

namespace Mockingbird.ViewModels.Pages;

/// <summary>
/// Maps the engine status pip — driven by <see cref="SidecarState"/> + the
/// healthy flag — to a fill <see cref="Brush"/> for the small 10x10 ellipse
/// next to the state label on the About page (main-017).
///
/// Colours:
/// <list type="bullet">
///   <item><description><c>Running</c> + healthy → green</description></item>
///   <item><description><c>Starting</c> / <c>Restarting</c> → amber</description></item>
///   <item><description><c>Failed</c> → red</description></item>
///   <item><description><c>NotStarted</c> / <c>Stopping</c> → neutral grey</description></item>
/// </list>
///
/// Used as a multi-binding (state + healthy) to distinguish "running but
/// unhealthy" from "running and serving requests".
/// </summary>
public sealed class EngineStateToPipBrushConverter : IMultiValueConverter
{
    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // #10B981
    private static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // #F59E0B
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x24)); // #FFE81224
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)); // #9CA3AF

    static EngineStateToPipBrushConverter()
    {
        GreenBrush.Freeze();
        AmberBrush.Freeze();
        RedBrush.Freeze();
        NeutralBrush.Freeze();
    }

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not SidecarState state)
            return NeutralBrush;

        var healthy = values.Length > 1 && values[1] is bool b && b;

        return state switch
        {
            SidecarState.Running => healthy ? GreenBrush : AmberBrush,
            SidecarState.Starting => AmberBrush,
            SidecarState.Restarting => AmberBrush,
            SidecarState.Failed => RedBrush,
            SidecarState.NotStarted => NeutralBrush,
            SidecarState.Stopping => NeutralBrush,
            _ => NeutralBrush,
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
