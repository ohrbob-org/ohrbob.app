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

    public AudioWidget()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshFrost();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BackdropSourceProperty)
            RefreshFrost();
    }

    // A VisualBrush snapshots its source when the brush is first created, which
    // happens before the source has any layout to draw. Re-assigning the visual
    // once layout has run fills in the snapshot.
    private void RefreshFrost()
    {
        if (FrostLayer.Background is not VisualBrush brush || BackdropSource is null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            brush.Visual = null;
            brush.Visual = BackdropSource;
        }, DispatcherPriority.Background);
    }
}
