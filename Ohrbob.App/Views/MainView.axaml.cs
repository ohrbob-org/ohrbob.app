using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Views;

public partial class MainView : UserControl
{
    private MainViewModel? _viewModel;
    private TopLevel? _topLevel;
    private bool _isSubscribed;

    public static readonly StyledProperty<bool> IsPortraitProperty =
        AvaloniaProperty.Register<MainView, bool>(nameof(IsPortrait));

    public bool IsPortrait
    {
        get => GetValue(IsPortraitProperty);
        set => SetValue(IsPortraitProperty, value);
    }

    public MainView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        PropertyChanged += (s, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                var bounds = Bounds;
                bool newIsPortrait = bounds.Width < bounds.Height;
                if (IsPortrait != newIsPortrait)
                {
                    IsPortrait = newIsPortrait;
                    if (DataContext is MainViewModel vm)
                    {
                        vm.IsPortrait = newIsPortrait;
                        if (newIsPortrait) vm.IsSidebarOpen = false;
                    }
                }
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindViewModel();
    }

    private void BindViewModel()
    {
        if (_isSubscribed && _viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as MainViewModel;
        _isSubscribed = _viewModel is not null;
        if (_isSubscribed)
            _viewModel!.PropertyChanged += OnViewModelPropertyChanged;

        ApplyPlayerSafeArea();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!_isSubscribed)
            BindViewModel();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // The view can be detached and reattached when the app resumes from the
        // background; restore the view-model subscription if that happened.
        if (!_isSubscribed)
            BindViewModel();

        _topLevel = TopLevel.GetTopLevel(this);

        // Ask the platform for an edge-to-edge surface so the player can paint
        // behind the status and navigation bars. The non-player screens still
        // get the automatic safe-area padding.
        if (_topLevel?.InsetsManager is { } insets)
            insets.DisplayEdgeToEdgePreference = true;

        ApplyPlayerSafeArea();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!_isSubscribed || _viewModel is null) return;

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _isSubscribed = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsNowPlayingOpen))
            ApplyPlayerSafeArea();
    }

    private void ApplyPlayerSafeArea()
    {
        // The now-playing layer is immersive: its cover art and transport deck
        // reach the physical screen edges. Onboarding and the station grid keep
        // the automatic inset so controls never sit under the system bars.
        var immersive = _viewModel?.IsNowPlayingOpen == true;
        TopLevel.SetAutoSafeAreaPadding(this, !immersive);
    }
}