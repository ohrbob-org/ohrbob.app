using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using Ohrbob.App.Models;
using Ohrbob.App.Services;
using Ohrbob.App.ViewModels;
using Ohrbob.App.Views;

namespace Ohrbob.App.Tests.ViewModels;

[TestFixture]
public class PlaybackEndFlowTests
{
    private string _tempConfigPath = null!;

    [SetUp]
    public void SetUp() =>
        _tempConfigPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempConfigPath))
        {
            try { File.Delete(_tempConfigPath); } catch { }
        }
    }

    private class CountingHttpHandler : HttpMessageHandler
    {
        public int NextRequests;
        public int StationRequests;
        public int StatusRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (request.Method == HttpMethod.Post && path.EndsWith("/stream/next"))
            {
                Interlocked.Increment(ref NextRequests);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/stream/stations"))
            {
                Interlocked.Increment(ref StationRequests);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/stream/status"))
            {
                Interlocked.Increment(ref StatusRequests);
            }

            var body = path.EndsWith("/stream/stations") ? "[]" : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private class FailingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }

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

    private class SwitchableHttpHandler : HttpMessageHandler
    {
        public bool Online { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (!Online)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            var body = path.EndsWith("/stream/stations")
                ? "[{\"station\":{\"id\":\"s1\",\"name\":\"Test Station\"},\"feeds\":[]}]"
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private MainViewModel CreateViewModel(HttpMessageHandler handler, out ConfigService config, out TestAudioPlayer player)
    {
        config = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(config, handler);
        player = new TestAudioPlayer();
        return new MainViewModel(config, client, null, player, action => action());
    }

    [Test]
    public async Task FailedInitialization_KeepsSavedCredentials()
    {
        var vm = CreateViewModel(new FailingHttpHandler(), out var config, out _);
        vm.ServerUrlInput = "https://example.com";
        vm.TokenInput = "secret";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.That(vm.IsConnected, Is.False);
        Assert.That(vm.ConnectionErrorMessage, Is.Not.Null.And.Not.Empty);
        Assert.That(config.ServerUrl, Is.EqualTo("https://example.com"));
        Assert.That(config.StreamToken, Is.EqualTo("secret"));
    }

    [Test]
    public async Task RetryConnection_KeepsPlayerVisibleWhenServerIsStillUnreachable()
    {
        var vm = CreateViewModel(new FailingHttpHandler(), out var config, out _);
        config.Save("https://example.com", "secret");
        vm.IsConnected = true;

        await vm.RetryConnectionAsync();

        Assert.That(vm.IsConnected, Is.True);
        Assert.That(vm.ConnectionErrorMessage, Is.Not.Null.And.Not.Empty);
        Assert.That(config.ServerUrl, Is.EqualTo("https://example.com"));
        Assert.That(config.StreamToken, Is.EqualTo("secret"));
    }

    [Test]
    public async Task RetryConnection_LoadsStationsOnceTheServerIsBack()
    {
        var handler = new SwitchableHttpHandler();
        var vm = CreateViewModel(handler, out var config, out _);
        config.Save("https://example.com", "secret");
        vm.IsConnected = true;

        await vm.RetryConnectionAsync();
        Assert.That(vm.Stations, Is.Empty);

        handler.Online = true;
        await vm.RetryConnectionAsync();

        Assert.That(vm.Stations.Count, Is.EqualTo(1));
        Assert.That(vm.ConnectionErrorMessage, Is.Null.Or.Empty);
    }

    [Test]
    public async Task PlaybackEnd_SkipsOnceAndDoesNotRetryOnEveryPositionTick()
    {        var handler = new CountingHttpHandler();
        var vm = CreateViewModel(handler, out var config, out var player);
        config.Save("https://example.com", "token");

        vm.CurrentEpisodeStreamUri = "https://example.com/stream.mp3";
        vm.IsPlaying = true;

        player.RaiseEnded();

        // The skip request runs asynchronously once.
        for (var i = 0; i < 50 && handler.NextRequests == 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.That(handler.NextRequests, Is.EqualTo(1));

        // At the end of a stream the player keeps reporting the final position.
        // Those ticks used to trigger a new skip request every 500 ms forever.
        vm.DurationSeconds = 600;
        vm.IsPlaying = true;
        for (var i = 0; i < 10; i++)
        {
            player.RaisePosition(600);
        }

        await Task.Delay(200);

        Assert.That(handler.NextRequests, Is.EqualTo(1));
    }

    [Test]
    public async Task RefreshAll_ReloadsStationsAndStatusThenShowsIndicator()
    {
        var handler = new CountingHttpHandler();
        var vm = CreateViewModel(handler, out var config, out _);
        config.Save("https://example.com", "token");

        await vm.RefreshAllCommand.ExecuteAsync(null);

        Assert.That(handler.StationRequests, Is.EqualTo(1));
        Assert.That(handler.StatusRequests, Is.EqualTo(1));
        Assert.That(vm.IsRefreshIndicatorVisible, Is.True);
    }

    [Test]
    public async Task RefreshAll_IgnoresTriggersWithinTenSeconds()
    {
        var handler = new CountingHttpHandler();
        var vm = CreateViewModel(handler, out var config, out _);
        config.Save("https://example.com", "token");

        await vm.RefreshAllAsync();
        await vm.RefreshAllAsync();

        Assert.That(handler.StationRequests, Is.EqualTo(1));
        Assert.That(handler.StatusRequests, Is.EqualTo(1));
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

    [AvaloniaTest]
    public void Slider_DoesNotWriteAClampedPositionBack()
    {
        var vm = CreateViewModel(new CountingHttpHandler(), out _, out _);
        var widget = new AudioWidget { DataContext = vm };
        var window = new Window { Width = 1200, Height = 140, Content = widget };
        window.Show();

        vm.DurationSeconds = 60;
        // A playhead past the known duration must not be pulled back by the
        // slider's Maximum, which previously fed the clamped value into Seek().
        vm.PositionSeconds = 90;

        Assert.That(vm.PositionSeconds, Is.EqualTo(90));
        window.Close();
    }
}
