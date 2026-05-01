namespace Mockingbird.Services.Speak;

/// <summary>
/// One unit of work flowing through the speak queue. Per ADR 0007:
/// queued by HTTP / hotkey / UI; dequeued by the single playback worker.
/// </summary>
public sealed class SpeakRequest
{
    public required string RequestId { get; init; }
    public required string Text { get; init; }
    public required string VoiceId { get; init; }
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Per-request cancellation token source. Stopped when the user hits stop.</summary>
    public CancellationTokenSource Cts { get; } = new();
}
