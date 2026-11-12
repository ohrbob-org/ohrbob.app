using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ohrbob.App.Services;

public sealed record LinkSegment(string Text, Uri? Uri)
{
    public bool IsLink => Uri is not null;
}

/// <summary>Splits plain text into text and clickable http(s)/mailto links.</summary>
public static partial class LinkParser
{
    public static IReadOnlyList<LinkSegment> Parse(string? text)
    {
        var result = new List<LinkSegment>();
        if (string.IsNullOrEmpty(text))
            return result;

        void AddText(string value)
        {
            if (value.Length == 0) return;
            if (result.Count > 0 && result[^1].Uri is null)
                result[^1] = new LinkSegment(result[^1].Text + value, null);
            else
                result.Add(new LinkSegment(value, null));
        }

        var index = 0;
        foreach (Match match in UrlRegex().Matches(text))
        {
            if (match.Index > index)
                AddText(text[index..match.Index]);
            index = match.Index;

            var raw = match.Value;
            var trimmed = raw.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'');
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto))
            {
                result.Add(new LinkSegment(trimmed, uri));
                index += trimmed.Length;
            }

            if (index < match.Index + raw.Length)
            {
                AddText(text[index..(match.Index + raw.Length)]);
                index = match.Index + raw.Length;
            }
        }

        if (index < text.Length)
            AddText(text[index..]);

        return result;
    }

    [GeneratedRegex(@"(?:https?://|mailto:)[^\s<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
