using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Settings;

namespace Mockingbird.Services.Tts;

/// <summary>
/// First-launch installer for the pocket-tts sidecar runtime. Per ADR 0008:
/// download Python 3.12 embeddable, pip-install pocket-tts (incl. torch CPU)
/// into a sibling site-packages folder, smoke-test the import. Persists
/// <c>bootstrap-state.json</c> so partial progress survives restarts.
///
/// The bootstrap dialog drives this via <see cref="BootstrapAsync"/> with a
/// progress callback. On every subsequent launch <see cref="IsBootstrapped"/>
/// returns true and the work is skipped.
/// </summary>
public sealed class PythonRuntimeBootstrapper
{
    // Pinned Python release. Embeddable zip is the small, redistributable
    // interpreter Microsoft documents as the "embed in another product" target.
    // 3.12 is what pocket-tts is tested against (3.10–3.14 supported per its README).
    private const string PythonVersion = "3.12.7";
    private const string PythonEmbedUrl =
        $"https://www.python.org/ftp/python/{PythonVersion}/python-{PythonVersion}-embed-amd64.zip";

    private const string GetPipUrl = "https://bootstrap.pypa.io/get-pip.py";

    // Pin the pocket-tts version that matches the API surface this engine wraps.
    // The probe install used 2.0.0; lock to >=2.0,<3 so patch updates are picked up
    // but a major bump (which could change /tts shape) isn't.
    private const string PocketTtsSpec = "pocket-tts>=2.0,<3";

    private readonly DataPathService _paths;
    private readonly ILogger<PythonRuntimeBootstrapper> _logger;

