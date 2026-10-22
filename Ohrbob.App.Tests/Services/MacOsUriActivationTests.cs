using System;
using Avalonia.Controls.ApplicationLifetimes;
using NUnit.Framework;
using Ohrbob.App.Services;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Tests.Services;

/// <summary>
/// macOS delivers a registered <c>ohrbob://</c> link as an Apple Event
/// (<c>kAEGetURL</c>/<c>openURLs:</c>), which Avalonia surfaces through
/// <see cref="IActivatableLifetime"/> as a
/// <see cref="ProtocolActivatedEventArgs"/>. <see cref="MacOsUriActivation.FromActivation"/>
/// is the pure translation of that event into the same URI string the argv path
/// produces.
/// </summary>
[TestFixture]
public class MacOsUriActivationTests
{
    [Test]
    public void FromActivation_ReturnsTheUriForAProtocolActivation()
    {
        var args = new ProtocolActivatedEventArgs(new Uri("ohrbob://connect?server=x&token=y"));

        Assert.That(
            MacOsUriActivation.FromActivation(args),
            Is.EqualTo("ohrbob://connect?server=x&token=y"));
    }

    [Test]
    public void FromActivation_OutputIsParsableAsAConnectDeepLink()
    {
        var args = new ProtocolActivatedEventArgs(
            new Uri("ohrbob://connect?server=https%3A%2F%2Fforward.example&token=tok"));

        var uri = MacOsUriActivation.FromActivation(args);

        Assert.That(uri, Is.Not.Null);
        Assert.That(MainViewModel.TryParseConnectDeepLink(uri!, out var server, out var token), Is.True);
        Assert.That(server, Is.EqualTo("https://forward.example"));
        Assert.That(token, Is.EqualTo("tok"));
    }

    [TestCase(ActivationKind.Reopen)]
    [TestCase(ActivationKind.Background)]
    [TestCase(ActivationKind.File)]
    public void FromActivation_ReturnsNullForNonProtocolActivations(ActivationKind kind)
    {
        Assert.That(MacOsUriActivation.FromActivation(new ActivatedEventArgs(kind)), Is.Null);
    }

    [Test]
    public void FromActivation_ReturnsNullForNull()
    {
        Assert.That(MacOsUriActivation.FromActivation(null), Is.Null);
    }
}
