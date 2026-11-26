using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Ohrbob.App.Views;

public partial class NowPlayingView : UserControl
{
    private readonly TranslateTransform _detailsBounce = new();
    private readonly Transitions _bounceTransitions;
    private Point _dragOrigin;
    private bool _trackingDrag;

    public NowPlayingView()
    {
        InitializeComponent();

        _bounceTransitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(320),
                Easing = new CubicEaseOut(),
            },
        };

        NowPlayingDetailsContent.RenderTransform = _detailsBounce;

        // Observe scrolling gestures without taking them away from the ScrollViewer.
        NowPlayingDetailsScroll.AddHandler(
            PointerPressedEvent, OnDetailsPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        NowPlayingDetailsScroll.AddHandler(
            PointerMovedEvent, OnDetailsPointerMoved,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        NowPlayingDetailsScroll.AddHandler(
            PointerReleasedEvent, OnDetailsPointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        NowPlayingDetailsScroll.AddHandler(
            PointerCaptureLostEvent, OnDetailsPointerCaptureLost,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        NowPlayingDetailsScroll.AddHandler(
            PointerWheelChangedEvent, OnDetailsWheel,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnDetailsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(NowPlayingDetailsScroll);
        if (e.Pointer.Type != PointerType.Touch && !point.Properties.IsLeftButtonPressed) return;

        _trackingDrag = true;
        _dragOrigin = e.GetPosition(NowPlayingDetailsScroll);
        _detailsBounce.Transitions = null;
        _detailsBounce.Y = 0;
    }

    private void OnDetailsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_trackingDrag) return;

        var delta = e.GetPosition(NowPlayingDetailsScroll).Y - _dragOrigin.Y;
        var offset = NowPlayingDetailsScroll.Offset.Y;
        var maximum = Math.Max(0, NowPlayingDetailsScroll.Extent.Height - NowPlayingDetailsScroll.Viewport.Height);

        _detailsBounce.Y = RubberBand.Offset(offset, maximum, delta);
    }

    private void OnDetailsPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        ReleaseRubberBand();

    private void OnDetailsPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        ReleaseRubberBand();

    private void OnDetailsWheel(object? sender, PointerWheelEventArgs e)
    {
        var offset = NowPlayingDetailsScroll.Offset.Y;
        var maximum = Math.Max(0, NowPlayingDetailsScroll.Extent.Height - NowPlayingDetailsScroll.Viewport.Height);

        if (offset <= 0.5 && e.Delta.Y > 0)
            BounceTo(12);
        else if (offset >= maximum - 0.5 && e.Delta.Y < 0)
            BounceTo(-12);
    }

    private void BounceTo(double distance)
    {
        _detailsBounce.Transitions = null;
        _detailsBounce.Y = distance;
        _detailsBounce.Transitions = _bounceTransitions;
        _detailsBounce.Y = 0;
    }

    private void ReleaseRubberBand()
    {
        if (!_trackingDrag) return;
        _trackingDrag = false;

        _detailsBounce.Transitions = _bounceTransitions;
        _detailsBounce.Y = 0;
    }
}
