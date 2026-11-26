using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ohrbob.App.Models;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Views;

public partial class PlaylistView : UserControl
{
    private const double DragThreshold = 8;
    private const double InertiaVelocityCutoff = 60;
    private const ulong StaleVelocityMs = 120;

    private readonly DispatcherTimer _longPressTimer;
    private readonly DispatcherTimer _inertiaTimer;
    private readonly TranslateTransform _playlistBounce = new();
    private readonly Transitions _bounceTransitions;
    private PlaylistItem? _pressedItem;
    private ScrollViewer? _scroll;
    private Control? _scrollContent;
    private bool _trackingPress;
    private bool _dragging;
    private Point _dragOrigin;
    private double _dragStartOffset;
    private double _lastPointerY;
    private ulong _lastPointerTimestamp;
    private double _offsetVelocity;
    private double _inertiaOffset;
    private double _inertiaVelocity;
    private long _inertiaLastTimestamp;

    public PlaylistView()
    {
        InitializeComponent();

        _longPressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(520),
        };
        _longPressTimer.Tick += OnLongPressElapsed;

        _inertiaTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(15),
        };
        _inertiaTimer.Tick += OnInertiaTick;

        _bounceTransitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(320),
                Easing = new CubicEaseOut(),
            },
        };

        PlaylistList.TemplateApplied += (_, _) => AttachScrollBehavior();
        PlaylistList.Loaded += (_, _) => AttachScrollBehavior();

        DetachedFromVisualTree += (_, _) =>
        {
            _inertiaTimer.Stop();
            _longPressTimer.Stop();
        };
    }

    private void AttachScrollBehavior()
    {
        if (_scroll is not null) return;

        _scroll = PlaylistList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scroll is null) return;

        _scrollContent = _scroll.Content as Control;
        if (_scrollContent is not null)
            _scrollContent.RenderTransform = _playlistBounce;

        _scroll.AddHandler(
            PointerPressedEvent, OnScrollPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        _scroll.AddHandler(
            PointerMovedEvent, OnScrollPointerMoved,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        _scroll.AddHandler(
            PointerReleasedEvent, OnScrollPointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnScrollPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_scroll is null) return;

        var point = e.GetCurrentPoint(_scroll);
        if (e.Pointer.Type == PointerType.Mouse && !point.Properties.IsLeftButtonPressed) return;
        if (e.Pointer.Type is not (PointerType.Mouse or PointerType.Touch or PointerType.Pen)) return;
        if (e.Source is Visual source &&
            source.FindAncestorOfType<ScrollBar>() is not null)
            return;

        StopInertia();
        _trackingPress = true;
        _dragging = false;
        _dragOrigin = e.GetPosition(_scroll);
        _dragStartOffset = _scroll.Offset.Y;
        _lastPointerY = _dragOrigin.Y;
        _lastPointerTimestamp = e.Timestamp;
        _offsetVelocity = 0;
        _playlistBounce.Transitions = null;
        _playlistBounce.Y = 0;
    }

    private void OnScrollPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_scroll is null || !_trackingPress) return;

        var position = e.GetPosition(_scroll);
        var dy = position.Y - _dragOrigin.Y;

        if (e.Pointer.Type == PointerType.Mouse)
        {
            // A missed release must not leave the list following the cursor.
            if (!e.GetCurrentPoint(_scroll).Properties.IsLeftButtonPressed)
            {
                EndMouseDrag();
                return;
            }

            if (!_dragging)
            {
                if (Math.Abs(dy) < DragThreshold ||
                    Math.Abs(dy) <= Math.Abs(position.X - _dragOrigin.X))
                    return;

                _dragging = true;
                CancelLongPress();
                e.Pointer.Capture(_scroll);
            }

            ApplyDrag(dy);
            TrackVelocity(position.Y, e.Timestamp);
            e.Handled = true;
        }
        else
        {
            // A finger that starts panning must not also trigger the
            // long-press action when it stays down for half a second.
            if (Math.Abs(dy) >= DragThreshold)
                CancelLongPress();

            // Touch and pen panning stays with the native scroll recognizer;
            // only add the rubber-band overscroll at the edges.
            _playlistBounce.Y = RubberBand.Offset(_scroll.Offset.Y, MaxOffset(), dy);
        }
    }

    private void OnScrollPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_trackingPress) return;

        var wasDragging = _dragging;
        var velocity = _offsetVelocity;
        _trackingPress = false;
        _dragging = false;

        if (e.Pointer.Type == PointerType.Mouse)
        {
            if (!wasDragging) return;

            e.Handled = true;
            if (e.Timestamp - _lastPointerTimestamp > StaleVelocityMs)
                velocity = 0;
            StartInertiaOrSpringBack(velocity);
        }
        else
        {
            SpringBack();
        }
    }

    private void ApplyDrag(double dy)
    {
        if (_scroll is null) return;

        var maximum = MaxOffset();
        var raw = _dragStartOffset - dy;

        if (raw < 0)
        {
            _scroll.Offset = default;
            _playlistBounce.Y = RubberBand.Distance(-raw);
        }
        else if (raw > maximum)
        {
            _scroll.Offset = new Vector(0, maximum);
            _playlistBounce.Y = -RubberBand.Distance(raw - maximum);
        }
        else
        {
            _scroll.Offset = new Vector(0, raw);
            _playlistBounce.Y = 0;
        }
    }

    private void StartInertiaOrSpringBack(double velocity)
    {
        if (_scroll is null) return;

        var offset = _scroll.Offset.Y;
        var maximum = MaxOffset();
        if (offset <= 0.5 || offset >= maximum - 0.5 ||
            Math.Abs(velocity) < InertiaVelocityCutoff)
        {
            SpringBack();
            return;
        }

        _inertiaOffset = offset;
        _inertiaVelocity = velocity;
        _inertiaLastTimestamp = Stopwatch.GetTimestamp();
        _inertiaTimer.Start();
    }

    private void OnInertiaTick(object? sender, EventArgs e)
    {
        if (_scroll is null)
        {
            StopInertia();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var seconds = (now - _inertiaLastTimestamp) / (double)Stopwatch.Frequency;
        _inertiaLastTimestamp = now;
        if (seconds <= 0) return;

        var result = ScrollInertia.Step(_inertiaOffset, _inertiaVelocity, seconds, MaxOffset());

        _scroll.Offset = new Vector(0, result.Offset);
        _inertiaOffset = result.Offset;
        _inertiaVelocity = result.Velocity;

        switch (result.Stop)
        {
            case ScrollInertia.StopReason.Top:
                StopInertia();
                BounceTo(RubberBand.Distance(Math.Abs(result.Velocity) * 0.05));
                break;
            case ScrollInertia.StopReason.Bottom:
                StopInertia();
                BounceTo(-RubberBand.Distance(Math.Abs(result.Velocity) * 0.05));
                break;
            case ScrollInertia.StopReason.Slow:
                StopInertia();
                break;
        }
    }

    private void TrackVelocity(double y, ulong timestamp)
    {
        var elapsedMs = timestamp - _lastPointerTimestamp;
        if (elapsedMs > 0)
        {
            var velocity = -(y - _lastPointerY) / (elapsedMs / 1000.0);
            _offsetVelocity = _offsetVelocity * 0.6 + velocity * 0.4;
        }

        _lastPointerY = y;
        _lastPointerTimestamp = timestamp;
    }

    private void EndMouseDrag()
    {
        _trackingPress = false;
        _dragging = false;
        StopInertia();
        SpringBack();
    }

    private void BounceTo(double distance)
    {
        _playlistBounce.Transitions = null;
        _playlistBounce.Y = distance;
        _playlistBounce.Transitions = _bounceTransitions;
        _playlistBounce.Y = 0;
    }

    private void SpringBack()
    {
        _playlistBounce.Transitions = _bounceTransitions;
        _playlistBounce.Y = 0;
    }

    private void StopInertia() => _inertiaTimer.Stop();

    private double MaxOffset() => _scroll is null
        ? 0
        : Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);

    private void CancelLongPress()
    {
        _longPressTimer.Stop();
        _pressedItem = null;
    }

    private void OnEpisodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PlaylistItem item }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _pressedItem = item;
        _longPressTimer.Stop();
        _longPressTimer.Start();
    }

    private void OnEpisodePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _longPressTimer.Stop();
        _pressedItem = null;
    }

    private void OnEpisodePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _longPressTimer.Stop();
        _pressedItem = null;
    }

    private void OnLongPressElapsed(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        if (_pressedItem is not { } item) return;

        _pressedItem = null;
        if (DataContext is MainViewModel vm)
            vm.OpenEpisodeActionsCommand.Execute(item);
    }
}
