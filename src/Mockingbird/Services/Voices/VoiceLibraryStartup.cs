using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mockingbird.Services.Voices;

/// <summary>
/// Hosted-service shim that runs <see cref="VoiceLibraryService.LoadAsync"/>
/// once at host startup. Per main-015 / Q4 the reconciliation must happen
/// before page VMs resolve so the catalog has the cloned-voice list ready
/// when the user navigates to the Voices page.
///
/// Pure plumbing — no domain logic lives here.
/// </summary>
public sealed class VoiceLibraryStartup : IHostedService
{
    private readonly VoiceLibraryService _library;
    private readonly ILogger<VoiceLibraryStartup> _logger;

    public VoiceLibraryStartup(VoiceLibraryService library, ILogger<VoiceLibraryStartup> logger)
    {
        _library = library;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
