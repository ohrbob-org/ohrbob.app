# Ohrbob App

The cross-platform desktop & mobile player client for **Ohrbob**, built with **Avalonia UI** and **CommunityToolkit.Mvvm**.

## Features
- **Modern Dark UI**: Fluid and responsive design mimicking professional audio players.
- **Onboarding Flow**: Quick-connection using standard QR code deep links (`ohrbob://connect`) or manual input.
- **Persistent State Syncing**: Syncs your playback progress with the Ohrbob Server every 10 seconds.
- **Full Media Controls**: Toggle play/pause, seek back 15s, seek forward 30s, or skip to the next episode in your station queue.

---

## Getting Started

### Prerequisites
- .NET 10.0 SDK

### Launch the Application
To launch the app directly from your terminal, run:

```bash
dotnet run --project Ohrbob.App/Ohrbob.App.csproj
```

### Run Tests
To run the full test suite (including unit tests and headless UI E2E tests):

```bash
dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj
```

---

## Connection Format
When connecting via a QR code or deep link, use this URI format:
`ohrbob://connect?server={Server_URL}&token={Your_Stream_Token}`
