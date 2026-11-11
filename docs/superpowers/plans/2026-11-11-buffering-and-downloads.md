# Buffering, Episode Downloads and Upcoming Playlist Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every station its own "buffer ahead" target, download its upcoming episodes for offline playback, and show an Up next panel with per-episode download state.

**Architecture:** The player API gets one read-only playlist endpoint that reuses the existing playback strategy. The Avalonia app adds per-station config, a download manager with a per-station cache, an offline snapshot, and a player view model that prefers local files. The UI adds a settings dialog for the active station and a swipe-open Up next panel.

**Tech Stack:** .NET 10, ASP.NET Core, MongoDB driver, Avalonia 12, CommunityToolkit.Mvvm, SoundFlow, NUnit.

**Spec:** `docs/superpowers/specs/2026-11-11-buffering-and-downloads-design.md`

## Global Constraints

- The backend lives in a separate git repository at `/home/kaiwegner/Projects/ohrbob2`; app changes stay in `/home/kaiwegner/Projects/ohrbob2.app`. Commit in the repository each task belongs to.
- Buffer values are exactly `0, 10, 15, 30, 60, 120` minutes; `0` means Off; any other value reads as `0`.
- The buffer target is per station, never global.
- Downloads are disabled for a station when `HostPaceEnabled` or `IsAging` is true, and when its buffer is `0`.
- Only the active station downloads; switching stations cancels only the outgoing queue and keeps its files.
- Download cache root: `%ApplicationData%/Ohrbob/downloads/<stationId>/`. Offline snapshot: `%ApplicationData%/Ohrbob/offline-state.json`. Never write the stream token into either.
- No secrets in logs. Delete only files the download manager created in its own cache folder.
- App and backend suites must both pass: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj` and `dotnet test Ohrbob.Player.Tests/Ohrbob.Player.Tests.csproj`.
- Copy in the UI is fixed by the spec, including the host-paced and aging explanations.

## Review Focus

- Server unreachable at cold start with offline snapshots present: the app must show the cached station, playlist and player state, not an empty player.
- Network loss at episode end: playback must advance to the next downloaded file without a failing `/stream/next` loop or a stop.
- Audio duration cannot be read: the download loop must still terminate within the playlist limit and never re-download in a loop.
- Host-paced or aging station with old cache files: those files must never play and must be pruned, even if they exist from before the flag was set.
- Per-station isolation: changing station A's target must not prune or change station B's downloads.
- Older self-hosted server without `/stream/playlist`: the panel must say unavailable and the app must otherwise work.

---

### Task 1: Backend playlist sequence in the playback strategy

**Files:**
- Modify: `/home/kaiwegner/Projects/ohrbob2/Ohrbob.Player/Services/PlaybackStrategyService.cs`
- Test: `/home/kaiwegner/Projects/ohrbob2/Ohrbob.Player.Tests/StreamControllerTests.cs`

**Interfaces:**
- Consumes: existing `PlaybackStrategyHelper.GetOrderedPlaylist`, `HostPaceHelper`, `ListenStateService`.
- Produces: `Task<List<Episode>> GetUpcomingEpisodesAsync(Station station, string? currentEpisodeId, Profile listener, int limit)` returning episodes **after** `currentEpisodeId`, matching what repeated `POST /stream/next` would produce.

- [ ] **Step 1: Write the failing service tests**

Add to `StreamControllerTests` (use existing `CreateOldestFirstStation` and `SeedEpisodesAsync`; add `using Microsoft.Extensions.DependencyInjection;`):

```csharp
[Test]
public async Task GetUpcomingEpisodes_ReturnsFollowingEpisodesInOrder()
{
    var stationId = ObjectId.GenerateNewId().ToString();
    var ep1 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-3) };
    var ep2 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) };
    var ep3 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 3", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) };
    var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", AllowedStationIds = new[] { stationId } };
    var station = CreateOldestFirstStation(stationId);
    await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
    await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
    await SeedEpisodesAsync(ep1, ep2, ep3);

    var strategy = _app.Services.GetRequiredService<PlaybackStrategyService>();
    var upcoming = await strategy.GetUpcomingEpisodesAsync(station, ep1.Id, profile, 10);

    Assert.That(upcoming.Select(e => e.Id), Is.EqualTo(new[] { ep2.Id, ep3.Id }));
}

[Test]
public async Task GetUpcomingEpisodes_SkipsListenedAndWrapsToFirstUnplayed()
{
    var stationId = ObjectId.GenerateNewId().ToString();
    var ep1 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-3) };
    var ep2 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) };
    var ep3 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 3", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) };
    var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", AllowedStationIds = new[] { stationId } };
    var station = CreateOldestFirstStation(stationId);
    await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
    await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
    await SeedEpisodesAsync(ep1, ep2, ep3);
    await _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection").InsertOneAsync(
        new ListenState { UserId = profile.Id, StationId = stationId, EpisodeId = ep2.Id, IsListened = true });

    var strategy = _app.Services.GetRequiredService<PlaybackStrategyService>();
    var upcoming = await strategy.GetUpcomingEpisodesAsync(station, ep1.Id, profile, 10);

    Assert.That(upcoming.Select(e => e.Id), Is.EqualTo(new[] { ep3.Id }),
        "listened ep2 is skipped and the first unplayed episode after the end is ep3");
}

[Test]
public async Task GetUpcomingEpisodes_EndlessSpinWrapsPastListenedEpisodes()
{
    var stationId = ObjectId.GenerateNewId().ToString();
    var ep1 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) };
    var ep2 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) };
    var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", AllowedStationIds = new[] { stationId } };
    var station = CreateOldestFirstStation(stationId);
    station.EndlessSpin = true;
    await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
    await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
    await SeedEpisodesAsync(ep1, ep2);
    await _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection").InsertOneAsync(
        new ListenState { UserId = profile.Id, StationId = stationId, EpisodeId = ep2.Id, IsListened = true });

    var strategy = _app.Services.GetRequiredService<PlaybackStrategyService>();
    var upcoming = await strategy.GetUpcomingEpisodesAsync(station, ep1.Id, profile, 3);

    Assert.That(upcoming.Select(e => e.Id), Is.EqualTo(new[] { ep2.Id, ep1.Id, ep2.Id }));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ohrbob.Player.Tests/Ohrbob.Player.Tests.csproj --filter "GetUpcomingEpisodes"`
Expected: FAIL, `GetUpcomingEpisodesAsync` does not exist.

- [ ] **Step 3: Implement `GetUpcomingEpisodesAsync` and route `GetNextEpisodeAsync` through it**

In `PlaybackStrategyService`:

```csharp
public async Task<List<Episode>> GetUpcomingEpisodesAsync(
    Station station, string? currentEpisodeId, Profile listener, int limit)
{
    var result = new List<Episode>();
    if (limit <= 0 || station.Feeds == null || station.Feeds.Length == 0) return result;

    var episodes = await _episodes.Find(Builders<Episode>.Filter.In(e => e.FeedUrl, station.Feeds)).ToListAsync();
    var hostCompleted = await _listenStateService.GetHostCompletedEpisodeIdsAsync(station, episodes);
    var allowedEpisodes = HostPaceHelper.GetAllowedEpisodes(station, listener.Id, episodes, hostCompleted);
    var listenedEpisodeIds = await _listenStateService.GetListenedEpisodeIdsAsync(listener.Id);
    var listenedSet = HostPaceHelper.GetUnavailableEpisodeIds(
        station, listener.Id, episodes, listenedEpisodeIds.ToHashSet(), hostCompleted);
    if (!string.IsNullOrEmpty(currentEpisodeId)) listenedSet.Add(currentEpisodeId);

    var ordered = PlaybackStrategyHelper.GetOrderedPlaylist(
        allowedEpisodes, station, new HashSet<string>(), currentEpisodeId);
    if (ordered.Count == 0) return result;

    var currentIndex = string.IsNullOrEmpty(currentEpisodeId)
        ? -1
        : ordered.FindIndex(e => e.Id == currentEpisodeId);
    var startIndex = currentIndex >= 0 ? currentIndex + 1 : 0;

    var iterations = station.EndlessSpin ? limit : ordered.Count;
    for (var i = 0; i < iterations && result.Count < limit; i++)
    {
        var episode = ordered[(startIndex + i) % ordered.Count];
        if (!station.EndlessSpin && listenedSet.Contains(episode.Id)) continue;
        if (!station.EndlessSpin && result.Any(e => e.Id == episode.Id)) break;
        result.Add(episode);
    }

    return result;
}
```

Replace the body of `GetNextEpisodeAsync` with:

```csharp
var upcoming = await GetUpcomingEpisodesAsync(station, currentEpisodeId, listener, 1);
return upcoming.FirstOrDefault();
```

The non-endless path deliberately wraps once and then stops because every remaining episode is listened.

- [ ] **Step 4: Run the strategy and existing next/prev tests**

Run: `dotnet test Ohrbob.Player.Tests/Ohrbob.Player.Tests.csproj --filter "GetUpcomingEpisodes|Next_|Prev_"`
Expected: PASS, including the existing `/next` and `/prev` tests.

- [ ] **Step 5: Commit in the backend repository**

```bash
cd /home/kaiwegner/Projects/ohrbob2
git add Ohrbob.Player/Services/PlaybackStrategyService.cs Ohrbob.Player.Tests/StreamControllerTests.cs
git commit -m "feat(player): expose ordered upcoming episodes"
```

---

### Task 2: Backend `GET /stream/playlist`

**Files:**
- Modify: `/home/kaiwegner/Projects/ohrbob2/Ohrbob.Player/Controllers/StreamController.cs`
- Test: `/home/kaiwegner/Projects/ohrbob2/Ohrbob.Player.Tests/StreamControllerTests.cs`

**Interfaces:**
- Consumes: `GetUpcomingEpisodesAsync` from Task 1.
- Produces: `GET /stream/playlist?limit=50`, response `{ stationId, hostPaceEnabled, isAging, episodes: [{ episodeId, title, description, artist, coverUrl, streamUri, publishedAt, isCurrent }] }`.

- [ ] **Step 1: Write the failing endpoint tests**

Add response types inside `StreamControllerTests`:

```csharp
public class PlaylistResponse
{
    public string? stationId { get; set; }
    public bool hostPaceEnabled { get; set; }
    public bool isAging { get; set; }
    public List<PlaylistTrackInfo>? episodes { get; set; }
}

