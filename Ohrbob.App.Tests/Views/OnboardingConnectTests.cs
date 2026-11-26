using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using Ohrbob.App.Services;
using Ohrbob.App.Tests.ViewModels;
using Ohrbob.App.ViewModels;
using Ohrbob.App.Views;

namespace Ohrbob.App.Tests.Views;

[TestFixture]
public class OnboardingConnectTests
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
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeQrScanner : IQrScanner
    {
        public bool IsAvailable => true;
        public Task<string?> ScanAsync(System.Threading.CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("ohrbob://connect?server=https://example.com&token=abc");
    }

    private (Window Window, OnboardingView View, MainViewModel ViewModel) ShowOnboarding(IQrScanner? scanner = null)
    {
        var configService = new ConfigService(_tempConfigPath);
        var client = new OhrbobClient(configService, new FakeHttpMessageHandler());
        var vm = new MainViewModel(configService, client, offlineState: new OfflineStateService(_tempOfflinePath),
            qrScanner: scanner);

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
    public void ScanQrButton_IsShownOnlyWhenAScannerIsAvailable()
    {
        var (window, view, _) = ShowOnboarding();
        Assert.That(view.FindControl<Button>("ScanQrButton")!.IsEffectivelyVisible, Is.False);
        window.Close();

        var (scanWindow, scanView, _) = ShowOnboarding(new FakeQrScanner());
        Assert.That(scanView.FindControl<Button>("ScanQrButton")!.IsEffectivelyVisible, Is.True);
        scanWindow.Close();
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

    // A failed "Connect with ohrbob.org" run leaves IsAdvancedMode false while
    // ApplyConnection/InitializeAppAsync set ConnectionErrorMessage; the message
    // must be visible on the normal screen or the failure is silent.
    [AvaloniaTest]
    public void Header_UsesTheOhrbobLogo()
    {
        var (window, view, _) = ShowOnboarding();

        var logo = view.FindControl<Image>("OnboardingLogo");
        Assert.That(logo, Is.Not.Null, "the header should show the Ohrbob logo");
        Assert.That(logo!.Source, Is.Not.Null, "the logo image needs a source");

        window.Close();
    }

    [AvaloniaTest]
    public void Footer_ShowsLearnMoreTextAndTheRunningBuildVersion()
    {
        var (window, view, _) = ShowOnboarding();

        var footer = view.FindControl<TextBlock>("OnboardingFooterText")!;
        var version = view.FindControl<TextBlock>("OnboardingVersionText")!;

        Assert.That(footer.Text, Is.EqualTo("learn more on ohrbob.org"));
        Assert.That(version.Text, Is.EqualTo(AppInfo.DisplayVersion));
        Assert.That(version.Text, Is.EqualTo($"v{AppInfo.Version}"));
        Assert.That(AppInfo.Version, Does.Match(@"^\d+\.\d+"),
            "the build should stamp a real version at compile time");

        window.Close();
    }

    [AvaloniaTest]
    public void FooterLink_HasATouchTargetAndOpensOhrbobOrg()
    {
        var launcher = new RecordingUriLauncher();
        var vm = new MainViewModel(
            new ConfigService(_tempConfigPath),
            null,
            launcher,
            offlineState: new OfflineStateService(_tempOfflinePath));

        var view = new OnboardingView { DataContext = vm };
        var window = new Window { Width = 380, Height = 820, Content = view };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var link = view.FindControl<Button>("OnboardingFooterLink")!;
        Assert.That(link.IsEffectivelyVisible, Is.True);
        Assert.That(link.Bounds.Height, Is.GreaterThanOrEqualTo(44),
            "the footer link needs a thumb-sized tap target");

        link.Command!.Execute(link.CommandParameter);

        Assert.That(launcher.Last, Is.EqualTo(new Uri("https://ohrbob.org")));

        window.Close();
    }

    [AvaloniaTest]
    public void NarrowLayout_DoesNotDoubleTheSideMargins()
    {
        var (window, view, _) = ShowOnboarding();
        window.Width = 380;
        window.Height = 820;
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var panel = view.FindControl<Border>("ConnectPanel")!;
        var button = view.FindControl<Button>("HostedConnectButton")!;

        Assert.That(panel.Bounds.Width, Is.GreaterThanOrEqualTo(330),
            "the connect panel should use the screen minus the outer gutter only");
        Assert.That(button.Bounds.Width, Is.GreaterThanOrEqualTo(250),
            "the hosted-connect button needs enough room for its full label");

        window.Close();
    }

    [AvaloniaTest]
    public void ConnectionError_IsVisibleInNormalMode()
    {
        var (window, view, vm) = ShowOnboarding();

        var error = view.FindControl<TextBlock>("ConnectionErrorTextBlock")!;
        Assert.That(vm.IsAdvancedMode, Is.False);
        Assert.That(error.IsEffectivelyVisible, Is.False, "no message should be shown until one is set");

        vm.ConnectionErrorMessage = "Initialization error: the callback server is unreachable";
        window.UpdateLayout();

        Assert.That(error.IsEffectivelyVisible, Is.True, "a normal-mode connection failure must be visible");
        Assert.That(error.Text, Is.EqualTo("Initialization error: the callback server is unreachable"));

        window.Close();
    }
}
