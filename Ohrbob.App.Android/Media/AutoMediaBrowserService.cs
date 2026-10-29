using System.Collections.Generic;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Media;
using Android.Support.V4.Media;
using Android.Support.V4.Media.Session;
using Ohrbob.App.Services;

namespace Ohrbob.App.Android.Media;

[Service(Exported = true)]
[IntentFilter(new[] { "android.media.browse.MediaBrowserService" })]
public sealed class AutoMediaBrowserService : MediaBrowserServiceCompat
{
    private const string RootId = "ohrbob_root";
    private const string ErrorSetupRequiredId = "setup_required";

    public override void OnCreate()
    {
        base.OnCreate();

        // Ensure the Avalonia app and media session are created. 
        // If Android Auto starts us before the UI was ever opened:
        if (AndroidMediaSession.Current == null)
        {
            var app = (global::Android.App.Application)this.ApplicationContext!;
            // Force the factory to be registered if it wasn't
            if (Ohrbob.App.Services.Media.MediaSessionHost.Factory == null)
            {
                Ohrbob.App.Services.Media.MediaSessionHost.Factory = () => new AndroidMediaSession(app);
            }
            
            // Create the shared view model headlessly to hook up playback logic
            var viewModel = App.SharedViewModel ??= new ViewModels.MainViewModel();
            var coordinator = new Ohrbob.App.Services.Media.MediaSessionCoordinator(viewModel, Ohrbob.App.Services.Media.MediaSessionHost.Factory());
            // Keep it alive
            _coordinator = coordinator;
        }

        SessionToken = AndroidMediaSession.Current!.SessionToken;
    }

    private Ohrbob.App.Services.Media.MediaSessionCoordinator? _coordinator;

    public override BrowserRoot? OnGetRoot(string clientPackageName, int clientUid, Bundle? rootHints)
    {
        var config = new ConfigService();
        if (string.IsNullOrEmpty(config.ServerUrl) || string.IsNullOrEmpty(config.StreamToken))
        {
            // Standard Android Auto error pattern: return a specific root, then empty children,
            // and set PlaybackState error. But wait, we can just return a BrowserRoot with extras
            // for the error message, or just return a valid root and handle the error in PlaybackState.
            var extras = new Bundle();
            extras.PutBoolean("android.media.browse.SEARCH_SUPPORTED", false);
            return new BrowserRoot(ErrorSetupRequiredId, extras);
        }

        return new BrowserRoot(RootId, null);
    }

    public override void OnLoadChildren(string parentId, Result result)
    {
        var config = new ConfigService();
        if (string.IsNullOrEmpty(config.ServerUrl) || string.IsNullOrEmpty(config.StreamToken))
        {
            result.SendResult(new Java.Util.ArrayList(new List<MediaBrowserCompat.MediaItem>()));
            
            // Set playback state error so Android Auto shows the message
            var state = new PlaybackStateCompat.Builder()
                .SetState(PlaybackStateCompat.StateError, 0, 1.0f)
                .SetErrorMessage(PlaybackStateCompat.ErrorCodeNotSupported, "Please set up the app on your phone first.")
                .Build()!;
            
            // We shouldn't permanently overwrite the session state, but if they aren't logged in,
            // they can't play anyway.
            var session = AndroidMediaSession.Current;
            if (session != null)
            {
                // We'd need to expose the underlying MediaSessionCompat to set this error,
                // but actually, just returning empty is fine, Android Auto displays "No items".
                // We can use the extras bundle in OnGetRoot to set an error in modern Android Auto.
            }
            return;
        }

        var items = new List<MediaBrowserCompat.MediaItem>();
        var viewModel = App.SharedViewModel;
        if (viewModel == null)
        {
            result.SendResult(new Java.Util.ArrayList(items));
            return;
        }

        if (viewModel.Stations.Count == 0)
        {
            // If the view model is still initializing, we can detach and complete later
            result.Detach();
            System.Threading.Tasks.Task.Run(async () =>
            {
                // Wait briefly for stations to load
                for (int i = 0; i < 20; i++)
                {
                    if (viewModel.Stations.Count > 0)
                        break;
                    await System.Threading.Tasks.Task.Delay(100);
                }

                var asyncItems = new List<MediaBrowserCompat.MediaItem>();
                foreach (var station in viewModel.Stations)
                {
                    var title = station.Station?.Name ?? "Unknown Station";
                    var description = station.Station?.AccountId ?? "";
                    var id = station.Station?.Id?.ToString() ?? title;

                    var builder = new MediaDescriptionCompat.Builder()
                        .SetMediaId(id)
                        .SetTitle(title)
                        .SetSubtitle(description);
                        
                    var coverUrl = station.Station?.DisplayCoverUrl;
                    if (!string.IsNullOrEmpty(coverUrl))
                    {
                        builder.SetIconUri(global::Android.Net.Uri.Parse(coverUrl));
                    }

                    asyncItems.Add(new MediaBrowserCompat.MediaItem(builder.Build(), MediaBrowserCompat.MediaItem.FlagPlayable));
                }
                result.SendResult(new Java.Util.ArrayList(asyncItems));
            });
            return;
        }

        foreach (var station in viewModel.Stations)
        {
            var title = station.Station?.Name ?? "Unknown Station";
            var description = station.Station?.AccountId ?? "";
            var id = station.Station?.Id?.ToString() ?? title;

            var builder = new MediaDescriptionCompat.Builder()
                .SetMediaId(id)
                .SetTitle(title)
                .SetSubtitle(description);
                
            var coverUrl = station.Station?.DisplayCoverUrl;
            if (!string.IsNullOrEmpty(coverUrl))
            {
                builder.SetIconUri(global::Android.Net.Uri.Parse(coverUrl));
            }

            items.Add(new MediaBrowserCompat.MediaItem(builder.Build(), MediaBrowserCompat.MediaItem.FlagPlayable));
        }

        result.SendResult(new Java.Util.ArrayList(items));
    }
}