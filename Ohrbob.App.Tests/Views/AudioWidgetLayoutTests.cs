using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using NUnit.Framework;
using Ohrbob.App.ViewModels;
using Ohrbob.App.Views;

namespace Ohrbob.App.Tests.Views;

[TestFixture]
public class AudioWidgetLayoutTests
{
    private static (Window Window, AudioWidget Widget) Show(double width)
    {
        var vm = new MainViewModel
        {
            CurrentEpisodeTitle = "Cosmic Journey",
            CurrentEpisodeDescription = "Space Explorer",
            PositionText = "12:00",
            DurationText = "42:12",
            IsPlaying = true,
        };
        var widget = new AudioWidget { DataContext = vm };
        var window = new Window { Width = width, Height = 140, Content = widget };
        window.Show();
        return (window, widget);
    }

    [AvaloniaTest]
    public void Desktop_SliderExpandsAndAllControlsVisible()
    {
        var (window, widget) = Show(1200);
        var progress = widget.FindControl<Grid>("ProgressGroup")!;
        var skipNext = widget.FindControl<Border>("SkipNextButton")!;

        Assert.That(progress.IsVisible, Is.True);
        Assert.That(skipNext.IsVisible, Is.True);
        // The slider column must soak up the leftover width rather than sit at its 120px minimum.
        Assert.That(progress.Bounds.Width, Is.GreaterThan(400));
        window.Close();
    }

    [AvaloniaTest]
    public void Mobile_HidesSliderAndSkipNext_PinsControlsToTheRight()
    {
        var (window, widget) = Show(420);
        var progress = widget.FindControl<Grid>("ProgressGroup")!;
        var skipNext = widget.FindControl<Border>("SkipNextButton")!;
        var stationRow = widget.FindControl<StackPanel>("StationNameRow")!;
        var controls = widget.FindControl<StackPanel>("ControlsPanel")!;
        var cover = widget.FindControl<Border>("CoverBorder")!;
        var root = widget.FindControl<Grid>("RootGrid")!;

        Assert.That(progress.IsVisible, Is.False);
        Assert.That(skipNext.IsVisible, Is.False);
        Assert.That(stationRow.IsVisible, Is.False);
        Assert.That(cover.Bounds.Width, Is.EqualTo(44));
        // Controls pin to the right edge when the slider is gone.
        Assert.That(controls.Bounds.Right, Is.GreaterThan(root.Bounds.Width - 40));
        window.Close();
    }
}
