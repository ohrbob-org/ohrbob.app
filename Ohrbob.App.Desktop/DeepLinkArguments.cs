using System;

namespace Ohrbob.App.Desktop;

/// <summary>
/// Picks the <c>ohrbob://</c> deep link out of the desktop process' command-line
/// arguments.
/// </summary>
/// <remarks>
/// The operating system hands a registered URL scheme to the app by appending
/// the URL to the command line (the exported <c>.desktop</c> file uses
/// <c>Exec=… %u</c>; Windows/macOS pass it on <c>argv</c> too). Everything else
/// on the command line — Avalonia flags, file names, …— must be ignored, so the
/// rest of startup can stay unaware of argv.
/// </remarks>
public static class DeepLinkArguments
{
    private const string Scheme = "ohrbob://";

    /// <summary>
    /// Returns the first argument that is an <c>ohrbob://</c> URI, or
    /// <c>null</c> when none is present.
    /// </summary>
    public static string? Find(string[] args)
    {
        if (args is null)
        {
            return null;
        }

        foreach (var arg in args)
        {
            if (arg is not null && arg.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            {
                return arg;
            }
        }

        return null;
    }
}
