using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Ohrbob.App.Services;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Tests.ViewModels;

[TestFixture]
public class ConnectFlowTests
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

    private MainViewModel CreateViewModel(out ConfigService config, RecordingUriLauncher? launcher = null)
    {
        config = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(config, new FakeHttpMessageHandler());
        return new MainViewModel(config, client, launcher ?? new RecordingUriLauncher());
    }

    [Test]
    public async Task OpenHostedConnectPage_LaunchesTheHostedUrl()
    {
        var launcher = new RecordingUriLauncher();
        var vm = CreateViewModel(out _, launcher);

        await vm.OpenHostedConnectPageCommand.ExecuteAsync(null);

        Assert.That(launcher.Last, Is.EqualTo(new Uri("https://ohrbob.org/connect/app")));
    }

    [Test]
    public void TryParseConnectDeepLink_AcceptsTheFixedShape()
    {
        var ok = MainViewModel.TryParseConnectDeepLink(
            "ohrbob://connect?server=https%3A%2F%2Fplayer.ohrbob.org&token=abc",
            out var server, out var token);

        Assert.That(ok, Is.True);
        Assert.That(server, Is.EqualTo("https://player.ohrbob.org"));
        Assert.That(token, Is.EqualTo("abc"));
    }

    [TestCase("http://example.com/x")]
    [TestCase("ohrbob://other?server=x&token=y")]
    [TestCase("ohrbob://connect?server=x")]
    [TestCase("not a uri")]
    public void TryParseConnectDeepLink_RejectsForeignOrIncompleteUris(string uri)
    {
        Assert.That(MainViewModel.TryParseConnectDeepLink(uri, out _, out _), Is.False);
    }

    // Ruling B: the router marshals through Dispatcher.UIThread.Post, which does
    // not run under NUnit, so the view model is exercised through its
    // deterministic ApplyDeepLink entry point instead of DeepLinkRouter.Publish.
    [Test]
    public void DeepLink_WhenNotConnected_IsAppliedAndSaved()
    {
        var vm = CreateViewModel(out var config);

        vm.ApplyDeepLink("ohrbob://connect?server=https%3A%2F%2Fplayer.ohrbob.org&token=abc");

        Assert.That(vm.IsConnected, Is.True);
        Assert.That(config.ServerUrl, Is.EqualTo("https://player.ohrbob.org"));
        Assert.That(config.StreamToken, Is.EqualTo("abc"));
    }

    [Test]
    public void DeepLinkRouter_DeliversPublishedUriToSubscriber()
    {
        const string uri = "ohrbob://connect?server=https%3A%2F%2Fplayer.ohrbob.org&token=abc";
        string? received = null;
        DeepLinkRouter.Subscribe(u => received = u);

        DeepLinkRouter.Publish(uri);

        Assert.That(received, Is.EqualTo(uri));
    }
}
