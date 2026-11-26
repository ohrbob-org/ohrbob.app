using System.Linq;
using System.Reflection;

namespace Ohrbob.App.Services;

/// <summary>
/// Build facts stamped into the assembly by the GenerateOhrbobVersion MSBuild
/// target. Keeping the lookup here means the version shown in the UI always
/// matches the binary that is actually running.
/// </summary>
public static class AppInfo
{
    internal const string VersionMetadataKey = "OhrbobVersion";

    public static string Version { get; } = ResolveVersion();

    public static string DisplayVersion => $"v{Version}";

    private static string ResolveVersion()
    {
        var value = typeof(AppInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == VersionMetadataKey)
            ?.Value;

        return string.IsNullOrWhiteSpace(value) ? "0.0.0" : value.Trim();
    }
}
