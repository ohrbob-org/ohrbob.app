using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Serilog;

namespace Ohrbob.App.Services;

public static class ImageLoader
{
    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Source", typeof(ImageLoader));

    /// <summary>
    /// Optional maximum decoded width. Use for small thumbnails so a full-size
    /// photo is not kept as a texture and rescaled on every frame.
    /// </summary>
    public static readonly AttachedProperty<int> MaxWidthProperty =
        AvaloniaProperty.RegisterAttached<Image, int>("MaxWidth", typeof(ImageLoader));

    static ImageLoader()
    {
        SourceProperty.Changed.AddClassHandler<Image>(OnSourceChanged);
        MaxWidthProperty.Changed.AddClassHandler<Image>((image, _) => LoadAndApply(image));
    }

    public static string? GetSource(Image element) => element.GetValue(SourceProperty);
    public static void SetSource(Image element, string? value) => element.SetValue(SourceProperty, value);

    public static int GetMaxWidth(Image element) => element.GetValue(MaxWidthProperty);
    public static void SetMaxWidth(Image element, int value) => element.SetValue(MaxWidthProperty, value);

    private static void OnSourceChanged(Image image, AvaloniaPropertyChangedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.NewValue as string))
        {
            image.Source = null;
            return;
        }

        LoadAndApply(image);
    }

    private static async void LoadAndApply(Image image)
    {
        var url = GetSource(image);
        if (string.IsNullOrWhiteSpace(url))
        {
            image.Source = null;
            return;
        }

        var bitmap = await LoadImageAsync(url, GetMaxWidth(image));

        // Only apply if the URL hasn't changed during the await
        if (GetSource(image) == url)
        {
            image.Source = bitmap;
        }
    }

    // Memory Cache
    private const int MaxMemoryCacheSize = 64;
    private static readonly ConcurrentDictionary<string, Bitmap> MemoryCache = new();
    private static readonly ConcurrentQueue<string> CacheKeys = new();

    private static readonly HttpClient HttpClient = new();

    private static string GetDiskCachePath(string url)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(url));
        var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cacheDir = Path.Combine(appData, "Ohrbob", "ImageCache");
        Directory.CreateDirectory(cacheDir);

        return Path.Combine(cacheDir, hashString);
    }

    private static void AddToMemoryCache(string key, Bitmap bitmap)
    {
        if (MemoryCache.TryAdd(key, bitmap))
        {
            CacheKeys.Enqueue(key);

            // Evict oldest if we exceed the limit
            if (MemoryCache.Count > MaxMemoryCacheSize)
            {
                if (CacheKeys.TryDequeue(out var oldestKey))
                {
                    if (MemoryCache.TryRemove(oldestKey, out var oldBitmap))
                    {
                        oldBitmap.Dispose();
                    }
                }
            }
        }
    }

    private static Bitmap Decode(Stream stream, int maxWidth) =>
        maxWidth > 0
            ? Bitmap.DecodeToWidth(stream, maxWidth)
            : new Bitmap(stream);

    private static async Task<Bitmap?> LoadImageAsync(string url, int maxWidth)
    {
        // Scaled variants get their own cache entry.
        var cacheKey = maxWidth > 0 ? $"{url}|{maxWidth}" : url;

        // 1. Check memory cache
        if (MemoryCache.TryGetValue(cacheKey, out var cachedBitmap))
            return cachedBitmap;

        try
        {
            var diskPath = GetDiskCachePath(url);

            // 2. Check Disk Cache
            if (File.Exists(diskPath))
            {
                var fileInfo = new FileInfo(diskPath);
                if (fileInfo.Length > 0)
                {
                    // Run disk IO on background thread
                    var bitmap = await Task.Run(() =>
                    {
                        using var stream = File.OpenRead(diskPath);
                        return Decode(stream, maxWidth);
                    });
                    AddToMemoryCache(cacheKey, bitmap);
                    return bitmap;
                }
            }

            // 3. Download from Network
            var response = await HttpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync();

            // Save to disk asynchronously
            await File.WriteAllBytesAsync(diskPath, bytes);

            // Load to memory and cache
            using var memoryStream = new MemoryStream(bytes);
            var downloadedBitmap = Decode(memoryStream, maxWidth);

            AddToMemoryCache(cacheKey, downloadedBitmap);
            return downloadedBitmap;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load image from {Url}", url);
            return null;
        }
    }
}
