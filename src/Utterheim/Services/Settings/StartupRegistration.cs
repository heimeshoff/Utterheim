using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Utterheim.Services.Settings;

/// <summary>
/// Tiny helper around the
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> registry key
/// that controls "Launch at startup" for utterheim (main-016).
///
/// Per the task spec, the registry is the **source of truth** for the
/// "Launch at startup" toggle — we do not duplicate the state in
/// <c>settings.json</c> because external uninstaller / cleanup tools that
/// drop the Run entry would otherwise leave the JSON value desynchronised.
/// The Settings page reads <see cref="IsRegistered"/> on every navigation
/// so the toggle reflects whatever the registry currently says.
/// </summary>
public sealed class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Registry value name written under <c>HKCU\…\Run</c>. Matches the
    /// product name so the entry is identifiable in <c>regedit</c> and
    /// Task Manager's Startup tab.
    /// </summary>
    public const string ValueName = "Utterheim";

    private readonly ILogger<StartupRegistration> _logger;

    public StartupRegistration(ILogger<StartupRegistration> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// True when the <c>Utterheim</c> value exists under
    /// <c>HKCU\…\Run</c>. Reads the registry every call — the registry is
    /// the source of truth, and external tools may have mutated it since
    /// the last visit.
    /// </summary>
    public bool IsRegistered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                if (key is null) return false;
                return key.GetValue(ValueName) is not null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read HKCU\\{Path}\\{Name} — assuming not registered.", RunKeyPath, ValueName);
                return false;
            }
        }
    }

    /// <summary>
    /// Resolved path to the current process's executable, used as the
    /// registry value's command. Surfaced for diagnostics / tests.
    /// </summary>
    public static string CurrentExecutablePath
    {
        get
        {
            // Process.MainModule.FileName returns the host .exe path (utterheim.exe),
            // not the .dll, which is exactly what Windows needs to spawn at logon.
            try
            {
                var path = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(path)) return path;
            }
            catch
            {
                // Fall through to the AppContext fallback.
            }
            // Fallback: AppContext.BaseDirectory + assembly name. Less reliable for
            // ClickOnce / single-file deployments but covers normal dev runs.
            return Path.Combine(AppContext.BaseDirectory, "utterheim.exe");
        }
    }

    /// <summary>
    /// Write <see cref="ValueName"/> = quoted current executable path under
    /// <c>HKCU\…\Run</c>. Idempotent — overwrites any previous value.
    /// Returns true on success, false on failure (logged).
    /// </summary>
    public bool Register()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException($"Could not open HKCU\\{RunKeyPath} for write.");
            // Quote the path so spaces in "C:\Program Files\…" don't break the
            // command parser at logon.
            var command = $"\"{CurrentExecutablePath}\"";
            key.SetValue(ValueName, command, RegistryValueKind.String);
            _logger.LogInformation("Registered launch-at-startup: HKCU\\{Path}\\{Name} = {Command}", RunKeyPath, ValueName, command);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register launch-at-startup under HKCU\\{Path}\\{Name}.", RunKeyPath, ValueName);
            return false;
        }
    }

    /// <summary>
    /// Remove <see cref="ValueName"/> from <c>HKCU\…\Run</c>. No-op when the
    /// value is already absent. Returns true on success (or already-absent),
    /// false on failure (logged).
    /// </summary>
    public bool Unregister()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return true; // No Run key at all — nothing to remove.
            if (key.GetValue(ValueName) is null) return true;
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            _logger.LogInformation("Unregistered launch-at-startup: HKCU\\{Path}\\{Name}.", RunKeyPath, ValueName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unregister launch-at-startup under HKCU\\{Path}\\{Name}.", RunKeyPath, ValueName);
            return false;
        }
    }
}
