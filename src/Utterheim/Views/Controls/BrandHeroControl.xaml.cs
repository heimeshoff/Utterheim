using System.Windows;
using System.Windows.Controls;
using Utterheim.Services;

namespace Utterheim.Views.Controls;

/// <summary>
/// Brand hero block used at the top of identity-bearing pages (Speak, About).
/// Composition matches WhisperHeim's hero exactly: 88x88 logo badge
/// (brand-blue border + tinted background) on top, "Utterheim" 40pt
/// ExtraBold beneath, version tag in <c>BrandDeepMutedBrush</c> next to the
/// name, optional tagline at the bottom.
///
/// <para>
/// Logo and app name are app-wide constants baked into the XAML; version is
/// read from <see cref="AppInfo.Version"/> on construction (no per-instance
/// version property — there's only one running app); tagline is the only
/// per-instance variable, exposed via the <see cref="Tagline"/>
/// <see cref="DependencyProperty"/>. When <c>Tagline</c> is null or empty,
/// the tagline <see cref="TextBlock"/> collapses.
/// </para>
///
/// <para>
/// Plain WPF <see cref="UserControl"/> — not a wpfui control. Reused by
/// main-030 (Speak page, no tagline) and main-032 (About page, with the
/// signed-off "Local voices for Claude Code" tagline).
/// </para>
/// </summary>
public partial class BrandHeroControl : UserControl
{
    /// <summary>
    /// Optional tagline rendered under the app name. Null or empty collapses
    /// the tagline <see cref="TextBlock"/>.
    /// </summary>
    public static readonly DependencyProperty TaglineProperty = DependencyProperty.Register(
        nameof(Tagline),
        typeof(string),
        typeof(BrandHeroControl),
        new PropertyMetadata(null, OnTaglineChanged));

    /// <inheritdoc cref="TaglineProperty" />
    public string? Tagline
    {
        get => (string?)GetValue(TaglineProperty);
        set => SetValue(TaglineProperty, value);
    }

    /// <summary>
    /// Backing flag the XAML's tagline TextBlock binds for visibility — true
    /// iff <see cref="Tagline"/> is non-null and non-whitespace. Exposed as a
    /// dependency property so XAML's element-name binding picks up updates.
    /// </summary>
    private static readonly DependencyPropertyKey HasTaglinePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasTagline),
        typeof(bool),
        typeof(BrandHeroControl),
        new PropertyMetadata(false));

    /// <inheritdoc cref="HasTaglinePropertyKey" />
    public static readonly DependencyProperty HasTaglineProperty = HasTaglinePropertyKey.DependencyProperty;

    /// <inheritdoc cref="HasTaglinePropertyKey" />
    public bool HasTagline => (bool)GetValue(HasTaglineProperty);

    public BrandHeroControl()
    {
        InitializeComponent();
        // Single-app version — read once on construction. AppInfo.Version is
        // a static lazily-computed string so this is just a field read.
        VersionText.Text = $"v{AppInfo.Version}";
    }

    private static void OnTaglineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BrandHeroControl)d;
        var tagline = e.NewValue as string;
        control.SetValue(HasTaglinePropertyKey, !string.IsNullOrWhiteSpace(tagline));
    }
}
