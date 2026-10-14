# Ohrbob Native Audio Playback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement actual native audio output in `Ohrbob.App` using `Plugin.SimpleAudioPlayer` across Windows, iOS, Android, macOS, and Linux.

**Architecture:** 
- Install `Plugin.SimpleAudioPlayer` NuGet package.
- Initialize and trigger the cross-platform native player inside the `MainViewModel` lifecycle (triggering on play/pause/skip and updates of `CurrentEpisodeStreamUri`).
- Synchronize native player playhead location with the view model's sync timers.

**Tech Stack:** C#, Avalonia, Plugin.SimpleAudioPlayer.

## Global Constraints
- Target Framework: .NET 10
- Maintain full compatibility with headless tests (ensure tests can run without initializing native audio devices)

---

### Task 1: Install Plugin.SimpleAudioPlayer and Clean Up LibVLC

**Files:**
- Modify: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj`

- [ ] **Step 1: Remove LibVLCSharp.Avalonia and add Plugin.SimpleAudioPlayer**
Add package `Plugin.SimpleAudioPlayer` to project:
```bash
dotnet add /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj package Plugin.SimpleAudioPlayer --version 1.6.0
dotnet remove /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj package LibVLCSharp.Avalonia
```

---

### Task 2: Implement Cross-Platform Native Playback in MainViewModel

**Files:**
- Modify: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Integrate the SimpleAudioPlayer**
Initialize `ISimpleAudioPlayer` from `Plugin.SimpleAudioPlayer.CrossSimpleAudioPlayer.Current` inside `MainViewModel`:
  - When `IsPlaying` becomes true, if the player is not loaded or the stream URL changed, load the `CurrentEpisodeStreamUri` as an audio stream/source and call `Play()`.
  - If the player is loaded and paused, resume playback.
  - When `IsPlaying` becomes false, call `Pause()`.
  - When seeking, update `player.Seek(PositionSeconds)`.
  - Make sure to wrap audio initialization inside safe `try...catch` blocks to gracefully fall back on headless environments or machines without sound hardware.

- [ ] **Step 2: Build the project and verify it compiles**
Run:
```bash
dotnet build /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj
```
Expected: SUCCESS
