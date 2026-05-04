using Microsoft.Extensions.Logging;
using Mockingbird.Services.Tts;

namespace Mockingbird.Services.Speak;

/// <summary>
/// Single source of truth for the list of voices the user can pick from.
/// Both the HTTP <c>GET /voices</c> endpoint and the Speak page consume this
/// catalogue, so future additions (cloned voices from <c>library.json</c> in
/// main-015) appear in both surfaces without parallel wiring.
///
/// v1 delegates to <see cref="ITtsEngine.ListVoicesAsync"/> for the engine's
/// built-in voices. main-015 will fold in cloned-voice rows.
/// </summary>
public sealed class VoiceCatalog
{
    private readonly ITtsEngine _engine;
    private readonly ILogger<VoiceCatalog> _logger;
    private readonly object _populationLock = new();
    private bool _initialPopulationFired;

    public VoiceCatalog(ITtsEngine engine, ILogger<VoiceCatalog> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// Return the current list of voices. v1 is a thin pass-through to the
    /// engine; main-015 will compose engine voices with cloned voices here.
    /// </summary>
    public async Task<IReadOnlyList<VoiceDescriptor>> ListAsync(CancellationToken ct)
    {
        var voices = await _engine.ListVoicesAsync(ct).ConfigureAwait(false);

        // Fire VoicesChanged once on the first successful population so any
        // listener that wants to react to the catalog being "ready" can do so
        // without polling. main-015 will fire it on save/delete instead.
        bool fire = false;
        lock (_populationLock)
        {
            if (!_initialPopulationFired)
            {
                _initialPopulationFired = true;
                fire = true;
            }
        }
        if (fire)
        {
            try { VoicesChanged?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { _logger.LogWarning(ex, "VoicesChanged handler threw."); }
        }

        return voices;
    }

    /// <summary>
    /// Fired when the catalog mutates. v1 fires it once on first population;
    /// main-015 will fire on cloned-voice save/delete.
    /// </summary>
    public event EventHandler? VoicesChanged;
}
