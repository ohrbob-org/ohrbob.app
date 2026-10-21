using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Ohrbob.App.Android.Media;

/// <summary>
/// Foreground service that keeps the media notification alive (and playback
/// running) while the app is in the background. The actual media session lives in
/// <see cref="AndroidMediaSession"/>; this service just hosts the foreground
/// notification and forwards notification button taps back to the session.
/// </summary>
[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class MediaPlaybackService : Service
{
    public const int NotificationId = 4711;

    public const string ActionTogglePlayPause = "com.ohrbob.app.action.TOGGLE_PLAY_PAUSE";
    public const string ActionNext = "com.ohrbob.app.action.NEXT";
    public const string ActionPrevious = "com.ohrbob.app.action.PREVIOUS";

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var session = AndroidMediaSession.Current;
        if (session == null)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        if (intent?.Action is { } action)
        {
            session.HandleNotificationAction(action);
        }

        // Must always promote to foreground after startForegroundService(), no matter
        // the playback state. The playback state can flip between when the service is
        // started and when this runs, and skipping StartForeground() then makes Android
        // kill the app with ForegroundServiceDidNotStartInTimeException (~30s later).
        StartForeground(NotificationId, session.BuildNotification());

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        AndroidMediaSession.Current?.OnServiceStopped();
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;
}
