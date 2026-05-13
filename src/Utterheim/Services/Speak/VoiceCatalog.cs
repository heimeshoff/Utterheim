using Microsoft.Extensions.Logging;
using Utterheim.Services.Tts;
using Utterheim.Services.Voices;

namespace Utterheim.Services.Speak;

/// <summary>
/// Single source of truth for the list of voices the user can pick from.
/// Both the HTTP <c>GET /voices</c> endpoint and the Speak page consume this
/// catalogue, so additions (cloned voices from <see cref="VoiceLibraryService"/>
/// in main-015) appear in both surfaces without parallel wiring.
///
/// <see cref="ListAsync"/> returns engine built-ins ∪ cloned voices, with
/// <see cref="VoiceDescriptor.IsBuiltIn"/> distinguishing the two sources.
/// </summary>
public sealed class VoiceCatalog
{
    private readonly ITtsEngine _engine;
    private readonly VoiceLibraryService _library;
    private readonly ILogger<VoiceCatalog> _logger;
    private readonly object _populationLock = new();
    private bool _initialPopulationFired;

    public VoiceCatalog(
        ITtsEngine engine,
        VoiceLibraryService library,
        ILogger<VoiceCatalog> logger)
    {
        _engine = engine;
        _library = library;
        _logger = logger;

        // Re-fire VoicesChanged whenever the library mutates so consumers
        // (Speak page picker, Voices page list) refresh on add / delete
        // without polling. The Voices page (main-014) already wires
        // OnNavigatedTo + this event handler — this is the call site that
        // makes it non-trivially fire post main-015.
        _library.LibraryChanged += OnLibraryChanged;
    }

    /// <summary>
    /// Return engine built-ins ∪ cloned voices. v1 orders built-ins first
    /// (matching their fixed alphabetical order from <see cref="PocketTtsEngine"/>),
    /// then cloned voices in created-at order.
    /// </summary>
    public async Task<IReadOnlyList<VoiceDescriptor>> ListAsync(CancellationToken ct)
    {
        var builtIns = await _engine.ListVoicesAsync(ct).ConfigureAwait(false);
        var cloned = await _library.ListClonedAsync(ct).ConfigureAwait(false);

        var combined = new List<VoiceDescriptor>(builtIns.Count + cloned.Count);
        combined.AddRange(builtIns);
        foreach (var c in cloned)
        {
            combined.Add(new VoiceDescriptor(
                Id: c.Id,
                Name: c.Name,
                Engine: c.Engine,
                IsBuiltIn: false));
        }

        // Fire VoicesChanged once on the first successful population so any
        // listener that wants to react to the catalog being "ready" can do so
        // without polling. Subsequent fires come via OnLibraryChanged.
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

        return combined;
    }

    /// <summary>
    /// Fired when the catalog mutates. v1 fires it once on first population
    /// and on every <see cref="VoiceLibraryService.LibraryChanged"/> event.
    /// Payload is <see cref="EventArgs.Empty"/> — listeners refetch via
    /// <see cref="ListAsync"/>.
    /// </summary>
    public event EventHandler? VoicesChanged;

    private void OnLibraryChanged(object? sender, LibraryChangedArgs e)
    {
        try { VoicesChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { _logger.LogWarning(ex, "VoicesChanged handler threw."); }
    }
}
