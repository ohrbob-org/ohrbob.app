namespace Ohrbob.App.Models;

public class IdentityProviderRequest
{
    public string? Issuer { get; set; }
    public string? UserId { get; set; }
    public string? AccountId { get; set; }
}