public class PlaylistTrackInfo
{
    public string? episodeId { get; set; }
    public string? title { get; set; }
    public string? description { get; set; }
    public string? artist { get; set; }
    public string? streamUri { get; set; }
    public DateTime? publishedAt { get; set; }
    public bool isCurrent { get; set; }
}
```

Tests:

```csharp
[Test]
public async Task GetPlaylist_ReturnsCurrentThenUpcomingInOrder()
{
    var stationId = ObjectId.GenerateNewId().ToString();
    var ep1 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-3), AudioUrl = "https://audio/1", PlainDescription = "One" };
    var ep2 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2), AudioUrl = "https://audio/2" };
    var ep3 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 3", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1), AudioUrl = "https://audio/3" };
    var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", LastActiveStationId = stationId, LastActiveEpisodeId = ep1.Id };
    var station = CreateOldestFirstStation(stationId);
    await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
    await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
    await SeedEpisodesAsync(ep1, ep2, ep3);
    await _app.MongoDbContext.GetCollection<Feed>("FeedCollection").InsertOneAsync(new Feed { Url = "https://feed.url", Title = "Feed Title" });

    _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
    var response = await _client.GetAsync("/stream/playlist?limit=3");
    response.EnsureSuccessStatusCode();

    var playlist = await response.Content.ReadFromJsonAsync<PlaylistResponse>();
    Assert.That(playlist!.stationId, Is.EqualTo(stationId));
    Assert.That(playlist.isAging, Is.False);
    Assert.That(playlist.episodes!.Select(e => e.episodeId), Is.EqualTo(new[] { ep1.Id, ep2.Id, ep3.Id }));
    Assert.That(playlist.episodes[0].isCurrent, Is.True);
    Assert.That(playlist.episodes[0].description, Is.EqualTo("One"));
    Assert.That(playlist.episodes[0].artist, Is.EqualTo("Feed Title"));
    Assert.That(playlist.episodes[0].streamUri, Is.EqualTo("https://audio/1"));
    Assert.That(playlist.episodes.Skip(1).All(e => !e.isCurrent), Is.True);
}

[Test]
public async Task GetPlaylist_ClampsLimit()
{
    var stationId = ObjectId.GenerateNewId().ToString();
    var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", AllowedStationIds = new[] { stationId } };
    var station = CreateOldestFirstStation(stationId);
    await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
    await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
    var episodes = Enumerable.Range(1, 3).Select(i => new Episode
    {
        Id = ObjectId.GenerateNewId().ToString(), Title = $"Ep {i}", FeedUrl = "https://feed.url",
        PublishedAt = DateTime.UtcNow.AddMinutes(i), AudioUrl = $"https://audio/{i}"
    }).ToArray();
    await SeedEpisodesAsync(episodes);

    _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
    var playlist = await (await _client.GetAsync("/stream/playlist?limit=1")).Content.ReadFromJsonAsync<PlaylistResponse>();

    Assert.That(playlist!.episodes!.Count, Is.EqualTo(1));
}

[Test]
public async Task GetPlaylist_HostPacedStation_HidesUnreleasedEpisodes()
{
    var stationId = ObjectId.GenerateNewId().ToString();
    var ep1 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Released", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) };
    var ep2 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Locked", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) };
    var profile = new Profile { AccountId = "subscriber", StreamToken = "token-123", AllowedStationIds = new[] { stationId } };
    var station = CreateOldestFirstStation(stationId);
    station.AccountId = "owner-account";
    station.HostPaceEnabled = true;
    station.HostProfileId = "host-profile";
    await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
    await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
    await SeedEpisodesAsync(ep1, ep2);
    await _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection").InsertOneAsync(new ListenState
    {
        UserId = "host-profile", EpisodeId = ep1.Id, StationId = stationId, IsListened = true
    });

    _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
    var playlist = await (await _client.GetAsync("/stream/playlist")).Content.ReadFromJsonAsync<PlaylistResponse>();

    Assert.That(playlist!.hostPaceEnabled, Is.True);
    Assert.That(playlist.episodes!.Select(e => e.episodeId), Is.EqualTo(new[] { ep1.Id }));
}

[Test]
public async Task GetPlaylist_WithoutActiveStation_ReturnsEmptyList()
{
    var profile = new Profile { AccountId = "test-account", StreamToken = "token-123" };
    await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);

    _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
    var response = await _client.GetAsync("/stream/playlist");
    response.EnsureSuccessStatusCode();
    var playlist = await response.Content.ReadFromJsonAsync<PlaylistResponse>();

    Assert.That(playlist!.episodes, Is.Empty);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ohrbob.Player.Tests/Ohrbob.Player.Tests.csproj --filter "GetPlaylist_"`
Expected: FAIL with 404.

- [ ] **Step 3: Extract current-station/current-episode helpers and add the endpoint**

In `StreamController`, add private helpers used by both status and playlist:

```csharp
private async Task<Station?> GetActiveStationAsync(Profile profile)
{
    var activeStationId = profile.LastActiveStationId;
    if (string.IsNullOrEmpty(activeStationId) && profile.AllowedStationIds?.Length > 0)
        activeStationId = profile.AllowedStationIds[0];
    if (string.IsNullOrEmpty(activeStationId)) return null;
    return await _stations.Find(s => s.Id == activeStationId && s.DeletedAt == null).FirstOrDefaultAsync();
}

private async Task<Episode?> ResolveCurrentEpisodeAsync(Station station, Profile profile)
{
    Episode? currentEpisode = null;
    if (!string.IsNullOrEmpty(profile.LastActiveEpisodeId))
    {
        currentEpisode = await _episodes.Find(e => e.Id == profile.LastActiveEpisodeId).FirstOrDefaultAsync();
        if (currentEpisode != null && !station.Feeds.Contains(currentEpisode.FeedUrl))
            currentEpisode = null;
    }
    if (currentEpisode != null &&
        !await _playbackStrategy.IsEpisodePlayableAsync(station, currentEpisode, profile))
        currentEpisode = null;
    return currentEpisode ?? await _playbackStrategy.GetNextEpisodeAsync(station, null, profile);
}
```

Refactor `GetStatus` to call them, keeping its responses byte-for-byte equivalent. Then add:

```csharp
[HttpGet("playlist")]
public async Task<IActionResult> GetPlaylist([FromQuery] int limit = 50)
{
    var profile = await GetProfile();
    if (profile == null) return Unauthorized();

    var station = await GetActiveStationAsync(profile);
    if (station == null)
        return Ok(new { stationId = (string?)null, hostPaceEnabled = false, isAging = false, episodes = Array.Empty<object>() });

    var current = await ResolveCurrentEpisodeAsync(station, profile);
    var take = Math.Clamp(limit, 1, 100);
    var upcoming = current == null
        ? new List<Episode>()
        : await _playbackStrategy.GetUpcomingEpisodesAsync(station, current.Id, profile, Math.Max(0, take - 1));

    var feeds = await _feeds.Find(f => station.Feeds.Contains(f.Url)).ToListAsync();
    string Artist(Episode ep) =>
        feeds.FirstOrDefault(f => f.Url == ep.FeedUrl || f.Id == ep.FeedId)?.Title ?? station.Name;

    var episodes = new List<object>();
    if (current != null)
        episodes.Add(Track(current, true, Artist(current)));
    episodes.AddRange(upcoming.Select(e => Track(e, false, Artist(e))));

    return Ok(new { stationId = station.Id, hostPaceEnabled = station.HostPaceEnabled, isAging = station.IsAging, episodes });

    object Track(Episode ep, bool isCurrent, string artist) => new
    {
        episodeId = ep.Id,
        title = ep.Title,
        description = ep.PlainDescription,
        artist,
        coverUrl = string.IsNullOrWhiteSpace(ep.ImageUrl) ? null : ep.ImageUrl,
        streamUri = ep.AudioUrl,
        publishedAt = ep.PublishedAt,
        isCurrent
    };
}
```

`limit` counts the current episode plus upcoming.

- [ ] **Step 4: Run the full backend suite**

Run: `dotnet test Ohrbob.Player.Tests/Ohrbob.Player.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit in the backend repository**

```bash
cd /home/kaiwegner/Projects/ohrbob2
git add Ohrbob.Player/Controllers/StreamController.cs Ohrbob.Player.Tests/StreamControllerTests.cs
git commit -m "feat(player): add upcoming playlist endpoint"
```

---

### Task 3: App per-station buffer config

**Files:**
- Modify: `Ohrbob.App/Services/ConfigService.cs`
- Test: `Ohrbob.App.Tests/Services/ConfigServiceTests.cs`

**Interfaces:**
- Produces: `int ConfigService.GetBufferMinutes(string stationId)` and `void ConfigService.SetBufferMinutes(string stationId, int minutes)`.

- [ ] **Step 1: Write the failing config tests**

