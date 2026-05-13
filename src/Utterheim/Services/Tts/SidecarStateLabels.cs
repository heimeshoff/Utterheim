namespace Utterheim.Services.Tts;

/// <summary>
/// Centralised friendly-label mapping for <see cref="SidecarState"/>. Shared
/// by the persistent footer (<c>EngineStatusViewModel</c>) and the About page
/// (<c>AboutPageViewModel</c>) so the two surfaces can't drift apart.
///
/// Per main-017 §Tactical pointers — refactored out of <c>EngineStatusViewModel</c>.
/// </summary>
public static class SidecarStateLabels
{
    /// <summary>
    /// Maps a coarse <see cref="SidecarState"/> to the user-facing label.
    /// <c>Running</c> reports as <c>"pocket-tts"</c> because the engine name is
    /// the most meaningful signal once it's up; transitional / failed states
    /// expose the lifecycle bucket itself.
    /// </summary>
    public static string Format(SidecarState state) => state switch
    {
        SidecarState.NotStarted => "not started",
        SidecarState.Starting => "starting",
        SidecarState.Running => "pocket-tts",
        SidecarState.Restarting => "restarting",
        SidecarState.Failed => "failed",
        SidecarState.Stopping => "stopping",
        _ => state.ToString().ToLowerInvariant(),
    };
}
