# Ohrbob App Testing Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a comprehensive test suite for `Ohrbob.App` using **NUnit** for unit testing ViewModels/Services, and **Avalonia.Headless.NUnit** for UI E2E integration tests.

**Architecture:** 
- A dedicated test project `Ohrbob.App.Tests`.
- **Unit Tests:** Mock network requests to verify `OhrbobClient` and test `MainViewModel` lifecycle and commands.
- **E2E UI Tests:** Headless integration tests using Avalonia's headless platform to simulate UI rendering and control interactions.

**Tech Stack:** C#, .NET 10, NUnit, Avalonia.Headless.NUnit, Moq or manually mocked dependencies.

## Global Constraints
- Target Framework: .NET 10
- Use NUnit as the primary test runner

---

### Task 1: Scaffold Ohrbob.App.Tests Project

**Files:**
- Create: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/Ohrbob.App.Tests.csproj`

- [ ] **Step 1: Create the NUnit test project**
```bash
dotnet new nunit -o /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests --framework net10.0
```

- [ ] **Step 2: Add project reference and NuGet packages**
```bash
dotnet add /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/Ohrbob.App.Tests.csproj reference /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj
dotnet add /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/Ohrbob.App.Tests.csproj package Avalonia.Headless.NUnit --version 12.1.4
```

- [ ] **Step 3: Create AppTestBuilder class for Headless Platform Initialization**
Create `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/Setup.cs` to initialize Avalonia Headless:
```csharp
using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Ohrbob.App;

[assembly: AvaloniaTestApplication(typeof(Ohrbob.App.Tests.TestAppBuilder))]

namespace Ohrbob.App.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
```

---

### Task 2: Implement Service Unit Tests

**Files:**
- Create: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/Services/ConfigServiceTests.cs`
- Create: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/Services/OhrbobClientTests.cs`

- [ ] **Step 1: Implement ConfigServiceTests.cs**
Tests that configuration loads, saves, and falls back correctly.
- [ ] **Step 2: Implement OhrbobClientTests.cs**
Mock `HttpMessageHandler` to verify REST endpoints send correct `X-Stream-Token` headers and parse responses correctly.

---

### Task 3: Implement MainViewModel Unit Tests

**Files:**
- Create: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/ViewModels/MainViewModelTests.cs`

- [ ] **Step 1: Test Connection & Onboarding**
Verify `ConnectCommand` parses `ohrbob://connect?...` deep links and updates `IsConnected`.
- [ ] **Step 2: Test Playback State & Controls**
Verify playback timers tick correctly, position increments, and play/pause commands work as expected.

---

### Task 4: Implement E2E UI Tests

**Files:**
- Create: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/Views/MainWindowE2ETests.cs`

- [ ] **Step 1: Implement MainWindowE2ETests.cs**
Uses `Avalonia.Headless.NUnit` to instantiate `MainWindow`, verify that when `IsConnected` is false, the onboarding grid is visible, and simulate inputs to trigger connection.