    public PythonRuntimeBootstrapper(DataPathService paths, ILogger<PythonRuntimeBootstrapper> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <summary>Path the sidecar process will execute (`python.exe`).</summary>
    public string PythonExePath => Path.Combine(_paths.PythonRuntimePath, "python.exe");

    /// <summary>True if a complete runtime is already installed and verified.
    /// Delegates to the same helpers the install path uses
    /// (<see cref="PocketTtsActuallyInstalled"/>, <see cref="MockingbirdSidecarActuallyInstalled"/>,
    /// <see cref="BundledSidecarMatchesInstalled"/>) so the launch-time gate
    /// and the install-time guard cannot drift out of sync — main-027.
    /// Returning false here triggers the bootstrap dialog on next launch,
    /// which then heals partial / stale installs by re-running the install
    /// step (its `File.Copy(overwrite: true)` overwrites stale wrapper bytes).</summary>
    public bool IsBootstrapped
    {
        get
        {
            var state = LoadState();
            return state.RuntimeReady
                   && File.Exists(PythonExePath)
                   && PocketTtsActuallyInstalled()
                   && MockingbirdSidecarActuallyInstalled()
                   && BundledSidecarMatchesInstalled();
        }
    }

    /// <summary>
    /// Perform the bootstrap end-to-end. Idempotent — already-completed steps
    /// are skipped via the persisted <c>bootstrap-state.json</c>.
    /// </summary>
    public async Task BootstrapAsync(IProgress<BootstrapProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.PythonRuntimePath);
        var state = LoadState();

        // Step 1 — download + extract embeddable Python.
        if (!state.PythonExtracted || !File.Exists(PythonExePath))
        {
            // Defensive reset: if we're re-extracting Python, anything pip / pocket-tts
            // wrote into the previous runtime's site-packages is gone. The state file
            // can outlive runtime/ (clean-machine simulation, manual wipe, etc.) — see
            // main-021. Force every downstream step to re-run rather than trusting the
            // stale "Installed: true" flags.
            if (state.PipInstalled || state.PocketTtsInstalled || state.MockingbirdSidecarInstalled || state.RuntimeReady)
            {
                _logger.LogInformation(
                    "Re-extracting Python runtime — resetting downstream bootstrap flags (PipInstalled, PocketTtsInstalled, MockingbirdSidecarInstalled, RuntimeReady).");
                state.PipInstalled = false;
                state.PocketTtsInstalled = false;
                state.MockingbirdSidecarInstalled = false;
                state.RuntimeReady = false;
            }

            progress.Report(new BootstrapProgress(BootstrapStep.DownloadPython, 0, "Downloading Python runtime…"));
            var zipPath = Path.Combine(_paths.PythonRuntimePath, $"python-{PythonVersion}-embed-amd64.zip");
            await DownloadFileAsync(PythonEmbedUrl, zipPath, p =>
                progress.Report(new BootstrapProgress(BootstrapStep.DownloadPython, p, $"Downloading Python {PythonVersion} ({(int)(p * 100)}%)")), ct);

            progress.Report(new BootstrapProgress(BootstrapStep.DownloadPython, 1.0, "Extracting Python…"));
            ZipFile.ExtractToDirectory(zipPath, _paths.PythonRuntimePath, overwriteFiles: true);
            File.Delete(zipPath);

            // Embeddable distributions ship with a `python<ver>._pth` file that
            // disables `site` (and therefore disables pip). We patch it so site-packages
            // is honoured, otherwise pip install would put files where they cannot be
            // imported.
            EnableSitePackages();

            state.PythonExtracted = true;
            SaveState(state);
            _logger.LogInformation("Python {Ver} embeddable extracted to {Path}", PythonVersion, _paths.PythonRuntimePath);
        }
        else
        {
            progress.Report(new BootstrapProgress(BootstrapStep.DownloadPython, 1.0, "Python runtime already present."));
        }

        ct.ThrowIfCancellationRequested();

        // Step 2 — install pip (the embeddable distribution does not ship it).
        if (!state.PipInstalled || !PipExists())
        {
            // If pip is missing on disk, anything that pip itself installed (pocket-tts)
            // is also gone — same defensive reset as step 1. The mockingbird_sidecar
            // copy is independent of pip but lives in the same site-packages tree, so
            // it's also gone if site-packages was wiped.
            if (state.PocketTtsInstalled || state.MockingbirdSidecarInstalled || state.RuntimeReady)
            {
                _logger.LogInformation(
                    "Re-installing pip — resetting downstream flags (PocketTtsInstalled, MockingbirdSidecarInstalled, RuntimeReady).");
                state.PocketTtsInstalled = false;
                state.MockingbirdSidecarInstalled = false;
                state.RuntimeReady = false;
            }

            progress.Report(new BootstrapProgress(BootstrapStep.InstallPip, 0, "Installing pip…"));
            var getPipPath = Path.Combine(_paths.PythonRuntimePath, "get-pip.py");
            await DownloadFileAsync(GetPipUrl, getPipPath, p =>
                progress.Report(new BootstrapProgress(BootstrapStep.InstallPip, p * 0.3, $"Downloading get-pip ({(int)(p * 100)}%)")), ct);

            progress.Report(new BootstrapProgress(BootstrapStep.InstallPip, 0.4, "Installing pip…"));
            await RunPythonAsync(getPipPath, "Installing pip", ct,
                line => progress.Report(new BootstrapProgress(BootstrapStep.InstallPip, 0.7, line)));

            try { File.Delete(getPipPath); } catch { /* tolerate */ }
            state.PipInstalled = true;
            SaveState(state);
        }
        else
        {
            progress.Report(new BootstrapProgress(BootstrapStep.InstallPip, 1.0, "pip already installed."));
        }

        ct.ThrowIfCancellationRequested();

        // Step 3 — pip install pocket-tts. This pulls torch CPU (~115 MB) plus deps.
        // Belt-and-braces: trust the on-disk presence of pocket_tts/__init__.py over
        // the persisted flag. main-021: we observed bootstrap-state.json outliving a
        // wiped runtime/, leading the JSON to lie about pocket-tts being installed
        // and step 4 (smoke test) failing with `ModuleNotFoundError`.
        if (!state.PocketTtsInstalled || !PocketTtsActuallyInstalled())
        {
            if (state.PocketTtsInstalled && !PocketTtsActuallyInstalled())
            {
                _logger.LogWarning(
                    "bootstrap-state.json says pocket-tts is installed but {InitPath} is missing — re-installing.",
                    Path.Combine(_paths.PythonRuntimePath, "Lib", "site-packages", "pocket_tts", "__init__.py"));
                state.PocketTtsInstalled = false;
                // mockingbird_sidecar lives in the same site-packages tree as pocket-tts;
                // if pocket-tts is gone the wrapper almost certainly is too.
                state.MockingbirdSidecarInstalled = false;
                state.RuntimeReady = false;
                SaveState(state);
            }

            progress.Report(new BootstrapProgress(BootstrapStep.InstallPocketTts, 0,
                "Installing pocket-tts (this downloads ~500 MB of dependencies)…"));
            await RunPipAsync($"install --no-warn-script-location {PocketTtsSpec}", ct,
                line => progress.Report(new BootstrapProgress(BootstrapStep.InstallPocketTts, 0.5, TruncateLine(line))));
            state.PocketTtsInstalled = true;
            SaveState(state);
            _logger.LogInformation("pocket-tts installed into {Path}", _paths.PythonRuntimePath);
        }
        else
        {
            progress.Report(new BootstrapProgress(BootstrapStep.InstallPocketTts, 1.0, "pocket-tts already installed."));
        }

        ct.ThrowIfCancellationRequested();

        // Step 3b — install the mockingbird_sidecar wrapper (ADR 0015) into the
        // bootstrapped runtime's site-packages. The wrapper itself is a tiny
        // pure-Python package shipped next to mockingbird.exe; it has no pip
        // dependencies of its own (everything it imports is satisfied by
        // pocket-tts above). Copy-from-bundled-files is the simplest install
        // shape per the ADR's "v1" recommendation.
        //
        // The guard also re-installs on version drift (main-027 follow-up):
        // IsBootstrapped's launch-time gate already fails on bundled vs.
        // installed __version__ mismatch, but without checking the same here
        // the install path would skip step 3b (files physically present!)
        // and the stale wrapper would survive a "successful" bootstrap run.
        if (!state.MockingbirdSidecarInstalled
            || !MockingbirdSidecarActuallyInstalled()
            || !BundledSidecarMatchesInstalled())
        {
            progress.Report(new BootstrapProgress(
                BootstrapStep.InstallPocketTts, 0.95, "Installing mockingbird sidecar wrapper…"));
            InstallMockingbirdSidecar();
            state.MockingbirdSidecarInstalled = true;
            state.RuntimeReady = false; // force smoke test re-run on first install
            SaveState(state);
            _logger.LogInformation("mockingbird_sidecar wrapper installed.");
        }

        // Step 4 — smoke test: import both pocket_tts and mockingbird_sidecar.
        // Cheap, doesn't trigger weight download. The sidecar import in
        // particular catches "wrong pocket-tts release" early per ADR 0015.
        progress.Report(new BootstrapProgress(BootstrapStep.SmokeTest, 0, "Verifying install…"));
        await RunPythonInlineAsync(
            "import pocket_tts; import mockingbird_sidecar; print('pocket_tts + mockingbird_sidecar ok')",
            ct,
            line => progress.Report(new BootstrapProgress(BootstrapStep.SmokeTest, 0.5, line)));

        state.RuntimeReady = true;
        SaveState(state);
        progress.Report(new BootstrapProgress(BootstrapStep.SmokeTest, 1.0, "Runtime ready."));
        _logger.LogInformation("Pocket-tts runtime bootstrap complete.");
    }

