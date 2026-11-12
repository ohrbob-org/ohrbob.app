using System;
using ZXing;
using ZXing.Common;

namespace Ohrbob.App.Services;

/// <summary>Decodes a QR code from a grayscale camera frame.</summary>
public static class QrCodeDecoder
{
    public static string? Decode(byte[] gray, int width, int height, int rowStride, int offset = 0)
    {
        if (width <= 0 || height <= 0 || gray.Length == 0) return null;

        byte[] source = gray;
        if (rowStride != width || offset != 0)
        {
            source = new byte[width * height];
            for (var row = 0; row < height; row++)
            {
                Buffer.BlockCopy(gray, offset + row * rowStride, source, row * width, width);
            }
        }

        try
        {
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                }
            };

            return reader.Decode(source, width, height, RGBLuminanceSource.BitmapFormat.Gray8)?.Text;
        }
        catch
        {
            return null;
        }
    }
}