```csharp
[Test]
public void BufferMinutes_AreIndependentPerStation_AndRoundTrip()
{
    var service = new ConfigService(_tempFilePath);
    service.SetBufferMinutes("station-a", 30);
    service.SetBufferMinutes("station-b", 10);

    var reloaded = new ConfigService(_tempFilePath);

    Assert.That(reloaded.GetBufferMinutes("station-a"), Is.EqualTo(30));
    Assert.That(reloaded.GetBufferMinutes("station-b"), Is.EqualTo(10));
    Assert.That(reloaded.GetBufferMinutes("station-c"), Is.EqualTo(0));
}

[Test]
public void SetBufferMinutes_ZeroRemovesStation_AndInvalidValueReadsAsZero()
{
    var service = new ConfigService(_tempFilePath);
    service.SetBufferMinutes("station-a", 60);
    service.SetBufferMinutes("station-a", 0);
    service.SetBufferMinutes("station-b", 17);

    var reloaded = new ConfigService(_tempFilePath);

    Assert.That(reloaded.GetBufferMinutes("station-a"), Is.EqualTo(0));
    Assert.That(reloaded.GetBufferMinutes("station-b"), Is.EqualTo(0));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "BufferMinutes"`
Expected: FAIL, methods do not exist.

- [ ] **Step 3: Implement per-station storage**

In `ConfigService` add a private `Dictionary<string, int> _bufferMinutesByStation = new();`, load/save it through `ConfigData.BufferMinutesByStation`, and:

```csharp
private static readonly int[] AllowedBufferMinutes = { 0, 10, 15, 30, 60, 120 };

public int GetBufferMinutes(string stationId) =>
    _bufferMinutesByStation.TryGetValue(stationId, out var minutes) &&
    AllowedBufferMinutes.Contains(minutes)
        ? minutes
        : 0;

public void SetBufferMinutes(string stationId, int minutes)
{
    if (string.IsNullOrEmpty(stationId)) return;
    var normalized = AllowedBufferMinutes.Contains(minutes) ? minutes : 0;
    if (normalized == 0) _bufferMinutesByStation.Remove(stationId);
    else _bufferMinutesByStation[stationId] = normalized;
    Persist();
}
```

Extract the existing write logic from `Save` into `private void Persist()` and make both `Save` and `SetBufferMinutes` call it. Keep the existing fallback path behavior.

- [ ] **Step 4: Run the config tests**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "ConfigServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit in the app repository**

```bash
git add Ohrbob.App/Services/ConfigService.cs Ohrbob.App.Tests/Services/ConfigServiceTests.cs
git commit -m "feat: store buffer target per station"
```

---

### Task 4: App playlist models and client call

**Files:**
- Create: `Ohrbob.App/Models/PlaylistDto.cs`
- Modify: `Ohrbob.App/Models/Station.cs`
- Modify: `Ohrbob.App/Services/OhrbobClient.cs`
- Test: `Ohrbob.App.Tests/Services/OhrbobClientTests.cs`

**Interfaces:**
- Produces: `Task<PlaylistDto?> OhrbobClient.GetPlaylistAsync(int limit = 50)`; `null` means the server does not support the endpoint.
- Produces: `Station.HostPaceEnabled`, `Station.IsAvailableOffline`.

- [ ] **Step 1: Write the failing client tests**

```csharp
[Test]
public async Task GetPlaylistAsync_ParsesPlaylistAndSendsLimit()
{
    var json = """
    {
      "stationId": "s1",
      "hostPaceEnabled": true,
      "isAging": false,
      "episodes": [
        { "episodeId": "e1", "title": "Ep 1", "description": "Desc", "artist": "Feed",
          "coverUrl": "https://img/1", "streamUri": "https://audio/1",
          "publishedAt": "2026-11-01T10:00:00Z", "isCurrent": true }
      ]
    }
    """;
    var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    });
    var client = new OhrbobClient(_mockConfig, handler);

    var playlist = await client.GetPlaylistAsync(2);

    Assert.That(handler.LastRequest!.RequestUri!.ToString(), Is.EqualTo($"{ServerUrl}/stream/playlist?limit=2"));
    Assert.That(handler.LastRequest.Headers.Contains("X-Stream-Token"), Is.True);
    Assert.That(playlist!.StationId, Is.EqualTo("s1"));
    Assert.That(playlist.HostPaceEnabled, Is.True);
    Assert.That(playlist.Episodes.Single().EpisodeId, Is.EqualTo("e1"));
    Assert.That(playlist.Episodes.Single().IsCurrent, Is.True);
}

[Test]
public async Task GetPlaylistAsync_NotFound_ReturnsNull()
{
    var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
    var client = new OhrbobClient(_mockConfig, handler);

    Assert.That(await client.GetPlaylistAsync(), Is.Null);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "GetPlaylistAsync"`
Expected: FAIL, `GetPlaylistAsync` does not exist.

- [ ] **Step 3: Add the models and client method**

`PlaylistDto.cs`:

```csharp
public class PlaylistDto
{
    public string? StationId { get; set; }
    public bool HostPaceEnabled { get; set; }
    public bool IsAging { get; set; }
    public List<PlaylistEpisodeDto> Episodes { get; set; } = new();
}

public class PlaylistEpisodeDto
{
    public string? EpisodeId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Artist { get; set; }
    public string? CoverUrl { get; set; }
    public string? StreamUri { get; set; }
    public DateTime? PublishedAt { get; set; }
    public bool IsCurrent { get; set; }
}
```

Add to `Station`: `public bool HostPaceEnabled { get; set; }` and `public bool IsAvailableOffline { get; set; }`.

In `OhrbobClient`, follow the existing request/header pattern:

```csharp
public async Task<PlaylistDto?> GetPlaylistAsync(int limit = 50)
{
    var serverUrl = _configService.ServerUrl?.TrimEnd('/');
    if (string.IsNullOrEmpty(serverUrl))
        throw new InvalidOperationException("Server URL is not configured.");

    var url = $"{serverUrl}/stream/playlist?limit={limit}";
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    ApplyHeaders(request);

    using var response = await _httpClient.SendAsync(request);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
    response.EnsureSuccessStatusCode();

    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    return await response.Content.ReadFromJsonAsync<PlaylistDto>(options);
}
```

- [ ] **Step 4: Run the client tests**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "OhrbobClientTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.App/Models/PlaylistDto.cs Ohrbob.App/Models/Station.cs Ohrbob.App/Services/OhrbobClient.cs Ohrbob.App.Tests/Services/OhrbobClientTests.cs
git commit -m "feat: fetch station playlist from the player API"
```

---

### Task 5: Episode download service

**Files:**
- Create: `Ohrbob.App/Models/PlaylistItem.cs`
- Create: `Ohrbob.App/Services/EpisodeDownloadService.cs`
- Test: `Ohrbob.App.Tests/Services/EpisodeDownloadServiceTests.cs`

**Interfaces:**
- Produces: `enum DownloadState { NotDownloaded, Queued, Downloading, Downloaded, Failed }`.
- Produces: `PlaylistItem` with `EpisodeId`, `Title`, `Description`, `Artist`, `CoverUrl`, `StreamUri`, `PublishedAt`, `IsCurrent`, `DownloadState`, `StateLabel`.
- Produces: `EpisodeDownloadService(string? cacheRoot = null, HttpClient? httpClient = null, Func<string, TimeSpan>? durationReader = null)`.
- Produces: `Task SyncAsync(string stationId, IReadOnlyList<PlaylistItem> playlist, int bufferMinutes, bool downloadsAllowed, int currentPositionSeconds, CancellationToken cancellationToken, string? protectedEpisodeId = null)`.
- Produces: `Task PruneAsync(string stationId, IReadOnlyList<PlaylistItem> playlist, int bufferMinutes, string? protectedEpisodeId = null)`.
- Produces: `string? GetDownloadedPath(string stationId, string episodeId)`.
- Produces: `void Cancel()` and `event EventHandler? DownloadStateChanged`.

- [ ] **Step 1: Write the failing download-service tests**

Create `EpisodeDownloadServiceTests` with a temp cache root and a stub handler. Use an injected `durationReader` returning one minute so byte content does not matter.

```csharp
private sealed class StubHandler : HttpMessageHandler
{
    public List<string> Requests { get; } = new();
    public Func<string, HttpResponseMessage>? Override { get; set; }
    public bool FailFirst { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);
        if (FailFirst && Requests.Count == 1)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        return Task.FromResult(Override?.Invoke(url) ?? new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
        });
    }
}

