using System;

namespace Ohrbob.App.Services.Media;

/// <summary>
/// Entry point for platform projects to plug in their <see cref="IMediaSessionService"/>.
/// A platform sets <see cref="Factory"/> before Avalonia initialises the app; the shared
/// <c>App</c> then wires the returned service to the main view model.
/// </summary>
public static class MediaSessionHost
{
    public static Func<IMediaSessionService>? Factory { get; set; }
}
