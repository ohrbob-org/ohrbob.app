using System;
using System.Collections.Generic;

namespace Ohrbob.App.Models;

public class OfflineSnapshot
{
    public int Version { get; set; } = 1;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ActiveStationId { get; set; }
    public string? ProfileName { get; set; }
    public List<StationWithFeedsDto> Stations { get; set; } = new();
    public Dictionary<string, StationOfflineState> StationStates { get; set; } = new();
}

public class StationOfflineState
{
    public string? StationId { get; set; }
    public bool HostPaceEnabled { get; set; }
    public bool IsAging { get; set; }
    public List<PlaylistEpisodeDto> Playlist { get; set; } = new();
    public string? CurrentEpisodeId { get; set; }
    public double PositionSeconds { get; set; }
}
