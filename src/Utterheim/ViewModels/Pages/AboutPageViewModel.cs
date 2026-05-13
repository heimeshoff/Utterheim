using CommunityToolkit.Mvvm.ComponentModel;
using Utterheim.Services;

namespace Utterheim.ViewModels.Pages;

/// <summary>
/// View-model for the About page. As of main-032 the page is a pure identity
/// surface (hero + Marco's profile / contact card + Ko-fi / GitHub support
/// card + credits) mirroring WhisperHeim's About — the engine-status panel,
/// Restart Engine button, and View-logs link that main-017 originally placed
/// here have moved to Settings → Diagnostics, surfaced through
/// <see cref="EngineStatusCardViewModel"/> composed on
/// <see cref="SettingsPageViewModel.EngineStatus"/>.
///
/// <para>
/// What remains: a single <see cref="Version"/> property sourced from
/// <see cref="AppInfo.Version"/> (the same helper <c>BrandHeroControl</c>
/// reads from). The page no longer needs <see cref="SidecarHost"/>,
/// <see cref="DataPathService"/>, <c>Attach</c>, <c>Detach</c>, or any
/// command — the hero composes the version inline; the support links resolve
/// to <see cref="AppInfo.KofiUrl"/> + <see cref="AppInfo.GithubUrl"/> from
/// the page code-behind.
/// </para>
/// </summary>
public sealed partial class AboutPageViewModel : ObservableObject
{
    /// <summary>Tagline signed off in the styleguide (2026-05-01).</summary>
    public const string Tagline = "Local voices for Claude Code";

    /// <summary>Credits line — minimal per main-017 Q4, retained for the page footer.</summary>
    public const string CreditsLine = "Synthesis powered by pocket-tts (Kyutai Labs).";

    /// <summary>
    /// Bare version string (no <c>v</c> prefix). Sourced from
    /// <see cref="AppInfo.Version"/> — single source of truth across the app.
    /// </summary>
    public string Version { get; } = AppInfo.Version;
}
