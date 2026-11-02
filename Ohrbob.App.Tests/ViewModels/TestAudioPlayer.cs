using System;
using System.Threading.Tasks;
using Ohrbob.App.Services;

namespace Ohrbob.App.Tests.ViewModels;

/// <summary>Audio player double that records events without touching native audio.</summary>
internal sealed class TestAudioPlayer : IAudioPlayer
{
    public bool IsPlaying { get; private set; }
    public int PositionSeconds { get; private set; }
    public int DurationSeconds { get; private set; }
    public string? CurrentUrl { get; private set; }

    public Task Load(string url)
    {
        CurrentUrl = url;
        return Task.CompletedTask;
    }

    public void Play()
    {
        IsPlaying = true;
        PlaybackStateChanged?.Invoke(this, true);
    }

    public void Pause()
    {
        IsPlaying = false;
        PlaybackStateChanged?.Invoke(this, false);
    }

    public void Stop() => IsPlaying = false;

    public void Seek(int positionSeconds) => PositionSeconds = positionSeconds;

    public event EventHandler<int>? PositionChanged;
    public event EventHandler<int>? DurationChanged;
    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler? PlaybackEnded;

    public void RaisePosition(int seconds)
    {
        PositionSeconds = seconds;
        PositionChanged?.Invoke(this, seconds);
    }

    public void RaiseEnded()
    {
        IsPlaying = false;
        PlaybackStateChanged?.Invoke(this, false);
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() { }
}
