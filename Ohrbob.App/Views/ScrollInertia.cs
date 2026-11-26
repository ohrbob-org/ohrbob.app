using System;

namespace Ohrbob.App.Views;

/// <summary>
/// Deceleration for a released drag. Velocity is expressed in content pixels
/// per second; a positive velocity scrolls towards the end of the list.
/// </summary>
internal static class ScrollInertia
{
    /// <summary>Fraction of the velocity left after one second (UIScrollView normal).</summary>
    public const double DecelerationPerSecond = 0.135;

    public const double StopSpeed = 30;

    public enum StopReason
    {
        None,
        Top,
        Bottom,
        Slow,
    }

    public readonly record struct StepResult(double Offset, double Velocity, StopReason Stop);

    public static StepResult Step(double offset, double velocity, double seconds, double maximum)
    {
        if (seconds <= 0)
            return new StepResult(offset, velocity, StopReason.None);

        var next = offset + velocity * seconds;

        if (next <= 0)
            return new StepResult(0, velocity, StopReason.Top);

        if (next >= maximum)
            return new StepResult(maximum, velocity, StopReason.Bottom);

        var decayed = velocity * Math.Pow(DecelerationPerSecond, seconds);
        if (Math.Abs(decayed) < StopSpeed)
            return new StepResult(next, 0, StopReason.Slow);

        return new StepResult(next, decayed, StopReason.None);
    }
}
