// Adapted from WhisperHeim/src/WhisperHeim/Services/Settings/DataPathService.cs @ 911bff0
// Mockingbird-flavoured: WhisperHeim-specific migration helpers stripped, paths
// remapped per ADR 0005 (voices folder + library.json, runtime/python under LocalAppData).
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Mockingbird.Services.Settings;

/// <summary>
/// Resolves the on-disk path layout per ADR 0005:
///
///   %APPDATA%\Mockingbird\         bootstrap, settings, logs (synced-friendly)
///   <dataPath>\voices\             voice profiles + library.json
///   %LOCALAPPDATA%\Mockingbird\    runtime\python, models, cache (machine-local)
///
/// dataPath defaults to %APPDATA%\Mockingbird\ and can be redirected via bootstrap.json
/// (e.g. to a OneDrive folder) — same pattern WhisperHeim uses.
/// </summary>
public sealed class DataPathService
{
    /// <summary>Roaming root: bootstrap pointer, settings, logs.</summary>
    public static readonly string RoamingRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mockingbird");

    /// <summary>Local-machine root: bundled Python, model weights, cache.</summary>
    public static readonly string LocalRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mockingbird");

    private static readonly string BootstrapPath = Path.Combine(RoamingRoot, "bootstrap.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<DataPathService> _logger;
    private BootstrapConfig _bootstrap = new();

    public DataPathService(ILogger<DataPathService> logger)
    {
        _logger = logger;
    }

    public BootstrapConfig Bootstrap => _bootstrap;

    /// <summary>Synced data path. Defaults to RoamingRoot. Can be overridden in bootstrap.json.</summary>
    public string DataPath =>
        !string.IsNullOrWhiteSpace(_bootstrap.DataPath) ? _bootstrap.DataPath! : RoamingRoot;

    public string SettingsPath => Path.Combine(DataPath, "settings.json");
    public string VoicesPath => Path.Combine(DataPath, "voices");
    public string VoiceLibraryPath => Path.Combine(VoicesPath, "library.json");

    /// <summary>Bundled Python interpreter root (machine-local).</summary>
    public string PythonRuntimePath => Path.Combine(LocalRoot, "runtime", "python");

    /// <summary>Pocket-tts model weights dir (machine-local).</summary>
    public string PocketTtsModelPath => Path.Combine(LocalRoot, "models", "pocket-tts");

    public string CachePath => Path.Combine(LocalRoot, "cache");
    public string LogsPath => Path.Combine(LocalRoot, "logs");
    public string BootstrapStatePath => Path.Combine(LocalRoot, "bootstrap-state.json");

    public void Load()
    {
        try
        {
            if (File.Exists(BootstrapPath))
            {
                var json = File.ReadAllText(BootstrapPath);
                _bootstrap = JsonSerializer.Deserialize<BootstrapConfig>(json, JsonOptions) ?? new BootstrapConfig();
            }
            else
            {
                _bootstrap = new BootstrapConfig();
                Save();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load bootstrap.json — using defaults");
            _bootstrap = new BootstrapConfig();
            Save();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(RoamingRoot);
        var json = JsonSerializer.Serialize(_bootstrap, JsonOptions);
        File.WriteAllText(BootstrapPath, json);
    }

    /// <summary>
    /// Creates the well-known directories declared in ADR 0005 if they don't exist,
    /// and seeds an empty library.json if missing. Safe to call on every launch.
    /// </summary>
    public void EnsureLayout()
    {
        Directory.CreateDirectory(RoamingRoot);
        Directory.CreateDirectory(LocalRoot);
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(VoicesPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(CachePath);
        Directory.CreateDirectory(Path.Combine(LocalRoot, "runtime"));
        Directory.CreateDirectory(Path.Combine(LocalRoot, "models"));

        if (!File.Exists(VoiceLibraryPath))
        {
            // ADR 0005: empty list is fine for v1 skeleton.
            File.WriteAllText(VoiceLibraryPath, "{\n  \"voices\": []\n}\n");
            _logger.LogInformation("Initialised voice library at {Path}", VoiceLibraryPath);
        }
    }
}

/// <summary>
/// Machine-local bootstrap pointer. v1 carries only an optional dataPath override;
/// future fields (UI position, audio device id) join here as they appear.
/// </summary>
public sealed class BootstrapConfig
{
    public string? DataPath { get; set; }
}
