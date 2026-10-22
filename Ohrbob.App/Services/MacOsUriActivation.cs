using System;
using Avalonia.Controls.ApplicationLifetimes;

namespace Ohrbob.App.Services;

/// <summary>
/// Turns a macOS <c>kAEGetURL</c>/<c>openURLs:</c> activation into a deep link.
/// </summary>
/// <remarks>
/// macOS does not pass a registered <c>ohrbob://</c> link on <c>argv</c>; the
/// native application delegate receives it and Avalonia surfaces it as an
/// <see cref="IActivatableLifetime"/> "OpenUri" activation carrying a
/// <see cref="ProtocolActivatedEventArgs"/>. <see cref="Ohrbob.App.App"/>
/// subscribes once at startup and forwards the link to <see cref="DeepLinkRouter"/>.
/// Windows/Linux keep using the argv path in the desktop head, and the mobile
/// heads have their own delegates, so this is only wired up on macOS.
/// </remarks>
public static class MacOsUriActivation
{
    /// <summary>
    /// Returns the URI string carried by a protocol activation, or <c>null</c>
    /// for any other activation kind. Pure.
    /// </summary>
    public static string? FromActivation(ActivatedEventArgs? args)
        => args is ProtocolActivatedEventArgs protocol ? protocol.Uri.OriginalString : null;

    /// <summary>
    /// Forwards every protocol activation to the shared router. Call once, from
    /// <c>OnFrameworkInitializationCompleted</c> (the documented point that is
    /// still early enough to catch a startup activation).
    /// </summary>
    public static void Subscribe(IActivatableLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        lifetime.Activated += (_, args) =>
        {
            if (FromActivation(args) is { } uri)
            {
                DeepLinkRouter.Publish(uri);
            }
        };
    }
}
