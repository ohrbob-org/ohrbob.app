# Ohrbob.org Connect — App Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the app's connect screen onboard people to ohrbob.org with one button, receive the `ohrbob://connect?…` callback on Linux/Windows/macOS/Android/iOS, and keep the manual server+token path behind "Advanced".

**Architecture:** A shared `DeepLinkRouter` (static publish/subscribe with a pending slot) feeds incoming URIs into `MainViewModel`. A shared `IUriLauncher` opens the browser. The view model gains normal/advanced state and replace-confirmation state. Platform heads publish URIs from `argv` (desktop, with single-instance forwarding), Android intents, and iOS `OpenUrl`.

**Tech Stack:** Avalonia 12 (`Ohrbob.App` shared; `Ohrbob.App.Desktop`/`.Android`/`.iOS` heads), CommunityToolkit.Mvvm, NUnit + Avalonia.Headless.NUnit.

**Spec:** `docs/superpowers/specs/2026-10-22-ohrbob-org-connect-flow-design.md`

## Global Constraints

- Deep-link shape is fixed: `ohrbob://connect?server=<server>&token=<token>`; `server` is the player base URL (`https://player.ohrbob.org`).
- The button opens exactly `https://ohrbob.org/connect/app`.
- Callback must work on **Linux, Windows, macOS, Android, iOS**.
- Receiving a link while **not** connected switches silently; while connected, **confirm before replacing**.
- Flatpak is the Linux delivery target: exported `.desktop` accepts a URL arg (`%u`); browser launch works inside the sandbox.
- `DEBUG`-only dev defaults: keep `http://10.0.2.2:5002` and the hardcoded token under `DEBUG`; non-DEBUG defaults are `https://player.ohrbob.org` and an empty token.
- Advanced mode (link paste + manual server/token) stays fully functional.

## Review Focus

Most likely to bite a real person, most likely first. Each line's test lives in the named task.

1. A foreign/foreign-host or malformed `ohrbob://` URI (e.g. `ohrbob://other`, missing token) must be ignored — never crash, never wipe the saved connection (Task 1).
2. A second launch while the app is already running must not open a second window and must not lose the URL (Task 4).
3. A link arriving over an active connection to a different server must prompt, not silently replace and interrupt playback (Task 3).
4. Advanced mode must still connect using a pasted link or manual server+token (Task 2).
5. Cold-start delivery (app not running, launched *by* the link) must work on Linux/Windows/macOS/Android/iOS — not only warm start (Tasks 4–7).

---

### Task 1: Shared connect plumbing (launcher, router, view-model state)

**Files:**
- Create: `Ohrbob.App/Services/IUriLauncher.cs`
- Create: `Ohrbob.App/Services/AppUriLauncher.cs`
- Create: `Ohrbob.App/Services/DeepLinkRouter.cs`
- Modify: `Ohrbob.App/ViewModels/MainViewModel.cs`
- Test: `Ohrbob.App.Tests/ViewModels/ConnectFlowTests.cs`
- Test double: `Ohrbob.App.Tests/ViewModels/RecordingUriLauncher.cs` (`IUriLauncher` recording the last launched URI)

**Interfaces:**
- Produces: `interface IUriLauncher { Task LaunchAsync(Uri uri); }`
- Produces: `static class DeepLinkRouter { static void Publish(string uri); static void Subscribe(Action<string> handler); }` — `Publish` stores the URI as pending when nothing is subscribed; `Subscribe` immediately delivers (and clears) any pending URI.
- Produces on `MainViewModel`: `bool IsAdvancedMode`; `IAsyncRelayCommand OpenHostedConnectPageCommand`; `const string HostedConnectUrl = "https://ohrbob.org/connect/app"`; `bool IsReplaceConfirmationVisible`; `IRelayCommand ConfirmReplaceCommand`; `IRelayCommand CancelReplaceCommand`; ctor overload accepting `IUriLauncher? uriLauncher = null`.
- Produces: `static bool TryParseConnectDeepLink(string uri, out string server, out string token)` in `MainViewModel` (shared by `ConnectAsync`).

- [ ] **Step 1: Write the failing tests**

Reference the existing `MainViewModelTests` for how the VM is constructed (`ConfigService` + fake/real client). Add:

