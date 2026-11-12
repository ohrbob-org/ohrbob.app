using System;
using System.Threading;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware;
using Android.OS;
using Android.Views;
using Android.Widget;
using Ohrbob.App.Services;
using Camera = Android.Hardware.Camera;
using SurfaceTexture = Android.Graphics.SurfaceTexture;

namespace Ohrbob.App.Android;

public sealed class AndroidQrScanner : IQrScanner
{
    public const int RequestCode = 4242;
    private static TaskCompletionSource<string?>? _pending;
    private readonly Activity _activity;

    public AndroidQrScanner(Activity activity) => _activity = activity;

    public bool IsAvailable =>
        _activity.PackageManager?.HasSystemFeature(PackageManager.FeatureCamera) == true;

    public Task<string?> ScanAsync(CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;
        try
        {
            _activity.StartActivityForResult(new Intent(_activity, typeof(QrScannerActivity)), RequestCode);
            cancellationToken.Register(() => QrScannerActivity.Current?.Finish());
        }
        catch (Exception)
        {
            _pending = null;
            tcs.TrySetResult(null);
        }

        return tcs.Task;
    }

    internal static void HandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestCode) return;
        var tcs = _pending;
        _pending = null;
        var text = resultCode == Result.Ok ? data?.GetStringExtra(QrScannerActivity.ExtraResult) : null;
        tcs?.TrySetResult(text);
    }
}

[Activity(Label = "Scan QR code", Theme = "@style/MyTheme.NoActionBar", Exported = false)]
public sealed class QrScannerActivity : Activity, TextureView.ISurfaceTextureListener, Camera.IPreviewCallback
{
    public const string ExtraResult = "qr_result";
    private const int PermissionRequest = 1;

    private Camera? _camera;
    private TextureView? _preview;
    private SurfaceTexture? _surface;
    private bool _decoded;

    public static QrScannerActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;

        BuildUi();
        if (CheckSelfPermission(Manifest.Permission.Camera) != Permission.Granted)
            RequestPermissions(new[] { Manifest.Permission.Camera }, PermissionRequest);
    }

    private void BuildUi()
    {
        var root = new FrameLayout(this);
        _preview = new TextureView(this);
        _preview.SurfaceTextureListener = this;
        root.AddView(_preview, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var cancel = new Button(this) { Text = "Cancel" };
        cancel.Click += (_, _) =>
        {
            SetResult(Result.Canceled);
            Finish();
        };
        var parameters = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.Bottom | GravityFlags.CenterHorizontal
        };
        parameters.BottomMargin = 48;
        root.AddView(cancel, parameters);
        SetContentView(root);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        if (requestCode != PermissionRequest) return;
        if (grantResults.Length == 0 || grantResults[0] != Permission.Granted)
        {
            SetResult(Result.Canceled);
            Finish();
            return;
        }

        if (_surface is not null && _camera is null)
            StartCamera(_surface);
    }

    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
    {
        _surface = surface;
        if (CheckSelfPermission(Manifest.Permission.Camera) == Permission.Granted)
            StartCamera(surface);
    }

    private void StartCamera(SurfaceTexture surface)
    {
        try
        {
            _camera = Camera.Open();
            if (_camera is null)
            {
                Finish();
                return;
            }

            var parameters = _camera.GetParameters();
            if (parameters is not null)
            {
                var size = ChoosePreviewSize(parameters.SupportedPreviewSizes);
                if (size is not null) parameters.SetPreviewSize(size.Width, size.Height);

                // Autofocus is optional: use continuous focus only when the device
                // actually advertises it, otherwise leave the camera's default.
                var focusModes = parameters.SupportedFocusModes;
                if (focusModes is not null && focusModes.Contains(Camera.Parameters.FocusModeContinuousVideo))
                    parameters.FocusMode = Camera.Parameters.FocusModeContinuousVideo;
                else if (focusModes is not null && focusModes.Contains(Camera.Parameters.FocusModeContinuousPicture))
                    parameters.FocusMode = Camera.Parameters.FocusModeContinuousPicture;

                _camera.SetParameters(parameters);
            }

            _camera.SetPreviewTexture(surface);
            _camera.SetPreviewCallback(this);
            _camera.StartPreview();
        }
        catch (Exception)
        {
            SetResult(Result.Canceled);
            Finish();
        }
    }

    private static Camera.Size? ChoosePreviewSize(System.Collections.Generic.IList<Camera.Size>? sizes)
    {
        if (sizes is null) return null;

        Camera.Size? best = null;
        long bestScore = long.MaxValue;
        foreach (var size in sizes)
        {
            var score = Math.Abs((long)size.Width * size.Height - 1280L * 720L);
            if (score < bestScore)
            {
                bestScore = score;
                best = size;
            }
        }

        return best;
    }

    public void OnPreviewFrame(byte[]? data, Camera? camera)
    {
        if (_decoded || data is null || _camera is null) return;

        var size = _camera.GetParameters()?.PreviewSize;
        if (size is null) return;

        var text = QrCodeDecoder.Decode(data, size.Width, size.Height, size.Width);
        if (text is null) return;

        _decoded = true;
        RunOnUiThread(() =>
        {
            SetResult(Result.Ok, new Intent().PutExtra(ExtraResult, text));
            Finish();
        });
    }

    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height) { }

    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
    {
        StopCamera();
        _surface = null;
        return true;
    }

    public void OnSurfaceTextureUpdated(SurfaceTexture surface) { }

    protected override void OnPause()
    {
        StopCamera();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        StopCamera();
        Current = null;
        base.OnDestroy();
    }

    private void StopCamera()
    {
        try
        {
            _camera?.SetPreviewCallback(null);
            _camera?.StopPreview();
        }
        catch
        {
            // Camera may already be gone.
        }
        finally
        {
            _camera?.Release();
            _camera = null;
        }
    }
}
