using System.Net;
using System.Text.RegularExpressions;

namespace Ohrbob.App.Services;

/// <summary>Converts feed/API HTML into text suitable for the player UI.</summary>
public static partial class HtmlText
{
    public static string ToPlainText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";

        var withoutBlocks = BlockRegex().Replace(value, " ");
        var withoutTags = TagRegex().Replace(withoutBlocks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex("<(script|style)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BlockRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
