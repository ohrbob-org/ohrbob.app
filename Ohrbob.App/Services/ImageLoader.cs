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

    static ImageLoader()
    {
        SourceProperty.Changed.AddClassHandler<Image>(OnSourceChanged);
    }

    public static string? GetSource(Image element) => element.GetValue(SourceProperty);
    public static void SetSource(Image element, string? value) => element.SetValue(SourceProperty, value);

    private static async void OnSourceChanged(Image image, AvaloniaPropertyChangedEventArgs args)
    {
        var url = args.NewValue as string;
        if (string.IsNullOrWhiteSpace(url))
        {
            image.Source = null;
            return;
        }

        var bitmap = await LoadImageAsync(url);
        
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

    private static void AddToMemoryCache(string url, Bitmap bitmap)
    {
        if (MemoryCache.TryAdd(url, bitmap))
        {
            CacheKeys.Enqueue(url);
            
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

    private static async Task<Bitmap?> LoadImageAsync(string url)
    {
        // 1. Check memory cache
        if (MemoryCache.TryGetValue(url, out var cachedBitmap))
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
                    var bitmap = await Task.Run(() => new Bitmap(diskPath));
                    AddToMemoryCache(url, bitmap);
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
            var downloadedBitmap = new Bitmap(memoryStream);
            
            AddToMemoryCache(url, downloadedBitmap);
            return downloadedBitmap;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load image from {Url}", url);
            return null;
        }
    }
}
