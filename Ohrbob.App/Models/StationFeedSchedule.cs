namespace Ohrbob.App.Models;

public class StationFeedSchedule
{
    public string? Url { get; set; }
    public string? Color { get; set; }
    public FeedSortOrder SortOrder { get; set; }
    public int EpisodesToPlay { get; set; }
}