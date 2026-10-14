# Ohrbob App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replicate the Ohrbob audio playback, station switching, and sync capabilities in a responsive Avalonia app with a QR-code based onboarding flow.

**Architecture:** MVVM architecture using CommunityToolkit.Mvvm for fast, lightweight performance, communicating with the Ohrbob.Player REST API.

**Tech Stack:** C#, .NET 10, Avalonia UI, CommunityToolkit.Mvvm, LibVLCSharp / Avalonia.Media.

## Global Constraints
- Target Framework: .NET 10
- Use CommunityToolkit.Mvvm for ViewModels and bindings
- Support deep linking scheme `ohrbob://connect`

---

### Task 1: Add Stations Endpoint to Player API

**Files:**
- Modify: `/home/kaiwegner/Projects/ohrbob2/Ohrbob.Player/Controllers/StreamController.cs`

**Interfaces:**
- Produces: `GET /stream/stations` which returns a JSON list of available stations.

- [ ] **Step 1: Modify StreamController.cs**
Add the `GetStations` method inside `StreamController.cs`.

```csharp
    [HttpGet("stations")]
    public async Task<IActionResult> GetStations()
    {
        var profile = await GetProfile();
        if (profile == null) return Unauthorized();

        var allowedStationIds = profile.AllowedStationIds ?? Array.Empty<string>();
        var stations = await _stations.Find(s => allowedStationIds.Contains(s.Id)).ToListAsync();
        
        return Ok(stations.Select(s => new { id = s.Id, name = s.Name }));
    }
```

- [ ] **Step 2: Verify the endpoint builds and runs**
Run build command on `Ohrbob.Player`:
```bash
dotnet build /home/kaiwegner/Projects/ohrbob2/Ohrbob.Player/Ohrbob.Player.csproj
```

---

### Task 2: Scaffold Avalonia App Project

**Files:**
- Create: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/` project structure

- [ ] **Step 1: Run dotnet new to scaffold the app**
```bash
dotnet new avalonia.mvvm -o /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App --framework net10.0
```

- [ ] **Step 2: Add NuGet Dependencies**
Add CommunityToolkit.Mvvm and LibVLCSharp.Avalonia packages.
```bash
dotnet add /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj package CommunityToolkit.Mvvm
dotnet add /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj package LibVLCSharp.Avalonia
```

---

### Task 3: Implement Ohrbob Client & Configuration Service

**Files:**
- Create: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Services/OhrbobClient.cs`
- Create: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Services/ConfigService.cs`

- [ ] **Step 1: Implement ConfigService.cs**
Manages saving and loading Server URL and StreamToken from local application data.
- [ ] **Step 2: Implement OhrbobClient.cs**
Provides HTTP wrappers for:
  - `GetStationsAsync()` -> calls `/stream/stations`
  - `GetStatusAsync()` -> calls `/stream/status`
  - `PlayStationAsync(stationId)` -> calls `/stream/play-station/{stationId}`
  - `SyncStatusAsync(episodeId, stationId, positionSeconds)` -> calls `/stream/status` (POST)
  - `NextAsync()` -> calls `/stream/next` (POST)

---

### Task 4: Implement ViewModels & Views

**Files:**
- Create/Modify ViewModels and Views inside the `Ohrbob.App` project.

- [ ] **Step 1: Implement MainViewModel.cs & MainWindow.xaml**
Provide UI layouts for:
  - Connect screen (QR code details, manual entry)
  - Stations list screen
  - Media player detail screen (Cover, metadata, Play/Pause, Seek buttons, Next button, Playhead Sync loop)
