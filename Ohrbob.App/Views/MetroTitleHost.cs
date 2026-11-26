using Avalonia;
using Avalonia.Controls;

namespace Ohrbob.App.Views;

/// <summary>
/// Reserves a shrinking height for a page title. The title is measured at its
/// natural height, so scaling it never clips the glyphs even though the host
/// itself takes less vertical space.
/// </summary>
public sealed class MetroTitleHost : Decorator
{
    public static readonly StyledProperty<double> VisibleHeightProperty =
        AvaloniaProperty.Register<MetroTitleHost, double>(nameof(VisibleHeight), double.NaN);

    static MetroTitleHost()
    {
        AffectsMeasure<MetroTitleHost>(VisibleHeightProperty);
    }

    /// <summary>The height this host reserves. NaN keeps the title's natural height.</summary>
    public double VisibleHeight
    {
        get => GetValue(VisibleHeightProperty);
        set => SetValue(VisibleHeightProperty, value);
    }

    /// <summary>The title's unconstrained height, measured with infinite room.</summary>
    public double NaturalHeight { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child)
            return new Size(0, double.IsNaN(VisibleHeight) ? 0 : VisibleHeight);

        child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        NaturalHeight = child.DesiredSize.Height;

        var height = double.IsNaN(VisibleHeight) ? NaturalHeight : VisibleHeight;
        return new Size(availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, Child.DesiredSize.Height));
        return finalSize;
    }
}
