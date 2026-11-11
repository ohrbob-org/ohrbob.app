using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Ohrbob.App.Models;
using Ohrbob.App.Services;
using Ohrbob.App.ViewModels;
using Ohrbob.App.Tests.Services;

namespace Ohrbob.App.Tests.ViewModels;

[TestFixture]
public class MainViewModelTests
{
    private string _tempConfigPath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempConfigPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempConfigPath))
        {
            try
            {
                File.Delete(_tempConfigPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            var path = request.RequestUri?.AbsolutePath ?? "";
            
            if (path.EndsWith("/stream/stations"))
            {
                response.Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json");
            }
            else if (path.EndsWith("/stream/status"))
            {
                response.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            }
            else
            {
                response.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            }
            
            return Task.FromResult(response);
        }
    }

    [Test]
    public async Task Connect_WithValidInputs_SavesAndSetsConnected()
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var vm = new MainViewModel(configService, client);

        vm.ServerUrlInput = "https://example.com/ohrbob";
        vm.TokenInput = "my-secret-token";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.That(vm.IsConnected, Is.True);
        Assert.That(configService.ServerUrl, Is.EqualTo("https://example.com/ohrbob"));
        Assert.That(configService.StreamToken, Is.EqualTo("my-secret-token"));
    }

    [Test]
    public async Task Connect_WithDeepLink_ParsesAndSetsConnected()
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var vm = new MainViewModel(configService, client);

        vm.DeepLinkInput = "ohrbob://connect?server=https://mytestserver.com&token=secret123";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.That(vm.ServerUrlInput, Is.EqualTo("https://mytestserver.com"));
        Assert.That(vm.TokenInput, Is.EqualTo("secret123"));
        Assert.That(vm.IsConnected, Is.True);
        Assert.That(configService.ServerUrl, Is.EqualTo("https://mytestserver.com"));
        Assert.That(configService.StreamToken, Is.EqualTo("secret123"));
    }

    [Test]
    public async Task Disconnect_ResetsViewModelStates()
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var vm = new MainViewModel(configService, client);

        vm.ServerUrlInput = "https://example.com/ohrbob";
        vm.TokenInput = "my-secret-token";

        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.That(vm.IsConnected, Is.True);

        vm.IsPlaying = true;
        vm.CurrentEpisodeTitle = "Some Playing Track";

        vm.DisconnectCommand.Execute(null);

        Assert.That(vm.IsConnected, Is.False);
        Assert.That(vm.IsPlaying, Is.False);
        Assert.That(vm.CurrentEpisodeTitle, Is.EqualTo("No Track Selected"));
        Assert.That(vm.CurrentEpisodeDescription, Is.EqualTo("Please choose a station to start listening."));
        Assert.That(vm.CurrentEpisodeCoverUrl, Is.Null);
        Assert.That(vm.CurrentEpisodeStreamUri, Is.Null);
    }

    [Test]
    public void Volume_WhenChanged_AppliesToAudioPlayer()
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var player = new TestAudioPlayer();
        var vm = new MainViewModel(configService, client, null, player);

        vm.Volume = 40;

        Assert.That(player.Volume, Is.EqualTo(0.4f).Within(0.001f));
    }

    private class PlaylistHttpHandler : HttpMessageHandler
    {
        public string StationJson { get; set; } =
            "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\"},\"feeds\":[]}]";
        public string PlaylistJson { get; set; } =
            "{\"stationId\":\"s1\",\"hostPaceEnabled\":false,\"isAging\":false,\"episodes\":[" +
            "{\"episodeId\":\"e1\",\"title\":\"Current\",\"streamUri\":\"https://audio/1\",\"isCurrent\":true}," +
            "{\"episodeId\":\"e2\",\"title\":\"Next\",\"streamUri\":\"https://audio/2\",\"isCurrent\":false}]}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            var body = path.EndsWith("/stream/stations") ? StationJson
                : path.EndsWith("/stream/playlist") ? PlaylistJson
                : path.EndsWith("/stream/status")
                    ? "{\"stationId\":\"s1\",\"current\":{\"episodeId\":\"e1\",\"title\":\"Current\",\"streamUri\":\"https://audio/1\"}}"
                    : "{}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private class GatedAudioHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _audioStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AudioRequests;

        public Task AudioStarted => _audioStarted.Task;

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.StartsWith("/audio/"))
            {
                Interlocked.Increment(ref AudioRequests);
                _audioStarted.TrySetResult();
                await _release.Task.WaitAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
                };
            }

            var body = path.EndsWith("/stream/stations")
                ? "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\"},\"feeds\":[]}]"
                : path.EndsWith("/stream/playlist")
                    ? "{\"stationId\":\"s1\",\"hostPaceEnabled\":false,\"isAging\":false,\"episodes\":[" +
                      "{\"episodeId\":\"e1\",\"title\":\"Current\",\"streamUri\":\"https://example.com/audio/1.mp3\",\"isCurrent\":true}]}"
                    : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
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
                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            var body = path.EndsWith("/stream/stations")
                ? "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\"},\"feeds\":[]}]"
                : "{}";
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
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

    [Test]
    public void TogglePlay_WhenStreamUriExists_TogglesIsPlaying()
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var vm = new MainViewModel(configService, client, null, new TestAudioPlayer());
        vm.CurrentEpisodeStreamUri = null;
        vm.IsPlaying = false;
        vm.TogglePlayCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.False);

        // Loading a stream does not change the playback intent on its own;
        // pressing play is what starts it.
        vm.CurrentEpisodeStreamUri = "http://stream.mp3";
        Assert.That(vm.IsPlaying, Is.False);

        vm.TogglePlayCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.True);

        vm.TogglePlayCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.False);

        vm.TogglePlayCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.True);
    }

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

    [Test]
    public async Task TriggerDownloadSync_ConcurrentTriggersDoNotRestartRunningSync()
    {
        var handler = new GatedAudioHandler();
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://example.com", "token");
        config.SetBufferMinutes("s1", 10);
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var downloads = new EpisodeDownloadService(root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), downloads);

        await vm.RetryConnectionAsync();
        await handler.AudioStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var triggers = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => vm.TriggerDownloadSync()))
            .ToArray();
        await Task.WhenAll(triggers);

        handler.Release();
        await vm.WaitForDownloadsAsync();

        Assert.That(handler.AudioRequests, Is.EqualTo(1),
            "concurrent triggers must not cancel and restart the running sync");
    }
}
