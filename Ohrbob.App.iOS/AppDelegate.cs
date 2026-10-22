using Foundation;
using UIKit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.iOS;
using Avalonia.Media;
using Ohrbob.App.Services;

namespace Ohrbob.App.iOS;

// The UIApplicationDelegate for the application. This class is responsible for launching the 
// User Interface of the application, as well as listening (and optionally responding) to 
// application events from iOS.
[Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    public AppDelegate()
    {
        // Avalonia funnels deep links from both the app-delegate callback
        // (`application:openURL:options:`) and the iOS 13+ scene callback
        // (`scene:openURLContexts:`) into this single event, so subscribing
        // here covers cold and warm starts without needing the (non-virtual)
        // OpenUrl override on AvaloniaAppDelegate.
        ((IAvaloniaAppDelegate)this).Activated += OnActivated;
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    private static void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e is ProtocolActivatedEventArgs { Uri: { } uri })
        {
            // ohrbob://... arrives as an absolute URI. On a cold start the view
            // model may not have subscribed to DeepLinkRouter yet; the router
            // holds the URI as pending until it does.
            DeepLinkRouter.Publish(uri.AbsoluteUri);
        }
    }
}
