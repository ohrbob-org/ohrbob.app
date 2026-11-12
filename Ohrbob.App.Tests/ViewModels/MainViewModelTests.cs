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
using Ohrbob.App.Tests.Services;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Tests.ViewModels;

[TestFixture]
public class MainViewModelTests
{
    private string _tempConfigPath = null!;
    private string _tempOfflinePath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempConfigPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
        _tempOfflinePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "offline.json");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var path in new[] { _tempConfigPath, _tempOfflinePath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                File.Delete(path);
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

    private class FailingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }

    private sealed class FakeQrScanner : IQrScanner
    {
        public bool IsAvailable { get; set; } = true;
        public string? Result { get; set; }

        public Task<string?> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    [Test]
    public async Task ScanQrCode_ValidConnectionLink_AppliesConnection()
    {
        var config = new ConfigService(_tempConfigPath);
        var scanner = new FakeQrScanner
        {
            Result = "ohrbob://connect?server=https://example.com&token=secret123"
        };
        var vm = new MainViewModel(config, new OhrbobClient(config, new FakeHttpMessageHandler()),
            null, new TestAudioPlayer(), a => a(), qrScanner: scanner);

        await vm.ScanQrCodeCommand.ExecuteAsync(null);

        Assert.That(vm.IsConnected, Is.True);
        Assert.That(config.ServerUrl, Is.EqualTo("https://example.com"));
        Assert.That(config.StreamToken, Is.EqualTo("secret123"));
    }

    [Test]
    public async Task ScanQrCode_InvalidCode_ShowsError()
    {
        var config = new ConfigService(_tempConfigPath);
        var scanner = new FakeQrScanner { Result = "https://example.com/not-a-connection" };
        var vm = new MainViewModel(config, new OhrbobClient(config, new FakeHttpMessageHandler()),
            null, new TestAudioPlayer(), a => a(), qrScanner: scanner);

        await vm.ScanQrCodeCommand.ExecuteAsync(null);

        Assert.That(vm.IsConnected, Is.False);
        Assert.That(vm.ConnectionErrorMessage, Does.Contain("not an Ohrbob connection code"));
    }

    [Test]
    public void SettingsAndUpNext_AreMutuallyExclusiveOverlays()
    {
        var config = new ConfigService(_tempConfigPath);
        var vm = new MainViewModel(config, new OhrbobClient(config, new FakeHttpMessageHandler()), null, new TestAudioPlayer(), a => a());

        vm.ToggleUpNextCommand.Execute(null);
        Assert.That(vm.IsUpNextOpen, Is.True);

        vm.OpenSettingsCommand.Execute(null);
        Assert.That(vm.IsSettingsOpen, Is.True);
        Assert.That(vm.IsUpNextOpen, Is.False);

        vm.CloseSettingsCommand.Execute(null);
        vm.ToggleUpNextCommand.Execute(null);
        Assert.That(vm.IsUpNextOpen, Is.True);

        vm.ToggleUpNextCommand.Execute(null);
        Assert.That(vm.IsUpNextOpen, Is.False);
    }

    [Test]
    public async Task Status_HtmlInTitleAndDescription_IsStripped()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            var body = path.EndsWith("/stream/stations")
                ? "[{\"station\":{\"id\":\"s1\",\"name\":\"<b>Station</b> One\"},\"feeds\":[]}]"
                : path.EndsWith("/stream/playlist")
                    ? "{\"stationId\":\"s1\",\"episodes\":[]}"
                    : "{\"stationId\":\"s1\",\"current\":{\"episodeId\":\"e1\",\"title\":\"<i>Episode</i> One\",\"description\":\"<div><p>Plain &amp; simple.</p></div>\"}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://example.com", "token");
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a());

        await vm.RetryConnectionAsync();

        Assert.That(vm.SelectedStation!.Station!.Name, Is.EqualTo("Station One"));
        Assert.That(vm.CurrentEpisodeTitle, Is.EqualTo("Episode One"));
        Assert.That(vm.CurrentEpisodeDescription, Is.EqualTo("Plain & simple."));
    }

    [Test]
    public async Task Connect_WithValidInputs_SavesAndSetsConnected()
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var vm = new MainViewModel(configService, client, offlineState: new OfflineStateService(_tempOfflinePath));

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
        var vm = new MainViewModel(configService, client, offlineState: new OfflineStateService(_tempOfflinePath));

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
        var vm = new MainViewModel(configService, client, offlineState: new OfflineStateService(_tempOfflinePath));

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
        var vm = new MainViewModel(configService, client, null, player, offlineState: new OfflineStateService(_tempOfflinePath));

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
        var vm = new MainViewModel(config, new OhrbobClient(config, new PlaylistHttpHandler()), null, new TestAudioPlayer(), a => a(), offlineState: new OfflineStateService(_tempOfflinePath));

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
        var vm = new MainViewModel(config, new OhrbobClient(config, new PlaylistHttpHandler()), null, new TestAudioPlayer(), a => a(), offlineState: new OfflineStateService(_tempOfflinePath));

        await vm.RetryConnectionAsync();
        vm.SelectedBufferOption = vm.BufferOptions.Single(o => o.Minutes == 30);
        await vm.ApplySelectedBufferAsync();

        Assert.That(config.GetBufferMinutes("s1"), Is.EqualTo(30));
        Assert.That(config.GetBufferMinutes("other-station"), Is.EqualTo(0));
    }

    [Test]
    public async Task HostPacedStation_HasExactDownloadBlockedReason()
    {
        var handler = new PlaylistHttpHandler
        {
            StationJson = "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\",\"hostPaceEnabled\":true},\"feeds\":[]}]",
            PlaylistJson = "{\"stationId\":\"s1\",\"hostPaceEnabled\":true,\"isAging\":false,\"episodes\":[]}"
        };
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://example.com", "token");
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), offlineState: new OfflineStateService(_tempOfflinePath));

        await vm.RetryConnectionAsync();

        Assert.That(vm.DownloadBlockedReason, Is.EqualTo("Downloads are off: episodes unlock when the host has listened to them."));
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
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), offlineState: new OfflineStateService(_tempOfflinePath));

        await vm.RetryConnectionAsync();

        Assert.That(vm.IsPlaylistUnavailable, Is.True);
        Assert.That(vm.PlaylistItems, Is.Empty);
    }

    [Test]
    public async Task AgingStation_HasExactDownloadBlockedReason()
    {
        var handler = new PlaylistHttpHandler
        {
            StationJson = "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\",\"isAging\":true},\"feeds\":[]}]"
        };
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://example.com", "token");
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), offlineState: new OfflineStateService(_tempOfflinePath));

        await vm.RetryConnectionAsync();

        Assert.That(vm.DownloadBlockedReason, Is.EqualTo("Downloads are off: this station only rotates the newest episode of each feed."));
        Assert.That(vm.IsDownloadEnabled, Is.False);
    }

    [Test]
    public void TogglePlay_WhenStreamUriExists_TogglesIsPlaying()
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var vm = new MainViewModel(configService, client, null, new TestAudioPlayer(), offlineState: new OfflineStateService(_tempOfflinePath));
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
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), downloads, new OfflineStateService(_tempOfflinePath));

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
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), downloads, new OfflineStateService(_tempOfflinePath));

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
        var vm = new MainViewModel(config, new OhrbobClient(config, new PlaylistHttpHandler()), null, player, a => a(), downloads, new OfflineStateService(_tempOfflinePath));

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
        var vm = new MainViewModel(config, new OhrbobClient(config, new PlaylistHttpHandler()), null, player, a => a(), downloads, new OfflineStateService(_tempOfflinePath));
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
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), downloads, new OfflineStateService(_tempOfflinePath));

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
        Assert.That(vm.PlaylistItems.Single(i => i.EpisodeId == "e2").IsCurrent, Is.True,
            "the advanced-to episode must be marked as the current one");
        Assert.That(vm.PlaylistItems.Single(i => i.EpisodeId == "e1").IsCurrent, Is.False,
            "the finished episode must no longer be marked as current");
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

    private class RecordingHttpHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> RequestBodies { get; } = new();
        public List<string> AudioRequests { get; } = new();
        public bool FailNext { get; set; }
        public string StationsJson { get; set; } = "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\"},\"feeds\":[]}]";
        public string StatusJson { get; set; } = "{\"stationId\":\"s1\",\"current\":{\"episodeId\":\"e1\",\"title\":\"Current\",\"streamUri\":\"https://audio/1\"}}";
        public string PlaylistJson { get; set; } = "{\"stationId\":\"s1\",\"hostPaceEnabled\":false,\"isAging\":false,\"episodes\":[{\"episodeId\":\"e1\",\"title\":\"Current\",\"streamUri\":\"https://audio/1\",\"isCurrent\":true}]}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content != null ? await request.Content.ReadAsStringAsync(ct) : null);
            if (FailNext)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.StartsWith("/audio/"))
            {
                AudioRequests.Add(path);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
                };
            }

            var body = path.EndsWith("/stream/stations") ? StationsJson
                : path.EndsWith("/stream/playlist") ? PlaylistJson
                : path.EndsWith("/stream/status") && request.Method == HttpMethod.Get ? StatusJson
                : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    [Test]
    public async Task Reconnect_AfterOffline_PushesLocalProgressThenRefreshes()
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
                    CurrentEpisodeId = "offline-ep",
                    PositionSeconds = 123,
                    Playlist = new List<PlaylistEpisodeDto>
                    {
                        new() { EpisodeId = "offline-ep", Title = "Offline", StreamUri = "https://audio/offline", IsCurrent = true }
                    }
                }
            }
        });
        var handler = new RecordingHttpHandler { FailNext = true };
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), null, offline);

        // Wait for the constructor's failed initialization to finish.
        await Task.Delay(100);
        Assert.That(vm.IsOffline, Is.True, "snapshot must put the player in offline mode when the server is unreachable");
        Assert.That(vm.CurrentEpisodeId, Is.EqualTo("offline-ep"));

        handler.FailNext = false;
        await vm.RetryConnectionAsync();

        var syncIndex = handler.Requests
            .FindIndex(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/stream/status"));
        Assert.That(syncIndex, Is.GreaterThanOrEqualTo(0),
            $"reconnect must push the local episode and position first; requests: {string.Join(", ", handler.Requests.Select(r => $"{r.Method} {r.RequestUri}"))}");
        var body = handler.RequestBodies[syncIndex] ?? "";
        Assert.That(body, Does.Contain("offline-ep"));
        Assert.That(body, Does.Contain("123"));

        Assert.That(vm.IsOffline, Is.False, "reconnect must clear offline mode");
        Assert.That(vm.CurrentEpisodeId, Is.EqualTo("e1"), "server state must still be refreshed after the push");
    }

    [Test]
    public async Task Reconnect_ReevaluatesDownloadCache()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "s1"));
        File.WriteAllBytes(Path.Combine(root, "s1", "e1.mp3"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(root, "s1", "stale.mp3"), new byte[] { 1 });

        var config = new ConfigService(_tempConfigPath);
        config.Save("https://example.com", "token");
        config.SetBufferMinutes("s1", 10);

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
                    CurrentEpisodeId = "offline-ep",
                    Playlist = new List<PlaylistEpisodeDto>
                    {
                        new() { EpisodeId = "offline-ep", Title = "Offline", StreamUri = "https://audio/offline", IsCurrent = true }
                    }
                }
            }
        });

        var handler = new RecordingHttpHandler
        {
            FailNext = true,
            PlaylistJson = "{\"stationId\":\"s1\",\"hostPaceEnabled\":false,\"isAging\":false,\"episodes\":[" +
                           "{\"episodeId\":\"e1\",\"title\":\"One\",\"streamUri\":\"https://example.com/audio/1.mp3\",\"isCurrent\":true}," +
                           "{\"episodeId\":\"e2\",\"title\":\"Two\",\"streamUri\":\"https://example.com/audio/2.mp3\",\"isCurrent\":false}]}"
        };
        var downloads = new EpisodeDownloadService(root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), downloads, offline);

        await Task.Delay(100);
        Assert.That(vm.IsOffline, Is.True);

        handler.FailNext = false;
        await vm.RetryConnectionAsync();
        await vm.WaitForDownloadsAsync();

        Assert.That(handler.AudioRequests, Is.EqualTo(new[] { "/audio/2.mp3" }),
            "the cached current episode must not be fetched again");
        Assert.That(downloads.GetDownloadedPath("s1", "e2"), Is.Not.Null,
            "reconnect must fill the missing episode in the buffer window");
        Assert.That(downloads.GetDownloadedPath("s1", "stale"), Is.Null,
            "reconnect must prune cache files that are no longer in the playlist");
        Assert.That(vm.PlaylistItems.Single(i => i.EpisodeId == "e2").DownloadState,
            Is.EqualTo(DownloadState.Downloaded));
    }

    [Test]
    public async Task SkipAsync_RefreshesPlaylist()
    {
        var handler = new RecordingHttpHandler
        {
            PlaylistJson = "{\"stationId\":\"s1\",\"hostPaceEnabled\":false,\"isAging\":false,\"episodes\":[" +
                           "{\"episodeId\":\"e1\",\"title\":\"One\",\"streamUri\":\"https://audio/1\",\"isCurrent\":true}," +
                           "{\"episodeId\":\"e2\",\"title\":\"Two\",\"streamUri\":\"https://audio/2\",\"isCurrent\":false}]}"
        };
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://example.com", "token");
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), offlineState: new OfflineStateService(_tempOfflinePath));

        await vm.RetryConnectionAsync();
        Assert.That(vm.PlaylistItems.Count, Is.EqualTo(2));

        handler.Requests.Clear();
        await vm.SkipToNextCommand.ExecuteAsync(null);

        Assert.That(handler.Requests.Any(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/stream/playlist")), Is.True,
            "skip must refresh the playlist so the Up next panel advances");
    }

    [Test]
    public async Task SyncDownloads_SetsStationAvailableOffline()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var handler = new PlaylistHttpHandler();
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://example.com", "token");
        config.SetBufferMinutes("s1", 10);
        var downloads = new EpisodeDownloadService(root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), downloads, new OfflineStateService(_tempOfflinePath));

        await vm.RetryConnectionAsync();
        await vm.WaitForDownloadsAsync();

        Assert.That(vm.Stations.Single().Station!.IsAvailableOffline, Is.True,
            "a station with downloaded content must show the offline badge");
    }

    [Test]
    public async Task ApplyBufferChange_OnOlderServer_KepsCachedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "s1"));
        File.WriteAllBytes(Path.Combine(root, "s1", "e1.mp3"), new byte[] { 1 });

        var requests = new List<string>();
        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? "";
            if (path.StartsWith("/audio/"))
            {
                requests.Add(path);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
                };
            }
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
        config.SetBufferMinutes("s1", 30);
        var downloads = new EpisodeDownloadService(root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), downloads, new OfflineStateService(_tempOfflinePath));

        await vm.RetryConnectionAsync();
        vm.SelectedStation = new StationWithFeedsDto { Station = new Station { Id = "s1", Name = "One" } };
        vm.CurrentEpisodeId = "e1";

        vm.SelectedBufferOption = vm.BufferOptions.Single(o => o.Minutes == 60);
        await vm.ApplySelectedBufferAsync();
        await vm.WaitForDownloadsAsync();

        Assert.That(downloads.GetDownloadedPath("s1", "e1"), Is.Not.Null,
            "cached files must not be pruned when the playlist is unavailable");
        Assert.That(requests, Is.Empty,
            "no download requests must be made when the playlist is unknown");
    }

    [Test]
    public async Task HostPacedStation_EmptyAvailablePlaylist_PrunesCachedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "s1"));
        File.WriteAllBytes(Path.Combine(root, "s1", "e1.mp3"), new byte[] { 1 });
        var handler = new PlaylistHttpHandler
        {
            StationJson = "[{\"station\":{\"id\":\"s1\",\"name\":\"Station One\",\"hostPaceEnabled\":true},\"feeds\":[]}]",
            PlaylistJson = "{\"stationId\":\"s1\",\"hostPaceEnabled\":true,\"isAging\":false,\"episodes\":[]}"
        };
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://example.com", "token");
        config.SetBufferMinutes("s1", 30);
        var downloads = new EpisodeDownloadService(root, null, _ => TimeSpan.FromMinutes(1));
        var vm = new MainViewModel(config, new OhrbobClient(config, handler), null, new TestAudioPlayer(), a => a(), downloads, new OfflineStateService(_tempOfflinePath));

        await vm.RetryConnectionAsync();
        await vm.WaitForDownloadsAsync();

        Assert.That(downloads.GetDownloadedPath("s1", "e1"), Is.Null,
            "a blocked station's cache must be pruned even when the server returns an empty playlist");
    }

    [Test]
    public void SaveOfflineSnapshot_MergesCachedStationsWithDownloads()
    {
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://example.com", "token");
        var snapshotPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "offline.json");
        var offline = new OfflineStateService(snapshotPath);
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "s1"));
        File.WriteAllBytes(Path.Combine(root, "s1", "e1.mp3"), new byte[] { 1 });
        var downloads = new EpisodeDownloadService(root, null, _ => TimeSpan.FromMinutes(1));
        offline.Save(new OfflineSnapshot
        {
            ActiveStationId = "s1",
            Stations = new List<StationWithFeedsDto>
            {
                new() { Station = new Station { Id = "s1", Name = "One" } }
            },
            StationStates = new Dictionary<string, StationOfflineState>
            {
                ["s1"] = new()
                {
                    StationId = "s1",
                    Playlist = new List<PlaylistEpisodeDto>
                    {
                        new() { EpisodeId = "e1", StreamUri = "https://audio/1" }
                    }
                }
            }
        });
        var vm = new MainViewModel(config, new OhrbobClient(config, new FailingHttpHandler()), null, new TestAudioPlayer(), a => a(), downloads, offline);
        vm.IsOffline = false;
        vm.IsConnected = true;
        vm.SelectedStation = new StationWithFeedsDto { Station = new Station { Id = "s2", Name = "Two" } };
        vm.CurrentEpisodeId = "e2";
        vm.Stations.Clear();

        vm.SaveOfflineSnapshot();

        var saved = offline.Load();
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.Stations.Any(s => s.Station!.Id == "s1"), Is.True,
            "a cached station that still has downloaded content must not disappear");
    }
}
