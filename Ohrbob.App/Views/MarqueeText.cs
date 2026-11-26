using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Ohrbob.App.Views;

/// <summary>
/// Scrolls a one-line <see cref="TextBlock"/> back and forth when its text is wider
/// than the available space. The first movement waits 3–5 seconds.
/// </summary>
public static class MarqueeText
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("IsEnabled", typeof(MarqueeText));

    public static readonly AttachedProperty<int> DelayMinProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, int>("DelayMin", typeof(MarqueeText), 3000);

    public static readonly AttachedProperty<int> DelayMaxProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, int>("DelayMax", typeof(MarqueeText), 5000);

    private static readonly ConditionalWeakTable<TextBlock, MarqueeState> States = new();

    static MarqueeText()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBlock>(OnIsEnabledChanged);
    }

    public static void SetIsEnabled(TextBlock text, bool value) => text.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(TextBlock text) => text.GetValue(IsEnabledProperty);

    public static void SetDelayMin(TextBlock text, int value) => text.SetValue(DelayMinProperty, value);
    public static int GetDelayMin(TextBlock text) => text.GetValue(DelayMinProperty);

    public static void SetDelayMax(TextBlock text, int value) => text.SetValue(DelayMaxProperty, value);
    public static int GetDelayMax(TextBlock text) => text.GetValue(DelayMaxProperty);

    internal static double MeasureOverflow(TextBlock text) => Overflow(text);

    private static void OnIsEnabledChanged(TextBlock text, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
            Attach(text);
        else
            Detach(text);
    }

    private static void Attach(TextBlock text)
    {
        if (States.TryGetValue(text, out _)) return;

        var state = new MarqueeState();
        state.Timer.Tick += (_, _) => OnTick(text, state);
        States.Add(text, state);

        text.RenderTransform = state.Transform;
        text.ClipToBounds = false;
        text.Loaded += (_, _) => Reset(text, state);
        text.SizeChanged += (_, _) => Reset(text, state);
        text.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty ||
                e.Property == TextBlock.FontSizeProperty ||
                e.Property == TextBlock.FontFamilyProperty ||
                e.Property == TextBlock.FontWeightProperty)
            {
                Reset(text, state);
            }
        };

        if (text.IsLoaded)
            Reset(text, state);
    }

    private static void Detach(TextBlock text)
    {
        if (!States.TryGetValue(text, out var state)) return;

        state.Timer.Stop();
        state.Transform.Transitions = null;
        state.Transform.X = 0;
        States.Remove(text);
    }

    private static void Reset(TextBlock text, MarqueeState state)
    {
        if (state.IsResetting) return;

        state.IsResetting = true;
        try
        {
            state.Timer.Stop();
            state.Transform.Transitions = null;
            state.Transform.X = 0;

            var viewport = ViewportWidth(text);
            var textWidth = MeasureTextWidth(text);
            state.Overflow = viewport > 0 ? Math.Max(0, textWidth - viewport) : 0;

            // Let the TextBlock render the full line before translating it; otherwise
            // its own layout would trim the part we want to reveal.
            var targetWidth = state.Overflow > 0 ? Math.Ceiling(textWidth + 1) : double.NaN;
            if (!NearlyEqual(text.Width, targetWidth))
                text.Width = targetWidth;

            state.Phase = Phase.Waiting;
            if (state.Overflow > 0)
                ScheduleDelay(text, state);
        }
        finally
        {
            state.IsResetting = false;
        }
    }

    private static void ScheduleDelay(TextBlock text, MarqueeState state)
    {
        var min = Math.Max(0, GetDelayMin(text));
        var max = Math.Max(min, GetDelayMax(text));
        var delay = min == max
            ? min
            : Random.Shared.Next(min, max + 1);

        state.Timer.Interval = TimeSpan.FromMilliseconds(delay);
        state.Timer.Start();
    }

    private static void OnTick(TextBlock text, MarqueeState state)
    {
        state.Timer.Stop();

        switch (state.Phase)
        {
            case Phase.Waiting:
                state.Overflow = Overflow(text);
                if (state.Overflow <= 0) return;

                state.Phase = Phase.MovingToEnd;
                ScrollToEnd(text, state);
                break;

            case Phase.MovingToEnd:
                state.Phase = Phase.MovingBack;
                ScrollBack(text, state);
                break;

            default:
                state.Transform.Transitions = null;
                state.Transform.X = 0;
                state.Phase = Phase.Waiting;
                ScheduleDelay(text, state);
                break;
        }
    }

    private static void ScrollToEnd(TextBlock text, MarqueeState state)
    {
        var duration = DurationFor(state.Overflow);
        state.Transform.Transitions = ScrollTransition(duration);
        state.Transform.X = -state.Overflow;
        state.Timer.Interval = duration + TimeSpan.FromMilliseconds(450);
        state.Timer.Start();
    }

    private static void ScrollBack(TextBlock text, MarqueeState state)
    {
        var duration = DurationFor(state.Overflow);
        state.Transform.Transitions = ScrollTransition(duration);
        state.Transform.X = 0;
        state.Timer.Interval = duration + TimeSpan.FromMilliseconds(450);
        state.Timer.Start();
    }

    private static Transitions ScrollTransition(TimeSpan duration) => new()
    {
        new DoubleTransition
        {
            Property = TranslateTransform.XProperty,
            Duration = duration,
            Easing = new CubicEaseInOut(),
        },
    };

    private static TimeSpan DurationFor(double overflow) =>
        TimeSpan.FromMilliseconds(Math.Clamp(overflow * 8, 900, 3200));

    private static double Overflow(TextBlock text)
    {
        var viewport = ViewportWidth(text);
        if (viewport <= 0) return 0;
        return Math.Max(0, MeasureTextWidth(text) - viewport);
    }

    private static double ViewportWidth(TextBlock text) =>
        text.Parent is Control { Bounds.Width: > 0 } parent ? parent.Bounds.Width : text.Bounds.Width;

    private static double MeasureTextWidth(TextBlock text)
    {
        if (string.IsNullOrEmpty(text.Text)) return 0;

        var typeface = new Typeface(
            text.FontFamily ?? FontFamily.Default,
            text.FontStyle,
            text.FontWeight,
            text.FontStretch);

        var formatted = new FormattedText(
            text.Text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            text.FontSize,
            text.Foreground ?? Brushes.White);

        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static bool NearlyEqual(double a, double b)
    {
        if (double.IsNaN(a) && double.IsNaN(b)) return true;
        return Math.Abs(a - b) < 0.5;
    }

    private enum Phase
    {
        Waiting,
        MovingToEnd,
        MovingBack,
    }

    private sealed class MarqueeState
    {
        public TranslateTransform Transform { get; } = new();
        public DispatcherTimer Timer { get; } = new();
        public double Overflow { get; set; }
        public Phase Phase { get; set; } = Phase.Waiting;
        public bool IsResetting { get; set; }
    }
}
