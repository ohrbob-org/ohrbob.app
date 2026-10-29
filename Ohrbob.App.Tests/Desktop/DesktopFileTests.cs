using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Ohrbob.App.Tests.Desktop;

/// <summary>
/// Guards the shipped .desktop entry's window-matching fields.
/// </summary>
[TestFixture]
public class DesktopFileTests
{
    [Test]
    public void DeclaresStartupWMClassMatchingTheAvaloniaX11WindowClass()
    {
        // Avalonia's X11 backend defaults WM_CLASS to the entry assembly name
        // (X11PlatformOptions.WmClass), which for the desktop app is
        // "Ohrbob.App.Desktop" - not the application id. Without StartupWMClass
        // the desktop shell cannot associate the running window with this
        // .desktop file, so the launcher/dock icon is wrong.
        var line = File.ReadAllLines(FindShippedDesktopFile())
            .FirstOrDefault(l => l.StartsWith("StartupWMClass="));

        Assert.That(line, Is.Not.Null, "the desktop file must declare StartupWMClass");
        Assert.That(line, Is.EqualTo("StartupWMClass=org.ohrbob.ohrbob"));
    }

    private static string FindShippedDesktopFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "org.ohrbob.ohrbob.desktop");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"could not locate org.ohrbob.ohrbob.desktop above {AppContext.BaseDirectory}");
        return string.Empty;
    }
}
