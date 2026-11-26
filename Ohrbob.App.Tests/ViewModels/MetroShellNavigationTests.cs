using System;
using System.IO;
using NUnit.Framework;
using Ohrbob.App.Models;
using Ohrbob.App.Services;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Tests.ViewModels;

[TestFixture]
public class MetroShellNavigationTests
{
    private string _tempConfigPath = null!;
    private string _tempOfflinePath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempConfigPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        _tempOfflinePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "offline.json");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var path in new[] { _tempConfigPath, _tempOfflinePath })
        {
            if (!File.Exists(path)) continue;
            try { File.Delete(path); } catch { }
        }
    }

    private MainViewModel CreateViewModel()
    {
        var config = new ConfigService(_tempConfigPath);
        return new MainViewModel(config, null, offlineState: new OfflineStateService(_tempOfflinePath));
    }

    [Test]
    public void OpenStation_NavigatesToThePlayer()
    {
        var vm = CreateViewModel();
        var station = new StationWithFeedsDto { Station = new Station { Id = "s1", Name = "News" } };
        vm.Stations.Add(station);

        vm.OpenStationCommand.Execute(station);

        Assert.That(vm.SelectedStation, Is.SameAs(station));
        Assert.That(vm.IsNowPlayingOpen, Is.True);
    }

    [Test]
    public void ShowStations_ReturnsToTheTileGrid()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = true;

        vm.ShowStationsCommand.Execute(null);

        Assert.That(vm.IsNowPlayingOpen, Is.False);
        Assert.That(vm.IsSettingsOpen, Is.False);
        Assert.That(vm.IsEpisodeActionsOpen, Is.False);
    }

    [Test]
    public void OpeningStationSettings_DoesNotChangeTheActiveStation()
    {
        var vm = CreateViewModel();
        var active = new StationWithFeedsDto { Station = new Station { Id = "s1", Name = "News" } };
        var other = new StationWithFeedsDto { Station = new Station { Id = "s2", Name = "Comedy" } };
        vm.Stations.Add(active);
        vm.Stations.Add(other);
        vm.SelectedStation = active;

        vm.OpenStationSettingsForCommand.Execute(other);

        Assert.That(vm.SelectedStation, Is.SameAs(active));
        Assert.That(vm.SettingsStation, Is.SameAs(other));
        Assert.That(vm.IsSettingsOpen, Is.True);
    }

    [Test]
    public void EpisodeLongPress_OpensActionsForThatEpisode()
    {
        var vm = CreateViewModel();
        var item = new PlaylistItem { EpisodeId = "e1", Title = "Episode One" };
        vm.PlaylistItems.Add(item);

        vm.OpenEpisodeActionsCommand.Execute(item);

        Assert.That(vm.IsEpisodeActionsOpen, Is.True);
        Assert.That(vm.SelectedEpisodeForActions, Is.SameAs(item));
        Assert.That(vm.SelectedEpisodeActionLabel, Is.EqualTo("Mark as completed"));
    }

    [Test]
    public void ToggleEpisodeCompletion_FlipsStateAndActionLabel()
    {
        var vm = CreateViewModel();
        var item = new PlaylistItem { EpisodeId = "e1", Title = "Episode One" };
        vm.PlaylistItems.Add(item);
        vm.OpenEpisodeActionsCommand.Execute(item);

        vm.ToggleEpisodeCompletionCommand.Execute(null);

        Assert.That(item.IsCompleted, Is.True);
        Assert.That(vm.SelectedEpisodeActionLabel, Is.EqualTo("Mark as uncompleted"));

        vm.ToggleEpisodeCompletionCommand.Execute(null);

        Assert.That(item.IsCompleted, Is.False);
        Assert.That(vm.SelectedEpisodeActionLabel, Is.EqualTo("Mark as completed"));
    }

    [Test]
    public void ClosingEpisodeActions_KeepsCompletionChoice()
    {
        var vm = CreateViewModel();
        var item = new PlaylistItem { EpisodeId = "e1", Title = "Episode One" };
        vm.PlaylistItems.Add(item);
        vm.OpenEpisodeActionsCommand.Execute(item);
        vm.ToggleEpisodeCompletionCommand.Execute(null);

        vm.CloseEpisodeActionsCommand.Execute(null);

        Assert.That(vm.IsEpisodeActionsOpen, Is.False);
        Assert.That(item.IsCompleted, Is.True);
    }
}
