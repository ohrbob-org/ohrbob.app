using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