    /// <summary>
    /// Copy the bundled mockingbird_sidecar package from the install folder
    /// into the bootstrapped Python runtime's site-packages. Idempotent —
    /// existing files are overwritten so a mockingbird upgrade refreshes the
    /// wrapper. Per ADR 0015 the bundled source lives next to the .exe under
    /// <c>PythonSidecar/mockingbird_sidecar/</c>.
    /// </summary>
    private void InstallMockingbirdSidecar()
    {
        var sourceRoot = LocateBundledSidecarRoot();
        if (sourceRoot is null)
        {
            throw new InvalidOperationException(
                "Could not locate bundled mockingbird_sidecar Python package. " +
                "Expected at <appBaseDirectory>/PythonSidecar/mockingbird_sidecar/. " +
                "Check the build output of src/Mockingbird/Mockingbird.csproj.");
        }

        var destRoot = Path.Combine(
            _paths.PythonRuntimePath, "Lib", "site-packages", "mockingbird_sidecar");
        Directory.CreateDirectory(destRoot);

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.py", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceRoot, file);
            var dest = Path.Combine(destRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }

        _logger.LogInformation(
            "mockingbird_sidecar copied from {Source} to {Dest}.", sourceRoot, destRoot);
    }

    /// <summary>
    /// Find the bundled package directory. In production it sits beside
    /// mockingbird.exe under <c>PythonSidecar/mockingbird_sidecar/</c>; in dev
    /// builds the .csproj copies it to the same location next to the binary.
    /// </summary>
    private static string? LocateBundledSidecarRoot()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory,
            "PythonSidecar", "mockingbird_sidecar");
        if (Directory.Exists(candidate)) return candidate;
        return null;
    }

    private bool MockingbirdSidecarActuallyInstalled()
    {
        return File.Exists(Path.Combine(
            _paths.PythonRuntimePath, "Lib", "site-packages", "mockingbird_sidecar", "__init__.py"))
            && File.Exists(Path.Combine(
                _paths.PythonRuntimePath, "Lib", "site-packages", "mockingbird_sidecar", "main.py"));
    }

    /// <summary>
    /// True iff the installed <c>mockingbird_sidecar/__init__.py</c>'s
    /// <c>__version__</c> matches the bundled wrapper's version (main-027).
    /// Drives the launch-time staleness check: bumping the bundled
    /// <c>__version__</c> forces a re-install on the next launch, which
    /// overwrites the on-disk wrapper bytes with the bundled ones.
    /// Returns false on any read / parse failure — "unknown version" must
    /// trigger re-install rather than silently skip it. The mismatch
    /// (or parse failure) is logged at Warning so a real parse bug
    /// doesn't hide forever behind a perpetual re-install.
    /// </summary>
    private bool BundledSidecarMatchesInstalled()
    {
        var installedPath = Path.Combine(
            _paths.PythonRuntimePath, "Lib", "site-packages",
            "mockingbird_sidecar", "__init__.py");
        var bundledRoot = LocateBundledSidecarRoot();
        if (bundledRoot is null)
        {
            // No bundled package next to the .exe — we can't compare. Treat
            // as "needs install"; the install step will throw a clearer
            // error if the bundle is genuinely missing.
            _logger.LogWarning(
                "Bundled mockingbird_sidecar package not found next to mockingbird.exe — forcing re-install attempt.");
            return false;
        }

        var bundledPath = Path.Combine(bundledRoot, "__init__.py");

        var installed = ReadVersion(installedPath);
        var bundled = ReadVersion(bundledPath);

        if (installed is null || bundled is null)
        {
            _logger.LogWarning(
                "mockingbird_sidecar __version__ unreadable (installed={Installed}, bundled={Bundled}) — forcing re-install.",
                installed ?? "<null>", bundled ?? "<null>");
            return false;
        }

        if (!string.Equals(installed, bundled, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "mockingbird_sidecar version drift: installed={Installed}, bundled={Bundled} — re-install will run.",
                installed, bundled);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Parse the <c>__version__ = "1.2.3"</c> line from a Python source file.
    /// Returns null on any failure (file missing, IO error, no version line) —
    /// caller treats null as "version unknown, force re-install" (safe default
    /// per main-027). Tolerant of single / double quotes and surrounding
    /// whitespace; deliberately not a Python parser.
    /// </summary>
    private static string? ReadVersion(string pyPath)
    {
        if (!File.Exists(pyPath)) return null;
        try
        {
            foreach (var line in File.ReadLines(pyPath))
            {
                var match = VersionRegex.Match(line);
                if (match.Success) return match.Groups[1].Value;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        return null;
    }

    private static readonly Regex VersionRegex =
        new(@"^\s*__version__\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled);

    // ---- private helpers ----

    private void EnableSitePackages()
    {
        // Match e.g. python312._pth — the version of the file ships with the embeddable.
        var pthFiles = Directory.EnumerateFiles(_paths.PythonRuntimePath, "python*._pth").ToList();
        foreach (var pth in pthFiles)
        {
            var lines = File.ReadAllLines(pth).ToList();
            // `import site` is the magic that activates site-packages. Embeddable
            // distros ship it commented out (`#import site`). Uncomment.
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("#import site"))
                {
                    lines[i] = "import site";
                }
            }
            // Also make sure the Lib\site-packages is on the import path.
            if (!lines.Any(l => l.Contains("Lib\\site-packages") || l.Contains("Lib/site-packages")))
            {
                lines.Add("Lib\\site-packages");
            }
            File.WriteAllLines(pth, lines);
            // Confirm the patch landed (main-021 acceptance criterion). After a
            // runtime wipe step 1 re-extracts a fresh _pth and we re-patch it
            // here; without this log the user has no way to verify.
            _logger.LogInformation("Patched embeddable Python ._pth for site-packages support: {Path}", pth);
        }
    }

    private bool PipExists()
    {
        return File.Exists(Path.Combine(_paths.PythonRuntimePath, "Scripts", "pip.exe"))
            || File.Exists(Path.Combine(_paths.PythonRuntimePath, "Lib", "site-packages", "pip", "__init__.py"));
    }

    /// <summary>
    /// True iff pocket-tts is physically present in the runtime's site-packages.
    /// Used alongside the persisted state flag so a stale flag (e.g. state file
    /// that outlived a wiped runtime) can't trick the bootstrapper into skipping
    /// the install.
    /// </summary>
    private bool PocketTtsActuallyInstalled()
    {
        return File.Exists(Path.Combine(
            _paths.PythonRuntimePath, "Lib", "site-packages", "pocket_tts", "__init__.py"));
    }

    private async Task RunPythonAsync(string scriptPath, string operationName, CancellationToken ct, Action<string>? onLine)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PythonExePath,
            Arguments = $"\"{scriptPath}\"",
            WorkingDirectory = _paths.PythonRuntimePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        await RunProcessAsync(psi, operationName, ct, onLine);
    }

    private async Task RunPythonInlineAsync(string code, CancellationToken ct, Action<string>? onLine)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PythonExePath,
            Arguments = $"-c \"{code}\"",
            WorkingDirectory = _paths.PythonRuntimePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        await RunProcessAsync(psi, "Python smoke test", ct, onLine);
    }

    private async Task RunPipAsync(string args, CancellationToken ct, Action<string>? onLine)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PythonExePath,
            Arguments = $"-m pip {args}",
            WorkingDirectory = _paths.PythonRuntimePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        await RunProcessAsync(psi, "pip", ct, onLine);
    }

    private async Task RunProcessAsync(ProcessStartInfo psi, string operationName, CancellationToken ct, Action<string>? onLine)
    {
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        // Buffer stderr so that, on non-zero exit, we can surface the actual error
        // (Python traceback, pip resolver complaint, etc.) in the thrown exception
        // and the user-facing dialog. Default Serilog config writes INF and above
        // to file, so before this fix any stderr logged at Debug was dropped on the
        // floor — see main-021 Bug B. Keep the live feed at Debug for successful
        // pip runs (pip writes progress to stderr); on failure we replay the buffer
        // at Error level so it shows up in the file log.
        var stderrBuffer = new StringBuilder();
        const int stderrLineCap = 200; // hard ceiling so a runaway process can't OOM us
        var stderrLineCount = 0;

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            _logger.LogDebug("[{Op} stdout] {Line}", operationName, e.Data);
            onLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            // pip writes progress to stderr, so this isn't necessarily an error.
            _logger.LogDebug("[{Op} stderr] {Line}", operationName, e.Data);
            lock (stderrBuffer)
            {
                if (stderrLineCount < stderrLineCap)
                {
                    stderrBuffer.AppendLine(e.Data);
                    stderrLineCount++;
                }
                else if (stderrLineCount == stderrLineCap)
                {
                    stderrBuffer.AppendLine("… (stderr truncated — line cap reached)");
                    stderrLineCount++;
                }
            }
            onLine?.Invoke(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start {operationName}.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* tolerate */ }
            throw;
        }

        if (process.ExitCode != 0)
        {
            // Tail of stderr — last ~30 lines is plenty to capture a Python
            // traceback or pip resolver error without bloating the dialog.
            string capturedStderr;
            lock (stderrBuffer) { capturedStderr = stderrBuffer.ToString(); }
            var tail = LastLines(capturedStderr, 30);

            // Replay at Error level so the file log retains the full diagnostic.
            // Single multi-line message; Serilog's file sink will preserve newlines.
            if (!string.IsNullOrWhiteSpace(tail))
            {
                _logger.LogError(
                    "{Op} exited with code {ExitCode}. Captured stderr (last 30 lines):{NewLine}{Stderr}",
                    operationName, process.ExitCode, Environment.NewLine, tail);
            }
            else
            {
                _logger.LogError(
                    "{Op} exited with code {ExitCode}. (No stderr was captured.)",
                    operationName, process.ExitCode);
            }

            var msg = string.IsNullOrWhiteSpace(tail)
                ? $"{operationName} exited with code {process.ExitCode}. (No stderr was captured — see the log for stdout.)"
                : $"{operationName} exited with code {process.ExitCode}.{Environment.NewLine}{Environment.NewLine}{tail}";
            throw new InvalidOperationException(msg);
        }
    }

    /// <summary>
    /// Return the last <paramref name="count"/> non-empty lines of <paramref name="text"/>,
    /// preserving line order. Used to keep error messages bounded.
    /// </summary>
    private static string LastLines(string text, int count)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        if (lines.Count <= count) return string.Join(Environment.NewLine, lines);
        return string.Join(Environment.NewLine, lines.Skip(lines.Count - count));
    }

    private async Task DownloadFileAsync(string url, string destination, Action<double> onProgress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = File.Create(destination);

        var buffer = new byte[81920];
        long readSoFar = 0;
        int n;
        while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            readSoFar += n;
            if (totalBytes > 0)
                onProgress(Math.Clamp((double)readSoFar / totalBytes, 0, 1));
        }
        onProgress(1.0);
    }

    private BootstrapState LoadState()
    {
        try
        {
            if (File.Exists(_paths.BootstrapStatePath))
            {
                var json = File.ReadAllText(_paths.BootstrapStatePath);
                return JsonSerializer.Deserialize<BootstrapState>(json) ?? new BootstrapState();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read bootstrap-state.json — restarting bootstrap.");
        }
        return new BootstrapState();
    }

    private void SaveState(BootstrapState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.BootstrapStatePath)!);
            File.WriteAllText(_paths.BootstrapStatePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist bootstrap-state.json — partial progress may not survive restart.");
        }
    }

    private static string TruncateLine(string line)
    {
        // pip emits long lines (e.g. wheel URLs) — keep status text manageable.
        const int max = 100;
        if (line.Length <= max) return line;
        return line[..max] + "…";
    }
}

