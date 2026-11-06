using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Ohrbob.App.Views;

public partial class AudioWidget : UserControl
{
    // The visual painted (blurred) behind the bar, usually the episode ScrollViewer.
    public static readonly StyledProperty<Visual?> BackdropSourceProperty =
        AvaloniaProperty.Register<AudioWidget, Visual?>(nameof(BackdropSource));

    public Visual? BackdropSource
    {
        get => GetValue(BackdropSourceProperty);
        set => SetValue(BackdropSourceProperty, value);
    }

    private ScrollViewer? _backdropScroll;
    private bool _frostRefreshPending;

    public AudioWidget()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachBackdrop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BackdropSourceProperty)
            AttachBackdrop();
    }

    private void AttachBackdrop()
    {
        if (!ReferenceEquals(_backdropScroll, BackdropSource))
        {
            if (_backdropScroll != null)
                _backdropScroll.ScrollChanged -= OnBackdropScrollChanged;

            _backdropScroll = BackdropSource as ScrollViewer;
            if (_backdropScroll != null)
                _backdropScroll.ScrollChanged += OnBackdropScrollChanged;
        }

        ScheduleFrostRefresh();
    }

    private void OnBackdropScrollChanged(object? sender, ScrollChangedEventArgs e) =>
        ScheduleFrostRefresh();

    // A VisualBrush snapshots its source when the brush is created. Layout and
    // scrolling change that snapshot, so re-assign the visual to refresh it.
    private void ScheduleFrostRefresh()
    {
        if (_frostRefreshPending) return;
        _frostRefreshPending = true;

        Dispatcher.UIThread.Post(() =>
        {
            _frostRefreshPending = false;
            if (FrostLayer.Background is not VisualBrush brush || BackdropSource is null)
                return;

            brush.Visual = null;
            brush.Visual = BackdropSource;
        }, DispatcherPriority.Background);
    }
}
