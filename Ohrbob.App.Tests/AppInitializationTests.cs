using Avalonia;
using Avalonia.Headless;
using NUnit.Framework;
using Ohrbob.App;

namespace Ohrbob.App.Tests
{
    [TestFixture]
    public class AppInitializationTests
    {
        [Test]
        public void App_Initializes_Without_Xaml_Errors()
        {
            Assert.DoesNotThrow(() =>
            {
                AppBuilder.Configure<App>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
            }, "Application should initialize and parse App.axaml without throwing XAML or InvalidCast exceptions.");
        }
    }
}
