using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using LibVLCSharp.Shared;

namespace Ohrbob.App.Services;

public class VlcAudioPlayer : IAudioPlayer
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private bool _isDisposed;
    private readonly HttpClient _httpClient = new();
    private Stream? _currentStream;

    public bool IsPlaying { get; private set; }
    public int PositionSeconds { get; private set; }
    public int DurationSeconds { get; private set; }
    public string? CurrentUrl { get; private set; }
    public bool IsLibVlcInstalled { get; private set; } = true;

    public event EventHandler<int>? PositionChanged;
    public event EventHandler<int>? DurationChanged;
    public event EventHandler<bool>? PlaybackStateChanged;

    public VlcAudioPlayer()
    {
        InitializeVlc();
    }

    private void InitializeVlc()
    {
        try
        {
            Core.Initialize();
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);

            _mediaPlayer.TimeChanged += OnTimeChanged;
            _mediaPlayer.LengthChanged += OnLengthChanged;
            _mediaPlayer.Playing += OnPlaying;
            _mediaPlayer.Paused += OnPaused;
            _mediaPlayer.Stopped += OnStopped;
            _mediaPlayer.EndReached += OnEndReached;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"VLC initialization failed: {ex.Message}");
            IsLibVlcInstalled = false;
        }
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        PositionSeconds = (int)(e.Time / 1000);
        PositionChanged?.Invoke(this, PositionSeconds);
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        DurationSeconds = (int)(e.Length / 1000);
        DurationChanged?.Invoke(this, DurationSeconds);
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        IsPlaying = true;
        PlaybackStateChanged?.Invoke(this, true);
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        IsPlaying = false;
        PlaybackStateChanged?.Invoke(this, false);
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        IsPlaying = false;
        PlaybackStateChanged?.Invoke(this, false);
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        IsPlaying = false;
        PlaybackStateChanged?.Invoke(this, false);
    }

    public async void Load(string url)
    {
        if (!IsLibVlcInstalled || _libVLC == null || _mediaPlayer == null) return;

        Stop();
        CurrentUrl = url;

        try
        {
            _currentStream?.Dispose();
            _currentStream = await _httpClient.GetStreamAsync(url);
            using var media = new Media(_libVLC, new StreamMediaInput(_currentStream));
            _mediaPlayer.Media = media;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load stream via HttpClient: {ex.Message}");
        }
    }

    public void Play()
    {
        if (!IsLibVlcInstalled || _mediaPlayer == null) return;
        _mediaPlayer.Play();
    }

    public void Pause()
    {
        if (!IsLibVlcInstalled || _mediaPlayer == null) return;
        _mediaPlayer.Pause();
    }

    public void Stop()
    {
        if (!IsLibVlcInstalled || _mediaPlayer == null) return;
        _mediaPlayer.Stop();
        PositionSeconds = 0;
        PositionChanged?.Invoke(this, 0);
    }

    public void Seek(int positionSeconds)
    {
        if (!IsLibVlcInstalled || _mediaPlayer == null) return;
        _mediaPlayer.Time = positionSeconds * 1000L;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _currentStream?.Dispose();
        _httpClient.Dispose();

        if (_mediaPlayer != null)
        {
            _mediaPlayer.TimeChanged -= OnTimeChanged;
            _mediaPlayer.LengthChanged -= OnLengthChanged;
            _mediaPlayer.Playing -= OnPlaying;
            _mediaPlayer.Paused -= OnPaused;
            _mediaPlayer.Stopped -= OnStopped;
            _mediaPlayer.EndReached -= OnEndReached;
            _mediaPlayer.Dispose();
        }
        _libVLC?.Dispose();
    }
}
