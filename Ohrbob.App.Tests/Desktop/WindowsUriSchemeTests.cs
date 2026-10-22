using NUnit.Framework;
using Ohrbob.App.Desktop;

namespace Ohrbob.App.Tests.Desktop;

/// <summary>
/// Windows hands a registered URL scheme to the process by launching the
/// command named under <c>HKCU\Software\Classes\&lt;scheme&gt;\shell\open\command</c>
/// and appending the URL. <see cref="WindowsUriScheme.BuildRegistryValues"/> is
/// the pure part of that mapping (path → data); <c>EnsureRegistered</c> writes it
/// to the current-user hive. The values are asserted here so the registry layout
/// is pinned without touching a real registry.
/// </summary>
[TestFixture]
public class WindowsUriSchemeTests
{
    [Test]
    public void BuildRegistryValues_RegistersTheCommandUnderHkcuClasses()
    {
        var values = WindowsUriScheme.BuildRegistryValues(@"C:\apps\ohrbob.exe");
        Assert.That(values["ohrbob"], Is.EqualTo("URL:Ohrbob Protocol"));
        Assert.That(values[@"ohrbob\shell\open\command"], Is.EqualTo("\"C:\\apps\\ohrbob.exe\" \"%1\""));
    }

    [Test]
    public void BuildRegistryValues_MarksTheSchemeAsAUrlProtocol()
    {
        // Windows only treats the key as a URL protocol handler when the
        // "URL Protocol" value exists (it may be empty).
        var values = WindowsUriScheme.BuildRegistryValues(@"C:\apps\ohrbob.exe");
        Assert.That(values[@"ohrbob\URL Protocol"], Is.EqualTo(string.Empty));
    }

    [Test]
    public void BuildRegistryValues_GivesTheSchemeADefaultIcon()
    {
        var values = WindowsUriScheme.BuildRegistryValues(@"C:\apps\ohrbob.exe");
        Assert.That(values[@"ohrbob\DefaultIcon"], Is.EqualTo("\"C:\\apps\\ohrbob.exe\",0"));
    }

    [Test]
    public void BuildRegistryValues_QuotesTheExecutablePathSoSpacesSurvive()
    {
        var values = WindowsUriScheme.BuildRegistryValues(@"C:\Program Files\Ohrbob\ohrbob.exe");
        Assert.That(
            values[@"ohrbob\shell\open\command"],
            Is.EqualTo("\"C:\\Program Files\\Ohrbob\\ohrbob.exe\" \"%1\""));
    }

    [Test]
    public void BuildRegistryValues_IsPureAndReturnsEveryEntry()
    {
        var first = WindowsUriScheme.BuildRegistryValues(@"C:\apps\ohrbob.exe");
        var second = WindowsUriScheme.BuildRegistryValues(@"C:\apps\ohrbob.exe");

        Assert.That(second, Is.EqualTo(first), "the same input must yield the same mapping");
        Assert.That(first.Keys, Is.EquivalentTo(new[]
        {
            "ohrbob",
            @"ohrbob\URL Protocol",
            @"ohrbob\DefaultIcon",
            @"ohrbob\shell\open\command",
        }));
    }

    [Test]
    public void EnsureRegistered_DoesNothingOffWindows()
    {
        // On the Linux test host this must simply return; it must not throw
        // PlatformNotSupportedException from Microsoft.Win32.Registry.
        Assume.That(!OperatingSystem.IsWindows(), "writing the real HKCU hive is not a test");
        Assert.DoesNotThrow(() => WindowsUriScheme.EnsureRegistered(@"C:\apps\ohrbob.exe"));
    }
}
