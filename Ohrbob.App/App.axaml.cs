using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ohrbob.App.Services;
using Ohrbob.App.Services.Media;
using Ohrbob.App.ViewModels;
using Ohrbob.App.Views;
using Serilog;

namespace Ohrbob.App;

public partial class App : Application
{
    private MediaSessionCoordinator? _mediaSession;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            AttachMediaSession(viewModel);
            AttachMacOsUriActivation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var viewModel = new MainViewModel();
            AttachMediaSession(viewModel);
            singleViewPlatform.MainView = new MainView
            {
                DataContext = viewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// On macOS a registered <c>ohrbob://</c> link arrives as an Apple Event
    /// rather than on <c>argv</c>, so subscribe to the platform's activatable
    /// lifetime and forward the link to <see cref="DeepLinkRouter"/>. Guarded to
    /// macOS: Linux/Windows use the argv path in the desktop head, and the
    /// mobile heads have their own delegates.
    /// </summary>
    private void AttachMacOsUriActivation()
    {
        if (!OperatingSystem.IsMacOS() ||
            this.TryGetFeature<IActivatableLifetime>() is not { } activatable)
        {
            return;
        }

        MacOsUriActivation.Subscribe(activatable);
    }

    /// <summary>
    /// Connects the view model to the platform's OS media session (Android media
    /// session/notification, Linux MPRIS), if the platform registered one.
    /// </summary>
    private void AttachMediaSession(MainViewModel viewModel)
    {
        var factory = MediaSessionHost.Factory;
        if (factory == null)
        {
            return;
        }

        try
        {
            _mediaSession = new MediaSessionCoordinator(viewModel, factory());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to attach the OS media session");
        }
    }
}