```csharp
[Test]
public async Task OpenHostedConnectPage_LaunchesTheHostedUrl()
{
    var launcher = new RecordingUriLauncher();
    var vm = new MainViewModel(new ConfigService(), null, launcher);

    await vm.OpenHostedConnectPageCommand.ExecuteAsync(null);

    Assert.That(launcher.Last, Is.EqualTo(new Uri("https://ohrbob.org/connect/app")));
}

[Test]
public void TryParseConnectDeepLink_AcceptsTheFixedShape()
{
    var ok = MainViewModel.TryParseConnectDeepLink(
        "ohrbob://connect?server=https%3A%2F%2Fplayer.ohrbob.org&token=abc",
        out var server, out var token);
    Assert.That(ok, Is.True);
    Assert.That(server, Is.EqualTo("https://player.ohrbob.org"));
    Assert.That(token, Is.EqualTo("abc"));
}

[TestCase("http://example.com/x")]
[TestCase("ohrbob://other?server=x&token=y")]
[TestCase("ohrbob://connect?server=x")]
[TestCase("not a uri")]
public void TryParseConnectDeepLink_RejectsForeignOrIncompleteUris(string uri)
{
    Assert.That(MainViewModel.TryParseConnectDeepLink(uri, out _, out _), Is.False);
}

[Test]
public void PublishedLink_WhenNotConnected_IsAppliedAndSaved()
{
    var config = new ConfigService();
    var vm = new MainViewModel(config, null, new RecordingUriLauncher());

    DeepLinkRouter.Publish("ohrbob://connect?server=https%3A%2F%2Fplayer.ohrbob.org&token=abc");

    Assert.That(config.ServerUrl, Is.EqualTo("https://player.ohrbob.org"));
    Assert.That(config.StreamToken, Is.EqualTo("abc"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests --filter FullyQualifiedName~ConnectFlowTests`
Expected: FAIL — `IUriLauncher`, `DeepLinkRouter`, `TryParseConnectDeepLink`, and the 3-arg ctor do not exist.

- [ ] **Step 3: Add `IUriLauncher`, `AppUriLauncher`, `DeepLinkRouter`**

- `IUriLauncher.LaunchAsync(Uri)`.
- `AppUriLauncher`: resolve `TopLevel` from `Application.Current?.ApplicationLifetime` (desktop `MainWindow`, single-view `MainView`) and call `topLevel.Launcher.LaunchUriAsync(uri)`; no-op if unavailable.
- `DeepLinkRouter` as specified (pending slot so a link delivered before the VM is built is not lost).

- [ ] **Step 4: Wire the view model**

Extract `TryParseConnectDeepLink` from the existing parsing in `ConnectAsync` (reject non-`ohrbob` schemes, missing `server`/`token`) and use it there too. Add `IsAdvancedMode`, `OpenHostedConnectPageCommand` (`uriLauncher.LaunchAsync(new Uri(HostedConnectUrl))`), `IsReplaceConfirmationVisible`, pending server/token fields, `ConfirmReplaceCommand`, `CancelReplaceCommand`. In the constructor, `DeepLinkRouter.Subscribe(uri => Dispatcher.UIThread.Post(() => HandleDeepLink(uri)))`; `HandleDeepLink` ignores unparseable URIs, connects immediately when `!IsConnected`, otherwise stores the pending link and sets `IsReplaceConfirmationVisible = true`. Gate the `_serverUrlInput`/`_tokenInput` field defaults behind `#if DEBUG`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests --filter FullyQualifiedName~ConnectFlowTests`
Expected: PASS.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests`
Expected: PASS (existing MPRIS/VM tests still green).

- [ ] **Step 7: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob2.app
git add Ohrbob.App/Services/IUriLauncher.cs Ohrbob.App/Services/AppUriLauncher.cs Ohrbob.App/Services/DeepLinkRouter.cs Ohrbob.App/ViewModels/MainViewModel.cs Ohrbob.App.Tests/ViewModels/ConnectFlowTests.cs
git commit -m "feat(app): route connect deep links and add hosted-connect action"
```

---

### Task 2: Onboarding view — normal vs advanced

**Files:**
- Modify: `Ohrbob.App/Views/OnboardingView.axaml`
- Test: `Ohrbob.App.Tests/Views/OnboardingConnectTests.cs`

**Interfaces:**
- Consumes: `MainViewModel.IsAdvancedMode`, `OpenHostedConnectPageCommand`, existing `DeepLinkInput`/`ServerUrlInput`/`TokenInput`/`ConnectCommand`.

- [ ] **Step 1: Write the failing test**

Use the existing `[AvaloniaTest]` headless pattern (see `MainWindowE2ETests`). Build `MainView` with a `MainViewModel`, and assert: by default the "Connect with ohrbob.org" button is present and the server-URL `TextBox` is not visible; after setting `IsAdvancedMode = true`, the server-URL and token `TextBox`es are visible.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests --filter FullyQualifiedName~OnboardingConnectTests`
Expected: FAIL — the button does not exist.

