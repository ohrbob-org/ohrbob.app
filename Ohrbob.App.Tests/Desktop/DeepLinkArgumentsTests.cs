using NUnit.Framework;
using Ohrbob.App.Desktop;

namespace Ohrbob.App.Tests.Desktop;

/// <summary>
/// The desktop head receives <c>ohrbob://</c> links as command-line arguments
/// (GNOME/KDE/the Flatpak portal pass the URL to the exported <c>.desktop</c>
/// file's <c>Exec=… %u</c>). <see cref="DeepLinkArguments.Find"/> is the pure
/// part of that: pick the scheme URI out of whatever else the launcher puts on
/// the command line, so the rest of startup can stay oblivious to argv.
/// </summary>
[TestFixture]
public class DeepLinkArgumentsTests
{
    [Test]
    public void Find_ReturnsTheOhrbobUriFromArguments()
    {
        var args = new[] { "--flag", "ohrbob://connect?server=x&token=y" };
        Assert.That(DeepLinkArguments.Find(args), Is.EqualTo("ohrbob://connect?server=x&token=y"));
    }

    [Test]
    public void Find_ReturnsNullWhenNoOhrbobUriPresent()
    {
        Assert.That(DeepLinkArguments.Find(new[] { "--flag", "x" }), Is.Null);
    }

    [Test]
    public void Find_ReturnsNullForEmptyOrNullArguments()
    {
        Assert.That(DeepLinkArguments.Find(System.Array.Empty<string>()), Is.Null);
        Assert.That(DeepLinkArguments.Find(null!), Is.Null);
    }

    [Test]
    public void Find_ReturnsTheFirstOhrbobUriWhenSeveralArePresent()
    {
        var args = new[] { "ohrbob://connect?server=a&token=b", "ohrbob://connect?server=c&token=d" };
        Assert.That(DeepLinkArguments.Find(args), Is.EqualTo("ohrbob://connect?server=a&token=b"));
    }

    [TestCase("OHRBOB://connect?server=x&token=y")]
    [TestCase("Ohrbob://connect?server=x&token=y")]
    public void Find_MatchesTheSchemeCaseInsensitively(string uri)
    {
        Assert.That(DeepLinkArguments.Find(new[] { uri }), Is.EqualTo(uri));
    }

    [Test]
    public void Find_IgnoresForeignSchemes()
    {
        Assert.That(DeepLinkArguments.Find(new[] { "https://example.com/", "file:///tmp/x" }), Is.Null);
    }
}
