# Multi-Target Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert Ohrbob.App into a multi-platform Avalonia application (Desktop, Android, iOS) and integrate the Soundflow library for HTTP MP3 playback.

**Architecture:** We will adopt a shared-core model. `Ohrbob.App` becomes the core library containing views, viewmodels, QR scanning, and Soundflow playback logic. We will create platform-specific launcher projects (`Ohrbob.App.Desktop`, `Ohrbob.App.Android`, `Ohrbob.App.iOS`) that reference the core library.

**Tech Stack:** Avalonia UI, .NET 10, SoundFlow, CommunityToolkit.Mvvm

**Spec:** docs/superpowers/specs/2026-10-15-multi-target-migration-design.md

## Global Constraints
- The app acts strictly as a client.
- No browser/WASM target.
- Reuse existing QR code/server discovery logic.

## Review Focus
- **QR Code Scanning breaks on mobile:** Ensure camera permissions and platform-specific view integration are handled correctly in the wrapper projects (or core if abstracted).
- **Soundflow HTTP streaming fails:** Ensure internet permissions are requested on Android/iOS.
- **App fails to start due to missing entry point:** Ensure platform wrappers correctly instantiate `Ohrbob.App.App` and the core project `OutputType` is changed to Library.

---

### Task 1: Convert Core Project & Add Dependencies

**Files:**
- Modify: `Ohrbob.App/Ohrbob.App.csproj`

**Interfaces:**
- Produces: A compiled Avalonia library that can be referenced by platform wrappers.

- [ ] **Step 1: Modify `Ohrbob.App/Ohrbob.App.csproj` to be a class library**
Change `<OutputType>WinExe</OutputType>` to not exist (making it a library). Ensure `TargetFramework` is `net10.0` (or `net8.0`/whatever it currently is).
- [ ] **Step 2: Add SoundFlow Package**
Run `dotnet add Ohrbob.App/Ohrbob.App.csproj package SoundFlow`
- [ ] **Step 3: Remove Desktop-specific boilerplate from Core**
Remove `Program.cs` from `Ohrbob.App` as it belongs in the `.Desktop` wrapper.

### Task 2: Create Platform Wrappers

**Files:**
- Create: `Ohrbob.App.Desktop/Ohrbob.App.Desktop.csproj` and `Program.cs`
- Create: `Ohrbob.App.Android/Ohrbob.App.Android.csproj`, `MainActivity.cs`, `SplashActivity.cs`
- Create: `Ohrbob.App.iOS/Ohrbob.App.iOS.csproj`, `AppDelegate.cs`, `Main.cs`
- Modify: `Ohrbob.App.slnx`

**Interfaces:**
- Consumes: `Ohrbob.App` core library.
- Produces: Executable apps for Desktop, Android, and iOS.

- [ ] **Step 1: Copy Desktop Wrapper**
Copy `/home/kaiwegner/Projects/Avalonia.Test.Mp3Player.Android/Avalonia.Test.Mp3Player.Desktop` to `./Ohrbob.App.Desktop`. Rename files and replace `Avalonia.Test.Mp3Player` references with `Ohrbob.App`.
- [ ] **Step 2: Copy Android Wrapper**
Copy `/home/kaiwegner/Projects/Avalonia.Test.Mp3Player.Android/Avalonia.Test.Mp3Player.Android` to `./Ohrbob.App.Android`. Rename files and replace references. Update AndroidManifest.xml to include `INTERNET` and `CAMERA` permissions.
- [ ] **Step 3: Copy iOS Wrapper**
Copy `/home/kaiwegner/Projects/Avalonia.Test.Mp3Player.Android/Avalonia.Test.Mp3Player.iOS` to `./Ohrbob.App.iOS`. Rename files and replace references. Update Info.plist for Camera/Internet.
- [ ] **Step 4: Update Solution File**
Modify `Ohrbob.App.slnx` to include `Ohrbob.App.Desktop`, `Ohrbob.App.Android`, and `Ohrbob.App.iOS` projects alongside `Ohrbob.App`.

### Task 3: Integrate SoundFlow Playback

**Files:**
- Modify: `Ohrbob.App/ViewModels/MainViewModel.cs` (or equivalent playback viewmodel)

**Interfaces:**
- Consumes: `SoundFlow` library APIs.
- Produces: Audio playback triggered by the server connection logic.

- [ ] **Step 1: Wire up SoundFlow in ViewModel**
Review how `Avalonia.Test.Mp3Player.Android` uses SoundFlow (e.g., initializing a player, passing a URL).
Implement the same HTTP MP3 playback initialization in `Ohrbob.App`'s player viewmodel once the QR code yields the server address.
- [ ] **Step 2: Build and Verify**
Run `dotnet build Ohrbob.App.Desktop` to verify the core and desktop wrapper compile successfully with the new changes.