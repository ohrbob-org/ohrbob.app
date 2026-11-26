using System;

namespace Ohrbob.App.Views;

/// <summary>
/// Shared damping math for the touch rubber-band scroll effect. The content
/// follows the finger with resistance past a scroll edge and springs back on
/// release; in the middle of the range the scroll viewer scrolls normally.
/// </summary>
internal static class RubberBand
{
    private const double MaximumDistance = 90;
    private const double Damping = 0.4;

    public static double Offset(double offset, double maximum, double dragDelta)
    {
        if (offset <= 0.5 && dragDelta > 0)
            return Distance(dragDelta);
        if (offset >= maximum - 0.5 && dragDelta < 0)
            return -Distance(-dragDelta);
        return 0;
    }

    public static double Distance(double dragDelta) =>
        Math.Min(MaximumDistance, dragDelta * Damping);
}
