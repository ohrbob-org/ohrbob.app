using NUnit.Framework;
using Ohrbob.App.Services;
using ZXing;
using ZXing.Common;

namespace Ohrbob.App.Tests.Services;

[TestFixture]
public class QrCodeDecoderTests
{
    [Test]
    public void Decode_ReadsAGeneratedQrCode()
    {
        const string value = "ohrbob://connect?server=https://example.com&token=abc";
        var writer = new BarcodeWriterGeneric
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 200, Height = 200, Margin = 4 }
        };
        var matrix = writer.Encode(value);
        var gray = new byte[matrix.Width * matrix.Height];
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
                gray[y * matrix.Width + x] = matrix[x, y] ? (byte)0 : (byte)255;
        }

        Assert.That(QrCodeDecoder.Decode(gray, matrix.Width, matrix.Height, matrix.Width), Is.EqualTo(value));
    }
}
