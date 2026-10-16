using Avalonia;
using Avalonia.Controls;

namespace Ohrbob.App.Views;

public partial class AudioWidget : UserControl
{
    // Matches the Bootstrap theme's `@media (max-width: 767.98px)` breakpoint.
    private const double MobileBreakpoint = 768;

    public AudioWidget()
    {
        InitializeComponent();
        RootBorder.SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var mobile = e.NewSize.Width < MobileBreakpoint;

        ProgressGroup.IsVisible = !mobile;
        SkipNextButton.IsVisible = !mobile;
        StationNameRow.IsVisible = !mobile;
        CoverBorder.Width = CoverBorder.Height = mobile ? 44 : 56;

        var columns = RootGrid.ColumnDefinitions;
        // Desktop: meta is content-sized and the slider column soaks up the rest.
        // Mobile: the slider is gone, so the meta column stretches to push the controls to the right edge.
        columns[0].Width = mobile ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        // Column 2 has no MinWidth (the MinWidth lives on ProgressGroup), so 0 fully collapses it.
        columns[2].Width = mobile ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
    }
}
