using System;
using System.Threading.Tasks;
using Ohrbob.App.Services;

namespace Ohrbob.App.Tests.ViewModels;

/// <summary>
/// Test double for <see cref="IUriLauncher"/> that records the last URI it was
/// asked to launch, so tests can assert which page the app opened.
/// </summary>
public class RecordingUriLauncher : IUriLauncher
{
    public Uri? Last { get; private set; }

    public Task LaunchAsync(Uri uri)
    {
        Last = uri;
        return Task.CompletedTask;
    }
}
