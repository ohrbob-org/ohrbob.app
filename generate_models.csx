using System.IO;

var modelsDir = "Ohrbob.App/Models";
Directory.CreateDirectory(modelsDir);

void WriteModel(string name, string content)
{
    File.WriteAllText(Path.Combine(modelsDir, $"{name}.cs"), content);
}

WriteModel("Account", @"using System;
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
}");

WriteModel("ApiKey", @"using System;

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
}");

WriteModel("CreateAccountRequest", @"namespace Ohrbob.App.Models;

public class CreateAccountRequest
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? ProfilePicture { get; set; }
    public IdpUser? IdentityProvider { get; set; }
}");

WriteModel("Episode", @"using System;

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
}");

WriteModel("EpisodePlayStateDto", @"namespace Ohrbob.App.Models;

public class EpisodePlayStateDto
{
    public Episode? Episode { get; set; }
    public int PositionSeconds { get; set; }
    public bool IsActive { get; set; }
    public bool IsCompleted { get; set; }
}");

WriteModel("FeedSortOrder", @"namespace Ohrbob.App.Models;

public enum FeedSortOrder
{
    Default = 0
}");

WriteModel("FeedStatsDto", @"using System;

namespace Ohrbob.App.Models;

public class FeedStatsDto
{
    public string? Url { get; set; }
    public string? Title { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime? LastUpdated { get; set; }
    public long EpisodeCount { get; set; }
}");

WriteModel("IdentityProviderRequest", @"namespace Ohrbob.App.Models;

public class IdentityProviderRequest
{
    public string? Issuer { get; set; }
    public string? UserId { get; set; }
    public string? AccountId { get; set; }
}");

WriteModel("IdpUser", @"namespace Ohrbob.App.Models;

public class IdpUser
{
    public string? Issuer { get; set; }
    public string? UserId { get; set; }
}");

WriteModel("Profile", @"using System;
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
}");

WriteModel("SetTierRequest", @"namespace Ohrbob.App.Models;

public class SetTierRequest
{
    public string? Tier { get; set; }
}");

WriteModel("Station", @"using System;
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
}");

WriteModel("StationFeedSchedule", @"namespace Ohrbob.App.Models;

public class StationFeedSchedule
{
    public string? Url { get; set; }
    public string? Color { get; set; }
    public FeedSortOrder SortOrder { get; set; }
    public int EpisodesToPlay { get; set; }
}");

WriteModel("StationSchedule", @"using System.Collections.Generic;

namespace Ohrbob.App.Models;

public class StationSchedule
{
    public List<StationFeedSchedule>? Feeds { get; set; }
}");

WriteModel("StationWithFeedsDto", @"using System.Collections.Generic;

namespace Ohrbob.App.Models;

public class StationWithFeedsDto
{
    public Station? Station { get; set; }
    public List<FeedStatsDto>? Feeds { get; set; }
    public bool IsUpdating { get; set; }
}");

WriteModel("SubscriptionTier", @"namespace Ohrbob.App.Models;

public enum SubscriptionTier
{
    Default = 0
}");

WriteModel("TierLimits", @"namespace Ohrbob.App.Models;

public class TierLimits
{
    public int MaxStations { get; set; }
    public int MaxFeedsPerStation { get; set; }
    public int MaxProfiles { get; set; }
    public bool StreamEnabled { get; set; }
}");

