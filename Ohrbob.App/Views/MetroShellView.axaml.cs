using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ohrbob.App.Models;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Views;

public partial class MetroShellView : UserControl
{
    private const double WideThreshold = 900;
    private const double WideAspectMinWidth = 720;
    private const double WideAspectRatio = 1.15;
    private const double NarrowPeek = 64;
    private const double CompactThreshold = 760;
    private const double StationGutterCompact = 16;
    private const double StationGutterWideLeft = 44;
    private const double StationGutterWideRight = 26;
    private const double StationTileMinWidth = 272;
    private const double StationTileGap = 8;
    // Matches the platform touch slop closely; 20 DIPs made slow drags count as taps.
    private const double StationTapSlop = 10;

    public static readonly StyledProperty<bool> IsNarrowProperty =
        AvaloniaProperty.Register<MetroShellView, bool>(nameof(IsNarrow));

    public bool IsNarrow
    {
        get => GetValue(IsNarrowProperty);
        set => SetValue(IsNarrowProperty, value);
    }

    /// <summary>The item slot width used by the station grid's WrapPanel.</summary>
    public static readonly StyledProperty<double> StationTileWidthProperty =
        AvaloniaProperty.Register<MetroShellView, double>(nameof(StationTileWidth), StationTileMinWidth);

    public double StationTileWidth
    {
        get => GetValue(StationTileWidthProperty);
        set => SetValue(StationTileWidthProperty, value);
    }

    private readonly TranslateTransform _panoramaTransform = new();
    private readonly Transitions _panoramaTransitions;
    private readonly TranslateTransform _stationsBounce = new();
    private readonly Transitions _stationsBounceTransitions;
    private readonly DispatcherTimer _stationLongPressTimer;
    private readonly MetroTitleCollapse _stationsTitleCollapse;
    private readonly BitmapCache _stationsZoomCache = new()
    {
        SnapsToDevicePixels = true,
        EnableClearType = true,
    };
    private readonly BitmapCache _playerZoomCache = new()
    {
        SnapsToDevicePixels = true,
        EnableClearType = true,
    };
    private DispatcherTimer? _zoomCacheTimer;
    private Point _stationsDragOrigin;
    private bool _trackingStationsDrag;
    private StationWithFeedsDto? _pressedStation;
    private StationWithFeedsDto? _tapStation;
    private Point _tapStart;
    private Vector _tapScrollOffset;
    private bool _tapInProgress;
    private bool _tapMoved;
    private bool _pendingStationTap;
    private bool _suppressStationClick;
    private bool _isApplyingLayout;
    private MainViewModel? _viewModel;
    private Point _panStart;
    private double _panStartOffset;
    private bool _isPanning;
    private IPointer? _panPointer;
    private bool _instantPanoramaForTests;
    private bool _isStationsRefreshing;

    public MetroShellView()
    {
        InitializeComponent();

        _stationsTitleCollapse = new MetroTitleCollapse(StationsTitle, StationsScroll, StationsTitleHost);
        StationsHeader.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
                UpdateStationsHeaderInset();
        };
        StationsScroll.SizeChanged += (_, _) => UpdateStationsHeaderInset();

