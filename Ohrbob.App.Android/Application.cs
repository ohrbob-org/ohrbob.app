using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Ohrbob.App.Services.Media;

namespace Ohrbob.App.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            // Register the Android media session before Avalonia builds the app.
            // base.OnCreate() runs Avalonia's initialization, which constructs the
            // app and wires up whatever MediaSessionHost.Factory returns — so the
            // factory must be set first, otherwise the media session is never
            // created and nothing shows on the lock screen / in notifications.
            // Android Auto may already have created the session while starting the
            // media browser service; reuse it so both sides share one session token.
            MediaSessionHost.Factory = () => Media.AndroidMediaSession.Current ?? new Media.AndroidMediaSession(this);

            base.OnCreate();
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}