private static PlaylistItem Item(string id, bool current = false) => new()
{
    EpisodeId = id,
    Title = id,
    StreamUri = $"https://audio/{id}.mp3",
    IsCurrent = current
};
```

Tests:

```csharp
[Test]
public async Task Sync_DownloadsInOrderUntilTarget()
{
    var handler = new StubHandler();
    var service = new EpisodeDownloadService(_root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
    var items = new[] { Item("e1", true), Item("e2"), Item("e3") };

    await service.SyncAsync("s1", items, 2, true, 0, CancellationToken.None);

    Assert.That(items[0].DownloadState, Is.EqualTo(DownloadState.Downloaded));
    Assert.That(items[1].DownloadState, Is.EqualTo(DownloadState.Downloaded));
    Assert.That(items[2].DownloadState, Is.EqualTo(DownloadState.NotDownloaded));
    Assert.That(handler.Requests.Count, Is.EqualTo(2));
}

[Test]
public async Task Sync_ReusesExistingFilesWithoutDownloadingAgain()
{
    var handler = new StubHandler();
    var service = new EpisodeDownloadService(_root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
    var first = new[] { Item("e1", true), Item("e2") };
    await service.SyncAsync("s1", first, 2, true, 0, CancellationToken.None);
    handler.Requests.Clear();

    var second = new[] { Item("e1", true), Item("e2") };
    await service.SyncAsync("s1", second, 2, true, 0, CancellationToken.None);

    Assert.That(handler.Requests, Is.Empty);
    Assert.That(second.All(i => i.DownloadState == DownloadState.Downloaded), Is.True);
}

[Test]
public async Task Sync_CountsOnlyRemainingTimeOfCurrentEpisode()
{
    var handler = new StubHandler();
    var service = new EpisodeDownloadService(_root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
    var items = new[] { Item("e1", true), Item("e2") };

    // Current episode has 30 seconds left, target is 1 minute -> e2 is fetched.
    await service.SyncAsync("s1", items, 1, true, 30, CancellationToken.None);

    Assert.That(handler.Requests.Count, Is.EqualTo(2));
    Assert.That(items[1].DownloadState, Is.EqualTo(DownloadState.Downloaded));
}

[Test]
public async Task Sync_FailedDownloadMarksFailedAndContinues()
{
    var handler = new StubHandler { FailFirst = true };
    var service = new EpisodeDownloadService(_root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
    var items = new[] { Item("e1", true), Item("e2") };

    await service.SyncAsync("s1", items, 2, true, 0, CancellationToken.None);

    Assert.That(items[0].DownloadState, Is.EqualTo(DownloadState.Failed));
    Assert.That(items[1].DownloadState, Is.EqualTo(DownloadState.Downloaded));
    Assert.That(Directory.Exists(Path.Combine(_root, "s1")), Is.True);
}

[Test]
public async Task Sync_WhenDownloadsNotAllowed_DoesNotRequest()
{
    var handler = new StubHandler();
    var service = new EpisodeDownloadService(_root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
    var items = new[] { Item("e1", true) };

    await service.SyncAsync("s1", items, 30, false, 0, CancellationToken.None);

    Assert.That(handler.Requests, Is.Empty);
    Assert.That(items[0].DownloadState, Is.EqualTo(DownloadState.NotDownloaded));
}

[Test]
public async Task Sync_UnknownDuration_StillTerminatesAtPlaylistEnd()
{
    var handler = new StubHandler();
    var service = new EpisodeDownloadService(_root, new HttpClient(handler), _ => TimeSpan.Zero);
    var items = new[] { Item("e1", true), Item("e2"), Item("e3") };

    await service.SyncAsync("s1", items, 30, true, 0, CancellationToken.None);

    Assert.That(handler.Requests.Count, Is.EqualTo(3));
    Assert.That(items.All(i => i.DownloadState == DownloadState.Downloaded), Is.True);
}

[Test]
public async Task Prune_RemovesOnlyTheSelectedStationBeyondTarget()
{
    var handler = new StubHandler();
    var service = new EpisodeDownloadService(_root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
    await service.SyncAsync("s1", new[] { Item("e1", true), Item("e2"), Item("e3") }, 3, true, 0, CancellationToken.None);
    await service.SyncAsync("s2", new[] { Item("f1", true) }, 1, true, 0, CancellationToken.None);

    await service.PruneAsync("s1", new[] { Item("e1", true) }, 1, "e1");

    Assert.That(service.GetDownloadedPath("s1", "e1"), Is.Not.Null);
    Assert.That(service.GetDownloadedPath("s1", "e2"), Is.Null);
    Assert.That(service.GetDownloadedPath("s1", "e3"), Is.Null);
    Assert.That(service.GetDownloadedPath("s2", "f1"), Is.Not.Null, "other station cache must survive");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "EpisodeDownloadService"`
Expected: FAIL, types do not exist.

- [ ] **Step 3: Implement `PlaylistItem` and `EpisodeDownloadService`**

`PlaylistItem`:

```csharp
public partial class PlaylistItem : ObservableObject
{
    public string EpisodeId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Artist { get; init; } = "";
    public string? CoverUrl { get; init; }
    public string? StreamUri { get; init; }
    public DateTime? PublishedAt { get; init; }
    public bool IsCurrent { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private DownloadState _downloadState;

    public string StateLabel => DownloadState switch
    {
        DownloadState.Downloaded => "Downloaded",
        DownloadState.Downloading => "Downloading…",
        DownloadState.Queued => "Queued",
        DownloadState.Failed => "Download failed",
        _ => "Not downloaded"
    };
}
```

`EpisodeDownloadService` responsibilities:

- Cache root default: `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ohrbob", "downloads")`.
- `GetDownloadedPath` checks the station folder for `<episodeId>.*` excluding `.part`, returns the first existing file or null.
- `SyncAsync`: if `!downloadsAllowed || bufferMinutes <= 0`, call `PruneAsync(stationId, playlist, 0, protectedEpisodeId)` and set every state according to `GetDownloadedPath` (which after pruning should be null except the protected current file); return. Otherwise walk the playlist:
  - `elapsed = item.IsCurrent ? currentPositionSeconds : 0`;
  - if file exists: state `Downloaded`; add `duration` (for current subtract `elapsed`); continue.
  - state `Queued`, then `Downloading`; download to `<path>.part` with one retry for `HttpRequestException`; on success move to final path and probe duration; on failure delete `.part`, state `Failed`; raise `DownloadStateChanged` after each change.
  - budget starts at 0; after each episode add `duration - elapsed` for current, or `duration` otherwise; stop when `budget >= bufferMinutes * 60`.
- Duration reader default: `SoundFlow.Metadata.SoundMetadataReader.Read(path, new ReadOptions { ReadTags = false, ReadAlbumArt = false, DurationAccuracy = DurationAccuracy.FastEstimate })`; return `TimeSpan.Zero` on failure.
- Extension from `Uri.AbsolutePath`, whitelist `.mp3 .m4a .aac .ogg .opus .flac .wav`, else `.audio`.
- `PruneAsync`: compute the keep set by walking the playlist with known durations until `bufferMinutes * 60` is reached; always keep `protectedEpisodeId`; delete every other file in the station folder.
- `Cancel` cancels the current `CancellationTokenSource`; `SyncAsync` links both tokens.
- Do not use `ConfigureAwait(false)` in `SyncAsync`: the view model calls it from the UI context, and the bound `PlaylistItem` state must change on the UI thread.

- [ ] **Step 4: Run the download tests**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "EpisodeDownloadService"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.App/Models/PlaylistItem.cs Ohrbob.App/Services/EpisodeDownloadService.cs Ohrbob.App.Tests/Services/EpisodeDownloadServiceTests.cs
git commit -m "feat: download episodes per station"
```

---

### Task 6: Offline snapshot service

**Files:**
- Create: `Ohrbob.App/Models/OfflineSnapshot.cs`
- Create: `Ohrbob.App/Services/OfflineStateService.cs`
- Test: `Ohrbob.App.Tests/Services/OfflineStateServiceTests.cs`

**Interfaces:**
- Produces: `OfflineSnapshot? OfflineStateService.Load()` and `void OfflineStateService.Save(OfflineSnapshot snapshot)`.
- Produces: `OfflineSnapshot` with `Version`, `SavedAt`, `ActiveStationId`, `ProfileName`, `Stations` (`List<StationWithFeedsDto>`), `StationStates` (`Dictionary<string, StationOfflineState>`); `StationOfflineState` with `StationId`, `HostPaceEnabled`, `IsAging`, `Playlist` (`List<PlaylistEpisodeDto>`), `CurrentEpisodeId`, `PositionSeconds`.

- [ ] **Step 1: Write the failing snapshot tests**

```csharp
[Test]
public void SaveAndLoad_RoundTripsMultipleStations()
{
    var path = Path.Combine(_root, "offline.json");
    var service = new OfflineStateService(path);
    service.Save(new OfflineSnapshot
    {
        ActiveStationId = "s2",
        ProfileName = "Kai",
        Stations = new List<StationWithFeedsDto>
        {
            new() { Station = new Station { Id = "s1", Name = "One" } },
            new() { Station = new Station { Id = "s2", Name = "Two" } }
        },
        StationStates = new Dictionary<string, StationOfflineState>
        {
            ["s1"] = new() { StationId = "s1", PositionSeconds = 10, CurrentEpisodeId = "e1" },
            ["s2"] = new() { StationId = "s2", IsAging = true, PositionSeconds = 20, CurrentEpisodeId = "e2" }
        }
    });

    var loaded = new OfflineStateService(path).Load();

    Assert.That(loaded!.ActiveStationId, Is.EqualTo("s2"));
    Assert.That(loaded.ProfileName, Is.EqualTo("Kai"));
    Assert.That(loaded.Stations.Select(s => s.Station!.Id), Is.EqualTo(new[] { "s1", "s2" }));
    Assert.That(loaded.StationStates["s1"].PositionSeconds, Is.EqualTo(10));
    Assert.That(loaded.StationStates["s2"].IsAging, Is.True);
}

[Test]
public void Load_MissingOrCorruptFile_ReturnsNull()
{
    var path = Path.Combine(_root, "offline.json");
    Assert.That(new OfflineStateService(path).Load(), Is.Null);

    File.WriteAllText(path, "{ not json");
    Assert.That(new OfflineStateService(path).Load(), Is.Null);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "OfflineStateService"`
Expected: FAIL, types do not exist.

- [ ] **Step 3: Implement models and service**

Use `System.Text.Json` with `WriteIndented = true`. Save to `<path>.tmp` and `File.Move(..., overwrite: true)`; create the directory. `Load` catches all JSON/IO errors and returns null. Default path is `%ApplicationData%/Ohrbob/offline-state.json`. Do not include the stream token anywhere.

- [ ] **Step 4: Run the snapshot tests**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "OfflineStateService"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.App/Models/OfflineSnapshot.cs Ohrbob.App/Services/OfflineStateService.cs Ohrbob.App.Tests/Services/OfflineStateServiceTests.cs
git commit -m "feat: persist offline station snapshots"
```

---

### Task 7: Main view model playlist and per-station settings

**Files:**
- Modify: `Ohrbob.App/ViewModels/MainViewModel.cs`
- Modify: `Ohrbob.App.Tests/ViewModels/TestAudioPlayer.cs` only if a helper is needed (unlikely)
- Test: `Ohrbob.App.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `ConfigService.GetBufferMinutes/SetBufferMinutes`, `OhrbobClient.GetPlaylistAsync`.
- Produces: `ObservableCollection<PlaylistItem> PlaylistItems`; `IReadOnlyList<BufferOption> BufferOptions`; `BufferOption SelectedBufferOption` with `Task ApplySelectedBufferAsync()`; `bool IsSettingsOpen`; `bool IsUpNextOpen`; commands `OpenSettingsCommand`, `CloseSettingsCommand`, `ToggleUpNextCommand`, `CloseUpNextCommand`; `string? DownloadBlockedReason`, `string? DownloadStatusMessage`, `bool IsDownloadEnabled`, `bool IsBufferSelectionEnabled`, `bool IsPlaylistUnavailable`, `bool IsPlaylistLoading`, `bool IsPlaylistEmpty`.
- Produces: `public sealed record BufferOption(int Minutes, string Label);`

- [ ] **Step 1: Write the failing view-model tests**

Add these to `MainViewModelTests` with a handler that returns a station and playlist:

```csharp
private class PlaylistHttpHandler : HttpMessageHandler
{
    public string StationJson { get; set; } =
        "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\"},\"feeds\":[]}]";
    public string PlaylistJson { get; set; } =
        "{\"stationId\":\"s1\",\"hostPaceEnabled\":false,\"isAging\":false,\"episodes\":[" +
        "{\"episodeId\":\"e1\",\"title\":\"Current\",\"streamUri\":\"https://audio/1\",\"isCurrent\":true}," +
        "{\"episodeId\":\"e2\",\"title\":\"Next\",\"streamUri\":\"https://audio/2\",\"isCurrent\":false}]}";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var body = path.EndsWith("/stream/stations") ? StationJson
            : path.EndsWith("/stream/playlist") ? PlaylistJson
            : path.EndsWith("/stream/status")
                ? "{\"stationId\":\"s1\",\"current\":{\"episodeId\":\"e1\",\"title\":\"Current\",\"streamUri\":\"https://audio/1\"}}"
                : "{}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
    }
}

[Test]
public async Task Initialize_PopulatesPlaylistFromServer()
{
    var config = new ConfigService(_tempConfigPath);
    config.Save("https://example.com", "token");
    var vm = new MainViewModel(config, new OhrbobClient(config, new PlaylistHttpHandler()), null, new TestAudioPlayer(), a => a());

    await vm.RetryConnectionAsync();

    Assert.That(vm.PlaylistItems.Select(p => p.EpisodeId), Is.EqualTo(new[] { "e1", "e2" }));
    Assert.That(vm.PlaylistItems[0].IsCurrent, Is.True);
    Assert.That(vm.IsPlaylistUnavailable, Is.False);
}

[Test]
public async Task BufferTarget_IsSavedPerStation()
{
    var config = new ConfigService(_tempConfigPath);
    config.Save("https://example.com", "token");
    var vm = new MainViewModel(config, new OhrbobClient(config, new PlaylistHttpHandler()), null, new TestAudioPlayer(), a => a());

    await vm.RetryConnectionAsync();
    vm.SelectedBufferOption = vm.BufferOptions.Single(o => o.Minutes == 30);
    await vm.ApplySelectedBufferAsync();

    Assert.That(config.GetBufferMinutes("s1"), Is.EqualTo(30));
    Assert.That(config.GetBufferMinutes("other-station"), Is.EqualTo(0));
}

[Test]
public async Task HostPacedStation_HasDownloadBlockedReason()
{
    var handler = new PlaylistHttpHandler
    {
        StationJson = "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\",\"hostPaceEnabled\":true},\"feeds\":[]}]",
        PlaylistJson = "{\"stationId\":\"s1\",\"hostPaceEnabled\":true,\"isAging\":false,\"episodes\":[]}"
    };
    var config = new ConfigService(_tempConfigPath);
    config.Save("https://example.com", "token");
    var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a());

    await vm.RetryConnectionAsync();

    Assert.That(vm.DownloadBlockedReason, Does.Contain("host"));
    Assert.That(vm.IsDownloadEnabled, Is.False);
}

[Test]
public async Task OlderServerWithoutPlaylist_MarksPlaylistUnavailable()
{
    var handler = new MockHttpMessageHandler(req =>
    {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.EndsWith("/stream/playlist"))
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        var body = path.EndsWith("/stream/stations")
            ? "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\"},\"feeds\":[]}]"
            : "{}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    });
    var config = new ConfigService(_tempConfigPath);
    config.Save("https://example.com", "token");
    var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a());

    await vm.RetryConnectionAsync();

    Assert.That(vm.IsPlaylistUnavailable, Is.True);
    Assert.That(vm.PlaylistItems, Is.Empty);
}

[Test]
public async Task AgingStation_HasDownloadBlockedReason()
{
    var handler = new PlaylistHttpHandler
    {
        StationJson = "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\",\"isAging\":true},\"feeds\":[]}]"
    };
    var config = new ConfigService(_tempConfigPath);
    config.Save("https://example.com", "token");
    var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a());

    await vm.RetryConnectionAsync();

    Assert.That(vm.DownloadBlockedReason, Does.Contain("aging"));
    Assert.That(vm.IsDownloadEnabled, Is.False);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "Initialize_PopulatesPlaylist|BufferTarget_IsSavedPerStation|HostPacedStation_HasDownloadBlockedReason|AgingStation_HasDownloadBlockedReason"`
Expected: FAIL, members do not exist.

- [ ] **Step 3: Implement playlist loading and settings state**

In `MainViewModel`:

- Add `private bool _suppressBufferSave;`.
- Add `public sealed record BufferOption(int Minutes, string Label);` in the same file or a `Models/BufferOption.cs`; keep it with the view model if it has no other consumer.
- `BufferOptions` = Off/10/15/30/60/120 in that order.
- `SelectedBufferOption` partial method: if `_suppressBufferSave` return; else `_ = ApplySelectedBufferAsync();`.
- `ApplySelectedBufferAsync`:
  ```csharp
  if (SelectedStation?.Station?.Id is not { } stationId) return;
  _configService.SetBufferMinutes(stationId, SelectedBufferOption.Minutes);
  await RefreshPlaylistAsync();
  ```
  Task 8 adds the download kickoff to this method.
- Add `RefreshPlaylistAsync`:
  - `if (!IsConnected) return;`
  - set `IsPlaylistLoading = true`;
  - `try { var playlist = await _client.GetPlaylistAsync(); ... } catch { IsPlaylistUnavailable = true; PlaylistItems.Clear(); }`
  - if null: `IsPlaylistUnavailable = true`, clear items;
  - else map `Episodes` to `PlaylistItem`, preserve existing `DownloadState` for matching ids, replace `PlaylistItems`;
  - update `DownloadBlockedReason` from the station flags (`HostPaceEnabled` -> host text, `IsAging` -> aging text);
  - update `DownloadStatusMessage` (blocked reason, else "Set a buffer in Settings to download episodes." when minutes == 0, else null);
  - `IsDownloadEnabled = DownloadBlockedReason == null && _configService.GetBufferMinutes(stationId) > 0`;
  - `IsBufferSelectionEnabled = DownloadBlockedReason == null`;
  - `finally IsPlaylistLoading = false; IsPlaylistEmpty = PlaylistItems.Count == 0 && !IsPlaylistUnavailable;`
- Call `await RefreshPlaylistAsync();` after `RefreshStatusAsync()` in `InitializeAppAsync`, `RefreshAllAsync`, and after `SwitchStationAsync`.
- On station change, restore `SelectedBufferOption` from config with `_suppressBufferSave = true`.
- Add overlay booleans and commands:
  ```csharp
  [ObservableProperty] private bool _isSettingsOpen;
  [ObservableProperty] private bool _isUpNextOpen;
  [RelayCommand] private void OpenSettings() => IsSettingsOpen = true;
  [RelayCommand] private void CloseSettings() => IsSettingsOpen = false;
  [RelayCommand] private void ToggleUpNext() => IsUpNextOpen = !IsUpNextOpen;
  [RelayCommand] private void CloseUpNext() => IsUpNextOpen = false;
  ```

- [ ] **Step 4: Run the view-model and client tests**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "MainViewModelTests|OhrbobClientTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.App/ViewModels/MainViewModel.cs Ohrbob.App.Tests/ViewModels/MainViewModelTests.cs
git commit -m "feat: load playlists and per-station buffer settings"
```

---

### Task 8: Download orchestration and local playback

**Files:**
- Modify: `Ohrbob.App/ViewModels/MainViewModel.cs`
- Modify: `Ohrbob.App/Services/SoundFlowAudioPlayer.cs`
- Test: `Ohrbob.App.Tests/ViewModels/MainViewModelTests.cs`
- Test: `Ohrbob.App.Tests/ViewModels/PlaybackEndFlowTests.cs`

**Interfaces:**
- Consumes: `EpisodeDownloadService`, `PlaylistItem`.
- Produces: `Task MainViewModel.SyncDownloadsAsync()`, `Task MainViewModel.WaitForDownloadsAsync()`, `void MainViewModel.TriggerDownloadSync()`; `bool MainViewModel.IsDownloadEnabled`; local path preference in `LoadAudioUrlAsync`.
- Extends the `MainViewModel` constructor with optional `EpisodeDownloadService? downloads = null` and `OfflineStateService? offlineState = null` (Task 9 uses the latter).

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public async Task SyncDownloads_SkipsForHostPacedStation()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    var requests = new List<string>();
    var handler = new PlaylistHttpHandler();
    var config = new ConfigService(_tempConfigPath);
    config.Save("https://example.com", "token");
    config.SetBufferMinutes("s1", 30);
    handler.StationJson = "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\",\"hostPaceEnabled\":true},\"feeds\":[]}]";
    handler.PlaylistJson = "{\"stationId\":\"s1\",\"hostPaceEnabled\":true,\"isAging\":false,\"episodes\":[{\"episodeId\":\"e1\",\"title\":\"Current\",\"streamUri\":\"https://audio/1\",\"isCurrent\":true}]}";
    var downloadHandler = new MockHttpMessageHandler(req => { requests.Add(req.RequestUri!.ToString()); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) }; });
    var downloads = new EpisodeDownloadService(root, new HttpClient(downloadHandler), _ => TimeSpan.FromMinutes(1));
    var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), downloads);

    await vm.RetryConnectionAsync();
    await vm.WaitForDownloadsAsync();

    Assert.That(requests, Is.Empty);
    Assert.That(vm.IsDownloadEnabled, Is.False);
}

[Test]
public async Task SyncDownloads_PrunesExistingFileForHostPacedStation()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(Path.Combine(root, "s1"));
    File.WriteAllBytes(Path.Combine(root, "s1", "e1.mp3"), new byte[] { 1 });
    var handler = new PlaylistHttpHandler
    {
        StationJson = "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\",\"hostPaceEnabled\":true},\"feeds\":[]}]",
        PlaylistJson = "{\"stationId\":\"s1\",\"hostPaceEnabled\":true,\"isAging\":false,\"episodes\":[{\"episodeId\":\"e1\",\"title\":\"Current\",\"streamUri\":\"https://audio/1\",\"isCurrent\":true}]}"
    };
    var config = new ConfigService(_tempConfigPath);
    config.Save("https://example.com", "token");
    config.SetBufferMinutes("s1", 30);
    var downloads = new EpisodeDownloadService(root, null, _ => TimeSpan.FromMinutes(1));
    var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), downloads);

    await vm.RetryConnectionAsync();
    await vm.WaitForDownloadsAsync();

    Assert.That(downloads.GetDownloadedPath("s1", "e1"), Is.Null,
        "a cached file must not survive host pace becoming active");
}

