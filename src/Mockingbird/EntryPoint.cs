using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mockingbird.Services.Audio;
using Mockingbird.Services.Hotkey;
using Mockingbird.Services.Http;
using Mockingbird.Services.Navigation;
using Mockingbird.Services.Settings;
using Mockingbird.Services.Speak;
using Mockingbird.Services.Tts;
using Mockingbird.Services.Voices;
using Mockingbird.ViewModels;
using Mockingbird.ViewModels.Pages;
using Mockingbird.Views;
using Mockingbird.Views.Pages;
using Serilog;
using Serilog.Events;
using Wpf.Ui;
using Wpf.Ui.Appearance;

namespace Mockingbird;

/// <summary>
/// The single thing <see cref="Main"/> creates. Owns the DI container,
/// the IHost lifecycle, the WPF Application, the tray window, and the
/// hotkey detector. Glue, not domain — keep it thin.
/// </summary>
public static class EntryPoint
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 1. Bootstrap path layout (per ADR 0005) — must happen before logging
        //    initialises since the log directory lives under LocalAppData.
        var pathBootstrap = new DataPathService(NullLogger<DataPathService>.Instance);
        pathBootstrap.Load();
        pathBootstrap.EnsureLayout();

        // 2. Serilog configuration (per ADR 0008) — rolling file under LocalAppData,
        //    plus a console sink for DEBUG-time visibility. JSON-on-disk would be
        //    nicer but plain-text rolling is enough for a personal tool's v1.
        var logFilePath = Path.Combine(pathBootstrap.LogsPath, "mockingbird-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            return Run(args, pathBootstrap);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Mockingbird host crashed.");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static int Run(string[] args, DataPathService pathBootstrap)
    {
        // 3. Configuration: appsettings.json + env overrides.
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "MOCKINGBIRD_")
            .AddCommandLine(args)
            .Build();

        var httpHost = config["Mockingbird:Http:Host"] ?? "127.0.0.1";
        var httpPort = int.TryParse(config["Mockingbird:Http:Port"], out var p) ? p : 7223;
        var hotkeyWindowMs = int.TryParse(config["Mockingbird:Hotkey:DoubleTapWindowMs"], out var hk) ? hk : 400;

        // 4. DI container.
        var hostBuilder = Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureAppConfiguration(c =>
            {
                c.AddConfiguration(config);
            })
            .ConfigureServices((ctx, services) =>
            {
                // Use the path service we already loaded (single instance throughout the app).
                services.AddSingleton(pathBootstrap);

                // Engine: real pocket-tts sidecar by default. The stub stays available behind
                // an env flag (MOCKINGBIRD_USE_STUB_ENGINE=1) for offline/CI testing per
                // main-011 acceptance criteria — useful when iterating UI work without
                // booting the full Python runtime.
                var useStub = string.Equals(
                    Environment.GetEnvironmentVariable("MOCKINGBIRD_USE_STUB_ENGINE"),
                    "1", StringComparison.Ordinal);

                services.AddSingleton<PythonRuntimeBootstrapper>();
                services.AddSingleton<SidecarHost>();

                if (useStub)
                {
                    services.AddSingleton<ITtsEngine, StubTtsEngine>();
                }
                else
                {
                    services.AddSingleton<ITtsEngine, PocketTtsEngine>();
                    services.AddHostedService(sp => sp.GetRequiredService<SidecarHost>());
                }

                services.AddSingleton<AudioPlayer>();
                services.AddSingleton<SpeakQueue>();
                services.AddHostedService(sp => sp.GetRequiredService<SpeakQueue>());

                // main-013 in-process seams: VoiceCatalog (shared by HTTP /voices
                // and the page picker), SpeakService (shared by HTTP /speak and
                // the Play button), and UserSettings (Default voice id storage).
                // main-015 adds the voice library backend (cloned-voice persistence
                // via temp+rename per ADR 0005, sidecar /export-voice client per
                // ADR 0015, hosted-service shim that reconciles library.json on
                // startup so the catalog has cloned voices ready before VMs resolve).
                services.AddSingleton<VoiceLibraryService>();
                services.AddSingleton<VoiceCloningClient>();
                services.AddHostedService<VoiceLibraryStartup>();
                services.AddSingleton<VoiceCatalog>();
                services.AddSingleton<SpeakService>();
                services.AddSingleton<UserSettings>();

                // Settings page registry helper (main-016) — Launch-at-startup
                // toggle reads/writes HKCU\…\Run\Mockingbird directly.
                services.AddSingleton<StartupRegistration>();

                // Audio capture services (main-025) — adapted from WhisperHeim per ADR 0006.
                // Registered transient so each cloning session is a fresh capture instance;
                // the page VM resolves them per recording session.
                services.AddTransient<IAudioCaptureService, AudioCaptureService>();
                services.AddTransient<IHighQualityLoopbackService, HighQualityLoopbackService>();

                services.AddSingleton(sp => new SpeakServer(
                    sp.GetRequiredService<SpeakQueue>(),
                    sp.GetRequiredService<SpeakService>(),
                    sp.GetRequiredService<VoiceCatalog>(),
                    sp.GetRequiredService<ILogger<SpeakServer>>(),
                    sp.GetService<SidecarHost>(),
                    httpHost,
                    httpPort));
                services.AddHostedService(sp => sp.GetRequiredService<SpeakServer>());

                services.AddSingleton(sp => new DoubleTapDetector(
                    sp.GetRequiredService<ILogger<DoubleTapDetector>>(),
                    NativeMethods.VK_RCONTROL,
                    hotkeyWindowMs));

                // Navigation shell (main-020, ADR 0009).
                services.AddSingleton<IPageService, PageService>();
                services.AddSingleton<INavigationService>(sp => new Wpf.Ui.NavigationService(sp));

                // ContentDialog host service (main-026) — owns the singleton
                // ContentPresenter where in-window confirm/error dialogs render.
                // MainWindow wires the presenter at Loaded; consumers (page VMs)
                // resolve the service and call ShowAsync.
                services.AddSingleton<IContentDialogService, ContentDialogService>();

                // Status-footer view-model — singleton so it survives navigation
                // between pages and keeps tracking sidecar state changes.
                services.AddSingleton(sp => new EngineStatusViewModel(
                    sp.GetRequiredService<SpeakServer>(),
                    sp.GetService<SidecarHost>()));

                // Pages registered transient so each navigation gets a fresh
                // instance (the wpfui-canonical model). View-models match.
                // EngineStatusCardViewModel (main-032) is composed onto the
                // SettingsPageViewModel and registered transient for the same
                // reason — fresh sub-VM per Settings-page resolution, mirroring
                // the VoiceCloningViewModel pattern.
                services.AddTransient<SpeakPageViewModel>();
                services.AddTransient<VoiceCloningViewModel>();
                services.AddTransient<VoicesPageViewModel>();
                services.AddTransient<EngineStatusCardViewModel>();
                services.AddTransient<SettingsPageViewModel>();
                services.AddTransient<AboutPageViewModel>();
                services.AddTransient<SpeakPage>();
                services.AddTransient<VoicesPage>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<AboutPage>();
            });

        using var host = hostBuilder.Build();

        // 5. Run the first-launch bootstrap dialog if the runtime isn't installed yet
        //    (per ADR 0008). The bootstrapper persists progress in bootstrap-state.json
        //    so a half-finished install resumes where it left off on the next launch.
        //    The stub engine path skips this entirely.
        var useStub = string.Equals(
            Environment.GetEnvironmentVariable("MOCKINGBIRD_USE_STUB_ENGINE"),
            "1", StringComparison.Ordinal);
        if (!useStub)
        {
            var bootstrapper = host.Services.GetRequiredService<PythonRuntimeBootstrapper>();
            if (!bootstrapper.IsBootstrapped)
            {
                EnsureApplication();
                var dialogLogger = host.Services.GetRequiredService<ILogger<BootstrapDialog>>();
                var dialog = new BootstrapDialog(bootstrapper, dialogLogger);
                dialog.ShowDialog();
                if (!dialog.BootstrapSucceeded)
                {
                    Log.Warning("First-run bootstrap did not complete — exiting.");
                    return 0;
                }
                Log.Information("First-run bootstrap completed.");
            }
        }

        // 6. Start hosted services (queue worker + HTTP + sidecar host when not stubbed).
        host.Start();

        // 7. WPF Application + tray window.
        EnsureApplication();
        var app = Application.Current;

        var queue = host.Services.GetRequiredService<SpeakQueue>();
        var hotkey = host.Services.GetRequiredService<DoubleTapDetector>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var pageService = host.Services.GetRequiredService<IPageService>();
        var engineStatus = host.Services.GetRequiredService<EngineStatusViewModel>();
        var contentDialogService = host.Services.GetRequiredService<IContentDialogService>();
        var userSettings = host.Services.GetRequiredService<UserSettings>();

        // main-029: apply the persisted appearance mode before MainWindow.Show() so
        // the initial paint matches the user's preference (no Light→Dark flicker).
        // The in-memory default (Light) covers fresh installs whose settings.json
        // has no appearanceMode field. Per ADR 0019 the default is not persisted on
        // read; only an explicit Settings → Appearance toggle writes the value.
        ApplyAppearanceMode(userSettings.AppearanceMode);

        Action exitAction = () => app.Dispatcher.BeginInvoke(() => app.Shutdown());

        var window = new MainWindow(queue, exitAction, pageService, engineStatus, contentDialogService);

        // main-016: honour UserSettings.StartMinimised — when true, the window
        // stays hidden in the tray on launch instead of activating. The tray
        // icon still appears (it's part of MainWindow.xaml) so the user can
        // restore via Show window / left-click. The HTTP / hotkey / sidecar
        // surfaces are independent of window visibility.
        if (userSettings.StartMinimised)
        {
            logger.LogInformation("StartMinimised is on — launching hidden in the tray.");
            // Show + Hide so the WPF message loop fully initialises the
            // tray:NotifyIcon (it relies on the window's HWND), then immediately
            // returns to a hidden state. Calling only Hide() with the window
            // never having been shown leaves the tray icon dormant on some
            // Windows builds.
            window.Show();
            window.Hide();
        }
        else
        {
            window.Show();
        }

        hotkey.DoubleTapped += (_, _) =>
        {
            logger.LogInformation("Stop hotkey (double-tap RCtrl) — draining queue.");
            queue.StopAndDrain();
        };
        hotkey.Register();

        // Surface unhandled dispatcher exceptions to logs rather than crashing silently.
        app.DispatcherUnhandledException += (s, e) =>
        {
            logger.LogError(e.Exception, "Unhandled WPF dispatcher exception.");
            e.Handled = true;
        };

        // 8. Run the WPF message loop. Returns when Application.Shutdown() fires
        //    (from the tray "Exit" menu).
        int exitCode = app.Run();

        // 9. Tear down: hotkey hook, hosted services (queue worker + HTTP).
        hotkey.Unregister();
        try
        {
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error stopping host.");
        }

        return exitCode;
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            var app = new App();
            app.InitializeComponent();
        }
    }

    /// <summary>
    /// main-029 — apply the user's persisted appearance preference via
    /// <see cref="ApplicationThemeManager"/>. Called once at startup before
    /// the main window is shown; the Settings page picker calls the same
    /// API live for in-app toggles. Keeping the dispatch logic here means
    /// startup and the picker hand off to a single helper.
    /// </summary>
    internal static void ApplyAppearanceMode(AppearanceMode mode)
    {
        switch (mode)
        {
            case AppearanceMode.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case AppearanceMode.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            case AppearanceMode.System:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }
}

/// <summary>Marker class so ILogger&lt;Program&gt; resolves nicely.</summary>
internal sealed class Program { }
