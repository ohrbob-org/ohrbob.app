using System;

namespace Ohrbob.App.Services;

public interface IAudioPlayer : IDisposable
{
    bool IsPlaying { get; }
    int PositionSeconds { get; }
    int DurationSeconds { get; }
    string? CurrentUrl { get; }
    bool IsLibVlcInstalled { get; }

    void Load(string url);
    void Play();
    void Pause();
    void Stop();
    void Seek(int positionSeconds);

    event EventHandler<int>? PositionChanged;
    event EventHandler<int>? DurationChanged;
    event EventHandler<bool>? PlaybackStateChanged;
}
