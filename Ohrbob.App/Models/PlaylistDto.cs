using System;
using System.Collections.Generic;

namespace Ohrbob.App.Models;

public class PlaylistDto
{
    public string? StationId { get; set; }
    public bool HostPaceEnabled { get; set; }
    public bool IsAging { get; set; }
    public List<PlaylistEpisodeDto> Episodes { get; set; } = new();
}

public class PlaylistEpisodeDto
{
    public string? EpisodeId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Artist { get; set; }
    public string? CoverUrl { get; set; }
    public string? StreamUri { get; set; }
    public DateTime? PublishedAt { get; set; }
    public bool IsCurrent { get; set; }
}
