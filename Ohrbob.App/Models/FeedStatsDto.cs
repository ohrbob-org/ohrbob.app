using System;

namespace Ohrbob.App.Models;

public class FeedStatsDto
{
    public string? Url { get; set; }
    public string? Title { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime? LastUpdated { get; set; }
    public long EpisodeCount { get; set; }
}