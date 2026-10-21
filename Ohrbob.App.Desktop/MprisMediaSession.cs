using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ohrbob.App.Services.Media;
using Serilog;
using Tmds.DBus.Protocol;

namespace Ohrbob.App.Desktop;

/// <summary>
/// Publishes playback state to the Linux desktop over MPRIS
/// (<c>org.mpris.MediaPlayer2</c>) so the sound menu, media keys, GNOME/KDE
/// media widgets and lock screens can show and control playback.
/// </summary>
public sealed class MprisMediaSession : IMediaSessionService
{
    private const string BusName = "org.mpris.MediaPlayer2.ohrbob";
    private const string ObjectPathValue = "/org/mpris/MediaPlayer2";
    private const string RootInterface = "org.mpris.MediaPlayer2";
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string TrackObjectPath = "/org/ohrbob/track/1";

    private readonly object _gate = new();
    private readonly Handler _handler;
    private DBusConnection? _connection;
    private MediaSessionInfo _info = new("Ohrbob", "Ohrbob", null, false, 0, 0);
    private string? _lastPlaybackStatus;
    private string? _lastMetadataKey;
    private bool _disposed;

    private MprisMediaSession() => _handler = new Handler(this);

    /// <summary>Creates the session and connects to the bus in the background.</summary>
    public static MprisMediaSession Create()
    {
        var session = new MprisMediaSession();
        _ = session.InitializeAsync();
        return session;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var address = DBusAddress.Session;
            if (string.IsNullOrEmpty(address))
            {
                Log.Warning("No D-Bus session address; MPRIS media controls are unavailable");
                return;
            }

            var connection = new DBusConnection(address);
            await connection.ConnectAsync();

            // Register the object handler *before* acquiring the well-known name.
            // Acquiring the name makes it visible to every MPRIS client (GNOME
            // Shell, media-key daemons, ...) at once, and they immediately query
            // us with Properties.GetAll. If the handler isn't in place yet those
            // requests get UnknownMethod, the client's GDBusProxy init fails, and
            // (for GNOME Shell) the player is dropped from the media controls for
            // the rest of the session.
            connection.AddMethodHandler(_handler);
            await connection.RequestNameAsync(BusName, RequestNameOptions.ReplaceExisting);
            _connection = connection;
            PublishChanged(force: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register the MPRIS media session");
        }
    }

    public void Update(MediaSessionInfo info)
    {
        lock (_gate)
        {
            _info = info;
        }

        PublishChanged(force: false);
    }

