# Ohrbob Self-Contained Multi-Platform Audio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Package LibVLC native binary NuGet dependencies for Windows, Mac, Android, and iOS, and package a self-contained static `ffplay` binary for Linux inside the application assets to make the entire player completely self-contained on all 5 platforms.

**Architecture:** 
- Add pre-packaged LibVLC platform-specific NuGet dependencies to `Ohrbob.App.csproj`.
- Copy `/usr/bin/ffplay` as a packaged asset `Assets/ffplay_linux` inside `Ohrbob.App` project and copy it to the output directory on build.
- Modify `MainViewModel.cs` on Linux to execute the packaged local `ffplay_linux` binary next to the executable, falling back to system `ffplay` if not found.

**Tech Stack:** C#, Avalonia, LibVLCSharp, MSBuild.

## Global Constraints
- Target Framework: .NET 10

---

### Task 1: Add Pre-packaged LibVLC and FFplay Assets

**Files:**
- Create: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Assets/ffplay_linux` (by copying from `/usr/bin/ffplay`)
- Modify: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj`

- [ ] **Step 1: Install official pre-packaged LibVLC packages**
```bash
dotnet add /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj package VideoLAN.LibVLC.Windows --version 3.0.18
dotnet add /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Ohrbob.App.csproj package VideoLAN.LibVLC.Mac --version 3.0.18
```

- [ ] **Step 2: Copy ffplay into the Assets folder**
Copy the system's `ffplay` into the project's Assets folder:
```bash
cp /usr/bin/ffplay /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Assets/ffplay_linux && chmod +x /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/Assets/ffplay_linux
```

- [ ] **Step 3: Modify Ohrbob.App.csproj to include the packaged ffplay asset**
Ensure the compiled output folders receive the local `ffplay_linux` binary.
Modify `Ohrbob.App.csproj` to append:
```xml
  <ItemGroup>
    <None Include="Assets/ffplay_linux" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

---

### Task 2: Update MainViewModel to execute local packaged ffplay

**Files:**
- Modify: `/home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Update PlayFfplay to run local asset**
Modify `PlayFfplay` to search for `Assets/ffplay_linux` next to the executable and use it:
```csharp
    private void PlayFfplay(string url, int position)
    {
        StopFfplay();
        try
        {
            var localFfplay = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ffplay_linux");
            var ffplayExe = File.Exists(localFfplay) ? localFfplay : "ffplay";

            var psi = new ProcessStartInfo
            {
                FileName = ffplayExe,
                Arguments = $"-nodisp -autoexit -ss {position} \"{url}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            _ffplayProcess = Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start ffplay: {ex.Message}");
        }
    }
```

- [ ] **Step 2: Compile the project and execute all tests**
Run:
```bash
dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/Ohrbob.App.Tests.csproj
```
Expected: PASS
