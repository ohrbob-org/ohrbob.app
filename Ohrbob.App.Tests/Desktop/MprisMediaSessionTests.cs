using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Ohrbob.App.Desktop;
using Tmds.DBus.Protocol;

namespace Ohrbob.App.Tests.Desktop;

/// <summary>
/// Regression tests for the Linux (MPRIS) half of issue #4.
///
/// <para>
/// The service must be able to answer property queries the instant the well-known
/// name becomes visible on the bus. Every MPRIS client reacts to that name
/// appearing by immediately calling <c>Properties.GetAll</c>; if the object handler
/// isn't registered yet the client gets <c>UnknownMethod</c>, its <c>GDBusProxy</c>
/// init fails and — in the case of GNOME Shell — the player is dropped from the
/// media controls for the rest of the session. This is exercised against a private
/// session bus so it does not disturb the developer's desktop.
/// </para>
/// </summary>
[TestFixture]
public class MprisMediaSessionTests
{
    private const string BusName = "org.mpris.MediaPlayer2.ohrbob";

    private string _busAddress = string.Empty;
    private int _busPid;
    private string? _previousAddress;
    private bool _busStarted;

    [OneTimeSetUp]
    public void StartPrivateBusForTheFixture()
    {
        if (!TryStartPrivateBus(out _busAddress, out _busPid))
        {
            Assert.Ignore("dbus-daemon is not available on this machine");
            return;
        }

        // DBusAddress.Session — which MprisMediaSession reads internally — caches the
        // address in a process-global static on first use. Every test in this fixture
        // must therefore share one private bus; otherwise the second test's session
        // silently connects to the first test's already-torn-down bus.
        _previousAddress = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", _busAddress);
        _busStarted = true;
    }

    [OneTimeTearDown]
    public void StopPrivateBusForTheFixture()
    {
        if (_busStarted)
        {
            Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", _previousAddress);
        }

        if (_busPid > 0)
        {
            try { Process.GetProcessById(_busPid).Kill(); } catch { /* ignore */ }
        }
    }

    [Test]
    public async Task ServiceAnswersPropertyQueriesImmediatelyWhenTheNameAppears()
    {
        var failures = new ConcurrentBag<string>();
        var successes = 0;
        using var cancellation = new CancellationTokenSource();

        // Hammer the bus from before the service exists, so that queries land in
        // the window where the name is owned but the handler may not be ready.
        var hammers = new Task[4];
        for (var i = 0; i < hammers.Length; i++)
        {
            hammers[i] = Task.Run(() => QueryUntilCancelledAsync(
                _busAddress, failures, () => Interlocked.Increment(ref successes), cancellation.Token));
        }

        using var session = MprisMediaSession.Create();

        await Task.Delay(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Task.WhenAll(hammers);

        Assert.That(successes, Is.GreaterThan(0),
            "the service never became reachable on the private bus");
        Assert.That(failures, Is.Empty,
            "clients querying the property handler as the name appeared got errors: " +
            string.Join(", ", failures));
    }

    [Test]
    public async Task DesktopEntryMatchesTheShippedDesktopFileId()
    {
        using var session = MprisMediaSession.Create();

        var desktopEntry = await ReadRootStringPropertyAsync(_busAddress, "DesktopEntry");
        Assert.That(desktopEntry, Is.Not.Null,
            "the service never became reachable on the private bus");

        // GNOME Shell (and other MPRIS clients) resolve the player's app — and
        // therefore its icon — by looking up "<DesktopEntry>.desktop". If the
        // value doesn't match the shipped desktop file id, the media widget
        // falls back to a generic placeholder icon.
        var expected = Path.GetFileNameWithoutExtension(FindShippedDesktopFile());
        Assert.That(desktopEntry, Is.EqualTo(expected),
            "MPRIS DesktopEntry must match the shipped .desktop file id");
    }

    /// <summary>
    /// Reads a string property from the MPRIS root interface, retrying until the
    /// service owns the name on the private bus.
    /// </summary>
    private static async Task<string?> ReadRootStringPropertyAsync(string address, string propertyName)
    {
        using var connection = new DBusConnection(address);
        await connection.ConnectAsync();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var message = CreateGetPropertyMessage(connection, "org.mpris.MediaPlayer2", propertyName);
                return await connection.CallMethodAsync(message,
                    static (Message m, object? _) => m.GetBodyReader().ReadVariantValue().GetString());
            }
            catch (DBusErrorReplyException ex)
                when (ex.ErrorName == "org.freedesktop.DBus.Error.ServiceUnknown")
            {
                // The service hasn't acquired the name yet; wait and retry.
                await Task.Delay(50);
            }
        }

        return null;
    }

    private static async Task QueryUntilCancelledAsync(
        string address,
        ConcurrentBag<string> failures,
        Action onSuccess,
        CancellationToken cancellationToken)
    {
        using var connection = new DBusConnection(address);
        await connection.ConnectAsync();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var message = CreateGetAllMessage(connection);
                await connection.CallMethodAsync(message);
                onSuccess();
            }
            catch (DBusErrorReplyException ex)
            {
                // Before the service owns the name the bus replies ServiceUnknown;
                // that is expected and not a defect. Anything else (in particular
                // UnknownMethod) means a client raced the handler registration.
                if (ex.ErrorName != "org.freedesktop.DBus.Error.ServiceUnknown")
                {
                    failures.Add(ex.ErrorName);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // The connection is being torn down; stop.
                return;
            }
        }
    }

    /// <summary>Builds the <c>Properties.GetAll</c> call for the MPRIS player interface.</summary>
    private static MessageBuffer CreateGetAllMessage(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: BusName,
            path: "/org/mpris/MediaPlayer2",
            @interface: "org.freedesktop.DBus.Properties",
            member: "GetAll",
            signature: "s");
        writer.WriteString("org.mpris.MediaPlayer2.Player");
        return writer.CreateMessage();
    }

    /// <summary>Builds the <c>Properties.Get</c> call for a root-interface property.</summary>
    private static MessageBuffer CreateGetPropertyMessage(DBusConnection connection, string @interface, string name)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: BusName,
            path: "/org/mpris/MediaPlayer2",
            @interface: "org.freedesktop.DBus.Properties",
            member: "Get",
            signature: "ss");
        writer.WriteString(@interface);
        writer.WriteString(name);
        return writer.CreateMessage();
    }

    /// <summary>
    /// Locates the desktop file shipped with the app by walking up from the test
    /// output directory to the repository root.
    /// </summary>
    private static string FindShippedDesktopFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "app.ohrbob.Ohrbob.desktop");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"could not locate app.ohrbob.Ohrbob.desktop above {AppContext.BaseDirectory}");
        return string.Empty;
    }

    /// <summary>Starts a throwaway session bus and returns its address and process id.</summary>
    private static bool TryStartPrivateBus(out string address, out int pid)
    {
        address = string.Empty;
        pid = 0;
        try
        {
            var startInfo = new ProcessStartInfo("dbus-daemon")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("--session");
            startInfo.ArgumentList.Add("--print-address=1");
            startInfo.ArgumentList.Add("--print-pid=1");
            startInfo.ArgumentList.Add("--fork");

            using var launcher = Process.Start(startInfo);
            if (launcher == null)
            {
                return false;
            }

            address = launcher.StandardOutput.ReadLine() ?? string.Empty;
            var pidLine = launcher.StandardOutput.ReadLine();
            launcher.WaitForExit(2000);

            if (string.IsNullOrEmpty(address) || !int.TryParse(pidLine, out pid))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
