using System;
using System.IO;
using System.Threading.Tasks;
using System.Net.Http;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using Ohrbob.App.Services;
using Ohrbob.App.ViewModels;
using Ohrbob.App.Views;

namespace Ohrbob.App.Tests.Views;

[TestFixture]
public class MainWindowE2ETests
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

    [AvaloniaTest]
    public void MainWindow_OnLaunch_WhenNotConnected_ShowsOnboardingScreen()
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var vm = new MainViewModel(configService, client);

        var window = new MainWindow
        {
            DataContext = vm
        };

        Assert.That(vm.IsConnected, Is.False);
    }

    [AvaloniaTest]
    public async Task MainWindow_ConnectCommand_SavesConfigAndSetsConnected()
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var vm = new MainViewModel(configService, client);

        var window = new MainWindow
        {
            DataContext = vm
        };

        vm.ServerUrlInput = "https://example.com/ohrbob";
        vm.TokenInput = "my-secret-token";

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.That(vm.IsConnected, Is.True);
        Assert.That(configService.ServerUrl, Is.EqualTo("https://example.com/ohrbob"));
        Assert.That(configService.StreamToken, Is.EqualTo("my-secret-token"));
    }
}
