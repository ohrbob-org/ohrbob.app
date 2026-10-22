using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using Ohrbob.App.Services;
using Ohrbob.App.ViewModels;
using Ohrbob.App.Views;

namespace Ohrbob.App.Tests.Views;

[TestFixture]
public class OnboardingConnectTests
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
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private (Window Window, OnboardingView View, MainViewModel ViewModel) ShowOnboarding()
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var vm = new MainViewModel(configService, client);

        var view = new OnboardingView { DataContext = vm };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();

        return (window, view, vm);
    }

    [AvaloniaTest]
    public void DefaultMode_ShowsHostedButton_AndHidesManualFields()
    {
        var (window, view, vm) = ShowOnboarding();

        var hostedButton = view.FindControl<Button>("HostedConnectButton")!;
        var serverUrlBox = view.FindControl<TextBox>("ServerUrlTextBox")!;
        var tokenBox = view.FindControl<TextBox>("TokenTextBox")!;

        Assert.That(vm.IsAdvancedMode, Is.False);
        Assert.That(hostedButton.IsEffectivelyVisible, Is.True, "the hosted connect button should be the primary action by default");
        Assert.That(serverUrlBox.IsEffectivelyVisible, Is.False, "the server URL box should stay hidden until Advanced is chosen");
        Assert.That(tokenBox.IsEffectivelyVisible, Is.False, "the token box should stay hidden until Advanced is chosen");

        window.Close();
    }

    [AvaloniaTest]
    public void AdvancedMode_RevealsManualFields_AndHidesHostedButton()
    {
        var (window, view, vm) = ShowOnboarding();

        var hostedButton = view.FindControl<Button>("HostedConnectButton")!;
        var serverUrlBox = view.FindControl<TextBox>("ServerUrlTextBox")!;
        var tokenBox = view.FindControl<TextBox>("TokenTextBox")!;

        vm.IsAdvancedMode = true;
        window.UpdateLayout();

        Assert.That(serverUrlBox.IsEffectivelyVisible, Is.True, "the server URL box should be visible in advanced mode");
        Assert.That(tokenBox.IsEffectivelyVisible, Is.True, "the token box should be visible in advanced mode");
        Assert.That(hostedButton.IsEffectivelyVisible, Is.False, "the hosted button is a simple-mode affordance");

        window.Close();
    }

    [AvaloniaTest]
    public void AdvancedButton_FlipsModeAndRevealsManualFields()
    {
        var (window, view, vm) = ShowOnboarding();

        var showAdvancedButton = view.FindControl<Button>("ShowAdvancedButton")!;
        showAdvancedButton.Command!.Execute(null);
        window.UpdateLayout();

        Assert.That(vm.IsAdvancedMode, Is.True);
        Assert.That(view.FindControl<TextBox>("ServerUrlTextBox")!.IsEffectivelyVisible, Is.True);

        window.Close();
    }
}
