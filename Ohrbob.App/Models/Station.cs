using System;
using System.Collections.Generic;

namespace Ohrbob.App.Models;

public class Station
{
    public string? AccountId { get; set; }
    public string? Name { get; set; }
    public List<string>? Feeds { get; set; }
    public StationSchedule? Schedule { get; set; }
    public bool IsPublic { get; set; }
    public bool IsAging { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Id { get; set; }
    
    // UI Helper properties that might be needed by views
    public string? CoverUrl { get; set; }
    public string? ImageUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? DisplayCoverUrl => CoverUrl ?? ImageUrl ?? LogoUrl;
}