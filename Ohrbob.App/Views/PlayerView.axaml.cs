using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Views;

public partial class PlayerView : UserControl
{
    private bool _pullFromTop;
    private bool _isRefreshing;

    public PlayerView()
    {
        InitializeComponent();

        // Attach the sidebar slide only once the view is loaded. Declaring the
        // transition in XAML would run it while the initial style is applied,
        // which can touch the animation clock off the UI thread in tests.
        Loaded += (_, _) =>
            Dispatcher.UIThread.Post(AttachSidebarTransition, DispatcherPriority.Background);

        MainSplitView.TemplateApplied += (_, e) => PinSidebarPane(e.NameScope);

        // The split view light-dismisses when you tap outside the pane, but it
        // only clears its own IsPaneOpen; propagate that to the view model so
        // the slide-out animation runs too.
        MainSplitView.PaneClosed += (_, _) =>
        {
            if (DataContext is MainViewModel { IsSidebarOpen: true } vm)
                vm.IsSidebarOpen = false;
        };

        // Desktop: the wheel-up that reaches the top also pulls the data again.
        // Use the bubble phase so the ScrollViewer has already applied the scroll.
        EpisodeScroll.AddHandler(PointerWheelChangedEvent, OnEpisodeScrollWheel,
            RoutingStrategies.Bubble, handledEventsToo: true);

        // Touch/pen: pull down from the top edge.
        EpisodeScroll.GestureRecognizers.Add(new PullGestureRecognizer(PullDirection.TopToBottom));
        EpisodeScroll.PullGesture += (_, _) => _pullFromTop = EpisodeScroll.Offset.Y <= 0.5;
        EpisodeScroll.PullGestureEnded += (_, _) =>
        {
            if (_pullFromTop) _ = RefreshAsync();
            _pullFromTop = false;
        };

        // Swipe right opens Up next, swipe left closes it.
        EpisodeScroll.GestureRecognizers.Add(new SwipeGestureRecognizer
        {
            CanHorizontallySwipe = true,
            IsMouseEnabled = true
        });
        EpisodeScroll.SwipeGestureEnded += (_, e) =>
        {
            if (DataContext is not MainViewModel vm) return;
            if (e.Velocity.X > 200) vm.IsUpNextOpen = true;
            else if (e.Velocity.X < -200) vm.IsUpNextOpen = false;
        };
    }

    /// <summary>
    /// Keeps the split-view pane at full width and stops the theme's Width
    /// animation, so opening the sidebar only moves the border instead of
    /// re-laying out the list on every frame.
    /// </summary>
    private static void PinSidebarPane(INameScope? scope)
    {
        if (scope?.Find<Panel>("PART_PaneRoot") is not { } paneRoot) return;
        paneRoot.Transitions = new Transitions();
        paneRoot.Width = 260;
    }

    private void AttachSidebarTransition()
    {
        if (SidebarPane.Transitions is { Count: > 0 }) return;

        SidebarPane.Transitions = new Transitions
        {
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromSeconds(0.2),
                Easing = new CubicEaseOut(),
            }
        };
    }

    private void CloseUpNextOnPointer(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.IsUpNextOpen = false;
    }

    private void CloseSettingsOnPointer(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.IsSettingsOpen = false;
    }

    private void OnEpisodeScrollWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y > 0 && EpisodeScroll.Offset.Y <= 0.5)
            _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing || DataContext is not MainViewModel vm) return;
        _isRefreshing = true;
        try
        {
            await vm.RefreshAllAsync();
        }
        finally
        {
            _isRefreshing = false;
        }
    }
}