        _panoramaTransitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = TimeSpan.FromMilliseconds(340),
                Easing = new CubicEaseOut(),
            },
        };
        _panoramaTransform.Transitions = _panoramaTransitions;
        PanoramaCanvas.RenderTransform = _panoramaTransform;

        _stationsBounceTransitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(320),
                Easing = new CubicEaseOut(),
            },
        };
        StationsContent.RenderTransform = _stationsBounce;

        _stationLongPressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(520),
        };
        _stationLongPressTimer.Tick += OnStationLongPressElapsed;

        // A direct horizontal drag works over the scrollable now-playing
        // content, unlike a recognizer competing with its ScrollViewer.
        PanoramaViewport.AddHandler(
            PointerPressedEvent, OnPanoramaPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        PanoramaViewport.AddHandler(
            PointerMovedEvent, OnPanoramaPointerMoved,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        PanoramaViewport.AddHandler(
            PointerReleasedEvent, OnPanoramaPointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        PanoramaViewport.AddHandler(
            PointerCaptureLostEvent, OnPanoramaPointerCaptureLost,
            RoutingStrategies.Tunnel, handledEventsToo: true);

        // Observe the station scroll gestures without taking them from the
        // ScrollViewer or the pull-to-refresh container. Watching the
        // RefreshContainer (the parent) keeps these events coming even after
        // it captures the pointer for a pull-to-refresh gesture.
        StationsRefresh.AddHandler(
            PointerPressedEvent, OnStationsPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        StationsRefresh.AddHandler(
            PointerMovedEvent, OnStationsPointerMoved,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        StationsRefresh.AddHandler(
            PointerReleasedEvent, OnStationsPointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        StationsScroll.AddHandler(
            PointerWheelChangedEvent, OnStationsWheel,
            RoutingStrategies.Bubble, handledEventsToo: true);
        StationsScroll.ScrollChanged += OnStationsScrollChanged;
        // The pull-to-refresh recognizer captures the pointer on the first
        // downward movement (no touch slop), so Avalonia stops raising public
        // pointer moves/releases. These gesture events keep the tap candidate
        // up to date and let it commit on release.
        StationsRefresh.AddHandler(
            InputElement.PullGestureEvent, OnStationsPullGesture,
            RoutingStrategies.Bubble, handledEventsToo: true);
        StationsRefresh.AddHandler(
            InputElement.PullGestureEndedEvent, OnStationsPullGestureEnded,
            RoutingStrategies.Bubble, handledEventsToo: true);

        PanoramaViewport.SizeChanged += (_, _) => ApplyResponsiveLayout();
        Loaded += (_, _) => ApplyResponsiveLayout();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        ApplyResponsiveLayout();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            ApplyResponsiveLayout();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsNowPlayingOpen))
        {
            // The view model can be driven by media keys / platform services; the
            // zoom caches themselves must always be touched on the UI thread.
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(BeginZoomCache);
                return;
            }

            BeginZoomCache();
            return;
        }

        if (e.PropertyName != nameof(MainViewModel.IsPlaylistOpen)) return;

        // The view model can be driven by media keys / platform services; the
        // panorama transform itself must always be touched on the UI thread.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyPanoramaOffset(animate: true));
            return;
        }

        ApplyPanoramaOffset(animate: !_instantPanoramaForTests);
    }

    /// <summary>
    /// Rasterize both zoom layers for the duration of the station/player
    /// transition. Re-recording the station grid, the player text and every
    /// playlist row on each frame is what makes the zoom stutter on phones.
    /// The cache is removed afterwards so normal scrolling is not re-rasterized.
    /// </summary>
    private void BeginZoomCache()
    {
        StationsLayer.CacheMode = _stationsZoomCache;
        PlayerLayer.CacheMode = _playerZoomCache;

        _zoomCacheTimer ??= new DispatcherTimer(DispatcherPriority.Normal, Dispatcher.UIThread)
        {
            Interval = TimeSpan.FromMilliseconds(700),
        };
        _zoomCacheTimer.Stop();
        _zoomCacheTimer.Tick -= OnZoomCacheTimerTick;
        _zoomCacheTimer.Tick += OnZoomCacheTimerTick;
        _zoomCacheTimer.Start();
    }

    private void OnZoomCacheTimerTick(object? sender, EventArgs e)
    {
        _zoomCacheTimer?.Stop();
        StationsLayer.CacheMode = null;
        PlayerLayer.CacheMode = null;
    }

    private void UpdateStationsHeaderInset()
    {
        var inset = StationsHeader.Bounds.Height +
                    StationsHeader.Margin.Top +
                    StationsHeader.Margin.Bottom;
        if (inset <= 0) return;

        var padding = StationsScroll.Padding;
        if (Math.Abs(padding.Top - inset) <= 0.5) return;

        StationsScroll.Padding = new Thickness(padding.Left, inset, padding.Right, padding.Bottom);
    }

    private void ApplyResponsiveLayout()
    {
        if (_isApplyingLayout) return;

        var width = PanoramaViewport.Bounds.Width;
        var height = PanoramaViewport.Bounds.Height;
        if (width <= 1 || height <= 1) return;

        _isApplyingLayout = true;
        try
        {
            // Desktop and landscape tablets get both Metro pages side by side.
            // Portrait tablets and phones use the panorama.
            var wide = width >= WideThreshold ||
                       (width >= WideAspectMinWidth && width > height * WideAspectRatio);
            var narrow = !wide;
            if (IsNarrow != narrow)
                IsNarrow = narrow;

            // Fill each station row with as many ~272px tiles as fit, then
            // split the leftover width evenly so portrait phones use their
            // tighter gutters instead of leaving a gap on the right.
            var compact = width <= CompactThreshold;
            var leftGutter = compact ? StationGutterCompact : StationGutterWideLeft;
            var rightGutter = compact ? StationGutterCompact : StationGutterWideRight;
            var stationWidth = Math.Max(1, width - leftGutter - rightGutter);
            var stationColumns = Math.Max(1,
                (int)Math.Floor((stationWidth + StationTileGap) / StationTileMinWidth));
            var tileWidth = Math.Max(200, stationWidth / stationColumns);
            if (Math.Abs(StationTileWidth - tileWidth) > 0.5)
                StationTileWidth = tileWidth;

            var nowPlayingWidth = narrow ? Math.Max(1, width - NarrowPeek) : Math.Max(1, width * 0.58);
            var playlistWidth = narrow ? Math.Max(1, width - NarrowPeek) : Math.Max(1, width - nowPlayingWidth);

            NowPlayingPage.Width = nowPlayingWidth;
            NowPlayingPage.Height = height;
            PlaylistPage.Width = playlistWidth;
            PlaylistPage.Height = height;
            Canvas.SetLeft(PlaylistPage, nowPlayingWidth);

            PanoramaCanvas.Width = nowPlayingWidth + playlistWidth;
            PanoramaCanvas.Height = height;

            ApplyPanoramaOffset(animate: false);
        }
        finally
        {
            _isApplyingLayout = false;
        }
    }

    private void ApplyPanoramaOffset(bool animate)
    {
        var width = PanoramaViewport.Bounds.Width;
        var offset = IsNarrow && _viewModel?.IsPlaylistOpen == true
            ? -Math.Max(1, width - NarrowPeek * 2)
            : 0;

        if (animate)
        {
            _panoramaTransform.Transitions = _panoramaTransitions;
            _panoramaTransform.X = offset;
        }
        else
        {
            _panoramaTransform.Transitions = null;
            _panoramaTransform.X = offset;
        }
    }

    private void OnPanoramaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsNarrow) return;

        var point = e.GetCurrentPoint(PanoramaViewport);
        if (e.Pointer.Type != PointerType.Touch && !point.Properties.IsLeftButtonPressed) return;

        // Sliders own horizontal drags inside their own track.
        if (e.Source is Visual source && source.FindAncestorOfType<Slider>() is not null) return;

        _panPointer = e.Pointer;
        _panStart = e.GetPosition(PanoramaViewport);
        _panStartOffset = _panoramaTransform.X;
        _isPanning = false;
    }

    private void OnPanoramaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsNarrow || _panPointer != e.Pointer) return;

        var position = e.GetPosition(PanoramaViewport);
        var dx = position.X - _panStart.X;
        var dy = position.Y - _panStart.Y;

        if (!_isPanning)
        {
            if (Math.Abs(dx) < 10 || Math.Abs(dx) <= Math.Abs(dy)) return;

            // Once the gesture is clearly horizontal, own the pointer. Without
            // this the scroll view underneath keeps reacting to vertical
            // wobble and the two gestures fight each other.
            _isPanning = true;
            e.Pointer.Capture(PanoramaViewport);
            _panoramaTransform.Transitions = null;
        }

        var maximum = MaxPanOffset();
        _panoramaTransform.X = Math.Clamp(_panStartOffset + dx, -maximum, 0);
        e.Handled = true;
    }

    private void OnPanoramaPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasPanning = _isPanning;
        if (_panPointer == e.Pointer)
            _panPointer = null;

        // Only release the capture we took for a pan. On a plain tap the
        // button owns the capture, and clearing it here would cancel the click.
        if (wasPanning)
            e.Pointer.Capture(null);

        // Commit even when the release belongs to a pointer we stopped
        // tracking: an interrupted gesture can otherwise leave the panorama
        // parked between the player and playlist.
        CommitPan();
    }

    private void OnPanoramaPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CommitPan();
        _panPointer = null;
    }

    private void CommitPan()
    {
        var maximum = MaxPanOffset();

        if (!_isPanning)
        {
            // A release can arrive after the pan flag was cleared (for example
            // when a snap is interrupted by a new touch). Never leave the
            // panorama parked between the two pages.
            var settled = _viewModel?.IsPlaylistOpen == true ? -maximum : 0;
            if (maximum > 0 && Math.Abs(_panoramaTransform.X - settled) > 0.5)
                AnimatePanoramaTo(settled);
            return;
        }

        _isPanning = false;

        var open = maximum > 0 && _panoramaTransform.X < -maximum * 0.35;

        if (_viewModel is not null && _viewModel.IsPlaylistOpen != open)
        {
            // The property change animates from the released drag position.
            _viewModel.IsPlaylistOpen = open;
            return;
        }

        AnimatePanoramaTo(open ? -maximum : 0);
    }

    private double MaxPanOffset()
    {
        var width = PanoramaViewport.Bounds.Width;
        return Math.Max(1, width - NarrowPeek * 2);
    }

    internal void SwipeForTests(double dx)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SwipeForTests(dx));
            return;
        }

        if (!IsNarrow || _viewModel is null) return;

        _panStartOffset = _panoramaTransform.X;
        _isPanning = true;
        _panoramaTransform.Transitions = null;
        _panoramaTransform.X = Math.Clamp(_panStartOffset + dx, -MaxPanOffset(), 0);

        _instantPanoramaForTests = true;
        try
        {
            CommitPan();
        }
        finally
        {
            _instantPanoramaForTests = false;
        }

        _panPointer = null;
    }

    private void AnimatePanoramaTo(double offset)
    {
        _panoramaTransform.Transitions = _instantPanoramaForTests ? null : _panoramaTransitions;
        _panoramaTransform.X = offset;
    }

    internal void SettlePanoramaForTests()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(SettlePanoramaForTests);
            return;
        }

        _instantPanoramaForTests = true;
        try
        {
            CommitPan();
        }
        finally
        {
            _instantPanoramaForTests = false;
        }
    }

    private void OnStationLongPressElapsed(object? sender, EventArgs e)
    {
        _stationLongPressTimer.Stop();
        if (_pressedStation is not { } station) return;

        _pressedStation = null;
        _suppressStationClick = true;

        // A long press must never fall through to the tap fallback on release.
        _tapInProgress = false;
        _tapStation = null;
        _tapMoved = false;

        if (DataContext is MainViewModel vm)
            vm.OpenStationSettingsForCommand.Execute(station);
    }

    private void OnProfileMenuActionClick(object? sender, RoutedEventArgs e)
    {
        ProfileMenuButton.Flyout?.Hide();
    }

    private void OnStationTileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { Tag: StationWithFeedsDto station }) return;

        // Touch taps are committed from the container's release handler. The
        // button's Click is only used for mouse/keyboard activation.
        if (_pendingStationTap) return;

        if (_suppressStationClick)
        {
            _suppressStationClick = false;
            return;
        }

        if (DataContext is MainViewModel vm)
            vm.OpenStationCommand.Execute(station);
    }

    private async void OnStationsRefreshRequested(object? sender, RefreshRequestedEventArgs e)
    {
        // The refresh takes over the pull gesture; stop following the pointer
        // so later moves do not keep shifting the list.
        ReleaseStationsRubberBand();

        var deferral = e.GetDeferral();
        if (_isStationsRefreshing)
        {
            deferral.Complete();
            return;
        }

        _isStationsRefreshing = true;
        try
        {
            if (_viewModel is not null)
                await _viewModel.RefreshAllAsync();
        }
        finally
        {
            _isStationsRefreshing = false;
            deferral.Complete();
        }
    }

    private void OnStationsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _tapInProgress = false;
        _tapStation = null;
        _tapMoved = false;
        _pendingStationTap = false;
        _suppressStationClick = false;
        CancelStationLongPress();

        var point = e.GetCurrentPoint(StationsScroll);
        if (e.Pointer.Type != PointerType.Touch && !point.Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual source &&
            source.FindAncestorOfType<Avalonia.Controls.Primitives.ScrollBar>() is not null)
            return;

        // The tile is a Button, and Button marks PointerPressed as handled in
        // its class handler. Watch the container's tunnel route instead (it
        // runs first and listens to handled events) so a held press can start
        // the settings long-press timer for touch and mouse alike.
        var stationTile = FindStationTile(e.Source as Visual);
        if (stationTile?.Tag is StationWithFeedsDto pressedStation)
        {
            _pressedStation = pressedStation;
            _tapStart = e.GetPosition(this);
            _tapScrollOffset = StationsScroll.Offset;
            _stationLongPressTimer.Stop();
            _stationLongPressTimer.Start();
        }

        // Remember taps on a station tile even if the scroll/refresh gesture
        // recognizer captures the pointer a few pixels later and cancels the
        // button's Click.
        if (e.Pointer.Type is PointerType.Touch or PointerType.Pen &&
            stationTile?.Tag is StationWithFeedsDto station)
        {
            _tapInProgress = true;
            _tapStation = station;
        }

        _trackingStationsDrag = true;
        _stationsDragOrigin = e.GetPosition(StationsScroll);
        _stationsBounce.Transitions = null;
        _stationsBounce.Y = 0;
    }

    private void OnStationsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedStation is not null)
        {
            var pressPosition = e.GetPosition(this);
            if (Math.Abs(pressPosition.X - _tapStart.X) > StationTapSlop ||
                Math.Abs(pressPosition.Y - _tapStart.Y) > StationTapSlop)
                CancelStationLongPress();
        }

        if (_tapInProgress && !_tapMoved && ExceedsTapSlop(e.GetPosition(this)))
            _tapMoved = true;

        if (!_trackingStationsDrag) return;

        // Hover moves arrive on the same route after the button was released
        // elsewhere; a missed release must not leave the list following the
        // pointer.
        if (e.Pointer.Type != PointerType.Touch &&
            !e.GetCurrentPoint(StationsScroll).Properties.IsLeftButtonPressed)
        {
            CancelStationLongPress();
            ReleaseStationsRubberBand();
            return;
        }

        var delta = e.GetPosition(StationsScroll).Y - _stationsDragOrigin.Y;
        var offset = StationsScroll.Offset.Y;
        var maximum = Math.Max(0, StationsScroll.Extent.Height - StationsScroll.Viewport.Height);

        _stationsBounce.Y = RubberBand.Offset(offset, maximum, delta);
    }

    private void OnStationsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        CancelStationLongPress();

        var station = _tapStation;
        var wasTap = _tapInProgress;
        // Gesture recognizers can swallow the intermediate moves (especially on
        // Android), so the release position is the last reliable drag signal.
        var moved = _tapMoved || (wasTap && ExceedsTapSlop(e.GetPosition(this)));
        var scrolled = wasTap && HasStationsScrolled();
        _tapInProgress = false;
        _tapStation = null;
        _tapMoved = false;

        // A cancelled touch still reaches the tile Button's Click on release.
        // Suppress it so a scroll never opens the station under the finger.
        if (wasTap && (moved || scrolled))
            _suppressStationClick = true;

        if (wasTap && !moved && !scrolled && station is not null)
        {
            // Post after the routed event so a normal button Click can mark
            // itself; captured taps still open from here.
            _pendingStationTap = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_pendingStationTap) return;
                _pendingStationTap = false;

                if (DataContext is MainViewModel vm)
                    vm.OpenStationCommand.Execute(station);
            });
        }

        ReleaseStationsRubberBand();
    }

    private void OnStationsPullGesture(object? sender, PullGestureEventArgs e)
    {
        if (Math.Abs(e.Delta.X) <= StationTapSlop && Math.Abs(e.Delta.Y) <= StationTapSlop)
            return;

        if (_tapInProgress)
            _tapMoved = true;

        CancelStationLongPress();
    }

    private void OnStationsPullGestureEnded(object? sender, PullGestureEndedEventArgs e)
    {
        CancelStationLongPress();

        var station = _tapStation;
        var moved = _tapMoved;
        var wasTap = _tapInProgress;
        var scrolled = wasTap && HasStationsScrolled();
        _tapInProgress = false;
        _tapStation = null;
        _tapMoved = false;

        if (wasTap && (moved || scrolled))
            _suppressStationClick = true;

        if (wasTap && !moved && !scrolled && station is not null && DataContext is MainViewModel vm)
            vm.OpenStationCommand.Execute(station);
    }

    private void OnStationsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!_tapInProgress || !HasStationsScrolled()) return;

        // Scrolling can capture the pointer and stop pointer-move delivery.
        // Treat any real offset change as movement so release cannot open the
        // station tile that happened to be under the finger.
        _tapMoved = true;
        _suppressStationClick = true;
        CancelStationLongPress();
    }

    private bool HasStationsScrolled()
    {
        var offset = StationsScroll.Offset;
        return Math.Abs(offset.X - _tapScrollOffset.X) > 0.5 ||
               Math.Abs(offset.Y - _tapScrollOffset.Y) > 0.5;
    }

    private bool ExceedsTapSlop(Point position) =>
        Math.Abs(position.X - _tapStart.X) > StationTapSlop ||
        Math.Abs(position.Y - _tapStart.Y) > StationTapSlop;

    private void CancelStationLongPress()
    {
        _stationLongPressTimer.Stop();
        _pressedStation = null;
    }

    private static Button? FindStationTile(Visual? source)
    {
        if (source is null) return null;
        if (source is Button { } button && button.Classes.Contains("stationTile"))
            return button;

        var ancestor = source.FindAncestorOfType<Button>();
        return ancestor is not null && ancestor.Classes.Contains("stationTile")
            ? ancestor
            : null;
    }

    private void ReleaseStationsRubberBand()
    {
        if (!_trackingStationsDrag) return;
        _trackingStationsDrag = false;

        _stationsBounce.Transitions = _stationsBounceTransitions;
        _stationsBounce.Y = 0;
    }

    private void OnStationsWheel(object? sender, PointerWheelEventArgs e)
    {
        var offset = StationsScroll.Offset.Y;
        var maximum = Math.Max(0, StationsScroll.Extent.Height - StationsScroll.Viewport.Height);

        if (e.Delta.Y > 0 && offset <= 0.5)
        {
            BounceStationsTo(12);
            StationsRefresh.RequestRefresh();
        }
        else if (e.Delta.Y < 0 && offset >= maximum - 0.5)
        {
            BounceStationsTo(-12);
        }
    }

    private void BounceStationsTo(double distance)
    {
        _stationsBounce.Transitions = null;
        _stationsBounce.Y = distance;
        _stationsBounce.Transitions = _stationsBounceTransitions;
        _stationsBounce.Y = 0;
    }

    private void OnSettingsScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseSettingsCommand.Execute(null);
    }

    private void OnEpisodeActionsScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseEpisodeActionsCommand.Execute(null);
    }
}