    private void PublishChanged(bool force)
    {
        var connection = _connection;
        if (connection == null)
        {
            return;
        }

        string playbackStatus;
        string metadataKey;
        lock (_gate)
        {
            playbackStatus = _info.IsPlaying ? "Playing" : "Paused";
            metadataKey = string.Join('\u0000', _info.Title, _info.Artist, _info.CoverUrl ?? "", _info.DurationSeconds);
        }

        var changed = new Dictionary<string, VariantValue>();
        if (force || playbackStatus != _lastPlaybackStatus)
        {
            changed["PlaybackStatus"] = VariantValue.String(playbackStatus);
            _lastPlaybackStatus = playbackStatus;
        }

        if (force || metadataKey != _lastMetadataKey)
        {
            changed["Metadata"] = BuildMetadata();
            _lastMetadataKey = metadataKey;
        }

        if (changed.Count == 0)
        {
            return;
        }

        try
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(
                destination: null!,
                path: ObjectPathValue,
                @interface: PropertiesInterface,
                member: "PropertiesChanged",
                signature: "sa{sv}as");
            writer.WriteString(PlayerInterface);
            writer.WriteDictionary(changed);
            writer.WriteArray(Array.Empty<string>());
            connection.TrySendMessage(writer.CreateMessage());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to publish an MPRIS update");
        }
    }

    private VariantValue BuildMetadata()
    {
        MediaSessionInfo info;
        lock (_gate)
        {
            info = _info;
        }

        var metadata = new Dict<string, VariantValue>();
        metadata.Add("mpris:trackid", VariantValue.ObjectPath(new ObjectPath(TrackObjectPath)));
        metadata.Add("xesam:title", VariantValue.String(info.Title));

        if (!string.IsNullOrWhiteSpace(info.Artist))
        {
            metadata.Add("xesam:artist", VariantValue.Array(new[] { info.Artist }));
            metadata.Add("xesam:album", VariantValue.String(info.Artist));
        }

        if (info.DurationSeconds > 0)
        {
            metadata.Add("mpris:length", VariantValue.Int64(info.DurationSeconds * 1_000_000L));
        }

        if (!string.IsNullOrEmpty(info.CoverUrl))
        {
            metadata.Add("mpris:artUrl", VariantValue.String(info.CoverUrl));
        }

        return metadata.AsVariantValue();
    }

    private VariantValue GetProperty(string @interface, string name)
    {
        lock (_gate)
        {
            if (@interface == PlayerInterface)
            {
                switch (name)
                {
                    case "PlaybackStatus":
                        return VariantValue.String(_info.IsPlaying ? "Playing" : "Paused");
                    case "LoopStatus":
                        return VariantValue.String("None");
                    case "Rate":
                        return VariantValue.Double(1.0);
                    case "Shuffle":
                        return VariantValue.Bool(false);
                    case "Metadata":
                        return BuildMetadata();
                    case "Volume":
                        return VariantValue.Double(1.0);
                    case "Position":
                        return VariantValue.Int64(_info.PositionSeconds * 1_000_000L);
                    case "MinimumRate":
                        return VariantValue.Double(1.0);
                    case "MaximumRate":
                        return VariantValue.Double(1.0);
                    case "CanGoNext":
                        return VariantValue.Bool(true);
                    case "CanGoPrevious":
                        return VariantValue.Bool(false);
                    case "CanPlay":
                        return VariantValue.Bool(true);
                    case "CanPause":
                        return VariantValue.Bool(true);
                    case "CanSeek":
                        return VariantValue.Bool(true);
                    case "CanControl":
                        return VariantValue.Bool(true);
                }
            }
            else if (@interface == RootInterface)
            {
                switch (name)
                {
                    case "CanQuit":
                        return VariantValue.Bool(false);
                    case "CanRaise":
                        return VariantValue.Bool(false);
                    case "HasTrackList":
                        return VariantValue.Bool(false);
                    case "Identity":
                        return VariantValue.String("Ohrbob");
                    case "DesktopEntry":
                        return VariantValue.String("ohrbob");
                    case "SupportedUriSchemes":
                        return VariantValue.Array(Array.Empty<string>());
                    case "SupportedMimeTypes":
                        return VariantValue.Array(new[] { "audio/mpeg" });
                }
            }
        }

        throw new ArgumentException($"Unknown property '{name}' on interface '{@interface}'");
    }

    private Dictionary<string, VariantValue> GetAllProperties(string @interface)
    {
        var result = new Dictionary<string, VariantValue>();

        if (@interface == PlayerInterface)
        {
            foreach (var name in new[]
            {
                "PlaybackStatus", "LoopStatus", "Rate", "Shuffle", "Metadata", "Volume",
                "Position", "MinimumRate", "MaximumRate", "CanGoNext", "CanGoPrevious",
                "CanPlay", "CanPause", "CanSeek", "CanControl",
            })
            {
                result[name] = GetProperty(@interface, name);
            }
        }
        else if (@interface == RootInterface)
        {
            foreach (var name in new[]
            {
                "CanQuit", "CanRaise", "HasTrackList", "Identity", "DesktopEntry",
                "SupportedUriSchemes", "SupportedMimeTypes",
            })
            {
                result[name] = GetProperty(@interface, name);
            }
        }

        return result;
    }

    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? TogglePlayPauseRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler<int>? SeekToRequested;
    public event EventHandler<int>? SeekByRequested;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var connection = _connection;
        _connection = null;
        if (connection != null)
        {
            connection.RemoveMethodHandler(ObjectPathValue);
            try { connection.ReleaseNameAsync(BusName).GetAwaiter().GetResult(); } catch { /* ignore */ }
            connection.Dispose();
        }
    }

    private sealed class Handler : IPathMethodHandler
    {
        private readonly MprisMediaSession _session;

        public Handler(MprisMediaSession session) => _session = session;

        public string Path => ObjectPathValue;
        public bool HandlesChildPaths => false;

        private static ReadOnlyMemory<byte> RootXml { get; } =
            """
            <interface name="org.mpris.MediaPlayer2">
              <method name="Raise"/>
              <method name="Quit"/>
              <property name="CanQuit" type="b" access="read"/>
              <property name="CanRaise" type="b" access="read"/>
              <property name="HasTrackList" type="b" access="read"/>
              <property name="Identity" type="s" access="read"/>
              <property name="DesktopEntry" type="s" access="read"/>
              <property name="SupportedUriSchemes" type="as" access="read"/>
              <property name="SupportedMimeTypes" type="as" access="read"/>
            </interface>
            """u8.ToArray();

        private static ReadOnlyMemory<byte> PlayerXml { get; } =
            """
            <interface name="org.mpris.MediaPlayer2.Player">
              <method name="Next"/>
              <method name="Previous"/>
              <method name="Pause"/>
              <method name="PlayPause"/>
              <method name="Stop"/>
              <method name="Play"/>
              <method name="Seek">
                <arg direction="in" type="x" name="Offset"/>
              </method>
              <method name="SetPosition">
                <arg direction="in" type="o" name="TrackId"/>
                <arg direction="in" type="x" name="Position"/>
              </method>
              <method name="OpenUri">
                <arg direction="in" type="s" name="Uri"/>
              </method>
              <property name="PlaybackStatus" type="s" access="read"/>
              <property name="LoopStatus" type="s" access="readwrite"/>
              <property name="Rate" type="d" access="readwrite"/>
              <property name="Shuffle" type="b" access="readwrite"/>
              <property name="Metadata" type="a{sv}" access="read"/>
              <property name="Volume" type="d" access="readwrite"/>
              <property name="Position" type="x" access="read"/>
              <property name="MinimumRate" type="d" access="read"/>
              <property name="MaximumRate" type="d" access="read"/>
              <property name="CanGoNext" type="b" access="read"/>
              <property name="CanGoPrevious" type="b" access="read"/>
              <property name="CanPlay" type="b" access="read"/>
              <property name="CanPause" type="b" access="read"/>
              <property name="CanSeek" type="b" access="read"/>
              <property name="CanControl" type="b" access="read"/>
            </interface>
            """u8.ToArray();

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml([RootXml, PlayerXml]);
                return default;
            }

            var request = context.Request;
            switch (request.InterfaceAsString)
            {
                case PropertiesInterface:
                    return _session.HandleProperties(context);
                case RootInterface:
                    return _session.HandleRoot(context);
                case PlayerInterface:
                    return _session.HandlePlayer(context);
                default:
                    context.ReplyUnknownMethodError();
                    return default;
            }
        }
    }

    private ValueTask HandleProperties(MethodContext context)
    {
        var request = context.Request;
        switch (request.MemberAsString)
        {
            case "Get":
            {
                var reader = request.GetBodyReader();
                var iface = reader.ReadString();
                var name = reader.ReadString();
                VariantValue value;
                try
                {
                    value = GetProperty(iface, name);
                }
                catch (ArgumentException)
                {
                    context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", $"Unknown property '{name}'");
                    return default;
                }

                using var writer = context.CreateReplyWriter("v");
                writer.WriteVariant(value);
                context.Reply(writer.CreateMessage());
                return default;
            }
            case "GetAll":
            {
                var reader = request.GetBodyReader();
                var iface = reader.ReadString();
                using var writer = context.CreateReplyWriter("a{sv}");
                writer.WriteDictionary(GetAllProperties(iface));
                context.Reply(writer.CreateMessage());
                return default;
            }
            case "Set":
            {
                // Volume/Rate/LoopStatus/Shuffle writes are accepted but ignored.
                using var writer = context.CreateReplyWriter("");
                context.Reply(writer.CreateMessage());
                return default;
            }
            default:
                context.ReplyUnknownMethodError();
                return default;
        }
    }

    private ValueTask HandleRoot(MethodContext context)
    {
        var request = context.Request;
        if (request.MemberAsString is "Raise" or "Quit")
        {
            using var writer = context.CreateReplyWriter("");
            context.Reply(writer.CreateMessage());
            return default;
        }

        context.ReplyUnknownMethodError();
        return default;
    }

    private ValueTask HandlePlayer(MethodContext context)
    {
        var request = context.Request;
        switch (request.MemberAsString)
        {
            case "Play":
                PlayRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "Pause":
            case "Stop":
                PauseRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "PlayPause":
                TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "Next":
                NextRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "Previous":
                PreviousRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "Seek":
            {
                var reader = request.GetBodyReader();
                var offset = reader.ReadInt64();
                SeekByRequested?.Invoke(this, (int)(offset / 1_000_000L));
                break;
            }
            case "SetPosition":
            {
                var reader = request.GetBodyReader();
                reader.ReadObjectPathAsString();
                var position = reader.ReadInt64();
                SeekToRequested?.Invoke(this, (int)(position / 1_000_000L));
                break;
            }
            case "OpenUri":
                break;
            default:
                context.ReplyUnknownMethodError();
                return default;
        }

        using var writer = context.CreateReplyWriter("");
        context.Reply(writer.CreateMessage());
        return default;
    }
}
