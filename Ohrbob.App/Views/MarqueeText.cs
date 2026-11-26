using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Ohrbob.App.Views;

/// <summary>
/// A one-line marquee made from two copies of the text. It waits 3–5 seconds,
/// then scrolls left forever. The reset happens when the second copy sits exactly
/// where the first started, so the loop is visually seamless.
/// </summary>
public static class MarqueeText
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(MarqueeText));

    public static readonly AttachedProperty<int> DelayMinProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("DelayMin", typeof(MarqueeText), 3000);

    public static readonly AttachedProperty<int> DelayMaxProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("DelayMax", typeof(MarqueeText), 5000);

    private static readonly ConditionalWeakTable<Control, MarqueeState> States = new();

    static MarqueeText()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    public static void SetIsEnabled(Control host, bool value) => host.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(Control host) => host.GetValue(IsEnabledProperty);

    public static void SetDelayMin(Control host, int value) => host.SetValue(DelayMinProperty, value);
    public static int GetDelayMin(Control host) => host.GetValue(DelayMinProperty);

    public static void SetDelayMax(Control host, int value) => host.SetValue(DelayMaxProperty, value);
    public static int GetDelayMax(Control host) => host.GetValue(DelayMaxProperty);

    internal static double MeasureOverflow(Control host)
    {
        if (!States.TryGetValue(host, out var state)) return 0;
        var viewport = ViewportWidth(host);
        return viewport > 0 ? Math.Max(0, MeasureTextWidth(state.Source) - viewport) : 0;
    }

    private static void OnIsEnabledChanged(Control host, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
            Attach(host);
        else
            Detach(host);
    }

    private static void Attach(Control host)
    {
        if (States.TryGetValue(host, out _)) return;
        if (host is not Panel panel) return;

        var texts = panel.Children.OfType<TextBlock>().ToList();
        if (texts.Count < 2)
        {
            // The attached property is applied while XAML is still adding the
            // two title copies. Retry once the control is loaded.
            host.Loaded -= OnDeferredLoaded;
            host.Loaded += OnDeferredLoaded;
            return;
        }

        var state = new MarqueeState(texts[0], texts[1]);
        state.Timer.Tick += (_, _) => OnTick(host, state);
        States.Add(host, state);

        host.RenderTransform = state.Transform;
        host.ClipToBounds = false;
        host.Loaded += (_, _) => Reset(host, state);
        host.SizeChanged += (_, _) => Reset(host, state);
        state.Source.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty ||
                e.Property == TextBlock.FontSizeProperty ||
                e.Property == TextBlock.FontFamilyProperty ||
                e.Property == TextBlock.FontWeightProperty)
            {
                Reset(host, state);
            }
        };

        if (host.IsLoaded)
            Reset(host, state);
    }

    private static void OnDeferredLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control host) return;

        host.Loaded -= OnDeferredLoaded;
        if (GetIsEnabled(host))
            Attach(host);
    }

    private static void Detach(Control host)
    {
        if (!States.TryGetValue(host, out var state)) return;

        state.Timer.Stop();
        state.Transform.Transitions = null;
        state.Transform.X = 0;
        States.Remove(host);
    }

    private static void Reset(Control host, MarqueeState state)
    {
        if (state.IsResetting) return;

        state.IsResetting = true;
        try
        {
            state.Timer.Stop();
            state.Transform.Transitions = null;
            state.Transform.X = 0;

            var viewport = ViewportWidth(host);
            var textWidth = MeasureTextWidth(state.Source);
            state.HasOverflow = viewport > 0 && textWidth > viewport;
            state.Distance = textWidth + Gap(host);

            if (state.Duplicate is { } duplicate)
                duplicate.IsVisible = state.HasOverflow;

            state.Phase = Phase.Waiting;
            if (state.HasOverflow)
                ScheduleDelay(host, state);
        }
        finally
        {
            state.IsResetting = false;
        }
    }

    private static void ScheduleDelay(Control host, MarqueeState state)
    {
        var min = Math.Max(0, GetDelayMin(host));
        var max = Math.Max(min, GetDelayMax(host));
        var delay = min == max ? min : Random.Shared.Next(min, max + 1);

        state.Timer.Interval = TimeSpan.FromMilliseconds(delay);
        state.Timer.Start();
    }

    private static void OnTick(Control host, MarqueeState state)
    {
        state.Timer.Stop();

        if (!state.HasOverflow) return;

        if (state.Phase == Phase.Waiting)
        {
            state.Phase = Phase.Moving;
            StartCycle(host, state);
            return;
        }

        // The second copy now occupies the first copy's starting position, so
        // snapping back is invisible. Begin the next leftward pass immediately.
        state.Transform.Transitions = null;
        state.Transform.X = 0;
        StartCycle(host, state);
    }

    private static void StartCycle(Control host, MarqueeState state)
    {
        var duration = DurationFor(state.Distance);
        state.Transform.Transitions = ScrollTransition(duration);
        state.Transform.X = -state.Distance;
        state.Timer.Interval = duration + TimeSpan.FromMilliseconds(60);
        state.Timer.Start();
    }

    private static Transitions ScrollTransition(TimeSpan duration) => new()
    {
        new DoubleTransition
        {
            Property = TranslateTransform.XProperty,
            Duration = duration,
            Easing = new LinearEasing(),
        },
    };

    private static TimeSpan DurationFor(double distance) =>
        TimeSpan.FromMilliseconds(Math.Clamp(distance * 8, 1600, 6000));

    private static double Gap(Control host) =>
        host is StackPanel panel ? Math.Max(8, panel.Spacing) : 32;

    private static double ViewportWidth(Control host) =>
        host.Parent is Control { Bounds.Width: > 0 } parent ? parent.Bounds.Width : host.Bounds.Width;

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

    private enum Phase
    {
        Waiting,
        Moving,
    }

    private sealed class MarqueeState
    {
        public MarqueeState(TextBlock source, TextBlock? duplicate)
        {
            Source = source;
            Duplicate = duplicate;
        }

        public TextBlock Source { get; }
        public TextBlock? Duplicate { get; }
        public TranslateTransform Transform { get; } = new();
        public DispatcherTimer Timer { get; } = new();
        public double Distance { get; set; }
        public bool HasOverflow { get; set; }
        public Phase Phase { get; set; } = Phase.Waiting;
        public bool IsResetting { get; set; }
    }
}
