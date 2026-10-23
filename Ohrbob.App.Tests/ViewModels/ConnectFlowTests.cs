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

        // The hosted page is told which machine is connecting so onboarding can
        // pre-fill the name; the value is this machine's, escaped the same way.
        var expected = new Uri(
            $"https://ohrbob.org/connect/app?name={Uri.EscapeDataString(Environment.MachineName)}");

        Assert.That(launcher.Last, Is.EqualTo(expected));
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

    // Review Focus: a foreign/malformed link must be ignored — never crash, never
    // wipe the saved connection.
    [TestCase("http://example.com/x")]
    [TestCase("ohrbob://other?server=https%3A%2F%2Fother.example&token=new")]
    [TestCase("ohrbob://connect?server=x")]
    public void ApplyDeepLink_WithForeignOrIncompleteUri_LeavesSavedConnectionUnchanged(string uri)
    {
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://player.ohrbob.org", "old");
        var client = new OhrbobClient(config, new FakeHttpMessageHandler());
        var vm = new MainViewModel(config, client, new RecordingUriLauncher());

        vm.ApplyDeepLink(uri);

        Assert.That(config.ServerUrl, Is.EqualTo("https://player.ohrbob.org"));
        Assert.That(config.StreamToken, Is.EqualTo("old"));
        Assert.That(vm.IsReplaceConfirmationVisible, Is.False);
    }

    // Receiving a link over an active connection must not replace the saved
    // connection until the user confirms.
    [Test]
    public void ApplyDeepLink_WhileConnected_LeavesSavedConnectionUnchangedAndPrompts()
    {
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://player.ohrbob.org", "old");
        var client = new OhrbobClient(config, new FakeHttpMessageHandler());
        var vm = new MainViewModel(config, client, new RecordingUriLauncher());
        vm.IsConnected = true;

        vm.ApplyDeepLink("ohrbob://connect?server=https%3A%2F%2Fother.example&token=new");

        Assert.That(config.ServerUrl, Is.EqualTo("https://player.ohrbob.org"));
        Assert.That(config.StreamToken, Is.EqualTo("old"));
        Assert.That(vm.IsReplaceConfirmationVisible, Is.True);
    }

    // Confirming the prompt swaps in the parked connection and closes the overlay.
    [Test]
    public void ConfirmReplace_AppliesThePendingConnectionAndClosesThePrompt()
    {
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://player.ohrbob.org", "old");
        var client = new OhrbobClient(config, new FakeHttpMessageHandler());
        var vm = new MainViewModel(config, client, new RecordingUriLauncher());
        vm.IsConnected = true;
        vm.ApplyDeepLink("ohrbob://connect?server=https%3A%2F%2Fother.example&token=new");

        vm.ConfirmReplaceCommand.Execute(null);

        Assert.That(config.ServerUrl, Is.EqualTo("https://other.example"));
        Assert.That(config.StreamToken, Is.EqualTo("new"));
        Assert.That(vm.IsReplaceConfirmationVisible, Is.False);
    }

    // Cancelling the prompt must leave the active connection untouched.
    [Test]
    public void CancelReplace_DiscardsThePendingConnectionAndClosesThePrompt()
    {
        var config = new ConfigService(_tempConfigPath);
        config.Save("https://player.ohrbob.org", "old");
        var client = new OhrbobClient(config, new FakeHttpMessageHandler());
        var vm = new MainViewModel(config, client, new RecordingUriLauncher());
        vm.IsConnected = true;
        vm.ApplyDeepLink("ohrbob://connect?server=https%3A%2F%2Fother.example&token=new");

        vm.CancelReplaceCommand.Execute(null);

        Assert.That(config.ServerUrl, Is.EqualTo("https://player.ohrbob.org"));
        Assert.That(config.StreamToken, Is.EqualTo("old"));
        Assert.That(vm.IsReplaceConfirmationVisible, Is.False);
    }
}
