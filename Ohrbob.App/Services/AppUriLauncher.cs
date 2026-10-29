using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Ohrbob.App.Services;

/// <summary>
/// Launches URIs through the current Avalonia application's top level, which
/// routes them to the OS browser. Falls back to a no-op when no top level is
/// available (e.g. headless tests or design mode).
/// </summary>
public class AppUriLauncher : IUriLauncher
{
    public async Task LaunchAsync(Uri uri)
    {
        try
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
                return;
            }
        }
        catch
        {
            // Fallback to Avalonia's launcher if Process.Start fails
        }

        var topLevel = ResolveTopLevel();
        if (topLevel?.Launcher is null)
        {
            return;
        }

        await topLevel.Launcher.LaunchUriAsync(uri);
    }

    private static TopLevel? ResolveTopLevel()
    {
        switch (Application.Current?.ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                return desktop.MainWindow;
            case ISingleViewApplicationLifetime singleView:
                return singleView.MainView is { } view ? TopLevel.GetTopLevel(view) : null;
            default:
                return null;
        }
    }
}
