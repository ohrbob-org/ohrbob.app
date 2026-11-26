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
            // Parse App.axaml on a fresh Application instance. Do not call
            // AppBuilder.SetupWithoutStarting here: it replaces the shared
            // headless platform and breaks input hit-testing for later tests.
            Assert.DoesNotThrow(() => new App().Initialize(),
                "Application should parse App.axaml without throwing XAML or InvalidCast exceptions.");
        }
    }
}
