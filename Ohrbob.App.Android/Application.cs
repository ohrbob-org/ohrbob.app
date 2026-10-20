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
            base.OnCreate();

            // Register the Android media session before Avalonia builds the app,
            // so the lock screen / notification can show and control playback.
            MediaSessionHost.Factory = () => new Media.AndroidMediaSession(this);
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}

