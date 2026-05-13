namespace Utterheim.Services.Tts;

/// <summary>
/// Coarse lifecycle states for the pocket-tts Python sidecar. Surfaced via
/// <c>GET /status</c> so callers (and the future tray UI) can tell why a
/// speak request might be queued or failing.
/// </summary>
public enum SidecarState
{
    /// <summary>Process not started yet — engine constructed but no synthesis attempted.</summary>
    NotStarted,

    /// <summary>Spawn issued; waiting for /health to return 200.</summary>
    Starting,

    /// <summary>/health reports healthy; synthesis requests are accepted.</summary>
    Running,

    /// <summary>Process exited or /health is failing; backoff timer between restart attempts.</summary>
    Restarting,

    /// <summary>Permanently failed (e.g. python.exe missing) — no further restarts queued.</summary>
    Failed,

    /// <summary>Host shutting down — sidecar is being terminated cleanly.</summary>
    Stopping,
}

/// <summary>
/// Snapshot of sidecar health for the <c>/status</c> endpoint.
/// </summary>
/// <param name="State">Coarse lifecycle bucket.</param>
/// <param name="Healthy">True iff the most recent <c>/health</c> probe succeeded.</param>
/// <param name="Port">Loopback TCP port the sidecar is bound to (0 if not yet known).</param>
/// <param name="LastError">Most recent error message surfaced by the sidecar host, or null.</param>
public sealed record SidecarStatus(
    SidecarState State,
    bool Healthy,
    int Port,
    string? LastError);
