# QR Code Scanner for Connecting the App — Design

## Intent

Let a listener connect the app by scanning an Ohrbob connection QR code with a
live camera preview on any supported device: Android, iOS, Linux, Windows and
macOS.

The web UI already renders the deep link `ohrbob://connect?server=…&token=…`
into that QR code. The scanner only needs to produce that text; the existing
deep-link parser and replace-confirmation flow stay authoritative.

## Goals

- Add a "Scan QR code" action to onboarding (simple and advanced modes).
- Open a live camera preview and return the first decoded QR code.
- Work on Android, iOS and desktop; hide/disable the action when no scanner is
  available.
- Reuse `MainViewModel.TryParseConnectDeepLink` and `ApplyDeepLink`.
- Keep camera hardware optional in the Android manifest, explicitly including
  autofocus as `required="false"`, so tablets without autofocus can install.
- Request camera permission at runtime on Android/iOS; no crash or silent
  failure when denied.

## Non-goals

- Scanning anything other than QR codes.
- Torch/flash, zoom, or camera selection UI.
- Continuous multi-code scanning.
- Adding a scanner to the web frontend.
- Storing scanned connection data anywhere except the existing config after the
  user completes the connection flow.

## UX

- Onboarding gets a secondary button, "Scan QR code", under "Connect with
  ohrbob.org" in simple mode and above the paste field in advanced mode.
- Clicking it opens a focused scanner surface with a live preview and a Cancel
  action.
- On decode, the scanner closes and the existing connect logic runs. A link
  that does not parse as `ohrbob://connect?server=…&token=…` shows the existing
  connection error.
- If the app is already connected, the existing replace-confirmation dialog
  appears before switching servers.
- Cancelling returns silently to onboarding.

## Architecture

Shared interface in the app project:

```csharp
public interface IQrScanner
{
    bool IsAvailable { get; }
    Task<string?> ScanAsync(CancellationToken cancellationToken = default);
}
```

- `MainViewModel` takes an optional `IQrScanner` constructor argument. `null`
  (tests, platforms without a scanner) hides the scan action.
- A small static `QrScannerProvider.Current` is assigned by each platform entry
  point before the Avalonia app starts. No new DI container is introduced.
- `ScanQrCodeCommand` calls `ScanAsync`, then `ApplyDeepLink` with the result.
  It ignores cancellation and empty results.

The camera preview is native, not embedded in the Avalonia visual tree. This
avoids platform view-hierarchy hacks and lets each OS use its own camera stack.
The scanner still provides a live preview; only the preview surface is native.

## Platform implementations

### Android

- `QrScannerActivity` shows a `TextureView` live preview and decodes preview
  frames with `ZXing.Net` (`PlanarYUVLuminanceSource` from the NV21 callback).
- Uses the platform `Android.Hardware.Camera` API; no CameraX/ML Kit dependency
  and no APK-size increase.
- Focus: query `SupportedFocusModes` and set continuous-video/continuous-picture
  only when present; otherwise leave focus alone. Never assume autofocus.
- Runtime `CAMERA` permission is requested before opening the camera. Denial
  finishes with no result.
- Manifest:
  ```xml
  <uses-permission android:name="android.permission.CAMERA" />
  <uses-feature android:name="android.hardware.camera" android:required="false" />
  <uses-feature android:name="android.hardware.camera.autofocus" android:required="false" />
  ```
- `MainActivity` owns `AndroidQrScanner` and handles the activity result.

### iOS

- `AVCaptureSession` + `AVCaptureVideoPreviewLayer` in a small
  `UIViewController`, with `AVCaptureMetadataOutput` for
  `AVMetadataObjectType.QRCode`.
- Presented over the Avalonia root view controller; dismissed on decode or
  cancel.
- `NSCameraUsageDescription` added to `Info.plist`; permission denial dismisses
  the scanner.
- Implemented but build-verified only on macOS; this Linux workstation cannot
  compile the iOS head.

### Desktop (Linux/Windows/macOS)

- `DesktopQrScanner` opens a small Avalonia window with an `Image` preview.
- OpenCvSharp `VideoCapture(0)` captures frames on a background loop; frames are
  shown in the window and decoded with `ZXing.Net`.
- On decode, closes the window and returns the text. Cancel closes it and
  returns null.
- Adds `OpenCvSharp4` plus the Linux/Windows/macOS runtime packages to the
  Desktop project only; Android/iOS do not reference them.

## Validation

- Only accept `ohrbob://connect` links with an `http`/`https` server and a
  non-empty token; the existing parser does this.
- The scanner returns raw text only. No connection is applied until validation
  succeeds.
- Never log the scanned token or the full deep link.

## Tests

- `MainViewModel`: scan command applies a valid link; invalid text sets the
  connection error; cancellation is silent; absent scanner hides the action.
- `QrDecode`: encode a known string with ZXing and decode the generated bitmap
  through the shared decoding helper.
- View: onboarding shows "Scan QR code" when a scanner is available and hides
  it when not.
- Manifest guard: a test asserts the Android manifest keeps
  `android.hardware.camera.autofocus` and the camera feature as
  `required="false"`, and that the iOS `Info.plist` has a camera usage string.
- Desktop manual smoke test with a webcam on Linux.

## Risks

- iOS compilation and camera behaviour cannot be verified on this machine.
- Desktop webcam capture depends on OpenCvSharp native runtimes for each OS.
- Some Android devices expose only deprecated camera APIs; continuous focus is
  best-effort and may be absent.
- A native scanner screen does not inherit the Avalonia theme; it uses minimal
  platform styling.
