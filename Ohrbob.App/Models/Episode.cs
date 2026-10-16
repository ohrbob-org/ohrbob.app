using System;

namespace Ohrbob.App.Models;

public class Episode
{
    public string? Guid { get; set; }
    public string? FeedUrl { get; set; }
    public string? FeedId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? AudioUrl { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Id { get; set; }
}