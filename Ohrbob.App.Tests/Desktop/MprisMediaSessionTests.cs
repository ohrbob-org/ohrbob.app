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

    [Test]
    public async Task ServiceAnswersPropertyQueriesImmediatelyWhenTheNameAppears()
    {
        if (!TryStartPrivateBus(out var address, out var pid))
        {
            Assert.Ignore("dbus-daemon is not available on this machine");
            return;
        }

        var previousAddress = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", address);
        try
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
                    address, failures, () => Interlocked.Increment(ref successes), cancellation.Token));
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
        finally
        {
            Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", previousAddress);
            if (pid > 0)
            {
                try { Process.GetProcessById(pid).Kill(); } catch { /* ignore */ }
            }
        }
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
