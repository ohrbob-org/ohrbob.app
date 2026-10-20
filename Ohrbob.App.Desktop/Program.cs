using System;
using Avalonia;
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
