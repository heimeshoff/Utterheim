using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mockingbird.Services.Hotkey;
using Mockingbird.Services.Http;
using Mockingbird.Services.Settings;
using Mockingbird.Services.Speak;
using Mockingbird.Services.Tts;
using Mockingbird.Views;
using Serilog;
using Serilog.Events;

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
                services.AddSingleton<ITtsEngine, StubTtsEngine>();
                services.AddSingleton<AudioPlayer>();
                services.AddSingleton<SpeakQueue>();
                services.AddHostedService(sp => sp.GetRequiredService<SpeakQueue>());

                services.AddSingleton(sp => new SpeakServer(
                    sp.GetRequiredService<SpeakQueue>(),
                    sp.GetRequiredService<ITtsEngine>(),
                    sp.GetRequiredService<ILogger<SpeakServer>>(),
                    httpHost,
                    httpPort));
                services.AddHostedService(sp => sp.GetRequiredService<SpeakServer>());

                services.AddSingleton(sp => new DoubleTapDetector(
                    sp.GetRequiredService<ILogger<DoubleTapDetector>>(),
                    NativeMethods.VK_LCONTROL,
                    hotkeyWindowMs));
            });

        using var host = hostBuilder.Build();

        // 5. Show the bootstrap dialog on first run (per ADR 0008).
        //    Marker file: <LocalAppData>\Mockingbird\bootstrap-state.json with {done:true}.
        var bootstrapMarker = pathBootstrap.BootstrapStatePath;
        if (!File.Exists(bootstrapMarker))
        {
            // We need an Application instance to show a Window. Create the WPF App
            // up-front so dialogs can use it; we'll keep using the same App below.
            EnsureApplication();
            var dialog = new BootstrapDialog();
            dialog.ShowDialog();
            File.WriteAllText(bootstrapMarker, "{\"done\":true}");
            Log.Information("Bootstrap marker written to {Path}", bootstrapMarker);
        }

        // 6. Start hosted services (queue worker + HTTP).
        host.Start();

        // 7. WPF Application + tray window.
        EnsureApplication();
        var app = Application.Current;

        var queue = host.Services.GetRequiredService<SpeakQueue>();
        var hotkey = host.Services.GetRequiredService<DoubleTapDetector>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        Action exitAction = () => app.Dispatcher.BeginInvoke(() => app.Shutdown());

        var window = new MainWindow(queue, exitAction);
        window.Show();

        hotkey.DoubleTapped += (_, _) =>
        {
            logger.LogInformation("Stop hotkey (double-tap LCtrl) — draining queue.");
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
}

/// <summary>Marker class so ILogger&lt;Program&gt; resolves nicely.</summary>
internal sealed class Program { }
