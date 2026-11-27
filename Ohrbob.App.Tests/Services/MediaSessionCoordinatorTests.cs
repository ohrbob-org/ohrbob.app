using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using Ohrbob.App.Models;
using Ohrbob.App.Services;
using Ohrbob.App.Services.Media;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Tests.Services;

/// <summary>
/// The coordinator is the shared, platform-agnostic half of the OS media integration
/// (issue #4): it mirrors the view model onto an <see cref="IMediaSessionService"/> and
/// routes the service's commands back into the view model.
/// </summary>
[TestFixture]
public class MediaSessionCoordinatorTests
{
    private string _tempConfigPath = null!;
    private string _tempOfflinePath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempConfigPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json");
        _tempOfflinePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".offline.json");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var path in new[] { _tempConfigPath, _tempOfflinePath })
        {
            try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { /* ignore */ }
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri?.AbsolutePath ?? "");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });
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
                    ? "{\"stationId\":\"s2\",\"current\":{\"episodeId\":\"e2\",\"title\":\"New episode\"}}"
                    : "{}";

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class FakeMediaSessionService : IMediaSessionService
    {
        public MediaSessionInfo? Last { get; private set; }
        public int UpdateCount { get; private set; }

        public void Update(MediaSessionInfo info)
        {
            Last = info;
            UpdateCount++;
        }

        public event EventHandler? PlayRequested;
        public event EventHandler? PauseRequested;
        public event EventHandler? TogglePlayPauseRequested;
        public event EventHandler? NextRequested;
        public event EventHandler? PreviousRequested;
        public event EventHandler<int>? SeekToRequested;
        public event EventHandler<int>? SeekByRequested;
        public event EventHandler<string?>? PlayFromMediaIdRequested;

        public void Dispose() { }

        public void RaisePlay() => PlayRequested?.Invoke(this, EventArgs.Empty);
        public void RaisePause() => PauseRequested?.Invoke(this, EventArgs.Empty);
        public void RaiseToggle() => TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);
        public void RaiseNext() => NextRequested?.Invoke(this, EventArgs.Empty);
        public void RaisePrevious() => PreviousRequested?.Invoke(this, EventArgs.Empty);
        public void RaiseSeekTo(int seconds) => SeekToRequested?.Invoke(this, seconds);
        public void RaiseSeekBy(int seconds) => SeekByRequested?.Invoke(this, seconds);
        public void RaisePlayFromMediaId(string mediaId) =>
            PlayFromMediaIdRequested?.Invoke(this, mediaId);
    }

    private (MainViewModel Vm, FakeMediaSessionService Service, MediaSessionCoordinator Coordinator, FakeHttpMessageHandler Handler, ConfigService Config) Create()
    {
        var handler = new FakeHttpMessageHandler();
        var config = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(config, handler);
        var vm = new MainViewModel(config, client, offlineState: new OfflineStateService(_tempOfflinePath));
        var service = new FakeMediaSessionService();
        var coordinator = new MediaSessionCoordinator(vm, service, action => action());
        return (vm, service, coordinator, handler, config);
    }

    private (MainViewModel Vm, FakeMediaSessionService Service, MediaSessionCoordinator Coordinator, StagedHttpHandler Handler, ConfigService Config) CreateWithHandler()
    {
        var handler = new StagedHttpHandler();
        var config = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(config, handler);
        var vm = new MainViewModel(config, client, offlineState: new OfflineStateService(_tempOfflinePath));
        var service = new FakeMediaSessionService();
        var coordinator = new MediaSessionCoordinator(vm, service, action => action());
        return (vm, service, coordinator, handler, config);
    }

    [AvaloniaTest]
    public void Constructor_PublishesInitialPlaybackState()
    {
        var (vm, service, _, _, _) = Create();
        vm.CurrentEpisodeTitle = "Episode One";
        vm.IsPlaying = true;

        Assert.That(service.Last, Is.Not.Null, "the coordinator should publish the initial state on construction");
        Assert.That(service.Last!.Title, Is.EqualTo("Episode One"));
        Assert.That(service.Last.IsPlaying, Is.True);
    }

    [AvaloniaTest]
    public void WhenEpisodeMetadataChanges_ItIsRepublished()
    {
        var (vm, service, _, _, _) = Create();
        var before = service.UpdateCount;

        vm.CurrentEpisodeTitle = "The Deep Sea";
        vm.CurrentEpisodeCoverUrl = "https://example.com/cover.jpg";
        // Id is left null so selecting the station does not kick off a network switch.
        vm.SelectedStation = new StationWithFeedsDto { Station = new Station { Name = "Gaming" } };

        Assert.That(service.UpdateCount, Is.GreaterThan(before));
        Assert.That(service.Last!.Title, Is.EqualTo("The Deep Sea"));
        Assert.That(service.Last.CoverUrl, Is.EqualTo("https://example.com/cover.jpg"));
        Assert.That(service.Last.Artist, Is.EqualTo("Gaming"));
    }

    [AvaloniaTest]
    public void WhenPlayingStateChanges_ItIsRepublished()
    {
        var (vm, service, _, _, _) = Create();

        vm.IsPlaying = true;

        Assert.That(service.Last!.IsPlaying, Is.True);
    }

    [AvaloniaTest]
    public void WhenBufferingChanges_ItIsRepublished()
    {
        var (vm, service, _, _, _) = Create();

        vm.IsBuffering = true;

        Assert.That(service.Last!.IsBuffering, Is.True,
            "the media session must expose buffering to the OS controls");

        vm.IsBuffering = false;

        Assert.That(service.Last!.IsBuffering, Is.False);
    }

    [AvaloniaTest]
    public void WhenStationLoadingChanges_ItIsReportedAsBuffering()
    {
        var (vm, service, _, _, _) = Create();

        vm.IsStationLoading = true;

        Assert.That(service.Last!.IsBuffering, Is.True,
            "a station switch is buffering from the head unit's point of view");

        vm.IsStationLoading = false;

        Assert.That(service.Last!.IsBuffering, Is.False);
    }

    [AvaloniaTest]
    public async Task PlayFromMediaId_ReportsBufferingAndStartsPlaybackBeforeThePlaylist()
    {
        var (vm, service, _, handler, config) = CreateWithHandler();
        config.ServerUrl = "https://example.com";
        config.StreamToken = "token";
        vm.IsConnected = true;

        var target = new StationWithFeedsDto
        {
            Station = new Station { Id = "s2", Name = "Morning Briefing" },
        };
        vm.Stations.Add(target);
        vm.IsPlaying = false;

        service.RaisePlayFromMediaId("s2");

        Assert.That(service.Last!.IsBuffering, Is.True,
            "the head unit should show buffering while the station switch is in flight");

        handler.ReleaseStatus();
        await WaitUntilAsync(() => vm.IsPlaying);

        Assert.That(vm.SelectedStation, Is.SameAs(target));
        Assert.That(handler.RequestPaths, Has.Some.Contains("/stream/playlist"),
            "the playlist request runs alongside status");
        Assert.That(vm.StationOpenTask.IsCompleted, Is.False,
            "playback must not wait for the playlist refresh");

        handler.ReleasePlaylist();
        await vm.StationOpenTask;
    }

    [AvaloniaTest]
    public void PlayRequested_StartsPlayback()
    {
        var (vm, service, _, _, _) = Create();
        vm.IsPlaying = false;

        service.RaisePlay();

        Assert.That(vm.IsPlaying, Is.True);
    }

    [AvaloniaTest]
    public void PauseRequested_StopsPlayback()
    {
        var (vm, service, _, _, _) = Create();
        vm.IsPlaying = true;

        service.RaisePause();

        Assert.That(vm.IsPlaying, Is.False);
    }

    [AvaloniaTest]
    public void TogglePlayPauseRequested_TogglesPlayback()
    {
        var (vm, service, _, _, _) = Create();
        vm.CurrentEpisodeStreamUri = "https://example.com/stream.mp3";
        vm.IsPlaying = false;

        service.RaiseToggle();

        Assert.That(vm.IsPlaying, Is.True);
    }

    [AvaloniaTest]
    public async Task NextRequested_AsksTheServerForTheNextEpisode()
    {
        var (vm, service, _, handler, config) = Create();
        config.ServerUrl = "https://example.com";
        config.StreamToken = "token";

        service.RaiseNext();
        var execution = vm.SkipToNextCommand.ExecutionTask;
        if (execution != null)
        {
            await execution;
        }

        Assert.That(handler.RequestPaths, Has.Some.Contains("/next"),
            "the next command should reach the server");
    }

    [AvaloniaTest]
    public async Task PreviousRequested_AsksTheServerForThePreviousEpisode()
    {
        var (vm, service, _, handler, config) = Create();
        config.ServerUrl = "https://example.com";
        config.StreamToken = "token";

        service.RaisePrevious();
        var execution = vm.SkipToPreviousCommand.ExecutionTask;
        if (execution != null)
        {
            await execution;
        }

        Assert.That(handler.RequestPaths, Has.Some.Contains("/prev"),
            "the previous command should reach the server");
    }

    [AvaloniaTest]
    public async Task PlayFromMediaId_SwitchesStationAndStartsPlayback()
    {
        var (vm, service, _, handler, config) = Create();
        config.ServerUrl = "https://example.com";
        config.StreamToken = "token";

        var first = new StationWithFeedsDto { Station = new Station { Id = "s1", Name = "First" } };
        var target = new StationWithFeedsDto { Station = new Station { Id = "s2", Name = "Morning Briefing" } };
        vm.Stations.Add(first);
        vm.Stations.Add(target);
        vm.SelectedStation = first;
        vm.IsPlaying = false;

        service.RaisePlayFromMediaId("s2");
        await vm.StationOpenTask;
        await WaitUntilAsync(() => vm.IsPlaying);

        Assert.That(vm.SelectedStation, Is.SameAs(target),
            "the tapped station becomes the active station");
        Assert.That(handler.RequestPaths, Has.Some.Contains("/stream/play-station/s2"),
            "the station switch must reach the server");
        Assert.That(vm.IsPlaying, Is.True,
            "playback must start after the new station is ready");
    }

    [AvaloniaTest]
    public async Task PlayFromMediaId_MatchesIdsIgnoringCaseAndWhitespace()
    {
        var (vm, service, _, _, config) = Create();
        config.ServerUrl = "https://example.com";
        config.StreamToken = "token";
        vm.IsConnected = true;

        var target = new StationWithFeedsDto
        {
            Station = new Station { Id = "s2", Name = "Morning Briefing" },
        };
        vm.Stations.Add(target);

        service.RaisePlayFromMediaId(" S2 ");
        await WaitUntilAsync(() => vm.IsPlaying);

        Assert.That(vm.SelectedStation, Is.SameAs(target));
    }

    [AvaloniaTest]
    public async Task PlayFromMediaId_MatchesNamesWithDifferentUnicodeNormalization()
    {
        var (vm, service, _, _, config) = Create();
        config.ServerUrl = "https://example.com";
        config.StreamToken = "token";
        vm.IsConnected = true;

        var target = new StationWithFeedsDto
        {
            Station = new Station { Id = "s2", Name = "Die Maus zum H\u00f6ren" },
        };
        vm.Stations.Add(target);

        // o + combining diaeresis, as some media browsers normalize names.
        service.RaisePlayFromMediaId("Die Maus zum Ho\u0308ren");
        await WaitUntilAsync(() => vm.IsPlaying);

        Assert.That(vm.SelectedStation, Is.SameAs(target));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.Fail("condition was not reached in time");
    }

    [AvaloniaTest]
    public void SeekToRequested_SetsThePosition()
    {
        var (vm, service, _, _, _) = Create();

        service.RaiseSeekTo(42);

        Assert.That(vm.PositionSeconds, Is.EqualTo(42));
    }

    [AvaloniaTest]
    public void SeekByRequested_MovesRelativeToThePosition()
    {
        var (vm, service, _, _, _) = Create();
        vm.PositionSeconds = 100;

        service.RaiseSeekBy(15);

        Assert.That(vm.PositionSeconds, Is.EqualTo(115));
    }

    [AvaloniaTest]
    public void SeekByRequested_DoesNotGoBelowZero()
    {
        var (vm, service, _, _, _) = Create();
        vm.PositionSeconds = 5;

        service.RaiseSeekBy(-30);

        Assert.That(vm.PositionSeconds, Is.EqualTo(0));
    }
}
