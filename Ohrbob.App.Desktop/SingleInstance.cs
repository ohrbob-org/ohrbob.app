using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Ohrbob.App.Desktop;

/// <summary>
/// Makes the desktop head a single-instance application: the first process
/// becomes the <em>primary</em> and listens on a per-user local endpoint for
/// forwarded <c>ohrbob://</c> links, while a later launch forwards its link to
/// the primary and exits before creating any window.
/// </summary>
/// <remarks>
/// <para>
/// On Linux/macOS the gate and the channel are one and the same: a Unix domain
/// socket in the temp directory. The first process to <c>bind</c> the path owns
/// the gate; the kernel makes that claim atomic, so no separate lock is needed.
/// A crashed primary can leave the socket file behind, so a failed bind probes
/// whether anyone still answers and otherwise removes the stale file and
/// retries once.
/// </para>
/// <para>
/// On Windows the gate is a named <see cref="Mutex"/> (named pipe names cannot
/// be claimed exclusively, so the pipe alone cannot arbitrate) and the channel
/// is a named pipe.
/// </para>
/// <para>
/// The endpoint name is scoped to the current user so two users on the same
/// machine never collide, and lives in the temp directory because that is
/// shared between separate runs of the same Flatpak sandbox.
/// </para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private const string DefaultName = "ohrbob-desktop";
    private const int ConnectTimeoutMs = 5000;
    private const int MaxMessageBytes = 64 * 1024;
    private const byte AckByte = 0x06;

    private readonly string _name;
    private readonly CancellationTokenSource _cts = new();
    private Mutex? _mutex;
    private Socket? _unixListener;
    private bool _primary;
    private bool _disposed;

    public SingleInstance(string? name = null) => _name = name ?? DefaultName;

    /// <summary>
    /// Tries to become the primary instance and start listening. Returns
    /// <c>true</c> when this process claimed the gate (the callback receives
    /// every URI forwarded by later launches), or <c>false</c> when another
    /// instance is already running — in that case call <see cref="Send"/> and
    /// exit.
    /// </summary>
    public bool TryAcquire(Action<string> onMessage)
    {
        ArgumentNullException.ThrowIfNull(onMessage);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return OperatingSystem.IsWindows()
            ? TryAcquireNamedPipe(onMessage)
            : TryAcquireUnixSocket(onMessage);
    }

    /// <summary>
    /// Forwards a deep link to the running primary instance. No-op when no
    /// primary can be reached (the URL is simply dropped rather than crashing a
    /// launch that is on its way out anyway).
    /// </summary>
    public void Send(string message)
    {
        if (string.IsNullOrEmpty(message) || _disposed || _primary)
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                SendNamedPipeAsync(message, CancellationToken.None).GetAwaiter().GetResult();
            }
            else
            {
                SendUnixSocketAsync(message, CancellationToken.None).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to forward the deep link to the running instance");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cts.Cancel();

        var listener = _unixListener;
        _unixListener = null;
        listener?.Dispose();

        // Only the primary owns the on-disk socket file; a forwarder must never
        // delete the primary's endpoint on its way out.
        if (_primary && !OperatingSystem.IsWindows())
        {
            TryDeleteSocket(SocketPath);
        }

        var mutex = _mutex;
        _mutex = null;
        if (mutex is not null)
        {
            if (_primary)
            {
                try { mutex.ReleaseMutex(); }
                catch (ApplicationException) { /* released by another thread */ }
            }

            mutex.Dispose();
        }
    }

    // ---------------------------------------------------------------- Unix --

    private bool TryAcquireUnixSocket(Action<string> onMessage)
    {
        var path = SocketPath;
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(path));
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            socket.Dispose();

            // Someone else may own the path — or a previous run may have
            // crashed and left the socket file behind. Only reclaim it when
            // nobody answers.
            if (CanReachUnixSocket(path))
            {
                return false;
            }

            TryDeleteSocket(path);
            socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Bind(new UnixDomainSocketEndPoint(path));
            }
            catch (SocketException)
            {
                // Lost a race against a concurrent launch; behave as a
                // forwarder rather than risk two primaries.
                socket.Dispose();
                return false;
            }
        }
        catch (Exception ex)
        {
            // Fail open: a broken IPC layer must not stop the user from
            // launching the app at all.
            socket.Dispose();
            Log.Error(ex, "Failed to claim the single-instance gate; starting as primary");
            _primary = true;
            return true;
        }

        socket.Listen(backlog: 16);
        _unixListener = socket;
        _primary = true;
        _ = Task.Run(() => AcceptUnixClientsAsync(onMessage));
        return true;
    }

    private async Task AcceptUnixClientsAsync(Action<string> onMessage)
    {
        var listener = _unixListener;
        if (listener is null)
        {
            return;
        }

        while (true)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            _ = Task.Run(() => HandleUnixClientAsync(client, onMessage));
        }
    }

    private async Task HandleUnixClientAsync(Socket client, Action<string> onMessage)
    {
        try
        {
            using var stream = new NetworkStream(client, ownsSocket: true);
            await ReceiveAndDispatchAsync(stream, onMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to handle a forwarded deep link");
        }
    }

    private async Task SendUnixSocketAsync(string message, CancellationToken cancellationToken)
    {
        var path = SocketPath;
        var deadline = Environment.TickCount64 + ConnectTimeoutMs;

        while (true)
        {
            Socket? socket = null;
            try
            {
                socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken)
                    .ConfigureAwait(false);

                using var stream = new NetworkStream(socket, ownsSocket: true);
                socket = null;
                await WriteMessageAsync(stream, message, cancellationToken).ConfigureAwait(false);
                await WaitForAckAsync(stream, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SocketException) when (Environment.TickCount64 < deadline)
            {
                // The primary may still be starting up and not yet listening;
                // retry briefly so an early launch never loses the URL.
                socket?.Dispose();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                socket?.Dispose();
            }
        }
    }

    private static bool CanReachUnixSocket(string path)
    {
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var connect = probe.ConnectAsync(new UnixDomainSocketEndPoint(path));
            return connect.Wait(TimeSpan.FromMilliseconds(500)) && probe.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteSocket(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to remove the single-instance socket");
        }
    }

    // ------------------------------------------------------------- Windows --

    private bool TryAcquireNamedPipe(Action<string> onMessage)
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName, out _);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // A previous primary died without releasing; we inherit the gate.
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        _primary = true;
        _ = Task.Run(() => AcceptNamedPipeClientsAsync(onMessage));
        return true;
    }

    private async Task AcceptNamedPipeClientsAsync(Action<string> onMessage)
    {
        while (true)
        {
            var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { server.Dispose(); break; }
            catch (ObjectDisposedException) { server.Dispose(); break; }
            catch (Exception ex)
            {
                server.Dispose();
                Log.Debug(ex, "Failed to accept a named-pipe connection");
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await ReceiveAndDispatchAsync(server, onMessage).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to handle a forwarded deep link");
                }
                finally
                {
                    server.Dispose();
                }
            });
        }
    }

    private async Task SendNamedPipeAsync(string message, CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(
            ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(ConnectTimeoutMs, cancellationToken).ConfigureAwait(false);
        await WriteMessageAsync(client, message, cancellationToken).ConfigureAwait(false);
        await WaitForAckAsync(client, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------- Channel --

    private async Task ReceiveAndDispatchAsync(Stream stream, Action<string> onMessage)
    {
        var message = await ReadMessageAsync(stream, _cts.Token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        onMessage(message);

        // Acknowledge only after the URL has been handed over, so the sender
        // knows it was delivered before it exits.
        try
        {
            await stream.WriteAsync(new[] { AckByte }, _cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to acknowledge a forwarded deep link");
        }
    }

    private static async Task WriteMessageAsync(Stream stream, string message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        var header = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        if (!await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var length = BitConverter.ToInt32(header, 0);
        if (length <= 0 || length > MaxMessageBytes)
        {
            return null;
        }

        var payload = new byte[length];
        if (!await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Encoding.UTF8.GetString(payload);
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static async Task WaitForAckAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeoutMs);
        var ack = new byte[1];
        try
        {
            await stream.ReadAsync(ack.AsMemory(0, 1), timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The message was already written; a missing ack is not fatal.
        }
    }

    // --------------------------------------------------------------- Names --

    private string MutexName => "ohrbob-single-instance-" + ScopeKey;

    private string PipeName => "ohrbob-single-instance-" + ScopeKey;

    private string SocketPath =>
        Path.Combine(Path.GetTempPath(), "ohrbob-single-instance-" + ScopeKey + ".sock");

    private string ScopeKey => ComputeScopeKey(_name);

    private static string ComputeScopeKey(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "|" + name));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
