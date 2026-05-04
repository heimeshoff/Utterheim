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
///
/// Per main-031 the data path is also mutable at runtime: <see cref="SetDataPath"/>
/// validates a new target with <see cref="ValidatePath"/>, persists bootstrap.json via
/// temp+rename, and fires <see cref="DataPathChanged"/>. In Mockingbird only the voice
/// library reacts to that event (only <c>&lt;dataPath&gt;\voices\</c> relocates;
/// <c>runtime/</c>, <c>models/</c>, <c>cache/</c>, <c>logs/</c>, <c>bootstrap-state.json</c>,
/// and <c>settings.json</c> are anchored to <see cref="LocalRoot"/> by design).
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

    /// <summary>
    /// Raised after <see cref="SetDataPath"/> successfully changes (or resets) the
    /// data path at runtime. The argument is the resolved <see cref="DataPath"/>
    /// (i.e. the override, or <see cref="RoamingRoot"/> after a reset). Subscribers
    /// (most importantly <c>VoiceLibraryService</c>) re-run their path-dependent
    /// load to point at the new location.
    /// </summary>
    public event EventHandler<string>? DataPathChanged;

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

    /// <summary>
    /// Persist <c>bootstrap.json</c>. Per main-031 the write is atomic:
    /// serialize to <c>bootstrap.json.tmp</c>, then <see cref="File.Move(string,string,bool)"/>
    /// with <c>overwrite: true</c> onto the live file. A crash mid-update leaves at
    /// worst an unreferenced <c>.tmp</c> alongside the previous good pointer file —
    /// matches the discipline <see cref="Voices.VoiceLibraryService"/> already
    /// enforces for <c>library.json</c>, <c>meta.json</c>, and
    /// <c>profile.safetensors</c>.
    /// </summary>
    public void Save()
    {
        Directory.CreateDirectory(RoamingRoot);
        var json = JsonSerializer.Serialize(_bootstrap, JsonOptions);
        var tmpPath = BootstrapPath + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, BootstrapPath, overwrite: true);
    }

    /// <summary>
    /// Verifies a candidate data folder is writable by creating it if necessary
    /// and round-tripping a tiny temp file. Returns <c>false</c> for any failure
    /// (permissions, read-only volume, missing parent on a UNC path that can't
    /// be created, etc.) — the caller surfaces a "pick a different folder"
    /// warning. Used by <see cref="SetDataPath"/> and by the Settings page's
    /// Browse… flow before the persist step.
    /// </summary>
    public static bool ValidatePath(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var testFile = Path.Combine(path, $".mockingbird_write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Change the data path at runtime. <c>null</c> or whitespace resets the
    /// override (the resolved <see cref="DataPath"/> falls back to
    /// <see cref="RoamingRoot"/>); a non-empty value is validated with
    /// <see cref="ValidatePath"/> first and rejected (returning <c>false</c>,
    /// no persistence) if it isn't writable. On success the bootstrap pointer
    /// is persisted via temp+rename and <see cref="DataPathChanged"/> fires
    /// with the new resolved path. Pointer-swap only — existing voice files
    /// at the old location are left untouched per the v1 boundary; only
    /// <c>VoiceLibraryService</c> reacts to the event.
    /// </summary>
    /// <returns>
    /// <c>true</c> when bootstrap.json was rewritten; <c>false</c> when the
    /// proposed path failed the writability test (bootstrap.json is unchanged).
    /// </returns>
    public bool SetDataPath(string? newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath))
        {
            _bootstrap.DataPath = null;
            Save();
            _logger.LogInformation("DataPathService: reset to default ({Default}).", RoamingRoot);
            DataPathChanged?.Invoke(this, DataPath);
            return true;
        }

        if (!ValidatePath(newPath))
        {
            _logger.LogWarning("DataPathService: path validation failed for '{Path}'.", newPath);
            return false;
        }

        _bootstrap.DataPath = newPath;
        Save();
        _logger.LogInformation("DataPathService: data path changed to '{Path}'.", newPath);
        DataPathChanged?.Invoke(this, DataPath);
        return true;
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