[Test]
public async Task HostPacedStation_DoesNotPlayCachedFile()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(Path.Combine(root, "s1"));
    File.WriteAllBytes(Path.Combine(root, "s1", "e1.mp3"), new byte[] { 1 });
    var config = new ConfigService(_tempConfigPath);
    config.SetBufferMinutes("s1", 30);
    var downloads = new EpisodeDownloadService(root, null, _ => TimeSpan.FromMinutes(1));
    var player = new TestAudioPlayer();
    var vm = new MainViewModel(config, new OhrbobClient(config, new PlaylistHttpHandler()), null, player, a => a(), downloads);

    vm.SelectedStation = new StationWithFeedsDto
    {
        Station = new Station { Id = "s1", Name = "One", HostPaceEnabled = true }
    };
    vm.CurrentEpisodeId = "e1";
    vm.CurrentEpisodeStreamUri = "https://audio/1";

    Assert.That(player.CurrentUrl, Is.EqualTo("https://audio/1"));
}

[Test]
public async Task CurrentDownloadedEpisode_LoadsLocalPath()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    var config = new ConfigService(_tempConfigPath);
    config.SetBufferMinutes("s1", 30);
    var downloads = new EpisodeDownloadService(root, null, _ => TimeSpan.FromMinutes(1));
    var player = new TestAudioPlayer();
    var vm = new MainViewModel(config, new OhrbobClient(config, new PlaylistHttpHandler()), null, player, a => a(), downloads);
    Directory.CreateDirectory(Path.Combine(root, "s1"));
    File.WriteAllBytes(Path.Combine(root, "s1", "e1.mp3"), new byte[] { 1 });

    vm.SelectedStation = new StationWithFeedsDto { Station = new Station { Id = "s1", Name = "One" } };
    vm.CurrentEpisodeId = "e1";
    vm.CurrentEpisodeStreamUri = "https://audio/1";

    Assert.That(player.CurrentUrl, Is.EqualTo(downloads.GetDownloadedPath("s1", "e1")));
}
```

Add to `PlaybackEndFlowTests` a handler and test that a download request is made for an enabled station:

```csharp
private class DownloadingHttpHandler : HttpMessageHandler
{
    public int AudioRequests;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (path.StartsWith("/audio/"))
        {
            Interlocked.Increment(ref AudioRequests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
            });
        }

        var body = path.EndsWith("/stream/stations")
            ? "[{\"station\":{\"id\":\"s1\",\"name\":\"Test Station\"},\"feeds\":[]}]"
            : path.EndsWith("/stream/playlist")
                ? "{\"stationId\":\"s1\",\"hostPaceEnabled\":false,\"isAging\":false,\"episodes\":[" +
                  "{\"episodeId\":\"e1\",\"title\":\"One\",\"streamUri\":\"https://example.com/audio/1.mp3\",\"isCurrent\":true}]}"
                : "{}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
    }
}

