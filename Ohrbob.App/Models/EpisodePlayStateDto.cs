namespace Ohrbob.App.Models;

public class EpisodePlayStateDto
{
    public Episode? Episode { get; set; }
    public int PositionSeconds { get; set; }
    public bool IsActive { get; set; }
    public bool IsCompleted { get; set; }
}