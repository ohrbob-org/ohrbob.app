using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ColorConversionCodes = OpenCvSharp.ColorConversionCodes;
using Cv2 = OpenCvSharp.Cv2;
using Mat = OpenCvSharp.Mat;
using VideoCapture = OpenCvSharp.VideoCapture;
using Ohrbob.App.Services;

namespace Ohrbob.App.Desktop;

/// <summary>Live webcam QR scanner for desktop platforms.</summary>
public sealed class DesktopQrScanner : IQrScanner
{
    public bool IsAvailable => true;

    public Task<string?> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return ShowAsync(cancellationToken);

        return Dispatcher.UIThread.InvokeAsync(() => ShowAsync(cancellationToken));
    }

    private static async Task<string?> ShowAsync(CancellationToken cancellationToken)
    {
        var window = new QrScannerWindow();
        var owner = (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is not null)
            _ = window.ShowDialog(owner);
        else
            window.Show();

        using var registration = cancellationToken.Register(window.Cancel);
        return await window.Result;
    }
}

public sealed class QrScannerWindow : Window
{
    private readonly TaskCompletionSource<string?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Image _preview;
    private readonly TextBlock _status;
    private readonly CancellationTokenSource _cts = new();
    private WriteableBitmap? _bitmap;

    public Task<string?> Result => _result.Task;

    public QrScannerWindow()
    {
        Title = "Scan QR code";
        Width = 520;
        Height = 460;
        CanResize = false;
        Background = Brushes.Black;

        _preview = new Image { Stretch = Stretch.Uniform };
        _status = new TextBlock
        {
            Text = "Point the camera at the QR code",
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12)
        };

        var cancel = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12)
        };
        cancel.Click += (_, _) => Cancel();

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                _status,
                _preview,
                cancel
            }
        };
        Grid.SetRow(_status, 0);
        Grid.SetRow(_preview, 1);
        Grid.SetRow(cancel, 2);

        Opened += (_, _) => _ = CaptureLoopAsync(_cts.Token);
        Closed += (_, _) =>
        {
            _cts.Cancel();
            _result.TrySetResult(null);
        };
    }

    public void Cancel()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Cancel);
            return;
        }

        _cts.Cancel();
        Close();
    }

    private void Complete(string text)
    {
        _result.TrySetResult(text);
        Close();
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        try
        {
            await Task.Run(() =>
            {
                using var capture = new VideoCapture(0);
                if (!capture.IsOpened())
                {
                    Dispatcher.UIThread.Post(() => _status.Text = "No camera found.");
                    return;
                }

                using var frame = new Mat();
                using var gray = new Mat();
                using var bgra = new Mat();

                while (!token.IsCancellationRequested)
                {
                    if (!capture.Read(frame) || frame.Empty())
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                    var grayBytes = CopyBytes(gray);
                    var text = QrCodeDecoder.Decode(grayBytes, gray.Width, gray.Height, (int)gray.Step());
                    if (text is not null)
                    {
                        Dispatcher.UIThread.Post(() => Complete(text));
                        return;
                    }

                    Cv2.CvtColor(frame, bgra, ColorConversionCodes.BGR2BGRA);
                    var frameBytes = CopyBytes(bgra);
                    Dispatcher.UIThread.Post(() => ShowFrame(frameBytes, bgra.Width, bgra.Height));
                    Thread.Sleep(40);
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => _status.Text = $"Camera error: {ex.Message}");
        }
    }

    private static byte[] CopyBytes(Mat mat)
    {
        var length = (int)(mat.Total() * mat.ElemSize());
        var bytes = new byte[length];
        Marshal.Copy(mat.Data, bytes, 0, length);
        return bytes;
    }

    private void ShowFrame(byte[] pixels, int width, int height)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
            _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using (var locked = _bitmap.Lock())
        {
            var rowBytes = width * 4;
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(pixels, y * rowBytes, locked.Address + y * locked.RowBytes, rowBytes);
            }
        }

        _preview.Source = _bitmap;
    }
}
