using System;
using Avalonia;
using Ohrbob.App.Services;
using Ohrbob.App.Services.Media;
using Serilog;

namespace Ohrbob.App.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Register the OS media session for this platform before Avalonia starts.
        // Linux uses MPRIS over D-Bus so the sound menu / media keys can control playback.
        if (OperatingSystem.IsLinux())
        {
            MediaSessionHost.Factory = () => MprisMediaSession.Create();
        }

        // 1. Configure global Serilog pipeline
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            #if DEBUG
            .WriteTo.File("logs/app-log.txt", rollingInterval: RollingInterval.Day)
            #endif
            .CreateLogger();
        
        
        try
        {
            Log.Information("Starting application...");

            // 2. Register the SerilogLogSink with Avalonia's Logger
            Avalonia.Logging.Logger.Sink = new SerilogSink(Log.Logger);

            // Windows delivers a registered ohrbob:// link by launching this
            // same executable with the URL appended to argv (see
            // DeepLinkArguments). Registering the scheme is best-effort: a
            // locked-down account must still be able to start the app.
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    WindowsUriScheme.EnsureRegistered(Environment.ProcessPath!);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not register the ohrbob:// scheme");
                }
            }

            // The OS delivers a registered ohrbob:// link as a command-line
            // argument (see the exported .desktop file's "%u").
            var deepLink = DeepLinkArguments.Find(args);

            // 3. Single instance: a later launch forwards its link to the
            // instance that is already running and exits without opening a
            // second window.
            using var instance = new SingleInstance();
            if (!instance.TryAcquire(uri => DeepLinkRouter.Publish(uri)))
            {
                if (deepLink is not null)
                {
                    instance.Send(deepLink);
                    Log.Information("Forwarded the deep link to the running instance");
                }

                return;
            }

            // Cold start: publish before the view model exists — the router's
            // pending slot hands the URI over as soon as MainViewModel
            // subscribes.
            if (deepLink is not null)
            {
                DeepLinkRouter.Publish(deepLink);
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    } 
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
