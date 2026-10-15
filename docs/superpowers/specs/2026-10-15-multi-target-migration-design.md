# Ohrbob.App Multi-Platform Migration

## Intent
Transform the existing `Ohrbob.App` into a multi-platform Avalonia application (Desktop, Android, iOS) while integrating the Soundflow library for HTTP MP3 playback.

## Scope & Constraints
- **Core Role**: The app acts strictly as a client.
- **Key Features**: 
  - QR Code scanning/server discovery (existing logic ported over).
  - HTTP MP3 Playback using Soundflow (adapted from the `Avalonia.Test.Mp3Player.Android` reference project).
- **Structure**: We will adopt the multi-target structure from the reference project (`.Desktop`, `.Android`, `.iOS` wrappers around a shared core library).

## How it gets built (The calls I'm making)
1.  **Project Restructuring**: I will rename the current `Ohrbob.App` to a core library format, and generate/copy the platform-specific wrapper projects (`Desktop`, `Android`, `iOS`) using `dotnet new avalonia.xapp` or by copying from the reference repo.
2.  **Soundflow Integration**: I will extract the Soundflow HTTP playback logic from the reference repo and wire it into the `Ohrbob.App` view models.
3.  **Dependencies**: Existing QR code and client dependencies will be kept in the core project.

## What's left out
- Server hosting logic (the app is exclusively a client).
- Browser / WASM target (not needed).
