using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Ohrbob.App.Desktop;

namespace Ohrbob.App.Tests.Desktop;

/// <summary>
/// Exercises the single-instance gate end to end within one process: the first
/// claim becomes the primary and receives forwarded links, a later claim is
/// rejected and can forward a link to the primary. On Windows the gate is a
/// named mutex that is owned per thread, so the forwarding claim is made on a
/// pool thread — matching how a real second launch runs on its own thread.
/// </summary>
[TestFixture]
public class SingleInstanceTests
{
    private static string NewName() => "test-" + Guid.NewGuid().ToString("N");

    [Test]
    public async Task SecondClaimIsNotPrimaryAndItsMessageReachesThePrimary()
    {
        var name = NewName();
        const string uri = "ohrbob://connect?server=https%3A%2F%2Fplayer.ohrbob.org&token=abc";

        using var primary = new SingleInstance(name);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.That(primary.TryAcquire(m => { received.TrySetResult(m); }), Is.True,
            "the first claim must become the primary");

        var forwarded = await Task.Run(() =>
        {
            using var forwarder = new SingleInstance(name);
            if (forwarder.TryAcquire(_ => { }))
            {
                return false;
            }

            forwarder.Send(uri);
            return true;
        });

        Assert.That(forwarded, Is.True, "the second claim must not become the primary");

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.That(completed, Is.SameAs(received.Task), "the primary never received the forwarded URI");
        Assert.That(await received.Task, Is.EqualTo(uri));
    }

    [Test]
    public async Task PrimaryReceivesEveryForwardedMessage()
    {
        var name = NewName();
        using var primary = new SingleInstance(name);

        var first = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        Assert.That(primary.TryAcquire(message =>
        {
            if (System.Threading.Interlocked.Increment(ref count) == 1)
            {
                first.TrySetResult(message);
            }
            else
            {
                second.TrySetResult(message);
            }
        }), Is.True);

        await Task.Run(() =>
        {
            using var forwarder = new SingleInstance(name);
            Assert.That(forwarder.TryAcquire(_ => { }), Is.False);
            forwarder.Send("ohrbob://connect?server=a&token=1");
        });

        await Task.Run(() =>
        {
            using var forwarder = new SingleInstance(name);
            Assert.That(forwarder.TryAcquire(_ => { }), Is.False);
            forwarder.Send("ohrbob://connect?server=b&token=2");
        });

        var firstCompleted = await Task.WhenAny(first.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        var secondCompleted = await Task.WhenAny(second.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.That(firstCompleted, Is.SameAs(first.Task), "the primary never received the first forwarded URI");
        Assert.That(secondCompleted, Is.SameAs(second.Task), "the primary never received the second forwarded URI");
        Assert.That(new[] { await first.Task, await second.Task },
            Is.EquivalentTo(new[] { "ohrbob://connect?server=a&token=1", "ohrbob://connect?server=b&token=2" }));
    }
}
