using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Mockingbird.Services.Http;
using Mockingbird.Services.Tts;

namespace Mockingbird.ViewModels;

/// <summary>
/// Backs the persistent status footer below the navigation shell — answers
/// "is mockingbird healthy?" at a glance. Surfaces the bound HTTP endpoint
/// and a coarse engine-state label that updates live when
/// <see cref="SidecarHost.StateChanged"/> fires.
///
/// Resolves Q4 of main-020 (engine-status interim visibility) — Path A:
/// thin always-visible footer, no signal regression between the placeholder
/// splash and main-017's About page. main-017 may surface a richer panel and
/// optionally remove this footer.
/// </summary>
public sealed partial class EngineStatusViewModel : ObservableObject, IDisposable
{
    private readonly SidecarHost? _sidecar;

    [ObservableProperty]
    private string _httpEndpoint = "—";

    [ObservableProperty]
    private string _engineState = "starting";

    public EngineStatusViewModel(SpeakServer speakServer, SidecarHost? sidecar = null)
    {
        _sidecar = sidecar;
        HttpEndpoint = $"{speakServer.Host}:{speakServer.Port}";

        if (_sidecar is null)
        {
            // Stub-engine path — no sidecar in DI. The state never changes.
            EngineState = "stub";
            return;
        }

        // Seed with whatever the sidecar already reports, then track changes.
        EngineState = SidecarStateLabels.Format(_sidecar.GetStatus().State);
        _sidecar.StateChanged += OnSidecarStateChanged;
    }

    private void OnSidecarStateChanged(object? sender, SidecarStatus status)
    {
        var label = SidecarStateLabels.Format(status.State);
        // The supervisor task raises on its own thread; marshal to the UI dispatcher.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            EngineState = label;
        else
            dispatcher.BeginInvoke(() => EngineState = label);
    }

    public void Dispose()
    {
        if (_sidecar is not null)
            _sidecar.StateChanged -= OnSidecarStateChanged;
    }
}
