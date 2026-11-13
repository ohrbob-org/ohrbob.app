using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
    internal const int MaxMemoryCacheSize = 64;
    private static readonly ConcurrentDictionary<string, Bitmap> MemoryCache = new();
    private static readonly ConcurrentQueue<string> CacheKeys = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<Bitmap?>>> InFlightLoads = new();

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static string? GetDiskCachePath(string url)
    {
        try
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(url));
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cacheDir = Path.Combine(appData, "Ohrbob", "ImageCache");
            Directory.CreateDirectory(cacheDir);

            return Path.Combine(cacheDir, hashString);
        }
        catch (Exception ex)
        {
            // An unavailable disk cache must not make images unloadable.
            Log.Warning(ex, "Image disk cache is unavailable");
            return null;
        }
    }

    private static void AddToMemoryCache(string key, Bitmap bitmap)
    {
        if (MemoryCache.TryAdd(key, bitmap))
        {
            CacheKeys.Enqueue(key);

            // Evict oldest if we exceed the limit
            if (MemoryCache.Count > MaxMemoryCacheSize && CacheKeys.TryDequeue(out var oldestKey))
            {
                // Do not dispose the evicted bitmap: an Image may still be
                // displaying it, and disposing it would blank that control.
                // Without a control reference it is collected by the GC.
                MemoryCache.TryRemove(oldestKey, out _);
            }
        }
    }

    internal static void AddToMemoryCacheForTests(string key, Bitmap bitmap) =>
        AddToMemoryCache(key, bitmap);

    internal static void ClearMemoryCacheForTests()
    {
        MemoryCache.Clear();
        while (CacheKeys.TryDequeue(out _)) { }
        InFlightLoads.Clear();
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

        // The large player art and the player-bar art ask for the same cover at
        // the same time. Share one download/decode instead of doing it twice.
        // Lazy keeps the factory from starting a second load for the same key.
        var load = InFlightLoads.GetOrAdd(cacheKey, _ => new Lazy<Task<Bitmap?>>(
            () => LoadAndCacheAsync(url, maxWidth, cacheKey),
            LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await load.Value;
        }
        finally
        {
            // Only remove our own in-flight entry, not a newer one for the same key.
            if (InFlightLoads.TryGetValue(cacheKey, out var current) && ReferenceEquals(current, load))
                InFlightLoads.TryRemove(cacheKey, out _);
        }
    }

    private static async Task<Bitmap?> LoadAndCacheAsync(string url, int maxWidth, string cacheKey)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var diskPath = GetDiskCachePath(url);

                // 2. Check disk cache. A truncated/corrupt file must not poison
                // every later load, so drop it and fall through to the network.
                if (diskPath is not null && File.Exists(diskPath) && new FileInfo(diskPath).Length > 0)
                {
                    try
                    {
                        var cached = await Task.Run(() =>
                        {
                            using var stream = File.OpenRead(diskPath);
                            return Decode(stream, maxWidth);
                        });
                        AddToMemoryCache(cacheKey, cached);
                        return cached;
                    }
                    catch
                    {
                        try { File.Delete(diskPath); } catch { }
                    }
                }

                // 3. Download from network
                using var response = await HttpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync();

                // The disk cache is an optimization only: never lose the image
                // because writing it failed (permissions, disk full, ...).
                // Write to a unique temp file and move it into place so a
                // concurrent load or a crash can never leave a partial image.
                if (diskPath is not null)
                {
                    var tempPath = $"{diskPath}.{Guid.NewGuid():N}.tmp";
                    try
                    {
                        await File.WriteAllBytesAsync(tempPath, bytes);
                        File.Move(tempPath, diskPath, overwrite: true);
                    }
                    catch
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }

                // Decoding may run on the UI thread after the network await.
                var downloaded = await Task.Run(() =>
                {
                    using var memoryStream = new MemoryStream(bytes);
                    return Decode(memoryStream, maxWidth);
                });

                AddToMemoryCache(cacheKey, downloaded);
                return downloaded;
            }
            catch (Exception ex)
            {
                if (attempt == 0)
                {
                    Log.Warning(ex, "Image load failed, retrying {Url}", url);
                    await Task.Delay(250);
                    continue;
                }

                Log.Error(ex, "Failed to load image from {Url}", url);
            }
        }

        return null;
    }
}
