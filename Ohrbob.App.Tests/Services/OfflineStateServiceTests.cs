using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ohrbob.App.Models;
using Ohrbob.App.Services;

namespace Ohrbob.App.Tests.Services;

[TestFixture]
public class OfflineStateServiceTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ohrbob-offline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

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
    public void Save_StripsQueryAndFragmentFromStreamUri_AndKeepsCallerEpisode()
    {
        var path = Path.Combine(_root, "offline.json");
        var episode = new PlaylistEpisodeDto { EpisodeId = "e1", StreamUri = "https://audio.example/ep.mp3?token=secret&x=1" };
        var snapshot = new OfflineSnapshot
        {
            StationStates = new Dictionary<string, StationOfflineState>
            {
                ["s1"] = new() { StationId = "s1", Playlist = new List<PlaylistEpisodeDto> { episode } }
            }
        };

        new OfflineStateService(path).Save(snapshot);

        var json = File.ReadAllText(path);
        Assert.That(json, Does.Contain("https://audio.example/ep.mp3"));
        Assert.That(json, Does.Not.Contain("secret"));
        Assert.That(json, Does.Not.Contain("token="));
        Assert.That(json, Does.Not.Contain("?"));
        Assert.That(episode.StreamUri, Is.EqualTo("https://audio.example/ep.mp3?token=secret&x=1"));
    }

    [Test]
    public void Load_MissingOrCorruptFile_ReturnsNull()
    {
        var path = Path.Combine(_root, "offline.json");
        Assert.That(new OfflineStateService(path).Load(), Is.Null);

        File.WriteAllText(path, "{ not json");
        Assert.That(new OfflineStateService(path).Load(), Is.Null);
    }
}
