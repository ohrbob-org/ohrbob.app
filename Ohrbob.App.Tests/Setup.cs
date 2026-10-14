using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Ohrbob.App.Tests.TestAppBuilder))]

namespace Ohrbob.App.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
