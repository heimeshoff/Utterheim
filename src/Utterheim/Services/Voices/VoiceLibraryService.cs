using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Utterheim.Services.Settings;

namespace Utterheim.Services.Voices;

/// <summary>
/// Owns the on-disk voice library per ADR 0005:
///
/// <code>
/// &lt;dataPath&gt;\voices\
///   library.json                        master index, mirrors a subset of meta
///   &lt;id&gt;\
///     profile.safetensors                exported voice state from /export-voice
///     meta.json                          full per-voice metadata (schema v1)
///     sample.wav                         optional captured sample (mic / loopback)
/// </code>
///
/// Singleton in DI. <see cref="LoadAsync"/> runs once at startup to reconcile
/// the index against on-disk folders. <see cref="AddAsync"/> persists a
/// just-cloned voice; <see cref="DeleteAsync"/> removes one. Every mutation
/// fires <see cref="LibraryChanged"/> so <see cref="Speak.VoiceCatalog"/> can
/// re-fire its own <c>VoicesChanged</c> event.
///
/// Per main-015 / Q4: writes are temp+rename and ordered so a crash never
/// leaves an unrecoverable state — the per-voice folder is written first,
/// the master index last (so a partial folder without an index entry is a
/// recoverable "orphan" cleaned up on next launch; the converse — index
/// pointing at a missing folder — is the genuinely confusing failure and
/// we order writes to avoid it).
/// </summary>
public sealed class VoiceLibraryService
{
    // Reserved at the id level — case-insensitive match against pocket-tts'
    // built-in voices (eight English + `juergen` as of main-040). A clone
    // with a generated id colliding with one of these gets a 4-hex suffix;
    // an explicit user attempt to name a voice "alba" (which sanitises to
    // id "alba") is rejected with VoiceValidationException.
    private static readonly HashSet<string> ReservedBuiltInIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "alba", "marius", "javert", "jean", "fantine", "cosette", "eponine", "azelma",
        "juergen",
    };

    private const int CurrentSchemaVersion = 1;
    private const int MaxDisplayNameLength = 40;
    private const int MaxIdLength = 40;

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
    };

    private readonly DataPathService _paths;
    private readonly ILogger<VoiceLibraryService> _logger;

    private readonly object _stateLock = new();
    // In-memory mirror of the persisted index. Initialised by LoadAsync.
    private readonly List<ClonedVoiceIndexEntry> _index = new();

    public VoiceLibraryService(DataPathService paths, ILogger<VoiceLibraryService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Fired on every mutation. Subscribers (most importantly <see cref="Speak.VoiceCatalog"/>)
    /// re-fire their own change events; main-014's Voices page already
    /// re-queries the catalog on that signal.
    /// </summary>
    public event EventHandler<LibraryChangedArgs>? LibraryChanged;

    /// <summary>
    /// Snapshot of cloned voices for surfacing in the catalog. Cheap — pulls
    /// from the in-memory index loaded at startup, no disk I/O per call.
    /// </summary>
    public Task<IReadOnlyList<ClonedVoiceIndexEntry>> ListClonedAsync(CancellationToken ct)
    {
        IReadOnlyList<ClonedVoiceIndexEntry> snapshot;
        lock (_stateLock)
        {
            snapshot = _index.ToList();
        }
        return Task.FromResult(snapshot);
    }

    /// <summary>
    /// Resolve a cloned voice id to its on-disk <c>profile.safetensors</c> path.
    /// Returns null if the id isn't in the library (caller treats as "unknown
    /// voice" and surfaces a clear error). Case-insensitive lookup matches the
    /// rest of the library's id semantics.
    /// </summary>
    public string? TryResolveProfilePath(string voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId)) return null;
        ClonedVoiceIndexEntry? entry;
        lock (_stateLock)
        {
            entry = _index.FirstOrDefault(v =>
                string.Equals(v.Id, voiceId, StringComparison.OrdinalIgnoreCase));
        }
        if (entry is null) return null;
        return Path.Combine(_paths.VoicesPath, entry.Id, "profile.safetensors");
    }

    /// <summary>
    /// Resolve a cloned voice id to its <see cref="VoiceLanguage"/> for sidecar
    /// routing (main-039 / ADR 0023). The C# host pre-resolves the language so
    /// it can tag the speak request with the routing hint the multi-model
    /// sidecar reads. Returns null if the id is not in the library — the
    /// caller treats that the same as <see cref="TryResolveProfilePath"/>
    /// returning null, i.e. unknown-voice surfaces as a clear error.
    /// Case-insensitive lookup matches the rest of the library's id semantics.
    /// </summary>
    public VoiceLanguage? TryResolveLanguage(string voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId)) return null;
        ClonedVoiceIndexEntry? entry;
        lock (_stateLock)
        {
            entry = _index.FirstOrDefault(v =>
                string.Equals(v.Id, voiceId, StringComparison.OrdinalIgnoreCase));
        }
        return entry?.Language;
    }

    /// <summary>
    /// Reconcile the in-memory index with on-disk state. Runs once at startup
    /// before page VMs resolve. Per ADR 0005 / main-015 acceptance criteria:
    ///   - Library entries without folders are pruned + warning logged.
    ///   - Folders without entries are reinserted from meta.json if readable.
    ///   - schemaVersion &gt; 1 files are skipped with a warning, never crash.
    /// Idempotent.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.VoicesPath);

        // 1. Read library.json (or start empty).
        var libraryPath = _paths.VoiceLibraryPath;
        VoiceLibraryFile loaded = new();
        if (File.Exists(libraryPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(libraryPath, ct).ConfigureAwait(false);
                loaded = JsonSerializer.Deserialize<VoiceLibraryFile>(json, JsonReadOptions)
                    ?? new VoiceLibraryFile();
                if (loaded.SchemaVersion > CurrentSchemaVersion)
                {
                    _logger.LogWarning(
                        "Voice library.json declares schemaVersion {Version} > {Current}; " +
                        "reading it anyway, but newer-than-known fields will be dropped on the next save.",
                        loaded.SchemaVersion, CurrentSchemaVersion);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse voice library.json — starting with empty index.");
                loaded = new VoiceLibraryFile();
            }
        }

        // 2. Verify each library entry has a folder + profile.safetensors.
        var verified = new List<ClonedVoiceIndexEntry>();
        var droppedIds = new List<string>();
        foreach (var entry in loaded.Voices)
        {
            var folder = Path.Combine(_paths.VoicesPath, entry.Id);
            var profilePath = Path.Combine(folder, "profile.safetensors");
            if (!Directory.Exists(folder) || !File.Exists(profilePath))
            {
                _logger.LogWarning(
                    "Voice '{Id}' is in library.json but its profile is missing on disk " +
                    "(folder='{Folder}'); dropping from index.", entry.Id, folder);
                droppedIds.Add(entry.Id);
                continue;
            }
            verified.Add(entry);
        }

        // 3. Find on-disk folders that aren't represented in the index.
        var indexedIds = new HashSet<string>(verified.Select(v => v.Id), StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(_paths.VoicesPath))
        {
            foreach (var folder in Directory.EnumerateDirectories(_paths.VoicesPath))
            {
                var id = Path.GetFileName(folder);
                if (string.IsNullOrEmpty(id)) continue;
                if (indexedIds.Contains(id)) continue;

                var profilePath = Path.Combine(folder, "profile.safetensors");
                var metaPath = Path.Combine(folder, "meta.json");
                if (!File.Exists(profilePath) || !File.Exists(metaPath))
                {
                    _logger.LogWarning(
                        "Orphan voice folder '{Folder}' is missing profile.safetensors or meta.json; " +
                        "leaving it on disk untouched (per ADR 0005 — orphans are surfaced, never silently dropped).",
                        folder);
                    continue;
                }

                ClonedVoiceMeta? meta;
                try
                {
                    var json = await File.ReadAllTextAsync(metaPath, ct).ConfigureAwait(false);
                    meta = JsonSerializer.Deserialize<ClonedVoiceMeta>(json, JsonReadOptions);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Could not read meta.json for orphan voice '{Id}'; skipping reinsert.", id);
                    continue;
                }

                if (meta is null) continue;
                if (meta.SchemaVersion > CurrentSchemaVersion)
                {
                    _logger.LogWarning(
                        "Orphan voice '{Id}' has meta.json with schemaVersion {Version} > {Current}; " +
                        "skipping reinsert.", id, meta.SchemaVersion, CurrentSchemaVersion);
                    continue;
                }

                var rebuilt = new ClonedVoiceIndexEntry
                {
                    Id = meta.Id,
                    Name = string.IsNullOrWhiteSpace(meta.Name) ? meta.Id : meta.Name,
                    Engine = string.IsNullOrWhiteSpace(meta.Engine) ? "pocket-tts" : meta.Engine,
                    Source = meta.Source,
                    Language = meta.Language,
                    CreatedAt = meta.CreatedAt,
                };
                _logger.LogInformation(
                    "Reinserting orphan voice '{Id}' (name='{Name}') into library.json from meta.json.",
                    rebuilt.Id, rebuilt.Name);
                verified.Add(rebuilt);
            }
        }

        // 4. Persist reconciled library.json if anything changed.
        var changed = droppedIds.Count > 0 || verified.Count != loaded.Voices.Count;
        if (changed)
        {
            try
            {
                await WriteLibraryFileAsync(verified, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist reconciled voice library.json.");
            }
        }

        lock (_stateLock)
        {
            _index.Clear();
            _index.AddRange(verified);
        }

        _logger.LogInformation(
            "VoiceLibraryService loaded {Count} cloned voice(s); reconciliation dropped {Dropped}.",
            verified.Count, droppedIds.Count);
    }

    /// <summary>
    /// Persist a freshly-cloned voice. Per main-015 transaction order:
    /// folder → profile.safetensors → optional sample.wav → meta.json → library.json.
    /// On any step's failure, best-effort delete the per-voice folder and rethrow.
    /// <paramref name="language"/> records which pocket-tts <c>TTSModel</c> the
    /// sidecar should route this profile to (ADR 0023). Optional with an
    /// <see cref="VoiceLanguage.English"/> default so pre-main-040 callers
    /// keep working until main-041 surfaces the picker.
    /// </summary>
    public async Task<ClonedVoiceMeta> AddAsync(
        string displayName,
        VoiceSource source,
        int sampleSeconds,
        ReadOnlyMemory<byte> profileBytes,
        ReadOnlyMemory<byte>? sampleBytes,
        CancellationToken ct,
        VoiceLanguage language = VoiceLanguage.English)
    {
        // 1. Validate display name. Empty/whitespace and oversize are caller bugs;
        //    surface as a 400-shaped exception the UI maps to an inline error.
        if (string.IsNullOrWhiteSpace(displayName))
            throw new VoiceValidationException("Voice name cannot be empty.");
        var trimmed = CollapseWhitespace(displayName.Trim());
        if (trimmed.Length > MaxDisplayNameLength)
            throw new VoiceValidationException(
                $"Voice name must be {MaxDisplayNameLength} characters or fewer.");
        if (profileBytes.IsEmpty)
            throw new VoiceValidationException("profileBytes is empty — sidecar export failed?");

        // 2. Generate id. Sanitise display name; suffix on collision.
        var sanitised = SanitiseId(trimmed);
        if (string.IsNullOrEmpty(sanitised))
            throw new VoiceValidationException(
                "Voice name produces an empty id after sanitising — use ASCII letters/digits.");
        if (sanitised.Length > MaxIdLength)
            sanitised = sanitised[..MaxIdLength];

        // Reject explicit collision with a built-in. The user typed "Alba"
        // (or similar) and we won't disambiguate that with a suffix — that
        // would silently shadow the built-in for the user. Make them rename.
        if (ReservedBuiltInIds.Contains(sanitised))
            throw new VoiceValidationException(
                $"'{trimmed}' collides with the built-in voice id '{sanitised}'. " +
                "Pick a different name.");

        var id = sanitised;
        lock (_stateLock)
        {
            // Disambiguate against existing cloned voices and (defensively) the
            // built-ins again with a 4-hex suffix.
            if (IndexContainsId(id) || Directory.Exists(Path.Combine(_paths.VoicesPath, id))
                || ReservedBuiltInIds.Contains(id))
            {
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    var suffix = Guid.NewGuid().ToString("N")[..4];
                    var candidate = $"{id}-{suffix}";
                    if (!IndexContainsId(candidate)
                        && !Directory.Exists(Path.Combine(_paths.VoicesPath, candidate))
                        && !ReservedBuiltInIds.Contains(candidate))
                    {
                        id = candidate;
                        break;
                    }
                }
            }
        }

        var folder = Path.Combine(_paths.VoicesPath, id);
        var profileTmp = Path.Combine(folder, "profile.safetensors.tmp");
        var profilePath = Path.Combine(folder, "profile.safetensors");
        var sampleTmp = Path.Combine(folder, "sample.wav.tmp");
        var samplePath = Path.Combine(folder, "sample.wav");
        var metaTmp = Path.Combine(folder, "meta.json.tmp");
        var metaPath = Path.Combine(folder, "meta.json");

        bool createdFolder = false;
        try
        {
            // 3. Create voice folder.
            Directory.CreateDirectory(folder);
            createdFolder = true;

            // 4. Write profile.safetensors via temp+rename.
            await WriteAllBytesAtomicAsync(profileTmp, profilePath, profileBytes, ct).ConfigureAwait(false);

            // 5. Write optional sample.wav via temp+rename.
            string? samplePathInMeta = null;
            if (sampleBytes is not null && !sampleBytes.Value.IsEmpty)
            {
                await WriteAllBytesAtomicAsync(sampleTmp, samplePath, sampleBytes.Value, ct).ConfigureAwait(false);
                samplePathInMeta = "sample.wav";
            }

            // 6. Write meta.json via temp+rename.
            var meta = new ClonedVoiceMeta
            {
                SchemaVersion = CurrentSchemaVersion,
                Id = id,
                Name = trimmed,
                Engine = "pocket-tts",
                Source = source,
                Language = language,
                CreatedAt = DateTimeOffset.UtcNow,
                SampleSeconds = Math.Max(0, sampleSeconds),
                SamplePath = samplePathInMeta,
                Tags = Array.Empty<string>(),
            };
            var metaJson = JsonSerializer.Serialize(meta, JsonWriteOptions);
            await WriteAllTextAtomicAsync(metaTmp, metaPath, metaJson, ct).ConfigureAwait(false);

            // 7. Append entry to library.json — this is the last-and-deciding step.
            //    A crash before this point leaves an "orphan" folder reconciled
            //    on next launch; a crash after has nothing to recover.
            ClonedVoiceIndexEntry entry = new()
            {
                Id = meta.Id,
                Name = meta.Name,
                Engine = meta.Engine,
                Source = meta.Source,
                Language = meta.Language,
                CreatedAt = meta.CreatedAt,
            };
            List<ClonedVoiceIndexEntry> snapshot;
            lock (_stateLock)
            {
                _index.Add(entry);
                snapshot = _index.ToList();
            }
            try
            {
                await WriteLibraryFileAsync(snapshot, ct).ConfigureAwait(false);
            }
            catch
            {
                // Roll back the in-memory append so the next attempt isn't blocked
                // by a phantom entry.
                lock (_stateLock)
                {
                    _index.RemoveAll(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                }
                throw;
            }

            FireLibraryChanged(added: new[] { id }, removed: Array.Empty<string>());
            _logger.LogInformation(
                "Added cloned voice '{Id}' (name='{Name}', source={Source}, {SampleSeconds}s).",
                id, trimmed, source, sampleSeconds);
            return meta;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddAsync failed for voice '{Id}'; rolling back partial folder.", id);
            if (createdFolder)
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx,
                        "Best-effort cleanup of partial voice folder '{Folder}' failed.", folder);
                }
            }
            throw;
        }
    }

    /// <summary>
    /// Remove a cloned voice. Per main-015 / Q10:
    ///   1. Prune library.json first (so the catalog stops surfacing the row
    ///      even if step 2 fails).
    ///   2. Delete the folder (one retry on file-lock).
    ///   3. Fire LibraryChanged regardless (the catalog already updated in 1).
    /// Throws <see cref="KeyNotFoundException"/> if the id isn't in the library.
    /// Built-in ids are always rejected with the same exception (built-ins are
    /// not in the library; matching by id alone keeps the API simple).
    /// </summary>
    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new KeyNotFoundException("Voice id is required.");

        ClonedVoiceIndexEntry? toRemove;
        List<ClonedVoiceIndexEntry> remaining;
        lock (_stateLock)
        {
            toRemove = _index.FirstOrDefault(v =>
                string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
            if (toRemove is null)
                throw new KeyNotFoundException($"Cloned voice '{id}' is not in the library.");

            _index.Remove(toRemove);
            remaining = _index.ToList();
        }

        // 1. Persist the pruned library.json. If this fails we restore the
        //    in-memory entry so the next attempt isn't a phantom delete.
        try
        {
            await WriteLibraryFileAsync(remaining, ct).ConfigureAwait(false);
        }
        catch
        {
            lock (_stateLock) { _index.Add(toRemove); }
            throw;
        }

        // 2. Delete the folder. One retry covers the common "preview just
        //    finished, sidecar still has the .safetensors mmap'd" race.
        var folder = Path.Combine(_paths.VoicesPath, toRemove.Id);
        if (Directory.Exists(folder))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex,
                    "Voice folder '{Folder}' could not be deleted (likely a file lock); retrying once after 200 ms.",
                    folder);
                try
                {
                    await Task.Delay(200, ct).ConfigureAwait(false);
                    Directory.Delete(folder, recursive: true);
                }
                catch (Exception retryEx)
                {
                    _logger.LogWarning(retryEx,
                        "Retry of voice folder delete '{Folder}' failed; leaving on disk for next-launch reconciliation.",
                        folder);
                }
            }
        }

        // 3. Fire even if the folder lingered — library.json no longer references
        //    it, the catalog stops surfacing it, and reconciliation cleans up.
        FireLibraryChanged(added: Array.Empty<string>(), removed: new[] { toRemove.Id });
        _logger.LogInformation("Deleted cloned voice '{Id}'.", toRemove.Id);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private bool IndexContainsId(string id) =>
        _index.Any(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    private void FireLibraryChanged(IReadOnlyList<string> added, IReadOnlyList<string> removed)
    {
        try
        {
            LibraryChanged?.Invoke(this, new LibraryChangedArgs(added, removed));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LibraryChanged subscriber threw.");
        }
    }

    private async Task WriteLibraryFileAsync(IReadOnlyList<ClonedVoiceIndexEntry> entries, CancellationToken ct)
    {
        var file = new VoiceLibraryFile
        {
            SchemaVersion = CurrentSchemaVersion,
            Voices = entries,
        };
        var json = JsonSerializer.Serialize(file, JsonWriteOptions);
        var libraryPath = _paths.VoiceLibraryPath;
        var tmpPath = libraryPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        await WriteAllTextAtomicAsync(tmpPath, libraryPath, json, ct).ConfigureAwait(false);
    }

    private static async Task WriteAllBytesAtomicAsync(
        string tmpPath, string finalPath, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
            await fs.FlushAsync(ct).ConfigureAwait(false);
        }
        if (File.Exists(finalPath))
            File.Replace(tmpPath, finalPath, destinationBackupFileName: null);
        else
            File.Move(tmpPath, finalPath);
    }

    private static async Task WriteAllTextAtomicAsync(
        string tmpPath, string finalPath, string content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        await File.WriteAllTextAsync(tmpPath, content, Encoding.UTF8, ct).ConfigureAwait(false);
        if (File.Exists(finalPath))
            File.Replace(tmpPath, finalPath, destinationBackupFileName: null);
        else
            File.Move(tmpPath, finalPath);
    }

    private static string SanitiseId(string displayName)
    {
        // Lower-kebab. Replace whitespace runs with a single dash, drop everything
        // that isn't [a-z0-9-], collapse repeated dashes, trim dashes from edges.
        var sb = new StringBuilder(displayName.Length);
        bool lastWasDash = false;
        foreach (var raw in displayName.ToLowerInvariant())
        {
            char c = raw;
            bool isAllowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            bool isSeparator = char.IsWhiteSpace(c) || c == '-' || c == '_' || c == '.';
            if (isAllowed)
            {
                sb.Append(c);
                lastWasDash = false;
            }
            else if (isSeparator)
            {
                if (!lastWasDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
                // else: skip to avoid leading-dash / repeated-dash.
            }
            // else: drop the character entirely (e.g. unicode punctuation).
        }
        var result = sb.ToString().Trim('-');
        return result;
    }

    private static string CollapseWhitespace(string input)
    {
        var sb = new StringBuilder(input.Length);
        bool prevWs = false;
        foreach (var c in input)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevWs && sb.Length > 0)
                {
                    sb.Append(' ');
                    prevWs = true;
                }
            }
            else
            {
                sb.Append(c);
                prevWs = false;
            }
        }
        // Trim trailing space if last char was a space.
        if (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        return sb.ToString();
    }
}
