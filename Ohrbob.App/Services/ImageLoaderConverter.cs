using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Serilog;

namespace Ohrbob.App.Services;

public class ImageLoaderConverter : IValueConverter
{
    public static readonly ImageLoaderConverter Instance = new();
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new();
    private static readonly HttpClient HttpClient = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        // If it's already in the cache, return it immediately
        if (Cache.TryGetValue(url, out var cachedBitmap))
            return cachedBitmap;

        // Otherwise, return a task that Avalonia's binding system will await automatically (using ^ symbol in XAML)
        // Note: Avalonia requires Task<object?> for async bindings converter results in some cases,
        // but often returning a raw Task<Bitmap?> works if bound with `Source="{Binding ImageUrl, Converter={x:Static...}}"`
        return new Task<Bitmap?>(() => LoadImageAsync(url).Result);
    }

    private async Task<Bitmap?> LoadImageAsync(string url)
    {
        if (Cache.TryGetValue(url, out var cachedBitmap))
            return cachedBitmap;

        try
        {
            var response = await HttpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            
            var bitmap = new Bitmap(memoryStream);
            Cache.TryAdd(url, bitmap);
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load image from {Url}", url);
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
