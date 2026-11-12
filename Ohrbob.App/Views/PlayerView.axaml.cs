using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Views;

public partial class PlayerView : UserControl
{
    private bool _pullFromTop;
    private bool _isRefreshing;

    public PlayerView()
    {
        InitializeComponent();

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
