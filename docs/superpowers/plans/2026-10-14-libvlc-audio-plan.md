# LibVLCSharp Audio Playback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a robust cross-platform audio player using LibVLCSharp, replacing SimpleAudioPlayer/ffplay and adding Linux package detection with user prompts.

**Architecture:** Model-View-ViewModel (MVVM) where `MainViewModel` consumes `IAudioPlayer`. The `VlcAudioPlayer` class implements `IAudioPlayer` and translates native LibVLC events into clean .NET events on the UI thread.

**Tech Stack:** C#, .NET 10, Avalonia, LibVLCSharp.

## Global Constraints
- Target Framework: `net10.0`
- LibVLCSharp version: `3.10.2`
- Project path: `/home/kaiwegner/Projects/ohrbob2.app`

---

### Task 1: Create IAudioPlayer Interface

**Files:**
- Create: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Services/IAudioPlayer.cs`

**Interfaces:**
- Produces: `IAudioPlayer` interface

- [ ] **Step 1: Write IAudioPlayer interface**
Write `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Services/IAudioPlayer.cs` with the following content:

```csharp
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
```

- [ ] **Step 2: Verify compiling**
Run build to verify compiling:
`dotnet build /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj`
Expected: SUCCESS

---

### Task 2: Implement VlcAudioPlayer with Linux Dependency Detection

**Files:**
- Create: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Services/VlcAudioPlayer.cs`

**Interfaces:**
- Consumes: `IAudioPlayer`
- Produces: `VlcAudioPlayer` class implementing `IAudioPlayer`

- [ ] **Step 1: Create the VlcAudioPlayer class**
Write `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Services/VlcAudioPlayer.cs` with native LibVLC initialization, try-catch safety for missing shared library on Linux, and event mapping.

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace Ohrbob.App.Services;

public class VlcAudioPlayer : IAudioPlayer
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private bool _isDisposed;

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
            Debug.WriteLine($"VLC initialization failed: {ex.Message}");
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

    public void Load(string url)
    {
        if (!IsLibVlcInstalled || _libVLC == null || _mediaPlayer == null) return;

        Stop();
        CurrentUrl = url;
        using var media = new Media(_libVLC, new Uri(url));
        _mediaPlayer.Media = media;
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
```

- [ ] **Step 2: Build the project**
Run build:
`dotnet build /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj`
Expected: SUCCESS

---

### Task 3: Integrate IAudioPlayer inside MainViewModel and Show Missing LibVLC Alert

**Files:**
- Modify: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `IAudioPlayer` from `VlcAudioPlayer`

- [ ] **Step 1: Replace simple player / ffplay with VlcAudioPlayer**
Edit `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/ViewModels/MainViewModel.cs` to inject `IAudioPlayer`, wire up its events to dispatch state changes to UI-friendly properties, and set a friendly error prompt if `_player.IsLibVlcInstalled` is false.

- [ ] **Step 2: Update View to display LibVLC missing warning**
If the dependency detection fails (e.g. `!_player.IsLibVlcInstalled`), set a prominent alert in `ConnectionErrorMessage` or a dedicated warning so Linux users see instructions to run `sudo apt install libvlc-dev`.

- [ ] **Step 3: Run project build**
Verify everything compiles:
`dotnet build /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj`
Expected: SUCCESS
