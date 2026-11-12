using System;
using System.Linq;
using NUnit.Framework;
using Ohrbob.App.Services;

namespace Ohrbob.App.Tests.Services;

[TestFixture]
public class LinkParserTests
{
    [Test]
    public void Parse_SplitsTextAndLinks_AndTrimsTrailingPunctuation()
    {
        var segments = LinkParser.Parse("Read https://example.com/a?x=1, then mailto:hi@example.com.");

        Assert.That(segments.Select(s => s.Text), Is.EqualTo(new[]
        {
            "Read ",
            "https://example.com/a?x=1",
            ", then ",
            "mailto:hi@example.com",
            "."
        }));
        Assert.That(segments[1].Uri, Is.EqualTo(new Uri("https://example.com/a?x=1")));
        Assert.That(segments[3].Uri, Is.EqualTo(new Uri("mailto:hi@example.com")));
    }

    [Test]
    public void Parse_LeavesPlainTextAlone()
    {
        var segments = LinkParser.Parse("No links here.");

        Assert.That(segments, Has.Count.EqualTo(1));
        Assert.That(segments[0].Uri, Is.Null);
    }
}
