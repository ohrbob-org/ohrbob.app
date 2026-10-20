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

    [SetUp]
    public void SetUp() =>
        _tempConfigPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json");

    [TearDown]
    public void TearDown()
    {
        if (System.IO.File.Exists(_tempConfigPath))
        {
            try { System.IO.File.Delete(_tempConfigPath); } catch { /* ignore */ }
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

        public void Dispose() { }

        public void RaisePlay() => PlayRequested?.Invoke(this, EventArgs.Empty);
        public void RaisePause() => PauseRequested?.Invoke(this, EventArgs.Empty);
        public void RaiseToggle() => TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);
        public void RaiseNext() => NextRequested?.Invoke(this, EventArgs.Empty);
        public void RaiseSeekTo(int seconds) => SeekToRequested?.Invoke(this, seconds);
        public void RaiseSeekBy(int seconds) => SeekByRequested?.Invoke(this, seconds);
    }

    private (MainViewModel Vm, FakeMediaSessionService Service, MediaSessionCoordinator Coordinator, FakeHttpMessageHandler Handler, ConfigService Config) Create()
    {
        var handler = new FakeHttpMessageHandler();
        var config = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(config, handler);
        var vm = new MainViewModel(config, client);
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
