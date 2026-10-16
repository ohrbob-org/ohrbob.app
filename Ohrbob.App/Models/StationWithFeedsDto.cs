using System.Collections.Generic;

namespace Ohrbob.App.Models;

public class StationWithFeedsDto
{
    public Station? Station { get; set; }
    public List<FeedStatsDto>? Feeds { get; set; }
    public bool IsUpdating { get; set; }
}