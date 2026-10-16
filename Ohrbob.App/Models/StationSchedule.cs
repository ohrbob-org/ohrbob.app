using System.Collections.Generic;

namespace Ohrbob.App.Models;

public class StationSchedule
{
    public List<StationFeedSchedule>? Feeds { get; set; }
}