/// <summary>
/// Persisted across restarts so a half-completed bootstrap (e.g. user
/// closed mid-pip-install) doesn't redo work that already succeeded.
/// </summary>
public sealed class BootstrapState
{
    public bool PythonExtracted { get; set; }
    public bool PipInstalled { get; set; }
    public bool PocketTtsInstalled { get; set; }
    /// <summary>
    /// True after the bundled mockingbird_sidecar wrapper has been copied into
    /// site-packages (ADR 0015). New in main-015 — older state files default to
    /// false here, which causes the install step to run on next launch.
    /// </summary>
    public bool MockingbirdSidecarInstalled { get; set; }
    public bool RuntimeReady { get; set; }
}

/// <summary>Coarse step buckets for progress display.</summary>
public enum BootstrapStep
{
    DownloadPython,
    InstallPip,
    InstallPocketTts,
    SmokeTest,
}

/// <summary>One progress update from <see cref="PythonRuntimeBootstrapper.BootstrapAsync"/>.</summary>
/// <param name="Step">Which top-level step is currently running.</param>
/// <param name="Fraction">0..1 progress within the step (best-effort; pip install fraction is approximate).</param>
/// <param name="Message">Human-readable status line for the dialog.</param>
public sealed record BootstrapProgress(BootstrapStep Step, double Fraction, string Message);
