namespace Ohrbob.App.Services.Media;

/// <summary>
/// Snapshot of the current playback that the OS media controls (lock screen,
/// notification, MPRIS) should display.
/// </summary>
public sealed record MediaSessionInfo(
    string Title,
    string Artist,
    string? CoverUrl,
    bool IsPlaying,
    int PositionSeconds,
    int DurationSeconds);
