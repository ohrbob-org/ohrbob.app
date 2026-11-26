using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Media;
using Avalonia.Threading;
using Ohrbob.App.Models;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Views;

public partial class MetroShellView : UserControl
{
    private const double WideThreshold = 900;
    private const double WideAspectMinWidth = 720;
    private const double WideAspectRatio = 1.15;
    private const double NarrowPeek = 64;

    public static readonly StyledProperty<bool> IsNarrowProperty =
        AvaloniaProperty.Register<MetroShellView, bool>(nameof(IsNarrow));

    public bool IsNarrow
    {
        get => GetValue(IsNarrowProperty);
        set => SetValue(IsNarrowProperty, value);
    }

    private readonly TranslateTransform _panoramaTransform = new();
    private readonly Transitions _panoramaTransitions;
    private readonly DispatcherTimer _stationLongPressTimer;
    private StationWithFeedsDto? _pressedStation;
    private bool _suppressStationClick;
    private bool _isApplyingLayout;
    private MainViewModel? _viewModel;

    public MetroShellView()
    {
        InitializeComponent();

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

        _stationLongPressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(520),
        };
        _stationLongPressTimer.Tick += OnStationLongPressElapsed;

        var swipe = new SwipeGestureRecognizer
        {
            CanHorizontallySwipe = true,
            IsMouseEnabled = true,
        };
        PanoramaViewport.GestureRecognizers.Add(swipe);
        PanoramaViewport.SwipeGestureEnded += OnPanoramaSwipeEnded;

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
        if (e.PropertyName != nameof(MainViewModel.IsPlaylistOpen)) return;

        // The view model can be driven by media keys / platform services; the
        // panorama transform itself must always be touched on the UI thread.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyPanoramaOffset(animate: true));
            return;
        }

        ApplyPanoramaOffset(animate: true);
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

            var nowPlayingWidth = narrow ? Math.Max(1, width - NarrowPeek) : Math.Max(1, width * 0.58);
            var playlistWidth = narrow ? Math.Max(1, width) : Math.Max(1, width - nowPlayingWidth);

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
            ? -Math.Max(1, width - NarrowPeek)
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

    private void OnPanoramaSwipeEnded(object? sender, SwipeGestureEndedEventArgs e)
    {
        if (!IsNarrow || _viewModel is null) return;

        if (e.Velocity.X < -200)
            _viewModel.ShowPlaylistCommand.Execute(null);
        else if (e.Velocity.X > 200)
            _viewModel.HidePlaylistCommand.Execute(null);
    }

    private void OnStationTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: StationWithFeedsDto station }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _suppressStationClick = false;
        _pressedStation = station;
        _stationLongPressTimer.Stop();
        _stationLongPressTimer.Start();
    }

    private void OnStationTilePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _stationLongPressTimer.Stop();
        _pressedStation = null;
    }

    private void OnStationTilePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _stationLongPressTimer.Stop();
        _pressedStation = null;
    }

    private void OnStationLongPressElapsed(object? sender, EventArgs e)
    {
        _stationLongPressTimer.Stop();
        if (_pressedStation is not { } station) return;

        _pressedStation = null;
        _suppressStationClick = true;

        if (DataContext is MainViewModel vm)
            vm.OpenStationSettingsForCommand.Execute(station);
    }

    private void OnStationTileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { Tag: StationWithFeedsDto station }) return;

        if (_suppressStationClick)
        {
            _suppressStationClick = false;
            return;
        }

        if (DataContext is MainViewModel vm)
            vm.OpenStationCommand.Execute(station);
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
