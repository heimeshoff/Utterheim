using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Mockingbird.Services.Settings;

/// <summary>
/// Typed wrapper over <c>%LOCALAPPDATA%\Mockingbird\settings.json</c>. v1 (main-013)
/// ships the storage layer for <see cref="DefaultVoiceId"/> only — main-016 will
/// add the UI to mutate it and additional setting slots (output device, start
/// minimised, etc.).
///
/// JSON shape is forward-compatible: unknown fields are ignored on read so a
/// settings.json that has been extended by a future build can still be loaded by
/// v1, and comments are tolerated.
///
/// Per ADR 0005 the file lives under <see cref="DataPathService.LocalRoot"/>
/// (machine-local), alongside <c>bootstrap-state.json</c> and the cache dir.
/// </summary>
public sealed class UserSettings
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<UserSettings> _logger;
    private readonly string _path;
    private readonly object _writeLock = new();
    private SettingsData _data = new();

    public UserSettings(ILogger<UserSettings> logger)
        : this(logger, Path.Combine(DataPathService.LocalRoot, "settings.json"))
    {
    }

    // Test-friendly constructor — production uses the LocalAppData default path.
    public UserSettings(ILogger<UserSettings> logger, string path)
    {
        _logger = logger;
        _path = path;
        Load();
    }

    /// <summary>Absolute path to the backing file (for diagnostics / tests).</summary>
    public string FilePath => _path;

    /// <summary>
    /// Voice id the Speak page pre-selects on entry. <c>null</c> means "no preference"
    /// — caller falls back to the alphabetical-first voice in the catalog (per main-013
    /// resolution order, Q7).
    /// </summary>
    public string? DefaultVoiceId
    {
        get => _data.DefaultVoiceId;
        set
        {
            if (string.Equals(_data.DefaultVoiceId, value, StringComparison.Ordinal)) return;
            _data.DefaultVoiceId = value;
            Save();
            DefaultVoiceIdChanged?.Invoke(this, value);
        }
    }

    /// <summary>Fired after persistence. main-013 does not subscribe (page reads on
    /// OnNavigatedTo); main-016 will when the Settings UI ships.</summary>
    public event EventHandler<string?>? DefaultVoiceIdChanged;

    /// <summary>
    /// Output device id passed to <c>WaveOutEvent.DeviceNumber</c>. <c>null</c>
    /// means "system default" — mapped to <c>-1</c> at the call site in
    /// <see cref="Speak.AudioPlayer"/>. Read once per utterance at
    /// <c>WaveOutEvent</c> construction time, so device changes apply to the
    /// next speak request only (no live-switch invariants — main-016).
    /// </summary>
    public int? OutputDeviceId
    {
        get => _data.OutputDeviceId;
        set
        {
            if (_data.OutputDeviceId == value) return;
            _data.OutputDeviceId = value;
            Save();
            OutputDeviceIdChanged?.Invoke(this, value);
        }
    }

    /// <summary>Fired after <see cref="OutputDeviceId"/> persistence (main-016).</summary>
    public event EventHandler<int?>? OutputDeviceIdChanged;

    /// <summary>
    /// When true, the main window starts hidden in the tray on launch instead
    /// of activating. Honoured by <c>MainWindow</c> on the initial show
    /// (main-016). Defaults to false.
    /// </summary>
    public bool StartMinimised
    {
        get => _data.StartMinimised;
        set
        {
            if (_data.StartMinimised == value) return;
            _data.StartMinimised = value;
            Save();
            StartMinimisedChanged?.Invoke(this, value);
        }
    }

    /// <summary>Fired after <see cref="StartMinimised"/> persistence (main-016).</summary>
    public event EventHandler<bool>? StartMinimisedChanged;

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _data = new SettingsData();
                return;
            }

            var json = File.ReadAllText(_path);
            _data = JsonSerializer.Deserialize<SettingsData>(json, ReadOptions) ?? new SettingsData();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings.json at {Path} — using defaults.", _path);
            _data = new SettingsData();
        }
    }

    private void Save()
    {
        try
        {
            lock (_writeLock)
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Atomic-ish write: temp file + replace. Avoids a half-written settings.json
                // if the process dies mid-flush.
                var tmp = _path + ".tmp";
                var json = JsonSerializer.Serialize(_data, WriteOptions);
                File.WriteAllText(tmp, json);
                if (File.Exists(_path))
                {
                    File.Replace(tmp, _path, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tmp, _path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save settings.json at {Path}", _path);
        }
    }

    /// <summary>
    /// On-disk shape. Forward-compatible: main-016 will extend this with new
    /// properties (output device, start minimised, etc.) without breaking v1
    /// reads thanks to <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/>
    /// and ignored unknown fields.
    /// </summary>
    private sealed class SettingsData
    {
        [JsonPropertyName("defaultVoiceId")]
        public string? DefaultVoiceId { get; set; }

        [JsonPropertyName("outputDeviceId")]
        public int? OutputDeviceId { get; set; }

        [JsonPropertyName("startMinimised")]
        public bool StartMinimised { get; set; }
    }
}
