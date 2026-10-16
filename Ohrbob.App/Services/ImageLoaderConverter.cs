using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Serilog;

namespace Ohrbob.App.Services;

public class ImageLoaderConverter : IValueConverter
{
    public static readonly ImageLoaderConverter Instance = new();
    
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

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        // 1. Check Memory Cache (instant sync return)
        if (MemoryCache.TryGetValue(url, out var cachedBitmap))
            return cachedBitmap;

        // 2. Fallback to async load (Disk or Network)
        return new Task<Bitmap?>(() => LoadImageAsync(url).Result);
    }

    private async Task<Bitmap?> LoadImageAsync(string url)
    {
        // Double check memory cache in case another task already loaded it
        if (MemoryCache.TryGetValue(url, out var cachedBitmap))
            return cachedBitmap;

        try
        {
            var diskPath = GetDiskCachePath(url);

            // 1. Check Disk Cache
            if (File.Exists(diskPath))
            {
                // Verify file isn't empty/corrupt
                var fileInfo = new FileInfo(diskPath);
                if (fileInfo.Length > 0)
                {
                    var bitmap = new Bitmap(diskPath);
                    AddToMemoryCache(url, bitmap);
                    return bitmap;
                }
            }

            // 2. Download from Network
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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
