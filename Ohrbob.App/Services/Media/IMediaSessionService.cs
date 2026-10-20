using System;

namespace Ohrbob.App.Services.Media;

/// <summary>
/// Platform bridge to the operating system's media playback API. Implementations
/// mirror the app's playback state onto the OS (Android media session/notification,
/// Linux MPRIS) and raise the events below when the user drives playback from there.
/// </summary>
public interface IMediaSessionService : IDisposable
{
    /// <summary>Push the latest playback state to the OS media controls.</summary>
    void Update(MediaSessionInfo info);

    event EventHandler? PlayRequested;
    event EventHandler? PauseRequested;
    event EventHandler? TogglePlayPauseRequested;
    event EventHandler? NextRequested;
    event EventHandler? PreviousRequested;

    /// <summary>Seek to an absolute position, in seconds.</summary>
    event EventHandler<int>? SeekToRequested;

    /// <summary>Seek relative to the current position, in seconds (may be negative).</summary>
    event EventHandler<int>? SeekByRequested;
}
