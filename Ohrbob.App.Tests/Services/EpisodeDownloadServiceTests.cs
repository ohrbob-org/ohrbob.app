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

namespace Ohrbob.App.Tests.Services;

[TestFixture]
public class EpisodeDownloadServiceTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ohrbob-downloads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        public Func<string, HttpResponseMessage>? Override { get; set; }
        public bool FailFirst { get; set; }
        public bool ThrowFirst { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            if (ThrowFirst && Requests.Count == 1)
                throw new HttpRequestException("network failure");
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

    [Test]
    public async Task Prune_DeletesStaleFileNotInBudget()
    {
        var handler = new StubHandler();
        var service = new EpisodeDownloadService(_root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
        await service.SyncAsync("s1", new[] { Item("e1", true), Item("e2") }, 2, true, 0, CancellationToken.None);
        var stalePath = Path.Combine(_root, "s1", "stale.mp3");
        await File.WriteAllTextAsync(stalePath, "stale");

        await service.PruneAsync("s1", new[] { Item("e1", true), Item("e2") }, 1, "e1");

        Assert.That(service.GetDownloadedPath("s1", "e1"), Is.Not.Null);
        Assert.That(service.GetDownloadedPath("s1", "e2"), Is.Null);
        Assert.That(File.Exists(stalePath), Is.False);
    }

    [Test]
    public async Task Sync_RetriesNetworkFailureOnce()
    {
        var handler = new StubHandler { ThrowFirst = true };
        var service = new EpisodeDownloadService(_root, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));
        var items = new[] { Item("e1", true) };

        await service.SyncAsync("s1", items, 1, true, 0, CancellationToken.None);

        Assert.That(handler.Requests.Count, Is.EqualTo(2));
        Assert.That(items[0].DownloadState, Is.EqualTo(DownloadState.Downloaded));
    }

    [Test]
    public async Task Prune_StationIdTraversal_CannotTouchFilesOutsideCacheRoot()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "ohrbob-traversal-" + Guid.NewGuid().ToString("N"));
        var cacheRoot = Path.Combine(baseDir, "downloads");
        Directory.CreateDirectory(cacheRoot);
        var victim = Path.Combine(baseDir, "victim.mp3");
        await File.WriteAllTextAsync(victim, "outside");

        try
        {
            var handler = new StubHandler();
            var service = new EpisodeDownloadService(cacheRoot, new HttpClient(handler), _ => TimeSpan.FromMinutes(1));

            await service.SyncAsync("..", new[] { Item("e1", true) }, 1, false, 0, CancellationToken.None);

            Assert.That(File.Exists(victim), Is.True, "files outside the cache root must never be pruned");
            Assert.That(service.GetDownloadedPath("..", "victim"), Is.Null);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }
}
