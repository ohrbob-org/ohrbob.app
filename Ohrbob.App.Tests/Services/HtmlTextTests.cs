using NUnit.Framework;
using Ohrbob.App.Services;

namespace Ohrbob.App.Tests.Services;

[TestFixture]
public class HtmlTextTests
{
    [Test]
    public void ToPlainText_RemovesTagsBlocksAndDecodesEntities()
    {
        var html = "<div><p>Reddington showed the jury a <b>small box</b> &amp; a book.</p><p>Second&nbsp;line.</p></div>";

        Assert.That(HtmlText.ToPlainText(html),
            Is.EqualTo("Reddington showed the jury a small box & a book. Second line."));
    }

    [Test]
    public void ToPlainText_StripsScriptAndStyleContent()
    {
        var html = "Title<script>alert('x')</script><style>.a{color:red}</style>Body";

        Assert.That(HtmlText.ToPlainText(html), Is.EqualTo("Title Body"));
    }

    [Test]
    public void ToPlainText_NullOrEmpty_IsEmpty()
    {
        Assert.That(HtmlText.ToPlainText(null), Is.Empty);
        Assert.That(HtmlText.ToPlainText(""), Is.Empty);
    }
}
