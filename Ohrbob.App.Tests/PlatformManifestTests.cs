using System.IO;
using NUnit.Framework;

namespace Ohrbob.App.Tests;

[TestFixture]
public class PlatformManifestTests
{
    private static string RepoFile(string relative) =>
        Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "../../../../", relative));

    [Test]
    public void Android_CameraFeatures_AreOptional()
    {
        var manifest = File.ReadAllText(RepoFile("Ohrbob.App.Android/Properties/AndroidManifest.xml"));

        Assert.That(manifest, Does.Contain("android.permission.CAMERA"));
        Assert.That(manifest, Does.Contain(
            "<uses-feature android:name=\"android.hardware.camera\" android:required=\"false\" />"));
        Assert.That(manifest, Does.Contain(
            "<uses-feature android:name=\"android.hardware.camera.autofocus\" android:required=\"false\" />"),
            "tablets without autofocus must still be able to install");
    }

    [Test]
    public void Ios_HasCameraUsageDescription()
    {
        var plist = File.ReadAllText(RepoFile("Ohrbob.App.iOS/Info.plist"));

        Assert.That(plist, Does.Contain("NSCameraUsageDescription"));
    }
}
