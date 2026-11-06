using System;
using System.Threading.Tasks;

namespace Ohrbob.App.Services;

public interface IAudioPlayer : IDisposable
{
    bool IsPlaying { get; }
    int PositionSeconds { get; }
    int DurationSeconds { get; }
    string? CurrentUrl { get; }

    /// <summary>Linear volume in the range 0..1.</summary>
    float Volume { get; set; }

    Task Load(string url);
    void Play();
    void Pause();
    void Stop();
    void Seek(int positionSeconds);

    event EventHandler<int>? PositionChanged;
    event EventHandler<int>? DurationChanged;
    event EventHandler<bool>? PlaybackStateChanged;

    /// <summary>Raised once when the loaded stream reaches its end.</summary>
    event EventHandler? PlaybackEnded;
}
