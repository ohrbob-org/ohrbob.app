namespace Ohrbob.App.Models;

public class CreateAccountRequest
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? ProfilePicture { get; set; }
    public IdpUser? IdentityProvider { get; set; }
}