[Test]
public async Task RefreshAll_DownloadsUpcomingEpisodesWhenBufferIsSet()
{
    var handler = new DownloadingHttpHandler();
    var config = new ConfigService(_tempConfigPath);
    config.Save("https://example.com", "token");
    config.SetBufferMinutes("s1", 10);
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    var downloads = new EpisodeDownloadService(root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
    var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), downloads);

    await vm.RefreshAllCommand.ExecuteAsync(null);
    await vm.WaitForDownloadsAsync();

    Assert.That(handler.AudioRequests, Is.EqualTo(1));
    Assert.That(vm.PlaylistItems.Single().DownloadState, Is.EqualTo(DownloadState.Downloaded));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "SyncDownloads_|CurrentDownloadedEpisode_|RefreshAll_DownloadsUpcomingEpisodes"`
Expected: FAIL, `SyncDownloadsAsync` and the local-path preference do not exist.

- [ ] **Step 3: Implement orchestration**

In `MainViewModel`:

- Field `_downloads`; constructor parameter `EpisodeDownloadService? downloads = null` defaulting to `new EpisodeDownloadService()`.
- `SyncDownloadsAsync`:
  ```csharp
  if (!IsConnected || SelectedStation?.Station?.Id is not { } stationId) return;
  var blocked = SelectedStation.Station.HostPaceEnabled || SelectedStation.Station.IsAging;
  var minutes = _configService.GetBufferMinutes(stationId);
  try
  {
      await _downloads.SyncAsync(stationId, PlaylistItems, minutes, !blocked && minutes > 0,
          PositionSeconds, CancellationToken.None, CurrentEpisodeId);
  }
  catch (OperationCanceledException) { }
  catch (Exception ex) { Log.Error(ex, "Episode download sync failed"); }
  ```
  Task 9 adds the `SaveOfflineSnapshot()` call at the end.
- Add `private Task? _downloadSyncTask;`
  ```csharp
  public void TriggerDownloadSync()
  {
      _downloads.Cancel();
      _downloadSyncTask = SyncDownloadsAsync();
  }

  public Task WaitForDownloadsAsync() => _downloadSyncTask ?? Task.CompletedTask;
  ```
- `ApplySelectedBufferAsync` calls `RefreshPlaylistAsync()` then `TriggerDownloadSync()`; this is the hook Task 7 deliberately left out.
- After every playlist refresh and on station switch call `TriggerDownloadSync()`, never `await` it, so a long download does not block refresh or navigation.
- `OnSelectedStationChanged`: before switching stations call `_downloads.Cancel()`.
- `OnCurrentEpisodeStreamUriChanged`/`LoadAudioUrlAsync`: resolve
  ```csharp
  var station = SelectedStation?.Station;
  var downloadsBlocked = station is { HostPaceEnabled: true } or { IsAging: true };
  var localPath = station?.Id is { } stationId && !downloadsBlocked && !string.IsNullOrEmpty(CurrentEpisodeId)
      ? _downloads.GetDownloadedPath(stationId, CurrentEpisodeId)
      : null;
  await _player.Load(localPath ?? url);
  ```
  A downloaded file wins only when it exists and the station still allows downloads; host-paced/aging files are ignored immediately and pruned by the next sync.
- `SoundFlowAudioPlayer.Load(string url)`: if `File.Exists(url)`, open a `FileStream` with `FileShare.Read`, set a completed source, and use the same `StreamDataProvider`/device setup as the HTTP path. Extract the shared provider/device setup into one private helper so the two paths cannot diverge.

- [ ] **Step 4: Run the focused and full app suites**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "SyncDownloads_|CurrentDownloadedEpisode_|PlaybackEndFlowTests"`
Expected: PASS.
Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.App/ViewModels/MainViewModel.cs Ohrbob.App/Services/SoundFlowAudioPlayer.cs Ohrbob.App.Tests/ViewModels/MainViewModelTests.cs Ohrbob.App.Tests/ViewModels/PlaybackEndFlowTests.cs
git commit -m "feat: orchestrate episode downloads and play local files"
```

---

### Task 9: Offline startup, station switching and local advance

**Files:**
- Modify: `Ohrbob.App/ViewModels/MainViewModel.cs`
- Test: `Ohrbob.App.Tests/ViewModels/MainViewModelTests.cs`
- Test: `Ohrbob.App.Tests/ViewModels/PlaybackEndFlowTests.cs`

**Interfaces:**
- Consumes: `OfflineStateService`, `EpisodeDownloadService`.
- Produces: `bool MainViewModel.IsOffline`; snapshot save on init/status/timer; offline station switch and local advance.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public async Task Startup_WithUnreachableServer_RestoresSnapshotAndStaysConnected()
{
    var config = new ConfigService(_tempConfigPath);
    config.Save("https://example.com", "token");
    var snapshotPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "offline.json");
    var offline = new OfflineStateService(snapshotPath);
    offline.Save(new OfflineSnapshot
    {
        ActiveStationId = "s1",
        ProfileName = "Kai",
        Stations = new List<StationWithFeedsDto> { new() { Station = new Station { Id = "s1", Name = "One" } } },
        StationStates = new Dictionary<string, StationOfflineState>
        {
            ["s1"] = new()
            {
                StationId = "s1",
                CurrentEpisodeId = "e1",
                PositionSeconds = 42,
                Playlist = new List<PlaylistEpisodeDto>
                {
                    new() { EpisodeId = "e1", Title = "Current", StreamUri = "https://audio/1", IsCurrent = true }
                }
            }
        }
    });
    var failing = new FailingHttpHandler();
    var vm = new MainViewModel(config, new OhrbobClient(config, failing), null, new TestAudioPlayer(), a => a(), null, offline);

    await vm.RetryConnectionAsync();

    Assert.That(vm.IsConnected, Is.True);
    Assert.That(vm.IsOffline, Is.True);
    Assert.That(vm.Stations.Single().Station!.Id, Is.EqualTo("s1"));
    Assert.That(vm.ProfileName, Is.EqualTo("Kai"));
    Assert.That(vm.CurrentEpisodeId, Is.EqualTo("e1"));
    Assert.That(vm.PositionSeconds, Is.EqualTo(42));
    Assert.That(vm.PlaylistItems.Single().EpisodeId, Is.EqualTo("e1"));
}

[Test]
public async Task Offline_AdvanceToNextDownloadedEpisode()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(Path.Combine(root, "s1"));
    File.WriteAllBytes(Path.Combine(root, "s1", "e2.mp3"), new byte[] { 1 });
    var config = new ConfigService(_tempConfigPath);
    config.Save("https://example.com", "token");
    var snapshotPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "offline.json");
    var offline = new OfflineStateService(snapshotPath);
    offline.Save(new OfflineSnapshot
    {
        ActiveStationId = "s1",
        Stations = new List<StationWithFeedsDto> { new() { Station = new Station { Id = "s1", Name = "One" } } },
        StationStates = new Dictionary<string, StationOfflineState>
        {
            ["s1"] = new()
            {
                StationId = "s1",
                CurrentEpisodeId = "e1",
                Playlist = new List<PlaylistEpisodeDto>
                {
                    new() { EpisodeId = "e1", Title = "One", StreamUri = "https://audio/1", IsCurrent = true },
                    new() { EpisodeId = "e2", Title = "Two", StreamUri = "https://audio/2" }
                }
            }
        }
    });
    var downloads = new EpisodeDownloadService(root, null, _ => TimeSpan.FromMinutes(1));
    var player = new TestAudioPlayer();
    var vm = new MainViewModel(config, new OhrbobClient(config, new FailingHttpHandler()), null, player, a => a(), downloads, offline);
    vm.IsOffline = true;
    vm.SelectedStation = vm.Stations.Single();
    vm.CurrentEpisodeId = "e1";
    vm.CurrentEpisodeStreamUri = "https://audio/1";

    player.RaiseEnded();
    for (var i = 0; i < 50 && vm.CurrentEpisodeId != "e2"; i++) await Task.Delay(20);

    Assert.That(vm.CurrentEpisodeId, Is.EqualTo("e2"));
    Assert.That(player.CurrentUrl, Is.EqualTo(Path.Combine(root, "s1", "e2.mp3")));
}

[Test]
public async Task Offline_SwitchingToStationWithoutDownloads_IsRefused()
{
    var config = new ConfigService(_tempConfigPath);
    config.Save("https://example.com", "token");
    var snapshotPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "offline.json");
    var offline = new OfflineStateService(snapshotPath);
    offline.Save(new OfflineSnapshot
    {
        ActiveStationId = "s1",
        Stations = new List<StationWithFeedsDto>
        {
            new() { Station = new Station { Id = "s1", Name = "One" } },
            new() { Station = new Station { Id = "s2", Name = "Two" } }
        },
        StationStates = new Dictionary<string, StationOfflineState>
        {
            ["s1"] = new() { StationId = "s1", CurrentEpisodeId = "e1" }
        }
    });
    var vm = new MainViewModel(config, new OhrbobClient(config, new FailingHttpHandler()), null, new TestAudioPlayer(), a => a(), null, offline);
    vm.IsOffline = true;

    vm.SelectedStation = vm.Stations.Single(s => s.Station!.Id == "s2");

    await Task.Delay(50);
    Assert.That(vm.SelectedStation.Station!.Id, Is.EqualTo("s1"),
        "a station without offline content must not replace the active station");
    Assert.That(vm.ConnectionErrorMessage, Does.Contain("offline"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "Startup_WithUnreachableServer|Offline_"`
