using System;

namespace Ohrbob.App.Services;

/// <summary>
/// Bridges platform deep-link delivery (desktop argv, Android intents, iOS
/// <c>OpenUrl</c>) to the shared view model. A URI published before a subscriber
/// exists is held as pending and delivered as soon as one subscribes, so a
/// cold-start link is never lost.
/// </summary>
public static class DeepLinkRouter
{
    private static readonly object Gate = new();
    private static Action<string>? _handler;
    private static string? _pending;

    public static void Publish(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        Action<string>? handler;
        lock (Gate)
        {
            handler = _handler;
            if (handler is null)
            {
                _pending = uri;
                return;
            }
        }

        handler(uri);
    }

    public static void Subscribe(Action<string> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        string? pending;
        lock (Gate)
        {
            _handler = handler;
            pending = _pending;
            _pending = null;
        }

        if (pending is not null)
        {
            handler(pending);
        }
    }
}
