using System;
using System.Collections.Generic;

namespace Ohrbob.App.Models;

public class Profile
{
    public string? AccountId { get; set; }
    public string? Name { get; set; }
    public string? StreamToken { get; set; }
    public List<string>? AllowedStationIds { get; set; }
    public string? LastActiveStationId { get; set; }
    public string? LastActiveEpisodeId { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Id { get; set; }
}