Expected: FAIL, `IsOffline` and restore behavior do not exist.

- [ ] **Step 3: Implement offline snapshot flow**

- Add a private `FailingHttpHandler` to `MainViewModelTests` (a handler returning `InternalServerError`) so the offline tests do not depend on the other fixture.
- Constructor: load `_offlineState.Load()`, keep it in `_snapshot`.
- Add `[ObservableProperty] private bool _isOffline;` and `[ObservableProperty] private string? _offlineMessage;`.
- `ApplySnapshot(OfflineSnapshot snapshot)`: set a `_isRestoringSnapshot` flag, populate `Stations` (mark each `Station.IsAvailableOffline` when its state has at least one existing download), `SelectedStation`, `ProfileName`, and the active state's `PlaylistItems`, `CurrentEpisodeId/Title/Description/CoverUrl/StreamUri`, `PositionSeconds`, then clear the flag. `OnSelectedStationChanged` returns immediately while the flag is set, so restoring never calls the server or the offline-switch guard.
- Call `ApplySnapshot` after setting `IsConnected` in the constructor, before `InitializeAppAsync`.
- `InitializeAppAsync` success: `IsOffline = false; OfflineMessage = null;` after refresh and downloads; save snapshot.
- `InitializeAppAsync` failure: if `_snapshot?.StationStates.Count > 0`, `IsOffline = true` and keep state; otherwise current behavior.
- `SaveOfflineSnapshot()`: build an `OfflineSnapshot` from `Stations`, `SelectedStation`, `ProfileName`, and a per-station map containing the active station's `PlaylistItems` (map to `PlaylistEpisodeDto`), current episode id and `PositionSeconds`. Keep existing state entries for other stations; update only the active one. Call it after status/playlist refresh and from the 10-second timer tick.
- `SyncDownloadsAsync` calls `SaveOfflineSnapshot()` after syncing.
- `OnSelectedStationChanged`: when `IsOffline`, do not call `PlayStationAsync`. Look up the state; if it has no downloaded file at all, restore the previous `SelectedStation` and set `ConnectionErrorMessage = "This station is not available offline."`. Otherwise load its playlist/current/position and, if the current file is missing, fall back to the first downloaded playlist item.
- `ContinueAfterEndAsync`: before the server skip, if `IsOffline`, try `AdvanceOffline()`. If online, still try the server; on any exception, try `AdvanceOffline()`. `AdvanceOffline()` finds the next playlist item after the current one with a downloaded path, applies its metadata with position 0, saves the snapshot, starts playback and returns true; otherwise returns false.

- [ ] **Step 4: Run the focused and full app suites**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "Startup_WithUnreachableServer|Offline_|PlaybackEndFlowTests"`
Expected: PASS.
Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.App/ViewModels/MainViewModel.cs Ohrbob.App.Tests/ViewModels/MainViewModelTests.cs Ohrbob.App.Tests/ViewModels/PlaybackEndFlowTests.cs
git commit -m "feat: start and continue playback offline"
```

---

### Task 10: Settings dialog and Up next UI

**Files:**
- Modify: `Ohrbob.App/Views/MainView.axaml`
- Modify: `Ohrbob.App/Views/PlayerView.axaml`
- Modify: `Ohrbob.App/Views/PlayerView.axaml.cs`
- Test: `Ohrbob.App.Tests/Views/PlayerViewLayoutTests.cs`
- Test: `Ohrbob.App.Tests/Views/MainWindowE2ETests.cs` only if the existing initialization test needs the new dialog.

**Interfaces:**
- Consumes: all view-model state from Tasks 7-9.
- Produces: `SettingsDialog` and `UpNextPanel` named controls; swipe opens/closes Up next.

- [ ] **Step 1: Write the failing view tests**

```csharp
[AvaloniaTest]
public void SettingsDialog_OpensAndShowsPerStationTargets()
{
    var vm = new MainViewModel { IsSettingsOpen = true };
    vm.Stations.Add(new StationWithFeedsDto { Station = new Station { Id = "s1", Name = "One" } });
    vm.SelectedStation = vm.Stations[0];
    var view = new MainView { DataContext = vm };
    var window = new Window { Width = 900, Height = 600, Content = view };
    window.Show();
    Dispatcher.UIThread.RunJobs();

    var dialog = view.FindControl<Border>("SettingsDialog")!;
    Assert.That(dialog.IsVisible, Is.True);
    var options = view.FindControl<ComboBox>("BufferOptionsBox")!;
    Assert.That(options.ItemsSource, Is.Not.Null);
    window.Close();
}

[AvaloniaTest]
public void UpNextPanel_ShowsDownloadStateAndClosesByCommand()
{
    var vm = new MainViewModel { IsUpNextOpen = true };
    vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = "e1", Title = "Current", IsCurrent = true, DownloadState = DownloadState.Downloaded });
    vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = "e2", Title = "Next", DownloadState = DownloadState.NotDownloaded });
    var view = new PlayerView { DataContext = vm };
    var window = new Window { Width = 900, Height = 600, Content = view };
    window.Show();
    Dispatcher.UIThread.RunJobs();

    var panel = view.FindControl<Border>("UpNextPanel")!;
    Assert.That(panel.IsVisible, Is.True);
    Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Downloaded"), Is.True);
    Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Not downloaded"), Is.True);

    vm.CloseUpNextCommand.Execute(null);
    Dispatcher.UIThread.RunJobs();
    Assert.That(panel.IsVisible, Is.False);
    window.Close();
}

[AvaloniaTest]
public void SwipeRecognizer_IsAttachedToPlayerScroll()
{
    var view = new PlayerView { DataContext = new MainViewModel() };
    var window = new Window { Width = 900, Height = 600, Content = view };
    window.Show();
    Dispatcher.UIThread.RunJobs();

    var scroll = view.FindControl<ScrollViewer>("EpisodeScroll")!;
    Assert.That(scroll.GestureRecognizers.OfType<SwipeGestureRecognizer>().Any(), Is.True);
    window.Close();
}

[AvaloniaTest]
public void OfflineChip_IsVisibleWhenOffline()
{
    var vm = new MainViewModel { IsOffline = true };
    var view = new PlayerView { DataContext = vm };
    var window = new Window { Width = 900, Height = 600, Content = view };
    window.Show();
    Dispatcher.UIThread.RunJobs();

    Assert.That(view.FindControl<Border>("OfflineChip")!.IsVisible, Is.True);
    window.Close();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "SettingsDialog_|UpNextPanel_|SwipeRecognizer_|OfflineChip_"`
