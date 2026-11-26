using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Views;

public partial class NowPlayingView : UserControl
{
    private readonly TranslateTransform _detailsBounce = new();
    private readonly Transitions _bounceTransitions;
    private readonly MetroTitleCollapse _titleCollapse;
    private Point _dragOrigin;
    private bool _trackingDrag;
    private IInsetsManager? _insets;
    private TopLevel? _topLevel;
    private MainViewModel? _viewModel;

    public NowPlayingView()
    {
        InitializeComponent();

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        Loaded += (_, _) => ApplySafeArea();

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

        _titleCollapse = new MetroTitleCollapse(
            NowPlayingStationTitle, NowPlayingDetailsScroll, NowPlayingStationTitleHost,
            collapsedScale: 0.5, distance: 80);
        NowPlayingHeader.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
                UpdateDetailsInset();
        };
        NowPlayingDetailsScroll.SizeChanged += (_, _) => UpdateDetailsInset();

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

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _topLevel = TopLevel.GetTopLevel(this);

        if (_topLevel is not null)
            _topLevel.ScalingChanged += OnScalingChanged;

        if (_topLevel?.InsetsManager is { } insets && !ReferenceEquals(insets, _insets))
        {
            if (_insets is not null)
                _insets.SafeAreaChanged -= OnSafeAreaChanged;

            _insets = insets;
            _insets.SafeAreaChanged += OnSafeAreaChanged;
        }

        ApplySafeArea();
        BindViewModel(DataContext as MainViewModel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindViewModel(DataContext as MainViewModel);
    }

    private void BindViewModel(MainViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel)) return;

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = viewModel;

        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedStation) or nameof(MainViewModel.CurrentEpisodeId))
            ResetDetailsScroll();
    }

    /// <summary>A new station or episode starts at the top, with the full title.</summary>
    private void ResetDetailsScroll()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ResetDetailsScroll);
            return;
        }

        if (NowPlayingDetailsScroll.Offset.Y > 0)
            NowPlayingDetailsScroll.Offset = default;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        BindViewModel(null);

        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged -= OnScalingChanged;
            _topLevel = null;
        }

        if (_insets is null) return;

        _insets.SafeAreaChanged -= OnSafeAreaChanged;
        _insets = null;
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => ApplySafeArea();

    private void OnScalingChanged(object? sender, EventArgs e) => ApplySafeArea();

    /// <summary>
    /// The cover art paints full-bleed; only the controls move inside the
    /// notch, status bar and gesture area.
    /// </summary>
    private void ApplySafeArea()
    {
        var safe = _insets?.SafeAreaPadding ?? default;

        var headerMargin = new Thickness(
            24 + safe.Left, 26 + safe.Top, 32 + safe.Right, 0);
        NowPlayingHeader.Margin = headerMargin;
        NowPlayingHeaderActions.Margin = headerMargin;
        NowPlayingDetailsScroll.Margin = new Thickness(
            24 + safe.Left, 20, 40 + safe.Right, 12);
        TransportDeck.Padding = new Thickness(
            24 + safe.Left, 14, 28 + safe.Right, 18 + safe.Bottom);
        UpdateDetailsInset();
    }

    /// <summary>
    /// Keeps the metadata below the page header at rest, while still letting
    /// it scroll behind the header once the content is taller than the view.
    /// </summary>
    private void UpdateDetailsInset()
    {
        var inset = NowPlayingHeader.Bounds.Height +
                    NowPlayingHeader.Margin.Top +
                    NowPlayingHeader.Margin.Bottom + 8;
        if (inset <= 8) return;

        var margin = NowPlayingDetailsContent.Margin;
        if (Math.Abs(margin.Top - inset) > 0.5)
            NowPlayingDetailsContent.Margin = new Thickness(margin.Left, inset, margin.Right, margin.Bottom);

        var minHeight = Math.Max(0, NowPlayingDetailsScroll.Viewport.Height - inset);
        if (Math.Abs(NowPlayingDetailsContent.MinHeight - minHeight) > 0.5)
            NowPlayingDetailsContent.MinHeight = minHeight;
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
