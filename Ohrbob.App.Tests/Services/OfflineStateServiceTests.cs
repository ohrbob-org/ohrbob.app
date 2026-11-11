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
    public void Load_MissingOrCorruptFile_ReturnsNull()
    {
        var path = Path.Combine(_root, "offline.json");
        Assert.That(new OfflineStateService(path).Load(), Is.Null);

        File.WriteAllText(path, "{ not json");
        Assert.That(new OfflineStateService(path).Load(), Is.Null);
    }
}
