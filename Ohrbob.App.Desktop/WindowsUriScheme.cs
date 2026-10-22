using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace Ohrbob.App.Desktop;

/// <summary>
/// Registers the <c>ohrbob://</c> URL scheme with Windows so the browser can
/// hand a link to this app.
/// </summary>
/// <remarks>
/// Windows resolves a scheme through
/// <c>HKCU\Software\Classes\&lt;scheme&gt;</c>: the key's default value names the
/// protocol, the named <c>URL Protocol</c> value (which may be empty) marks the
/// key as a URL protocol handler, and <c>shell\open\command</c> holds the
/// command line to launch with the URL appended. The command is written from
/// <see cref="Environment.ProcessPath"/> on the real run, but
/// <see cref="WindowsUriScheme.BuildRegistryValues"/> is split out as a pure
/// function so the mapping can be asserted on any OS (Linux CI included).
/// </remarks>
public static class WindowsUriScheme
{
    private const string Scheme = "ohrbob";
    private const string ClassesKey = @"Software\Classes";
    private const string UrlProtocolValueName = "URL Protocol";

    /// <summary>
    /// Returns the values to write under <c>HKCU\Software\Classes</c> for the
    /// <c>ohrbob</c> scheme, keyed by their path relative to that key.
    /// </summary>
    /// <param name="executablePath">
    /// Absolute path of the desktop executable the OS should launch.
    /// </param>
    /// <remarks>
    /// Every returned path names a registry <em>key</em> whose default value is
    /// the data, except <c>ohrbob\URL Protocol</c>, whose final segment is the
    /// named value that flags the scheme as a URL protocol. See
    /// <see cref="EnsureRegistered"/> for how each path is written.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> BuildRegistryValues(string executablePath)
    {
        ArgumentNullException.ThrowIfNull(executablePath);

        var quoted = "\"" + executablePath + "\"";

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Scheme] = "URL:Ohrbob Protocol",
            [Scheme + @"\" + UrlProtocolValueName] = string.Empty,
            [Scheme + @"\DefaultIcon"] = quoted + ",0",
            [Scheme + @"\shell\open\command"] = quoted + " \"%1\"",
        };
    }

    /// <summary>
    /// Writes the <c>ohrbob</c> scheme registration into the current user's
    /// hive. Does nothing on operating systems other than Windows.
    /// </summary>
    public static void EnsureRegistered(string executablePath)
    {
        ArgumentNullException.ThrowIfNull(executablePath);

        // Microsoft.Win32.Registry throws PlatformNotSupportedException off
        // Windows, so keep it behind the guard.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var (path, data) in BuildRegistryValues(executablePath))
        {
            WriteValue(path, data);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteValue(string path, string data)
    {
        // "ohrbob\URL Protocol" is the one entry whose last segment is a value
        // *name*; Windows marks a key as a URL protocol handler with the
        // presence of that named value, not with a subkey of that name. Every
        // other entry names a key whose default value carries the data.
        var suffix = "\\" + UrlProtocolValueName;
        var isUrlProtocolValue = path.EndsWith(suffix, StringComparison.Ordinal);
        var keyPath = isUrlProtocolValue ? path[..^suffix.Length] : path;

        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ClassesKey + "\\" + keyPath);
        key?.SetValue(isUrlProtocolValue ? UrlProtocolValueName : null, data);
    }
}
