using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Ohrbob.App.Services;

namespace Ohrbob.App.Android;

[Activity(
    Label = "Ohrbob Player",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "ohrbob",
    DataHost = "connect",
    AutoVerify = false)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PublishDeepLink();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        PublishDeepLink();
    }

    private void PublishDeepLink()
    {
        var uri = Intent?.DataString;
        if (!string.IsNullOrWhiteSpace(uri))
        {
            DeepLinkRouter.Publish(uri);
        }
    }
}