Expected: FAIL, controls do not exist.

- [ ] **Step 3: Add the settings dialog to `MainView.axaml`**

Add a second overlay next to the replace-connection dialog:

```xml
<Border IsVisible="{Binding IsSettingsOpen}" Background="#80000000">
    <Border x:Name="SettingsDialog" MaxWidth="420" Margin="16" Padding="24" CornerRadius="12"
            HorizontalAlignment="Center" VerticalAlignment="Center"
            Background="{DynamicResource AppBackgroundBrush}"
            BoxShadow="0 4 20 0 #40000000">
        <StackPanel Spacing="16">
            <TextBlock Text="Settings" Classes="h2" />
            <TextBlock Text="{Binding SelectedStation.Station.Name, FallbackValue='No active station'}"
                       FontSize="13" FontWeight="SemiBold"
                       Foreground="{DynamicResource AppMutedTextBrush}" />
            <TextBlock Text="Buffer ahead" FontSize="13" FontWeight="SemiBold" />
            <ComboBox x:Name="BufferOptionsBox"
                      ItemsSource="{Binding BufferOptions}"
                      SelectedItem="{Binding SelectedBufferOption, Mode=TwoWay}"
                      IsEnabled="{Binding IsBufferSelectionEnabled}"
                      HorizontalAlignment="Stretch">
                <ComboBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding Label}" />
                    </DataTemplate>
                </ComboBox.ItemTemplate>
            </ComboBox>
            <TextBlock Text="Whole upcoming episodes of this station are downloaded. More buffer uses more storage."
                       FontSize="12" TextWrapping="Wrap"
                       Foreground="{DynamicResource AppMutedTextBrush}" />
            <Border Background="{StaticResource ThemeWarningBrush}" CornerRadius="6" Padding="10"
                    IsVisible="{Binding DownloadBlockedReason, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
                <TextBlock Text="{Binding DownloadBlockedReason}" FontSize="12" TextWrapping="Wrap"
                           Foreground="{StaticResource OhrbobDarkBlue}" />
            </Border>
            <Button Content="Close" Command="{Binding CloseSettingsCommand}"
                    Classes="btn-primary" HorizontalAlignment="Right" MinWidth="100" />
        </StackPanel>
    </Border>
</Border>
```

- [ ] **Step 4: Add the sidebar settings button and Up next UI to `PlayerView.axaml`**

Sidebar footer: change the grid to `ColumnDefinitions="*, Auto, Auto"`, keep "Listening as" in column 0, add a gear button in column 1 bound to `OpenSettingsCommand` (reuse the existing gear `PathIcon` from `AudioWidget`), and move Disconnect to column 2.

In the station card template, add a small "Offline" text badge beside the station name, visible with `{Binding Station.IsAvailableOffline}`.

Add an "Offline" chip near the station name:

```xml
<Border x:Name="OfflineChip" Background="{StaticResource ThemeWarningBrush}" CornerRadius="4" Padding="6,2"
        IsVisible="{Binding IsOffline}">
    <TextBlock Text="Offline" FontSize="9" FontWeight="Bold" Foreground="{StaticResource OhrbobDarkBlue}" />
</Border>
```

Add an "Up next" button beside the Menu/refresh area, bound to `ToggleUpNextCommand`.

Add the panel inside the player `Panel` after `AudioWidget`:

```xml
<Panel IsVisible="{Binding IsUpNextOpen}">
    <Border Background="#80000000" PointerPressed="CloseUpNextOnPointer" />
    <Border x:Name="UpNextPanel" Width="360" HorizontalAlignment="Right"
            Background="{DynamicResource AppSurfaceBrush}"
            BorderBrush="{DynamicResource AppBorderBrush}" BorderThickness="1,0,0,0">
        <Grid RowDefinitions="Auto,Auto,*">
            <Grid Grid.Row="0" ColumnDefinitions="*,Auto" Margin="16">
                <StackPanel>
                    <TextBlock Text="Up next" Classes="h2" />
                    <TextBlock Text="{Binding SelectedStation.Station.Name}" FontSize="12"
                               Foreground="{DynamicResource AppMutedTextBrush}" />
                </StackPanel>
                <Button Grid.Column="1" Content="Close" Command="{Binding CloseUpNextCommand}" />
            </Grid>
            <Border Grid.Row="1" Margin="16,0,16,8" Padding="10" CornerRadius="6"
                    Background="{StaticResource ThemeWarningBrush}"
                    IsVisible="{Binding DownloadStatusMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
                <TextBlock Text="{Binding DownloadStatusMessage}" FontSize="12" TextWrapping="Wrap"
                           Foreground="{StaticResource OhrbobDarkBlue}" />
            </Border>
            <ListBox Grid.Row="2" ItemsSource="{Binding PlaylistItems}" Background="Transparent"
                     BorderThickness="0" Margin="8">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid ColumnDefinitions="Auto,*,Auto" Margin="8">
                            <Border Width="44" Height="44" CornerRadius="6" ClipToBounds="True"
                                    Background="{DynamicResource AppBackgroundBrush}">
                                <Image services:ImageLoader.Source="{Binding CoverUrl}" Stretch="UniformToFill"
                                       IsVisible="{Binding CoverUrl, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                            </Border>
                            <StackPanel Grid.Column="1" Margin="10,0" VerticalAlignment="Center">
                                <TextBlock Text="{Binding Title}" FontSize="13" FontWeight="SemiBold"
                                           Foreground="{DynamicResource AppForegroundBrush}"
                                           TextTrimming="CharacterEllipsis" />
                                <TextBlock Text="{Binding Artist}" FontSize="11"
                                           Foreground="{DynamicResource AppMutedTextBrush}"
                                           TextTrimming="CharacterEllipsis" />
                            </StackPanel>
                            <Panel Grid.Column="2" VerticalAlignment="Center">
                                <TextBlock Text="{Binding StateLabel}" FontSize="10" FontWeight="Bold"
                                           IsVisible="{Binding !IsCurrent}"
                                           Foreground="{StaticResource OhrbobStrongOrange}" />
                                <TextBlock Text="Playing" FontSize="10" FontWeight="Bold"
                                           IsVisible="{Binding IsCurrent}"
                                           Foreground="{StaticResource ThemeSecondaryBrush}" />
                            </Panel>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Grid>
    </Border>
</Panel>
```

Add the empty/unavailable states in the panel header area: a `TextBlock` bound to `IsPlaylistLoading` ("Loading…"), one bound to `IsPlaylistUnavailable` ("Up next is not available on this server.") and one bound to `IsPlaylistEmpty` ("No upcoming episodes"), hidden otherwise. Follow the existing `IsVisible` boolean style.

- [ ] **Step 5: Wire the swipe and close handlers in `PlayerView.axaml.cs`**

```csharp
EpisodeScroll.GestureRecognizers.Add(new SwipeGestureRecognizer
{
    CanHorizontallySwipe = true,
    IsMouseEnabled = true
});
EpisodeScroll.SwipeGestureEnded += (_, e) =>
{
    if (DataContext is not MainViewModel vm) return;
    if (e.Velocity.X > 200) vm.IsUpNextOpen = true;
    else if (e.Velocity.X < -200) vm.IsUpNextOpen = false;
};

private void CloseUpNextOnPointer(object? sender, PointerPressedEventArgs e)
{
    if (DataContext is MainViewModel vm) vm.IsUpNextOpen = false;
}
```

- [ ] **Step 6: Run view tests and the full app suite**

Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj --filter "SettingsDialog_|UpNextPanel_|SwipeRecognizer_|OfflineChip_"`
Expected: PASS.
Run: `dotnet test Ohrbob.App.Tests/Ohrbob.App.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Ohrbob.App/Views/MainView.axaml Ohrbob.App/Views/PlayerView.axaml Ohrbob.App/Views/PlayerView.axaml.cs Ohrbob.App.Tests/Views/PlayerViewLayoutTests.cs
git commit -m "feat: add per-station settings and up-next panel"
```

---

### Final verification

- [ ] Run the backend suite: `dotnet test /home/kaiwegner/Projects/ohrbob2/Ohrbob.Player.Tests/Ohrbob.Player.Tests.csproj`
- [ ] Run the app suite: `dotnet test /home/kaiwegner/Projects/ohrbob2.app/Ohrbob.App.Tests/Ohrbob.App.Tests.csproj`
- [ ] Manual smoke test with a local player API:
  - Set a station to 10 minutes; confirm upcoming episodes download one at a time and the panel badges update.
  - Set a second station to 30 minutes; confirm the first station's files and setting are untouched.
  - Host-pace and aging stations show the blocked reason and download nothing.
  - Stop the server, restart the app; confirm the cached station, playlist and current episode appear, and episode end advances to the next downloaded file.
  - Point the app at a server without `/stream/playlist`; confirm the panel says unavailable and playback still works.
