# Ohrbob App Design Spec

## Status: Approved

## Overview
This specification details the architecture and implementation of the cross-platform **Ohrbob App** written using **Avalonia** and **CommunityToolkit.Mvvm** (Approach 2). The application replicates the audio playback, station switching, and sync capabilities of the Ohrbob Web portal (Option A).

## 1. Solution & Architecture
* **Language/Framework:** C# / .NET 10 / Avalonia UI.
* **Architecture Pattern:** MVVM using `CommunityToolkit.Mvvm`.
* **Platform Support:** Desktop (Windows, macOS, Linux) and Mobile (Android, iOS).
* **Data Flow & REST API:**
  - Standard REST communication with `Ohrbob.Player` via an `HttpClient`-based client (`OhrbobClient`).
  - Read configuration via scanned QR Code or Deep Link URL: `ohrbob://connect?server={server}&token={token}`.
  - Store configuration securely on-device.

## 2. API Additions to Ohrbob.Player
To support station listing in the app using the `StreamToken`, a new endpoint is added to `Ohrbob.Player/Controllers/StreamController.cs`:
* **GET `/stream/stations`**: Returns a list of stations available to the active profile based on `AllowedStationIds`.

## 3. UI Views & Layouts
* **OnboardingView**: Displays QR Code Scanner and manual entry inputs for Server URL and API StreamToken.
* **MainView**: Adapts responsively.
  - **Sidebar / Navigation Drawer**: Shows List of Stations.
  - **Player Panel**: Detailed screen showing cover art, episode title, description, progress slider, and control buttons (Play/Pause, Seek -15s, Seek +30s, Skip/Next).
* **SettingsView**: Option to disconnect/reconnect and view current connection status.

## 4. Media Player & Sync Logic
* **Playback Integration**: Media player using LibVLCSharp or platform-native wrapper.
* **Syncing Loop**:
  - GET `/stream/status` to load active episode, cover, stream URI, and playhead position.
  - Periodic POST `/stream/status` every 5-10 seconds to sync back current position.
  - POST `/stream/next` on skip or end-of-track, then refresh status.
