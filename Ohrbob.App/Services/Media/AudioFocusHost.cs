using System;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Services.Media;

/// <summary>
/// Entry point for platform projects to plug in audio-focus / interruption handling.
/// A platform sets <see cref="Factory"/> before Avalonia initialises the app; the shared
/// <c>App</c> then attaches the returned handler to the main view model.
/// </summary>
public static class AudioFocusHost
{
    public static Func<MainViewModel, IDisposable>? Factory { get; set; }
}
