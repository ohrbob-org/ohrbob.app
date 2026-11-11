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
        string? protectedEpisodeId = null)
    {
        using var own = new CancellationTokenSource();
        _cts = own;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, own.Token);
        var token = linked.Token;

        try
        {
            if (!downloadsAllowed || bufferMinutes <= 0)
            {
                await PruneAsync(stationId, playlist, 0, protectedEpisodeId);
                foreach (var item in playlist)
                {
                    var state = GetDownloadedPath(stationId, item.EpisodeId) is null
                        ? DownloadState.NotDownloaded
                        : DownloadState.Downloaded;
                    SetState(item, state);
                }
                return;
            }

            var target = TimeSpan.FromMinutes(bufferMinutes);
            var budget = TimeSpan.Zero;
            var folder = StationFolder(stationId);

            foreach (var item in playlist)
            {
                token.ThrowIfCancellationRequested();

                var elapsed = item.IsCurrent
                    ? TimeSpan.FromSeconds(currentPositionSeconds)
                    : TimeSpan.Zero;
                var existing = GetDownloadedPath(stationId, item.EpisodeId);
                TimeSpan duration;

                if (existing is not null)
                {
                    SetState(item, DownloadState.Downloaded);
                    duration = _durationReader(existing);
                }
                else
                {
                    SetState(item, DownloadState.Queued);
                    SetState(item, DownloadState.Downloading);

                    var downloaded = await TryDownloadAsync(item, folder, token);
                    if (downloaded is null)
                    {
                        SetState(item, DownloadState.Failed);
                        continue;
                    }

                    duration = downloaded.Value;
                    SetState(item, DownloadState.Downloaded);
                }

                var remaining = duration - elapsed;
                if (remaining > TimeSpan.Zero)
                    budget += remaining;
                if (budget >= target)
                    break;
            }
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
        string? protectedEpisodeId = null)
    {
        var folder = StationFolder(stationId);
        if (!Directory.Exists(folder))
            return Task.CompletedTask;

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(protectedEpisodeId))
            keep.Add(SafeSegment(protectedEpisodeId));

        var target = TimeSpan.FromMinutes(Math.Max(0, bufferMinutes));
        if (target > TimeSpan.Zero)
        {
            var budget = TimeSpan.Zero;
            foreach (var item in playlist)
            {
                keep.Add(SafeSegment(item.EpisodeId));
                var existing = GetDownloadedPath(stationId, item.EpisodeId);
                if (existing is not null)
                    budget += _durationReader(existing);
                if (budget >= target)
                    break;
            }
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
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

        return Task.CompletedTask;
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

        return _durationReader(path);
    }

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

    private string StationFolder(string stationId) =>
        Path.Combine(_cacheRoot, SafeSegment(stationId));

    private static string SafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(c => invalid.Contains(c) || c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar
                ? '_'
                : c)
            .ToArray();
        return chars.Length == 0 ? "_" : new string(chars);
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
