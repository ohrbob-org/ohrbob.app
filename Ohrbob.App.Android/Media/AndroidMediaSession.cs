using System;
using System.Net.Http;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Support.V4.Media;
using Android.Support.V4.Media.Session;
using AndroidX.Core.App;
using Ohrbob.App.Services.Media;

// MediaSessionCompat is the compatibility media API exposed by AndroidX; the
// binding marks it obsolete, and its builder methods are annotated as nullable,
// so those two binding-only warnings are suppressed here.
#pragma warning disable CS0618, CS8602

namespace Ohrbob.App.Android.Media;

/// <summary>
/// Android half of the OS media integration (issue #4): publishes now-playing
/// metadata and playback state to a <see cref="MediaSessionCompat"/> so the lock
/// screen and the media notification can show and control playback, and raises the
/// matching events when the user drives playback from there.
/// </summary>
public sealed class AndroidMediaSession : IMediaSessionService
{
    private const string ChannelId = "ohrbob_playback";
    private const string SessionTag = "Ohrbob";

    // android.media.metadata.* keys, used directly to avoid binding-name drift.
    private const string MetadataKeyTitle = "android.media.metadata.TITLE";
    private const string MetadataKeyArtist = "android.media.metadata.ARTIST";
    private const string MetadataKeyAlbum = "android.media.metadata.ALBUM";
    private const string MetadataKeyDuration = "android.media.metadata.DURATION";
    private const string MetadataKeyAlbumArt = "android.media.metadata.ALBUM_ART";
    private const string MetadataKeyAlbumArtUri = "android.media.metadata.ALBUM_ART_URI";
    private const string MetadataKeyArt = "android.media.metadata.ART";
    private const string MetadataKeyDisplayIconUri = "android.media.metadata.DISPLAY_ICON_URI";
    private const string MetadataKeyArtUri = "android.media.metadata.ART_URI";

    private static readonly HttpClient ArtworkClient = new();

    public static AndroidMediaSession? Current { get; private set; }

    public MediaSessionCompat.Token SessionToken => _session.SessionToken!;

    private readonly Context _context;
    private readonly MediaSessionCompat _session;
    private readonly Handler _mainHandler = new(Looper.MainLooper!);
    private MediaSessionInfo _info = new("Ohrbob", "Ohrbob", null, false, 0, 0);
    private bool _serviceStarted;
    private bool _disposed;
    private string _lastContentKey = string.Empty;
    private Bitmap? _artwork;
    private string? _artworkUrl;
    private string? _pendingArtworkUrl;

    public AndroidMediaSession(Context context)
    {
        _context = context;
        Current = this;

        EnsureChannel();

        _session = new MediaSessionCompat(context, SessionTag);
        _session.SetCallback(new SessionCallback(this));
        _session.SetFlags(MediaSessionCompat.FlagHandlesMediaButtons | MediaSessionCompat.FlagHandlesTransportControls);
        _session.Active = true;
    }

    public bool IsPlaying => _info.IsPlaying;

    public void Update(MediaSessionInfo info)
    {
        var contentChanged = ContentChanged(info);
        _info = info;

        if (contentChanged)
        {
            // Drop the previous episode's artwork immediately; leaving it in place
            // would show the wrong cover until the new download finishes.
            if (_artworkUrl != info.CoverUrl)
            {
                _artwork = null;
                _artworkUrl = null;
            }

            UpdateMetadata();
        }

        // The position changes every second; keep the playback state fresh but only
        // rebuild the notification / metadata when the displayed content changes.
        UpdatePlaybackState();

        if (contentChanged)
        {
            UpdateNotification();
            _ = LoadArtworkAsync(info.CoverUrl);
        }
    }

    private bool ContentChanged(MediaSessionInfo info)
    {
        var key = string.Join(
            '\u0000', info.Title, info.Artist, info.CoverUrl, info.IsPlaying, info.DurationSeconds);
        if (key == _lastContentKey)
        {
            return false;
        }

        _lastContentKey = key;
        return true;
    }

    private void UpdateMetadata()
    {
        var metadataBuilder = new MediaMetadataCompat.Builder()
            .PutString(MetadataKeyTitle, _info.Title)
            .PutString(MetadataKeyArtist, _info.Artist)
            .PutString(MetadataKeyAlbum, _info.Artist)
            .PutLong(MetadataKeyDuration, _info.DurationSeconds * 1000L);

        if (!string.IsNullOrEmpty(_info.CoverUrl))
        {
            metadataBuilder.PutString(MetadataKeyAlbumArtUri, _info.CoverUrl)
                           .PutString(MetadataKeyArtUri, _info.CoverUrl)
                           .PutString(MetadataKeyDisplayIconUri, _info.CoverUrl);

            if (_artwork != null && _artworkUrl == _info.CoverUrl)
            {
                metadataBuilder.PutBitmap(MetadataKeyAlbumArt, _artwork)
                               .PutBitmap(MetadataKeyArt, _artwork);
            }
        }

        var metadata = metadataBuilder.Build()!;
        _session.SetMetadata(metadata);
    }

