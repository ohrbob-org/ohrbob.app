using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
/// On Linux/macOS the gate is an exclusively locked ("<c>flock</c>") lock file
/// in a directory only the current user can reach; the OS makes the lock
/// claim atomic and releases it automatically if the process dies, so a second
/// launch can never become a second primary and there is no stale lock to clean
/// up. The channel is a Unix domain socket next to it, which the lock holder
/// owns and may (re)create freely.
/// </para>
/// <para>
/// On Windows the gate is a named <see cref="Mutex"/> (named pipe names cannot
/// be claimed exclusively, so the pipe alone cannot arbitrate) and the channel
/// is a named pipe.
/// </para>
/// <para>
/// The channel lives in <c>$XDG_RUNTIME_DIR</c> when available (a <c>0700</c>
/// directory owned by the user, which is also how Flatpak exposes it), and
/// otherwise in a <c>0700</c> directory under the temp directory. That is what
/// keeps a <c>server</c>+<c>token</c> credential from being handed to another
/// user's socket; on Linux the connected peer's uid is checked as well before a
/// credential is written.
/// </para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private const string DefaultName = "ohrbob-desktop";
    private const int ConnectTimeoutMs = 5000;
    private const int MaxMessageBytes = 64 * 1024;
    private const byte AckByte = 0x06;

    // Linux socket options used to ask the kernel who is on the other end.
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;
    private const int PeerCredLength = 12; // struct ucred { pid_t pid; uid_t uid; gid_t gid; }

    private static readonly Lazy<uint?> CurrentUid = new(ReadCurrentUid);

    private readonly string _name;
    private readonly CancellationTokenSource _cts = new();
    private Mutex? _mutex;
    private FileStream? _lockFile;
    private Socket? _unixListener;
    private string? _channelDirectory;
    private bool _primary;
    private bool _ownsEndpointFile;
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

        // Only remove the endpoint file when this instance actually created it,
        // so a forwarder — or a failed acquisition — never deletes a socket the
        // running primary still owns. The lock file itself is deliberately left
        // in place: deleting a file another process may be locking is its own
        // race.
        if (_ownsEndpointFile && !OperatingSystem.IsWindows())
        {
            TryDeleteEndpoint(SocketPath);
        }

        _lockFile?.Dispose();
        _lockFile = null;

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
        string directory;
        try
        {
            directory = ChannelDirectory;
        }
        catch (Exception ex)
        {
            // Fail open: a broken IPC layer must not stop the user from
            // launching the app at all.
            Log.Error(ex, "Failed to prepare the single-instance directory; starting as primary");
            _primary = true;
            return true;
        }

        FileStream lockFile;
        try
        {
            lockFile = new FileStream(
                Path.Combine(directory, LockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            // Another process holds the lock: we are a forwarder.
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to claim the single-instance lock; starting as primary");
            _primary = true;
            return true;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            // We hold the lock, so any leftover socket file belongs to a
            // previous, now-dead primary. Reclaim it before binding.
            TryDeleteEndpoint(SocketPath);
            socket.Bind(new UnixDomainSocketEndPoint(SocketPath));
            socket.Listen(backlog: 16);
        }
        catch (Exception ex)
        {
            socket.Dispose();
            lockFile.Dispose();
            Log.Error(ex, "Failed to start the single-instance listener; starting as primary");
            _primary = true;
            return true;
        }

        _lockFile = lockFile;
        _unixListener = socket;
        _ownsEndpointFile = true;
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

                // Never hand the server+token credential to a socket owned by
                // anyone but this user.
                if (!IsTrustedPeer(socket))
                {
                    Log.Warning("Refusing to forward the deep link to an untrusted single-instance endpoint");
                    return;
                }

                using var stream = new NetworkStream(socket, ownsSocket: true);
                socket = null;
                await WriteMessageAsync(stream, message, cancellationToken).ConfigureAwait(false);
                await WaitForAckAsync(stream, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SocketException) when (Environment.TickCount64 < deadline)
            {
                // The primary may hold the lock but not yet be listening;
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

    private static bool IsTrustedPeer(Socket socket)
    {
        if (!OperatingSystem.IsLinux())
        {
            // Only Linux exposes a portable-enough peer-credential lookup here;
            // elsewhere the private channel directory is the guarantee.
            return true;
        }

        var ownUid = CurrentUid.Value;
        if (ownUid is null)
        {
            return true;
        }

        try
        {
            Span<byte> buffer = stackalloc byte[PeerCredLength];
            var length = socket.GetRawSocketOption(SolSocket, SoPeerCred, buffer);
            if (length < 8)
            {
                return true;
            }

            var peerUid = BitConverter.ToUInt32(buffer.Slice(4, 4));
            return peerUid == ownUid.Value;
        }
        catch (Exception ex)
        {
            // A kernel/API quirk is not evidence of an attack; the private
            // directory already keeps other users out.
            Log.Debug(ex, "Could not verify the single-instance peer credentials");
            return true;
        }
    }

    private static uint? ReadCurrentUid()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            return NativeGetUid();
        }
        catch
        {
            return null;
        }
    }

    [DllImport("libc", EntryPoint = "getuid", SetLastError = false)]
    private static extern uint NativeGetUid();

    private static void TryDeleteEndpoint(string path)
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

    // --------------------------------------------------------------- Paths --

    private string MutexName => "ohrbob-single-instance-" + ScopeKey;

    private string PipeName => "ohrbob-single-instance-" + ScopeKey;

    private string LockFileName => "ohrbob-single-instance-" + ScopeKey + ".lock";

    private string SocketPath =>
        Path.Combine(ChannelDirectory, "ohrbob-single-instance-" + ScopeKey + ".sock");

    private string ScopeKey => ComputeScopeKey(_name);

    /// <summary>
    /// The directory that holds the lock file and socket. Prefers
    /// <c>$XDG_RUNTIME_DIR</c> (a private, user-owned runtime directory, which
    /// Flatpak also provides) and otherwise creates a <c>0700</c> directory
    /// under the temp directory.
    /// </summary>
    private string ChannelDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The Unix single-instance channel is not used on Windows");
            }

            if (_channelDirectory is not null)
            {
                return _channelDirectory;
            }

            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (IsPrivateDirectory(runtime))
            {
                _channelDirectory = runtime!;
                return _channelDirectory;
            }

            var fallback = Path.Combine(Path.GetTempPath(), "ohrbob-" + UserTag());
            try
            {
                Directory.CreateDirectory(
                    fallback,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to create the single-instance fallback directory");
            }

            if (!IsPrivateDirectory(fallback))
            {
                throw new IOException(
                    $"Could not obtain a private directory for the single-instance channel ('{fallback}')");
            }

            _channelDirectory = fallback;
            return _channelDirectory;
        }
    }

    private static bool IsPrivateDirectory(string? path)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            // Reject symlinks so the fallback cannot be redirected elsewhere.
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            var mode = File.GetUnixFileMode(path);
            const UnixFileMode foreign =
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            return (mode & foreign) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string UserTag()
    {
        var uid = CurrentUid.Value;
        return uid is not null ? uid.Value.ToString() : Sanitize(Environment.UserName);
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return builder.Length == 0 ? "user" : builder.ToString();
    }

    private static string ComputeScopeKey(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "|" + name));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
