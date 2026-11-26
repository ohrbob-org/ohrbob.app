using System;
using System.Text.RegularExpressions;

namespace Ohrbob.App.Services;

/// <summary>
/// Profile avatar colors and initials. The server owns the chosen color; the
/// palette/hash fallback keeps older servers and offline snapshots colorful.
/// </summary>
public static partial class ProfileColors
{
    // Keep in sync with Ohrbob.Server.Shared.Data.ProfileColors.
    public static readonly string[] Palette =
    {
        "#ef4444", "#f97316", "#f59e0b", "#ca8a04", "#65a30d",
        "#16a34a", "#059669", "#0d9488", "#0891b2", "#0284c7",
        "#3b82f6", "#6366f1", "#8b5cf6", "#a855f7", "#d946ef",
        "#ec4899", "#f43f5e", "#78716c", "#64748b", "#475569",
    };

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    public static bool IsValid(string? color) =>
        !string.IsNullOrEmpty(color) && HexColor().IsMatch(color);

    public static string Resolve(string? color, string? name)
    {
        if (IsValid(color)) return color!;

        // FNV-1a keeps the fallback stable across app launches, unlike
        // string.GetHashCode().
        var hash = 2166136261u;
        foreach (var ch in name ?? "")
            hash = (hash ^ char.ToUpperInvariant(ch)) * 16777619u;

        return Palette[(int)(hash % (uint)Palette.Length)];
    }

    public static string Initial(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? "?"
            : char.ToUpperInvariant(trimmed[0]).ToString();
    }
}
