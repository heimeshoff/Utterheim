using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
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

    /// <summary>True if a complete runtime is already installed and verified.</summary>
    public bool IsBootstrapped
    {
        get
        {
            var state = LoadState();
            return state.RuntimeReady
                   && File.Exists(PythonExePath)
                   && File.Exists(Path.Combine(_paths.PythonRuntimePath, "Lib", "site-packages", "pocket_tts", "__init__.py"));
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
        if (!state.PocketTtsInstalled)
        {
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

        // Step 4 — smoke test: import pocket_tts. Cheap, doesn't trigger weight download.
        progress.Report(new BootstrapProgress(BootstrapStep.SmokeTest, 0, "Verifying install…"));
        await RunPythonInlineAsync("import pocket_tts; print('pocket_tts ok')", ct,
            line => progress.Report(new BootstrapProgress(BootstrapStep.SmokeTest, 0.5, line)));

        state.RuntimeReady = true;
        SaveState(state);
        progress.Report(new BootstrapProgress(BootstrapStep.SmokeTest, 1.0, "Runtime ready."));
        _logger.LogInformation("Pocket-tts runtime bootstrap complete.");
    }

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
        }
    }

    private bool PipExists()
    {
        return File.Exists(Path.Combine(_paths.PythonRuntimePath, "Scripts", "pip.exe"))
            || File.Exists(Path.Combine(_paths.PythonRuntimePath, "Lib", "site-packages", "pip", "__init__.py"));
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
            throw new InvalidOperationException(
                $"{operationName} exited with code {process.ExitCode}. See logs for details.");
        }
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
