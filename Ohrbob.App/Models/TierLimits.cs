namespace Ohrbob.App.Models;

public class TierLimits
{
    public int MaxStations { get; set; }
    public int MaxFeedsPerStation { get; set; }
    public int MaxProfiles { get; set; }
    public bool StreamEnabled { get; set; }
}