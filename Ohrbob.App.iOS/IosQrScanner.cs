using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AVFoundation;
using Foundation;
using UIKit;
using Ohrbob.App.Services;

namespace Ohrbob.App.iOS;

public sealed class IosQrScanner : IQrScanner
{
    public bool IsAvailable => AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video) is not null;

    public async Task<string?> ScanAsync(CancellationToken cancellationToken = default)
    {
        var status = AVCaptureDevice.GetAuthorizationStatus(AVMediaTypes.Video);
        if (status == AVAuthorizationStatus.NotDetermined)
        {
            if (!await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVMediaTypes.Video))
                return null;
        }
        else if (status != AVAuthorizationStatus.Authorized)
        {
            return null;
        }

        var window = UIApplication.SharedApplication.Windows.FirstOrDefault(w => w.IsKeyWindow)
                     ?? UIApplication.SharedApplication.Windows.FirstOrDefault();
        var root = window?.RootViewController;
        if (root is null) return null;

        while (root.PresentedViewController is not null)
            root = root.PresentedViewController;

        using var controller = new QrScannerViewController();
        controller.ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
        await root.PresentViewControllerAsync(controller, true);

        using var registration = cancellationToken.Register(controller.Cancel);
        return await controller.Result;
    }
}

public sealed class QrScannerViewController : UIViewController
{
    private readonly AVCaptureSession _session = new();
    private readonly TaskCompletionSource<string?> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly QrMetadataDelegate _delegate;
    private AVCaptureVideoPreviewLayer? _previewLayer;

    public Task<string?> Result => _result.Task;

    public QrScannerViewController()
    {
        _delegate = new QrMetadataDelegate(Complete);
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.Black;

        var device = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
        if (device is null || !TryConfigureSession(device))
        {
            Complete(null);
            return;
        }

        _previewLayer = new AVCaptureVideoPreviewLayer(_session)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = View.Bounds
        };
        View.Layer.AddSublayer(_previewLayer);

        var cancel = new UIButton(UIButtonType.System)
        {
            Frame = new CoreGraphics.CGRect(0, View.Bounds.Height - 72, View.Bounds.Width, 48),
            AutoresizingMask = UIViewAutoresizing.FlexibleTopMargin | UIViewAutoresizing.FlexibleWidth
        };
        cancel.SetTitle("Cancel", UIControlState.Normal);
        cancel.SetTitleColor(UIColor.White, UIControlState.Normal);
        cancel.TouchUpInside += (_, _) => Cancel();
        View.AddSubview(cancel);
    }

    private bool TryConfigureSession(AVCaptureDevice device)
    {
        var input = AVCaptureDeviceInput.FromDevice(device, out var inputError);
        if (input is null || inputError is not null) return false;
        if (!_session.CanAddInput(input)) return false;
        _session.AddInput(input);

        var output = new AVCaptureMetadataOutput();
        if (!_session.CanAddOutput(output)) return false;
        _session.AddOutput(output);
        output.SetDelegate(_delegate, DispatchQueue.MainQueue);
        output.MetadataObjectTypes = new[] { AVMetadataObjectType.QRCode };
        return true;
    }

    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        _session.StartRunning();
    }

    public override void ViewWillDisappear(bool animated)
    {
        _session.StopRunning();
        base.ViewWillDisappear(animated);
    }

    public void Cancel() => Complete(null);

    private void Complete(string? text)
    {
        if (_result.Task.IsCompleted) return;
        _result.TrySetResult(text);
        DismissViewController(true, null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewLayer?.RemoveFromSuperLayer();
            _previewLayer?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class QrMetadataDelegate : AVCaptureMetadataOutputObjectsDelegate
    {
        private readonly Action<string?> _onResult;

        public QrMetadataDelegate(Action<string?> onResult) => _onResult = onResult;

        public override void DidOutputMetadataObjects(
            AVCaptureMetadataOutput captureOutput,
            AVMetadataObject[] metadataObjects,
            AVCaptureConnection connection)
        {
            foreach (var metadata in metadataObjects)
            {
                if (metadata is AVMetadataMachineReadableCodeObject { StringValue: { } value } code &&
                    code.Type == AVMetadataObjectType.QRCode)
                {
                    _onResult(value);
                    return;
                }
            }
        }
    }
}
