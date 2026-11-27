using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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

    private (MainViewModel ViewModel, GatedHttpHandler Handler, StationWithFeedsDto Station)
        CreateGatedViewModel()
    {
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://example.com", "token");
        var handler = new GatedHttpHandler();
        var vm = new MainViewModel(
            config,
            new OhrbobClient(config, handler),
            null,
            new TestAudioPlayer(),
            action => action());
        vm.IsConnected = true;

        var station = new StationWithFeedsDto
        {
            Station = new Station { Id = "s2", Name = "New Station" },
        };
        vm.Stations.Add(station);
        return (vm, handler, station);
    }

    private (MainViewModel ViewModel, StagedHttpHandler Handler, StationWithFeedsDto Station)
        CreateStagedViewModel()
    {
        var config = new ConfigService(_tempConfigPath);
        var handler = new StagedHttpHandler();
        var vm = new MainViewModel(
            config,
            new OhrbobClient(config, handler),
            null,
            new TestAudioPlayer(),
            action => action(),
            offlineState: new OfflineStateService(_tempOfflinePath));

        // Configure after construction so no background initial sync competes
        // with the station switch under test.
        config.Save("https://example.com", "token");
        vm.IsConnected = true;

        var station = new StationWithFeedsDto
        {
            Station = new Station { Id = "s2", Name = "New Station" },
        };
        vm.Stations.Add(station);
        return (vm, handler, station);
    }

    [Test]
    public async Task OpenStation_NavigatesToThePlayer()
    {
        var vm = CreateViewModel();
        var station = new StationWithFeedsDto { Station = new Station { Id = "s1", Name = "News" } };
        vm.Stations.Add(station);

        vm.OpenStationCommand.Execute(station);
        await vm.StationOpenTask;

        Assert.That(vm.SelectedStation, Is.SameAs(station));
        Assert.That(vm.IsNowPlayingOpen, Is.True);
    }

    [Test]
    public async Task OpenStation_RequestsContentBeforeZoomingIn()
    {
        var (vm, handler, station) = CreateGatedViewModel();
        vm.StationOpenDelay = TimeSpan.FromSeconds(5);

        vm.OpenStationCommand.Execute(station);

        Assert.That(vm.IsNowPlayingOpen, Is.False,
            "the station grid must stay up while the new content is requested");
        Assert.That(vm.IsStationLoading, Is.False,
            "no loader is shown before the player is opened");

        handler.Release();
        await vm.StationOpenTask;

        Assert.That(vm.IsNowPlayingOpen, Is.True);
        Assert.That(vm.IsStationLoading, Is.False);
        Assert.That(vm.CurrentEpisodeTitle, Is.EqualTo("New episode"));
        Assert.That(vm.PlaylistItems, Has.Count.EqualTo(1),
            "the playlist must be populated before the zoom starts");
    }

    [Test]
    public async Task OpenStation_OpensThePlayerPromptlyByDefault()
    {
        var (vm, handler, station) = CreateGatedViewModel();

        vm.OpenStationCommand.Execute(station);
        await Task.Delay(400);

        Assert.That(vm.IsNowPlayingOpen, Is.True,
            "a tapped station must not leave the user staring at the grid");
        Assert.That(vm.IsStationLoading, Is.True,
            "the player shows a loader while the station content is still loading");

        handler.Release();
        await vm.StationOpenTask;
        Assert.That(vm.IsStationLoading, Is.False);
    }

    [Test]
    public async Task OpenStation_ZoomsInWithALoaderAfterTheTimeout()
    {
        var (vm, handler, station) = CreateGatedViewModel();
        vm.StationOpenDelay = TimeSpan.FromMilliseconds(50);

        vm.OpenStationCommand.Execute(station);
        await Task.Delay(200);

        Assert.That(vm.IsNowPlayingOpen, Is.True,
            "a slow station still opens the player after the timeout");
        Assert.That(vm.IsStationLoading, Is.True,
            "the player shows a loader until the content arrives");

        handler.Release();
        await vm.StationOpenTask;

        Assert.That(vm.IsStationLoading, Is.False);
        Assert.That(vm.CurrentEpisodeTitle, Is.EqualTo("New episode"));
    }

    [Test]
    public async Task ShowStations_CancelsAPendingZoomIn()
    {
        var (vm, handler, station) = CreateGatedViewModel();
        vm.StationOpenDelay = TimeSpan.FromMilliseconds(50);

        vm.OpenStationCommand.Execute(station);
        vm.ShowStationsCommand.Execute(null);
        handler.Release();
        await vm.StationOpenTask;

        Assert.That(vm.IsNowPlayingOpen, Is.False);
        Assert.That(vm.IsStationLoading, Is.False);
    }

    [Test]
    public async Task OpenStation_PlaybackReadyCompletesBeforeThePlaylist()
    {
        var (vm, handler, station) = CreateStagedViewModel();

        vm.OpenStationCommand.Execute(station);
        Assert.That(vm.StationPlaybackReadyTask.IsCompleted, Is.False,
            "playback must wait for the new station's status");
        await Task.Delay(100);

        Assert.That(vm.StationPlaybackReadyTask.IsCompleted, Is.False,
            "playback must wait for the new station's status, not start prematurely");
        Assert.That(handler.RequestPaths, Has.Some.Contains("/stream/playlist"),
            "the playlist request must already be in flight alongside status");

        handler.ReleaseStatus();
        await vm.StationPlaybackReadyTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(vm.CurrentEpisodeTitle, Is.EqualTo("New episode"),
            "status has been applied when playback is declared ready");
        Assert.That(vm.StationOpenTask.IsCompleted, Is.False,
            "the UI still waits for the playlist before zooming in");

        handler.ReleasePlaylist();
        await vm.StationOpenTask;

        Assert.That(vm.PlaylistItems, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task StationSelection_IsNotRevertedByAnInFlightStatusPoll()
    {
        var handler = new PollRaceHttpHandler();
        var config = new ConfigService(_tempConfigPath);
        var vm = new MainViewModel(
            config,
            new OhrbobClient(config, handler),
            null,
            new TestAudioPlayer(),
            action => action(),
            offlineState: new OfflineStateService(_tempOfflinePath));
        config.Save("https://example.com", "token");
        vm.IsConnected = true;

        var first = new StationWithFeedsDto { Station = new Station { Id = "s1", Name = "First" } };
        var second = new StationWithFeedsDto { Station = new Station { Id = "s2", Name = "Second" } };
        vm.Stations.Add(first);
        vm.Stations.Add(second);

        // A periodic poll starts against the old station and stays in flight.
        var poll = vm.PollStatusAsync();
        await handler.PollStarted.WaitAsync(TimeSpan.FromSeconds(2));

        // The user selects another station while that poll is still in flight.
        vm.OpenStationCommand.Execute(second);
        await vm.StationOpenTask;
        Assert.That(vm.SelectedStation, Is.SameAs(second));

        // The stale poll response now arrives.
        handler.ReleasePoll();
        await poll;

        Assert.That(vm.SelectedStation, Is.SameAs(second),
            "a status response requested before the switch must not revert it");
        Assert.That(vm.CurrentEpisodeTitle, Is.EqualTo("New episode"));
    }

    private sealed class PollRaceHttpHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _pollStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _pollGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string _activeStationId = "s1";
        private int _statusCalls;

        public Task PollStarted => _pollStarted.Task;
        public void ReleasePoll() => _pollGate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";

            if (path.StartsWith("/stream/play-station/"))
            {
                _activeStationId = path.Split('/').Last();
                return Json("{}");
            }

            if (path.EndsWith("/stream/status"))
            {
                // Capture the station at request time so the first (polled)
                // response stays stale even after the switch completes.
                var stationAtRequest = _activeStationId;
                if (Interlocked.Increment(ref _statusCalls) == 1)
                {
                    _pollStarted.TrySetResult();
                    await _pollGate.Task.WaitAsync(cancellationToken);
                }

                return Json(stationAtRequest == "s2"
                    ? "{\"stationId\":\"s2\",\"current\":{\"episodeId\":\"e2\",\"title\":\"New episode\"}}"
                    : "{\"stationId\":\"s1\",\"current\":{\"episodeId\":\"e1\",\"title\":\"Old episode\"}}");
            }

            if (path.EndsWith("/stream/playlist"))
                return Json("{\"stationId\":\"s2\",\"episodes\":[]}");

            return Json("{}");
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    [Test]
    public async Task StationSelection_IsNotDiscardedByAConcurrentPlaylistRefresh()
    {
        var config = new ConfigService(_tempConfigPath);
        var handler = new StatusGatedHttpHandler();
        var vm = new MainViewModel(
            config,
            new OhrbobClient(config, handler),
            null,
            new TestAudioPlayer(),
            action => action(),
            offlineState: new OfflineStateService(_tempOfflinePath));
        config.Save("https://example.com", "token");
        vm.IsConnected = true;

        var station = new StationWithFeedsDto
        {
            Station = new Station { Id = "s2", Name = "Second" },
        };
        vm.Stations.Add(station);

        vm.OpenStationCommand.Execute(station);
        await handler.StatusStarted.WaitAsync(TimeSpan.FromSeconds(2));

        // A concurrent playlist refresh (for example the initial sync) used to
        // bump the same generation that guarded the station switch status.
        await InvokeRefreshPlaylistAsync(vm);

        handler.ReleaseStatus();
        await vm.StationPlaybackReadyTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(vm.CurrentEpisodeTitle, Is.EqualTo("New episode"),
            "a concurrent playlist refresh must not discard the switch status");
        await vm.StationOpenTask;
    }

    private static async Task InvokeRefreshPlaylistAsync(MainViewModel vm)
    {
        var method = typeof(MainViewModel).GetMethod(
            "RefreshPlaylistAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)method.Invoke(vm, null)!;
    }

    private sealed class StatusGatedHttpHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _statusStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _statusGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StatusStarted => _statusStarted.Task;
        public void ReleaseStatus() => _statusGate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";

            if (path.EndsWith("/stream/status"))
            {
                _statusStarted.TrySetResult();
                await _statusGate.Task.WaitAsync(cancellationToken);
                return Json("{\"stationId\":\"s2\",\"current\":{\"episodeId\":\"e2\",\"title\":\"New episode\"}}");
            }

            if (path.EndsWith("/stream/playlist"))
                return Json("{\"stationId\":\"s2\",\"episodes\":[]}");

            return Json("{}");
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private sealed class GatedHttpHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await _gate.Task.WaitAsync(cancellationToken);

            var path = request.RequestUri?.AbsolutePath ?? "";
            var body = path.EndsWith("/stream/playlist")
                ? "{\"stationId\":\"s2\",\"episodes\":[{\"episodeId\":\"e2\",\"title\":\"New episode\",\"isCurrent\":true}]}"
                : path.EndsWith("/stream/status")
                    ? "{\"stationId\":\"s2\",\"current\":{\"episodeId\":\"e2\",\"title\":\"New episode\",\"description\":\"Fresh content\"}}"
                    : "{}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class StagedHttpHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _statusGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _playlistGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> RequestPaths { get; } = new();

        public void ReleaseStatus() => _statusGate.TrySetResult();
        public void ReleasePlaylist() => _playlistGate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            RequestPaths.Add(path);

            if (path.EndsWith("/stream/status"))
                await _statusGate.Task.WaitAsync(cancellationToken);
            else if (path.EndsWith("/stream/playlist"))
                await _playlistGate.Task.WaitAsync(cancellationToken);

            var body = path.EndsWith("/stream/playlist")
                ? "{\"stationId\":\"s2\",\"episodes\":[{\"episodeId\":\"e2\",\"title\":\"New episode\",\"isCurrent\":true}]}"
                : path.EndsWith("/stream/status")
                    ? "{\"stationId\":\"s2\",\"current\":{\"episodeId\":\"e2\",\"title\":\"New episode\",\"description\":\"Fresh content\"}}"
                    : "{}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
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

    [Test]
    public async Task ToggleEpisodeCompletion_SyncsBothDirectionsWithTheServer()
    {
        var requests = new List<HttpRequestMessage>();
        var config = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(config, new RecordingHandler(requests));
        var vm = new MainViewModel(config, client, null, new TestAudioPlayer(), a => a(),
            offlineState: new OfflineStateService(_tempOfflinePath));
        config.Save("https://example.com", "token");
        vm.IsConnected = true;
        var item = new PlaylistItem { EpisodeId = "e1", Title = "Episode One" };
        vm.PlaylistItems.Add(item);
        vm.OpenEpisodeActionsCommand.Execute(item);

        await vm.ToggleEpisodeCompletionCommand.ExecuteAsync(null);
        await vm.ToggleEpisodeCompletionCommand.ExecuteAsync(null);

        Assert.That(item.IsCompleted, Is.False);
        Assert.That(requests.Count, Is.EqualTo(2));
        Assert.That(requests[0].RequestUri!.AbsolutePath, Is.EqualTo("/stream/episodes/e1/complete"));
        Assert.That(requests[1].RequestUri!.AbsolutePath, Is.EqualTo("/stream/episodes/e1/uncomplete"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests;

        public RecordingHandler(List<HttpRequestMessage> requests) => _requests = requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
