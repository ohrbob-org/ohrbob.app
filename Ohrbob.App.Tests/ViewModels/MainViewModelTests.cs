using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Ohrbob.App.Services;
using Ohrbob.App.ViewModels;
using System.Net.Http;

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
    public void TogglePlay_WhenStreamUriExists_TogglesIsPlaying()
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var vm = new MainViewModel(configService, client);

        // Setting CurrentEpisodeStreamUri to null
        vm.CurrentEpisodeStreamUri = null;
        vm.IsPlaying = false;
        vm.TogglePlayCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.False);

        // Set CurrentEpisodeStreamUri to "http://stream.mp3"
        vm.CurrentEpisodeStreamUri = "http://stream.mp3";
        vm.TogglePlayCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.True);

        vm.TogglePlayCommand.Execute(null);
        Assert.That(vm.IsPlaying, Is.False);
    }
}