    private async Task LoadArtworkAsync(string? coverUrl)
    {
        if (_disposed || string.IsNullOrEmpty(coverUrl))
        {
            return;
        }

        if (_artworkUrl == coverUrl || _pendingArtworkUrl == coverUrl)
        {
            return;
        }

        _pendingArtworkUrl = coverUrl;

        Bitmap? bitmap = null;
        try
        {
            var bytes = await ArtworkClient.GetByteArrayAsync(coverUrl);
            bitmap = DecodeArtwork(bytes);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("Ohrbob", $"Cover art load failed: {ex.Message}");
        }

        _mainHandler.Post(() =>
        {
            if (_pendingArtworkUrl == coverUrl)
            {
                _pendingArtworkUrl = null;
            }

            if (_disposed || bitmap == null || _info.CoverUrl != coverUrl)
            {
                bitmap?.Recycle();
                return;
            }

            _artwork = bitmap;
            _artworkUrl = coverUrl;
            UpdateMetadata();
            UpdateNotification();
        });
    }

    private static Bitmap? DecodeArtwork(byte[] bytes)
    {
        // Keep the bitmap small: the copy in the media session crosses a Binder
        // transaction, and oversized art makes the system drop the metadata.
        const int maxSize = 256;

        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds);

        var sample = 1;
        while (bounds.OutWidth / sample > maxSize * 2 || bounds.OutHeight / sample > maxSize * 2)
        {
            sample *= 2;
        }