- [ ] **Step 3: Update `OnboardingView.axaml`**

Keep the card. Add a normal-mode block (`IsVisible="{Binding !IsAdvancedMode}"`): short explanation, the primary button bound to `OpenHostedConnectPageCommand` (e.g. "Connect with ohrbob.org"), and a subtle "Advanced" button that sets advanced mode. Wrap today's fields (deep-link paste, server URL, token, error, Connect) in a block with `IsVisible="{Binding IsAdvancedMode}"`. Add a command/binding to toggle `IsAdvancedMode` (e.g. an `Advanced` `Button` whose `Command` flips the flag, or a `ToggleButton` bound two-way).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests --filter FullyQualifiedName~OnboardingConnectTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob2.app
git add Ohrbob.App/Views/OnboardingView.axaml Ohrbob.App.Tests/Views/OnboardingConnectTests.cs
git commit -m "feat(app): hosted one-button connect with advanced fallback"
```

---

### Task 3: Replace-confirmation overlay

**Files:**
- Modify: `Ohrbob.App/Views/MainView.axaml`
- Test: `Ohrbob.App.Tests/ViewModels/ConnectFlowTests.cs`

**Interfaces:**
- Consumes: `IsReplaceConfirmationVisible`, `ConfirmReplaceCommand`, `CancelReplaceCommand` (Task 1).

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void LinkWhileConnectedToDifferentServer_PromptsInsteadOfReplacing()
{
    var config = new ConfigService();
    config.Save("https://player.ohrbob.org", "old");
    var vm = new MainViewModel(config, null, new RecordingUriLauncher());
    vm.IsConnected = true;

    DeepLinkRouter.Publish("ohrbob://connect?server=https%3A%2F%2Fother.example&token=new");

    Assert.That(vm.IsReplaceConfirmationVisible, Is.True);
    Assert.That(config.StreamToken, Is.EqualTo("old")); // unchanged until confirmed
}

[Test]
public void ConfirmReplace_AppliesThePendingConnection()
{
    var config = new ConfigService();
    config.Save("https://player.ohrbob.org", "old");
    var vm = new MainViewModel(config, null, new RecordingUriLauncher());
    vm.IsConnected = true;
    DeepLinkRouter.Publish("ohrbob://connect?server=https%3A%2F%2Fother.example&token=new");

    vm.ConfirmReplaceCommand.Execute(null);

    Assert.That(config.StreamToken, Is.EqualTo("new"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests --filter FullyQualifiedName~ConnectFlowTests`
Expected: FAIL.

- [ ] **Step 3: Add the overlay to `MainView.axaml`**

A top-level `Border`/`Panel` over the `Grid`, `IsVisible="{Binding IsReplaceConfirmationVisible}"`, with a short message ("Replace the current connection?") and "Replace"/"Cancel" buttons bound to the two commands.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests --filter FullyQualifiedName~ConnectFlowTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob2.app
git add Ohrbob.App/Views/MainView.axaml Ohrbob.App.Tests/ViewModels/ConnectFlowTests.cs
git commit -m "feat(app): confirm before replacing an active connection"
```

---

### Task 4: Desktop — argv, single instance, packaging

**Files:**
- Create: `Ohrbob.App.Desktop/SingleInstance.cs`
- Create: `Ohrbob.App.Desktop/DeepLinkArguments.cs`
- Modify: `Ohrbob.App.Desktop/Program.cs`
- Modify: `app.ohrbob.Ohrbob.desktop`
- Modify: `app.ohrbob.Ohrbob.yml`
- Test: `Ohrbob.App.Tests/Desktop/DeepLinkArgumentsTests.cs`

**Interfaces:**
- Produces: `static class DeepLinkArguments { static string? Find(string[] args); }` — returns the first arg that is a `ohrbob://` URI.
- Produces: `sealed class SingleInstance : IDisposable { static bool TryAcquire(Action<string> onMessage); void Send(string message); }` — second launch sends its URI and exits; first instance receives it.

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void Find_ReturnsTheOhrbobUriFromArguments()
{
    var args = new[] { "--flag", "ohrbob://connect?server=x&token=y" };
    Assert.That(DeepLinkArguments.Find(args), Is.EqualTo("ohrbob://connect?server=x&token=y"));
}

