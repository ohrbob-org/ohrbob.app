using System;

namespace Ohrbob.App.Models;

public class ApiKey
{
    public string? AccountId { get; set; }
    public string? Name { get; set; }
    public string? Token { get; set; }
    public string? ProfileId { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Id { get; set; }
}