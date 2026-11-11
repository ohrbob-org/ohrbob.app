using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ohrbob.App.Models;

namespace Ohrbob.App.Services;

public class OfflineStateService
{
    private readonly string _path;

    public OfflineStateService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ohrbob",
            "offline-state.json");
    }

    public OfflineSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            return JsonSerializer.Deserialize<OfflineSnapshot>(File.ReadAllText(_path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(OfflineSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(Sanitize(snapshot), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempPath, _path, overwrite: true);
    }

    // Copies, so the caller's in-memory StreamUri (needed for playback) keeps its query string.
    private static OfflineSnapshot Sanitize(OfflineSnapshot snapshot)
    {
        var states = snapshot.StationStates.ToDictionary(
            pair => pair.Key,
            pair => new StationOfflineState
            {
                StationId = pair.Value.StationId,
                HostPaceEnabled = pair.Value.HostPaceEnabled,
                IsAging = pair.Value.IsAging,
                CurrentEpisodeId = pair.Value.CurrentEpisodeId,
                PositionSeconds = pair.Value.PositionSeconds,
                Playlist = pair.Value.Playlist.Select(episode => new PlaylistEpisodeDto
                {
                    EpisodeId = episode.EpisodeId,
                    Title = episode.Title,
                    Description = episode.Description,
                    Artist = episode.Artist,
                    CoverUrl = episode.CoverUrl,
                    StreamUri = episode.StreamUri?.Split('?', '#')[0],
                    PublishedAt = episode.PublishedAt,
                    IsCurrent = episode.IsCurrent
                }).ToList()
            });

        return new OfflineSnapshot
        {
            Version = snapshot.Version,
            SavedAt = snapshot.SavedAt,
            ActiveStationId = snapshot.ActiveStationId,
            ProfileName = snapshot.ProfileName,
            Stations = snapshot.Stations,
            StationStates = states
        };
    }
}