[Test]
public void Find_ReturnsNullWhenNoOhrbobUriPresent()
{
    Assert.That(DeepLinkArguments.Find(new[] { "--flag", "x" }), Is.Null);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests --filter FullyQualifiedName~DeepLinkArgumentsTests`
Expected: FAIL — `DeepLinkArguments` does not exist.

- [ ] **Step 3: Implement `DeepLinkArguments` and `SingleInstance`**

`DeepLinkArguments.Find` as specified. `SingleInstance` uses a named OS primitive (`Mutex` + a local IPC channel: named pipe on Windows, Unix domain socket on Linux/macOS) so the first instance receives forwarded URLs; the second instance calls `Send` then exits before creating a window.

- [ ] **Step 4: Wire `Program.cs`**

Before `BuildAvaloniaApp().StartWithClassicDesktopLifetime(...)`: if a `ohrbob://` arg is present and another instance is running, forward it and exit; otherwise acquire. Publish cold-start args via `DeepLinkRouter.Publish(uri)` (the pending slot covers the case where the VM isn't built yet) and route forwarded messages to `DeepLinkRouter.Publish`.

- [ ] **Step 5: Packaging**

In `app.ohrbob.Ohrbob.desktop`, change `Exec=app.ohrbob.Ohrbob` to `Exec=app.ohrbob.Ohrbob %u` and keep `MimeType=x-scheme-handler/ohrbob;`. Confirm the manifest still installs the desktop file (no manifest change needed unless the finish-args must grant the OpenURI portal — add the portal talk-name if the sandboxed browser launch is blocked).

- [ ] **Step 6: Verify build + tests, then rebuild/reinstall the Flatpak**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests --filter FullyQualifiedName~DeepLinkArgumentsTests` (PASS), then `./build-flatpak.sh` (or the host `flatpak-builder --user --install` command). Launch, and confirm the app registers as the `ohrbob` handler and receives a link.

- [ ] **Step 7: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob2.app
git add Ohrbob.App.Desktop/SingleInstance.cs Ohrbob.App.Desktop/DeepLinkArguments.cs Ohrbob.App.Desktop/Program.cs app.ohrbob.Ohrbob.desktop app.ohrbob.Ohrbob.yml Ohrbob.App.Tests/Desktop/DeepLinkArgumentsTests.cs
git commit -m "feat(desktop): receive ohrbob:// links via argv and single instance"
```

---

### Task 5: Android — scheme intent

**Files:**
- Modify: `Ohrbob.App.Android/AndroidManifest.xml`
- Modify: `Ohrbob.App.Android/MainActivity.cs`

**Interfaces:**
- Consumes: `DeepLinkRouter.Publish(string)` (Task 1).

- [ ] **Step 1: Add the intent filter**

Inside `<application>`, add to the launcher `<activity>` an `<intent-filter>` with `<data android:scheme="ohrbob" android:host="connect" />`, `android:autoVerify="false"`, action `VIEW`, category `DEFAULT` + `BROWSABLE`; set `android:launchMode="singleTask"` so warm starts come through `OnNewIntent`.

- [ ] **Step 2: Publish the URI**

Override `OnCreate` (call base, then publish `Intent?.DataString`) and `OnNewIntent` (call base, read `Intent?.DataString`, publish). Route through `DeepLinkRouter.Publish`.

- [ ] **Step 3: Verify**

Build the Android head (`dotnet build Ohrbob.App.Android`) and, on device/emulator, run `adb shell am start -a android.intent.action.VIEW -d "ohrbob://connect?server=x&token=y"` for both cold and warm start. Expected: the app comes to front and attempts the connection.

- [ ] **Step 4: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob2.app
git add Ohrbob.App.Android/AndroidManifest.xml Ohrbob.App.Android/MainActivity.cs
git commit -m "feat(android): receive ohrbob:// links"
```

---

### Task 6: iOS — URL scheme

**Files:**
- Modify: `Ohrbob.App.iOS/Info.plist`
- Modify: `Ohrbob.App.iOS/AppDelegate.cs`

**Interfaces:**
- Consumes: `DeepLinkRouter.Publish(string)` (Task 1).

- [ ] **Step 1: Register the scheme**

Add a `CFBundleURLTypes` array to `Info.plist` with `CFBundleURLName` = the bundle id and `CFBundleURLSchemes` = `["ohrbob"]`.

- [ ] **Step 2: Publish the URI**

In `AppDelegate`, override `OpenUrl` (and, on iOS 13+, `OpenUrl(UIApplication, ...)` / scene `WillConnect`/`OpenUrlContexts`) to read the absolute URL and call `DeepLinkRouter.Publish(url.AbsoluteString)`. Return `true`.

- [ ] **Step 3: Verify**

Build the iOS head and, in the iOS simulator, run `xcrun simctl openurl booted "ohrbob://connect?server=x&token=y"` for cold and warm start. Expected: the app comes to front and attempts the connection.

- [ ] **Step 4: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob2.app
git add Ohrbob.App.iOS/Info.plist Ohrbob.App.iOS/AppDelegate.cs
git commit -m "feat(ios): receive ohrbob:// links"
```

---

### Task 7: Windows & macOS scheme registration

**Files:**
- Create: `Ohrbob.App.Desktop/WindowsUriScheme.cs`
- Modify: `Ohrbob.App.Desktop/Program.cs`
- Create/modify: the macOS bundle `Info.plist` (only if this repo produces a `.app` bundle; see Step 4)
- Test: `Ohrbob.App.Tests/Desktop/WindowsUriSchemeTests.cs`

**Interfaces:**
- Consumes: `DeepLinkRouter.Publish(string)` (Task 1).
- Produces: `static class WindowsUriScheme` with a pure `static IReadOnlyDictionary<string, string> BuildRegistryValues(string executablePath)` and an `static void EnsureRegistered(string executablePath)` that writes those values under `HKCU\Software\Classes`.

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void BuildRegistryValues_RegistersTheCommandUnderHkcuClasses()
{
    var values = WindowsUriScheme.BuildRegistryValues(@"C:\apps\ohrbob.exe");
    Assert.That(values["ohrbob"], Is.EqualTo("URL:Ohrbob Protocol"));
    Assert.That(values[@"ohrbob\shell\open\command"], Is.EqualTo("\"C:\\apps\\ohrbob.exe\" \"%1\""));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests --filter FullyQualifiedName~WindowsUriSchemeTests`
Expected: FAIL — `WindowsUriScheme` does not exist.

- [ ] **Step 3: Implement `WindowsUriScheme`**

`BuildRegistryValues` returns the `HKCU\Software\Classes` values for the `ohrbob` scheme (`ohrbob` → `URL:Ohrbob Protocol`; `ohrbob` → `URL Protocol` (empty); `ohrbob\DefaultIcon`; `ohrbob\shell\open\command`). `EnsureRegistered` no-ops on non-Windows and writes each value under `Microsoft.Win32.Registry.CurrentUser`.

- [ ] **Step 4: Wire per-OS registration in `Program.cs`**

On Windows, call `WindowsUriScheme.EnsureRegistered(Environment.ProcessPath!)` before startup and let Task 4's single-instance handle the forwarded URL. On macOS, ensure `CFBundleURLTypes` (scheme `ohrbob`) is present in the `.app` bundle's `Info.plist` — if this repo does not yet produce a bundle, add the bundle's `Info.plist` (or the packaging step that generates it) as part of this task. Cold start receives the URL via `argv` (Task 4); no extra code path.

- [ ] **Step 5: Verify**

Build on Windows and run once; confirm `HKCU\Software\Classes\ohrbob\shell\open\command` exists. Then cold- and warm-start with `ohrbob://connect?server=x&token=y` (e.g. from `Run`). On macOS, build the bundle, confirm `CFBundleURLTypes` is present, and cold- and warm-start with `open "ohrbob://connect?server=x&token=y"`. Expected: the app comes to front and attempts the connection; a second launch does not open a second window.

- [ ] **Step 6: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob2.app
git add Ohrbob.App.Desktop/WindowsUriScheme.cs Ohrbob.App.Desktop/Program.cs Ohrbob.App.Tests/Desktop/WindowsUriSchemeTests.cs
git commit -m "feat(desktop): register the ohrbob scheme on Windows and macOS"
```
