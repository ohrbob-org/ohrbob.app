# Ohrbob LibVLCSharp Audio Playback Integration Spec

## 1. Goal
Implement a robust, cross-platform audio playback engine using `LibVLCSharp` to replace both the buggy `Plugin.SimpleAudioPlayer` and the Linux-specific `ffplay` subprocess spawning. This provides a unified, production-grade playback experience across Windows, macOS, and Linux.

## 2. Architecture & Design

### Interface `IAudioPlayer`
We define a decoupled interface `IAudioPlayer` in `Ohrbob.App/Services/IAudioPlayer.cs`:
```csharp
using System;

namespace Ohrbob.App.Services;

public interface IAudioPlayer : IDisposable
{
    bool IsPlaying { get; }
    int PositionSeconds { get; }
    int DurationSeconds { get; }
    string? CurrentUrl { get; }

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

### Implementation `VlcAudioPlayer`
We implement `VlcAudioPlayer` in `Ohrbob.App/Services/VlcAudioPlayer.cs`:
- On static construction or during the first instance creation, call `Core.Initialize()`.
- **Linux Dependency Detection**:
  - Before initializing, on Linux systems, detect if `libvlc` is installed (e.g. check for common library files like `libvlc.so`, `libvlc.so.5` in standard system library paths, or try a `try-catch` around `Core.Initialize()`).
  - If LibVLC fails to load or is not found, expose a flag or throw a specific exception.
  - The application UI can detect this and display a user-friendly modal/prompt/message asking the user to install the package (e.g. `sudo apt install libvlc-dev` or `sudo apt install vlc`).
- Maintain single instances of `LibVLC` and `MediaPlayer`.
- Handle native events from `MediaPlayer`:
  - `TimeChanged`: Trigger standard `.NET` `PositionChanged` event.
  - `LengthChanged`: Trigger standard `.NET` `DurationChanged` event.
  - `Playing`/`Paused`/`Stopped`/`EndReached`: Update `IsPlaying` status and raise `PlaybackStateChanged`.
- Implement thread-safe disposal of all native resources.

### ViewModel Integration
In `Ohrbob.App/ViewModels/MainViewModel.cs`:
- Replace `_player` (of type `ISimpleAudioPlayer`) and `_ffplayProcess` with an instance of `IAudioPlayer`.
- Wire up the player's events (`PositionChanged`, `DurationChanged`, `PlaybackStateChanged`) to update UI properties (`PositionSeconds`, `DurationSeconds`, `IsPlaying`) safely on the UI/dispatcher thread.
- Eliminate platform-specific branching (`_isLinux` checks) for audio operations, simplifying the codebase drastically.
