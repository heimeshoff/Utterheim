using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mockingbird.Services.Settings;

namespace Mockingbird.Services.Voices;

/// <summary>
/// Hosted-service shim that runs <see cref="VoiceLibraryService.LoadAsync"/>
/// once at host startup. Per main-015 / Q4 the reconciliation must happen
/// before page VMs resolve so the catalog has the cloned-voice list ready
/// when the user navigates to the Voices page.
///
/// Per main-031 it also subscribes to <see cref="DataPathService.DataPathChanged"/>
/// and re-runs <see cref="VoiceLibraryService.LoadAsync"/> on every event so a
/// runtime data-path swap surfaces in the catalog without app restart. The
/// existing <c>LibraryChanged → VoicesChanged</c> chain (main-014 / main-015)
/// then refreshes the Voices page rows live. The subscription is detached on
/// <see cref="StopAsync"/> so the hosted-service lifecycle owns it cleanly.
///
/// Pure plumbing — no domain logic lives here.
/// </summary>
public sealed class VoiceLibraryStartup : IHostedService
{
    private readonly VoiceLibraryService _library;
    private readonly DataPathService _paths;
    private readonly ILogger<VoiceLibraryStartup> _logger;

    public VoiceLibraryStartup(
        VoiceLibraryService library,
        DataPathService paths,
        ILogger<VoiceLibraryStartup> logger)
    {
        _library = library;
        _paths = paths;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe before the initial LoadAsync so an unlikely-but-possible
        // SetDataPath invocation racing with startup doesn't drop a reload.
        _paths.DataPathChanged += OnDataPathChanged;

        try
        {
            await _library.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Don't fail host start because of a corrupt library.json — the
            // service starts with an empty in-memory index, the user can
            // still use built-in voices, and the next clone will overwrite
            // library.json cleanly.
            _logger.LogError(ex, "VoiceLibraryService.LoadAsync failed; continuing with empty in-memory index.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _paths.DataPathChanged -= OnDataPathChanged;
        return Task.CompletedTask;
    }

    /// <summary>
    /// React to a runtime data-path swap by reloading the voice library from
    /// the new <see cref="DataPathService.VoicesPath"/>. Fire-and-forget on
    /// the threadpool — the event is raised from the UI dispatcher and we
    /// don't want to block the user's click. Errors are logged but
    /// swallowed: an unreadable target folder leaves the in-memory index
    /// intact and the user sees an empty Cloned section, which matches the
    /// pointer-swap "old voices stay behind" semantics.
    /// </summary>
    private void OnDataPathChanged(object? sender, string newPath)
    {
        _logger.LogInformation(
            "DataPathChanged → reloading voice library from '{Path}'.", newPath);

        _ = Task.Run(async () =>
        {
            try
            {
                await _library.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Voice library reload after DataPathChanged failed; in-memory index left intact.");
            }
        });
    }
}
