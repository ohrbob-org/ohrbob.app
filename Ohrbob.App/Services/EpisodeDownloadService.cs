using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ohrbob.App.Models;
using SoundFlow.Metadata;
using SoundFlow.Metadata.Models;

namespace Ohrbob.App.Services;

public class EpisodeDownloadService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".flac", ".wav"
    };

    private readonly string _cacheRoot;
    private readonly HttpClient _httpClient;
    private readonly Func<string, TimeSpan> _durationReader;
    private CancellationTokenSource? _cts;

    public event EventHandler? DownloadStateChanged;

    public EpisodeDownloadService(
        string? cacheRoot = null,
        HttpClient? httpClient = null,
        Func<string, TimeSpan>? durationReader = null)
    {
        _cacheRoot = cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ohrbob",
            "downloads");
        _httpClient = httpClient ?? new HttpClient();
        _durationReader = durationReader ?? ReadDuration;
    }

    public string? GetDownloadedPath(string stationId, string episodeId)
    {
        var folder = StationFolder(stationId);
        if (!Directory.Exists(folder))
            return null;

        var stem = SafeSegment(episodeId);
        return Directory.EnumerateFiles(folder)
            .FirstOrDefault(file =>
                !file.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileNameWithoutExtension(file), stem, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SyncAsync(
        string stationId,
        IReadOnlyList<PlaylistItem> playlist,
        int bufferMinutes,
        bool downloadsAllowed,
        int currentPositionSeconds,
        CancellationToken cancellationToken,
        string? protectedEpisodeId = null,
        bool prefetchNext = false)
    {
        using var own = new CancellationTokenSource();
        _cts = own;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, own.Token);
        var token = linked.Token;

        try
        {
            var folder = StationFolder(stationId);
            var next = prefetchNext ? FindNextItem(playlist) : null;

            if (!downloadsAllowed)
            {
                await PruneAsync(stationId, playlist, 0, protectedEpisodeId);
                ReconcileStates(stationId, playlist);
                return;
            }

            if (bufferMinutes <= 0)
            {
                // Without a buffer only one episode is kept ready for the next skip.
                if (next is not null && next.DownloadState != DownloadState.Downloaded)
                    await EnsureDownloadedAsync(stationId, next, folder, token);

                await PruneAsync(stationId, playlist, 0, protectedEpisodeId,
                    currentPositionSeconds, prefetchNext);
                ReconcileStates(stationId, playlist);
                return;
            }

            var target = TimeSpan.FromMinutes(bufferMinutes);
            var budget = TimeSpan.Zero;
            var nextAttempted = false;

            foreach (var item in playlist)
            {
                token.ThrowIfCancellationRequested();

                if (ReferenceEquals(item, next))
                    nextAttempted = true;

                var elapsed = item.IsCurrent
                    ? TimeSpan.FromSeconds(currentPositionSeconds)
                    : TimeSpan.Zero;
                var duration = await EnsureDownloadedAsync(stationId, item, folder, token);
                if (duration is null)
                    continue;

                var remaining = duration.Value - elapsed;
                if (remaining > TimeSpan.Zero)
                    budget += remaining;
                if (budget >= target)
                    break;
            }

            // The next episode stays prefetched even when the current episode alone
            // already fills the buffer target.
            if (next is not null && !nextAttempted && next.DownloadState != DownloadState.Downloaded)
                await EnsureDownloadedAsync(stationId, next, folder, token);

            // Lowering the target must remove files that no longer fit, while keeping
            // the protected current episode, the prefetched next episode and any
            // files still inside the budget.
            await PruneAsync(stationId, playlist, bufferMinutes, protectedEpisodeId,
                currentPositionSeconds, prefetchNext);

            ReconcileStates(stationId, playlist);
        }
        finally
        {
            if (ReferenceEquals(_cts, own))
                _cts = null;
        }
    }

    public Task PruneAsync(
        string stationId,
        IReadOnlyList<PlaylistItem> playlist,
        int bufferMinutes,
        string? protectedEpisodeId = null,
        int currentPositionSeconds = 0,
        bool prefetchNext = false)
    {
        // File enumeration and deletion run off the UI thread; SyncAsync awaits this
        // without ConfigureAwait(false) so its state updates still resume on the UI thread.
        return Task.Run(() => Prune(stationId, playlist, bufferMinutes, protectedEpisodeId, currentPositionSeconds, prefetchNext));
    }

    private void Prune(
        string stationId,
        IReadOnlyList<PlaylistItem> playlist,
        int bufferMinutes,
        string? protectedEpisodeId,
        int currentPositionSeconds,
        bool prefetchNext)
    {
        var folder = StationFolder(stationId);
        if (!Directory.Exists(folder))
            return;

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(protectedEpisodeId))
            keep.Add(SafeSegment(protectedEpisodeId));

        if (prefetchNext && FindNextItem(playlist) is { } next)
            keep.Add(SafeSegment(next.EpisodeId));

        var target = TimeSpan.FromMinutes(Math.Max(0, bufferMinutes));
        if (target > TimeSpan.Zero)
        {
            var budget = TimeSpan.Zero;
            foreach (var item in playlist)
            {
                // Only files that actually exist count towards the budget; stale files
                // for episodes that are not downloaded must not be kept.
                var existing = GetDownloadedPath(stationId, item.EpisodeId);
                if (existing is null)
                    continue;

                keep.Add(SafeSegment(item.EpisodeId));
                var elapsed = item.IsCurrent ? TimeSpan.FromSeconds(currentPositionSeconds) : TimeSpan.Zero;
                var remaining = _durationReader(existing) - elapsed;
                if (remaining > TimeSpan.Zero)
                    budget += remaining;
                if (budget >= target)
                    break;
            }
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (file.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                continue;
            if (keep.Contains(Path.GetFileNameWithoutExtension(file)))
                continue;
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Makes sure the item is on disk. Returns its duration, or null when the
    /// download failed (the item is then marked Failed).
    /// </summary>
    private async Task<TimeSpan?> EnsureDownloadedAsync(
        string stationId,
        PlaylistItem item,
        string folder,
        CancellationToken token)
    {
        var existing = GetDownloadedPath(stationId, item.EpisodeId);
        if (existing is not null)
        {
            SetState(item, DownloadState.Downloaded);
            return await ReadDurationAsync(existing);
        }

        SetState(item, DownloadState.Queued);
        SetState(item, DownloadState.Downloading);

        var downloaded = await TryDownloadAsync(item, folder, token);
        if (downloaded is null)
        {
            SetState(item, DownloadState.Failed);
            return null;
        }

        SetState(item, DownloadState.Downloaded);
        return downloaded.Value;
    }

    private void ReconcileStates(string stationId, IReadOnlyList<PlaylistItem> playlist)
    {
        foreach (var item in playlist)
        {
            var path = GetDownloadedPath(stationId, item.EpisodeId);
            // Keep a failed attempt visible until a later sync retries it.
            if (path is null && item.DownloadState == DownloadState.Failed)
                continue;
            SetState(item, path is null ? DownloadState.NotDownloaded : DownloadState.Downloaded);
        }
    }

    /// <summary>The episode that would start if the listener skipped right now.</summary>
    private static PlaylistItem? FindNextItem(IReadOnlyList<PlaylistItem> playlist)
    {
        var currentIndex = -1;
        for (var i = 0; i < playlist.Count; i++)
        {
            if (!playlist[i].IsCurrent) continue;
            currentIndex = i;
            break;
        }

        if (currentIndex < 0)
            return playlist.FirstOrDefault(item => !item.IsCurrent);
        return currentIndex + 1 < playlist.Count ? playlist[currentIndex + 1] : null;
    }

    private async Task<TimeSpan?> TryDownloadAsync(PlaylistItem item, string folder, CancellationToken token)
    {
        var uri = item.StreamUri;
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out _))
            return null;

        var path = Path.Combine(folder, SafeSegment(item.EpisodeId) + ExtensionFor(uri));
        var partPath = path + ".part";

        try
        {
            if (!await DownloadToFileAsync(uri, partPath, token))
            {
                TryDelete(partPath);
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(partPath);
            SetState(item, DownloadState.NotDownloaded);
            throw;
        }
        catch (HttpRequestException)
        {
            // Network failures are retried once.
            try
            {
                if (!await DownloadToFileAsync(uri, partPath, token))
                {
                    TryDelete(partPath);
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                TryDelete(partPath);
                throw;
            }
            catch (Exception)
            {
                TryDelete(partPath);
                return null;
            }
        }
        catch (Exception)
        {
            TryDelete(partPath);
            return null;
        }

        try
        {
            File.Move(partPath, path, overwrite: true);
        }
        catch (Exception)
        {
            TryDelete(partPath);
            return null;
        }

        return await ReadDurationAsync(path);
    }

    // SoundMetadataReader blocks waiting on its own async read. Calling it on the UI
    // thread therefore deadlocks the app; keep every probe on a worker thread.
    private Task<TimeSpan> ReadDurationAsync(string path) => Task.Run(() => _durationReader(path));

    private async Task<bool> DownloadToFileAsync(string uri, string partPath, CancellationToken token)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(partPath)!);
        await using var file = File.Create(partPath);
        await response.Content.CopyToAsync(file, token);
        return true;
    }

    private void SetState(PlaylistItem item, DownloadState state)
    {
        if (item.DownloadState == state)
            return;
        item.DownloadState = state;
        DownloadStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private string StationFolder(string stationId)
    {
        var root = Path.GetFullPath(_cacheRoot);
        var folder = Path.GetFullPath(Path.Combine(root, SafeSegment(stationId)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        // Defense in depth: never operate outside the cache root.
        return folder.StartsWith(prefix, StringComparison.Ordinal)
            ? folder
            : Path.Combine(root, "_");
    }

    private static string SafeSegment(string value)
    {
        if (value.Length == 0 || value == "." || value == "..")
            return "_";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(c => invalid.Contains(c) || c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar
                ? '_'
                : c)
            .ToArray();
        return new string(chars);
    }

    private static string ExtensionFor(string uri)
    {
        var extension = Path.GetExtension(new Uri(uri).AbsolutePath);
        return AllowedExtensions.Contains(extension) ? extension : ".audio";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static TimeSpan ReadDuration(string path)
    {
        try
        {
            var result = SoundMetadataReader.Read(path, new ReadOptions
            {
                ReadTags = false,
                ReadAlbumArt = false,
                DurationAccuracy = DurationAccuracy.FastEstimate
            });
            return result.Value?.Duration ?? TimeSpan.Zero;
        }
        catch (Exception)
        {
            return TimeSpan.Zero;
        }
    }
}
