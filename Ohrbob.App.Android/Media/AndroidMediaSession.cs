using System;
using Android.App;
using Android.Content;
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
    private const string MetadataKeyAlbumArtUri = "android.media.metadata.ALBUM_ART_URI";
    private const string MetadataKeyDisplayIconUri = "android.media.metadata.DISPLAY_ICON_URI";
    private const string MetadataKeyArtUri = "android.media.metadata.ART_URI";

    public static AndroidMediaSession? Current { get; private set; }

    public MediaSessionCompat.Token SessionToken => _session.SessionToken!;

    private readonly Context _context;
    private readonly MediaSessionCompat _session;
    private MediaSessionInfo _info = new("Ohrbob", "Ohrbob", null, false, 0, 0);
    private bool _serviceStarted;
    private string _lastContentKey = string.Empty;

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
            UpdateMetadata();
        }

        // The position changes every second; keep the playback state fresh but only
        // rebuild the notification / metadata when the displayed content changes.
        UpdatePlaybackState();

        if (contentChanged)
        {
            UpdateNotification();
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
        }

        var metadata = metadataBuilder.Build()!;
        _session.SetMetadata(metadata);
    }

    private void UpdatePlaybackState()
    {
        var stateBuilder = new PlaybackStateCompat.Builder()
            .SetActions(
                PlaybackStateCompat.ActionPlay |
                PlaybackStateCompat.ActionPause |
                PlaybackStateCompat.ActionPlayPause |
                PlaybackStateCompat.ActionSkipToNext |
                PlaybackStateCompat.ActionSkipToPrevious |
                PlaybackStateCompat.ActionRewind |
                PlaybackStateCompat.ActionFastForward |
                PlaybackStateCompat.ActionSeekTo)
            .SetState(
                _info.IsPlaying ? PlaybackStateCompat.StatePlaying : PlaybackStateCompat.StatePaused,
                _info.PositionSeconds * 1000L,
                1.0f);

        // Android Auto needs custom actions to explicitly show fast forward / rewind buttons
        stateBuilder.AddCustomAction(
            new PlaybackStateCompat.CustomAction.Builder(
                "ACTION_REWIND",
                "Rewind",
                global::Android.Resource.Drawable.IcMediaRew // standard Android rewind icon
            ).Build());

        stateBuilder.AddCustomAction(
            new PlaybackStateCompat.CustomAction.Builder(
                "ACTION_FAST_FORWARD",
                "Fast Forward",
                global::Android.Resource.Drawable.IcMediaFf // standard Android fast-forward icon
            ).Build());

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
            ? global::Android.Resource.Drawable.IcMediaPause
            : global::Android.Resource.Drawable.IcMediaPlay;

        return new NotificationCompat.Builder(_context, ChannelId)
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
            .AddAction(BuildAction(global::Android.Resource.Drawable.IcMediaNext, "Next", MediaPlaybackService.ActionNext))
            .Build()!;
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
            if (action == "ACTION_REWIND")
                _owner.SeekByRequested?.Invoke(_owner, -1);
            else if (action == "ACTION_FAST_FORWARD")
                _owner.SeekByRequested?.Invoke(_owner, 1);
        }
    }
}