        var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, new BitmapFactory.Options
        {
            InSampleSize = sample,
        });

        if (bitmap == null || (bitmap.Width <= maxSize && bitmap.Height <= maxSize))
        {
            return bitmap;
        }

        var scale = Math.Min((float)maxSize / bitmap.Width, (float)maxSize / bitmap.Height);
        var scaled = Bitmap.CreateScaledBitmap(
            bitmap,
            Math.Max(1, (int)(bitmap.Width * scale)),
            Math.Max(1, (int)(bitmap.Height * scale)),
            true);

        if (scaled != null)
        {
            bitmap.Recycle();
            return scaled;
        }

        return bitmap;
    }

    private const string ActionSkipBack = "com.ohrbob.app.action.SKIP_BACK";
    private const string ActionSeekBack = "com.ohrbob.app.action.SEEK_BACK";
    private const string ActionSeekForward = "com.ohrbob.app.action.SEEK_FORWARD";
    private const string ActionSkipForward = "com.ohrbob.app.action.SKIP_FORWARD";

    private void UpdatePlaybackState()
    {
        var stateBuilder = new PlaybackStateCompat.Builder()
            .SetActions(
                PlaybackStateCompat.ActionPlay |
                PlaybackStateCompat.ActionPause |
                PlaybackStateCompat.ActionPlayPause |
                PlaybackStateCompat.ActionSeekTo)
            .SetState(
                _info.IsPlaying ? PlaybackStateCompat.StatePlaying : PlaybackStateCompat.StatePaused,
                _info.PositionSeconds * 1000L,
                1.0f);

        // Android Auto fills the two inner flanking slots with the first custom actions and the
        // two outer slots with the ones added after them. To end up with
        // skip back | seek back | play/pause | seek forward | skip forward,
        // the inner pair is added first and the outer pair afterwards.
        stateBuilder.AddCustomAction(new PlaybackStateCompat.CustomAction.Builder(
            ActionSeekBack, "Seek back", Resource.Drawable.ic_media_seek_back).Build());
        stateBuilder.AddCustomAction(new PlaybackStateCompat.CustomAction.Builder(
            ActionSeekForward, "Seek forward", Resource.Drawable.ic_media_seek_forward).Build());
        stateBuilder.AddCustomAction(new PlaybackStateCompat.CustomAction.Builder(
            ActionSkipBack, "Skip back", Resource.Drawable.ic_media_skip_previous).Build());
        stateBuilder.AddCustomAction(new PlaybackStateCompat.CustomAction.Builder(
            ActionSkipForward, "Skip forward", Resource.Drawable.ic_media_skip_next).Build());

        _session.SetPlaybackState(stateBuilder.Build()!);
    }

    private void UpdateNotification()
    {
        if (_info.IsPlaying && !_serviceStarted)
        {
            var intent = new Intent(_context, typeof(MediaPlaybackService));
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                _context.StartForegroundService(intent);
            }
            else
            {
                _context.StartService(intent);
            }

            _serviceStarted = true;
        }

        try
        {
            NotificationManagerCompat.From(_context)!.Notify(MediaPlaybackService.NotificationId, BuildNotification());
        }
        catch (Exception)
        {
            // Posting notifications can fail if the permission is missing; playback is unaffected.
        }
    }

    public Notification BuildNotification()
    {
        var contentIntent = PendingIntent.GetActivity(
            _context,
            0,
            new Intent(_context, typeof(MainActivity)),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

        var style = new AndroidX.Media.App.NotificationCompat.MediaStyle()
            .SetMediaSession(_session.SessionToken!)
            .SetShowActionsInCompactView(0, 1);

        var playPauseIcon = _info.IsPlaying
            ? Resource.Drawable.ic_media_pause
            : Resource.Drawable.ic_media_play;

        var builder = new NotificationCompat.Builder(_context, ChannelId)
            .SetContentTitle(_info.Title)
            .SetContentText(_info.Artist)
            .SetSmallIcon(Resource.Drawable.ic_stat_ohrbob)
            .SetContentIntent(contentIntent)
            .SetVisibility(NotificationCompat.VisibilityPublic)
            .SetOngoing(_info.IsPlaying)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(false)
            .SetStyle(style)
            .AddAction(BuildAction(playPauseIcon, _info.IsPlaying ? "Pause" : "Play", MediaPlaybackService.ActionTogglePlayPause))
            .AddAction(BuildAction(Resource.Drawable.ic_media_skip_next, "Next", MediaPlaybackService.ActionNext));

        if (_artwork != null && _artworkUrl == _info.CoverUrl)
        {
            builder.SetLargeIcon(_artwork);
        }

        return builder.Build()!;
    }

    private NotificationCompat.Action BuildAction(int icon, string title, string action)
    {
        var intent = new Intent(_context, typeof(MediaPlaybackService));
        intent.SetAction(action);
        var requestCode = action.GetHashCode();
        var flags = PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent;
        // Start the service as a foreground service so the action still works when the
        // app is in the background (a plain startService is refused by Android 8+).
        var pendingIntent = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? PendingIntent.GetForegroundService(_context, requestCode, intent, flags)!
            : PendingIntent.GetService(_context, requestCode, intent, flags)!;
        return new NotificationCompat.Action(icon, title, pendingIntent);
    }

    internal void HandleNotificationAction(string action)
    {
        switch (action)
        {
            case MediaPlaybackService.ActionTogglePlayPause:
                TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);
                break;
            case MediaPlaybackService.ActionNext:
                NextRequested?.Invoke(this, EventArgs.Empty);
                break;
            case MediaPlaybackService.ActionPrevious:
                PreviousRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    internal void OnServiceStopped() => _serviceStarted = false;

    private void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = (NotificationManager?)_context.GetSystemService(Context.NotificationService);
        if (manager == null)
        {
            return;
        }

        var channel = new NotificationChannel(ChannelId, "Playback", NotificationImportance.Low)
        {
            Description = "Now playing and playback controls",
        };
        manager.CreateNotificationChannel(channel);
    }

    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? TogglePlayPauseRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler<int>? SeekToRequested;
    public event EventHandler<int>? SeekByRequested;
    public event EventHandler<string?>? PlayFromMediaIdRequested;

    public void Dispose()
    {
        _disposed = true;
        if (Current == this)
        {
            Current = null;
        }

        _context.StopService(new Intent(_context, typeof(MediaPlaybackService)));
        NotificationManagerCompat.From(_context).Cancel(MediaPlaybackService.NotificationId);
        _session.Active = false;
        _session.Release();
        _session.Dispose();
    }

    private sealed class SessionCallback : MediaSessionCompat.Callback
    {
        private readonly AndroidMediaSession _owner;

        public SessionCallback(AndroidMediaSession owner) => _owner = owner;

        public override void OnPlay() => _owner.PlayRequested?.Invoke(_owner, EventArgs.Empty);

        public override void OnPause() => _owner.PauseRequested?.Invoke(_owner, EventArgs.Empty);

        public override void OnStop() => _owner.PauseRequested?.Invoke(_owner, EventArgs.Empty);

        public override void OnSkipToNext() => _owner.NextRequested?.Invoke(_owner, EventArgs.Empty);

        public override void OnSkipToPrevious() => _owner.PreviousRequested?.Invoke(_owner, EventArgs.Empty);

        public override void OnSeekTo(long pos) => _owner.SeekToRequested?.Invoke(_owner, (int)(pos / 1000));

        public override void OnRewind() => _owner.SeekByRequested?.Invoke(_owner, -30);

        public override void OnFastForward() => _owner.SeekByRequested?.Invoke(_owner, 30);

        public override void OnPlayFromMediaId(string? mediaId, Bundle? extras) => _owner.PlayFromMediaIdRequested?.Invoke(_owner, mediaId);

        public override void OnCustomAction(string? action, Bundle? extras)
        {
            switch (action)
            {
                case ActionSkipBack:
                    _owner.PreviousRequested?.Invoke(_owner, EventArgs.Empty);
                    break;
                case ActionSeekBack:
                    _owner.SeekByRequested?.Invoke(_owner, -30);
                    break;
                case ActionSeekForward:
                    _owner.SeekByRequested?.Invoke(_owner, 30);
                    break;
                case ActionSkipForward:
                    _owner.NextRequested?.Invoke(_owner, EventArgs.Empty);
                    break;
            }
        }
    }
}
