using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Ohrbob.App.Services;

namespace Ohrbob.App.Tests.Services;

[TestFixture]
public class SoundFlowAudioPlayerLifecycleTests
{
    [Test]
    public void Stop_DuringInitialization_DoesNotDisposeTheSourceStream()
    {
        var path = Path.GetTempFileName();
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var player = new SoundFlowAudioPlayer();

        SetField(player, "_localStream", source);
        SetField(player, "_initializingCount", 1);

        player.Stop();

        Assert.DoesNotThrow(() => source.ReadByte(),
            "Stop must leave the source alive while SoundFlow is still initializing it");

        // Let normal disposal run so the timer and stream do not outlive the test.
        SetField(player, "_initializingCount", 0);
        player.Dispose();
        try { File.Delete(path); } catch { }
    }

    private static void SetField<T>(SoundFlowAudioPlayer player, string name, T value)
    {
        var field = typeof(SoundFlowAudioPlayer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"missing field {name}");
        field!.SetValue(player, value);
    }
}
