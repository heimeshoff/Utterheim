using System.Reflection;

namespace Mockingbird.Services;

/// <summary>
/// Static helper exposing app-wide identity values that have no per-instance
/// state — currently just the user-facing version string. Extracted in
/// main-030 so <see cref="Mockingbird.Views.Controls.BrandHeroControl"/> can
/// read the version without per-page plumbing; main-032 migrates the inline
/// lookup in <c>AboutPageViewModel</c> over to this helper as well.
///
/// <para>
/// Intentionally not a DI service: there is no state, no test seam, and no
/// alternate implementation. If a future test needs to mock it, promote then;
/// not now (per main-030 Notes).
/// </para>
/// </summary>
public static class AppInfo
{
    /// <summary>
    /// User-facing version string (no <c>v</c> prefix). Prefers
    /// <see cref="AssemblyInformationalVersionAttribute"/> with any <c>+sha</c>
    /// suffix stripped, falls back to <see cref="AssemblyName.Version"/>
    /// (3-part), and finally to <c>"unknown"</c> for unconfigured dev builds.
    /// Same algorithm <see cref="ViewModels.Pages.AboutPageViewModel"/> shipped
    /// inline in main-017; main-030 lifts it here so the brand hero can render
    /// the same value without a VM dependency.
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var asm = Assembly.GetExecutingAssembly();

        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Strip the +sha suffix MSBuild appends when SourceLink is enabled —
            // a bare semver reads cleaner on the brand hero / About page.
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        var ver = asm.GetName().Version;
        if (ver is not null) return ver.ToString(3);

        return "unknown";
    }
}
