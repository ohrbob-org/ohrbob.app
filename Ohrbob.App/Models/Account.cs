using System;
using System.Collections.Generic;

namespace Ohrbob.App.Models;

public class Account
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? ProfilePicture { get; set; }
    public List<IdpUser>? IdentityProviders { get; set; }
    public string? LastUsedProfileId { get; set; }
    public bool NeedsOnboarding { get; set; }
    public SubscriptionTier Tier { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Id { get; set; }
}