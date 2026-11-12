using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using NUnit.Framework;
using Ohrbob.App.ViewModels;
using Ohrbob.App.Views;

namespace Ohrbob.App.Tests.Views;

[TestFixture]
public class AudioWidgetLayoutTests
{
    private static (Window Window, AudioWidget Widget) Show(double width, int seekMaximum = 0, int position = 0)
    {
        var vm = new MainViewModel
        {
            CurrentEpisodeTitle = "Cosmic Journey",
            CurrentEpisodeDescription = "Space Explorer",
            PositionText = "12:00",
            DurationText = "42:12",
            IsPlaying = true,
            DurationSeconds = seekMaximum,
            PositionSeconds = position,
        };
        var widget = new AudioWidget { DataContext = vm };
        // A vertical StackPanel gives the widget its natural height, like the
        // Auto row in PlayerView (a bare Window would stretch it).
        var window = new Window
        {
            Width = width,
            Height = 400,
            Content = new StackPanel { Children = { widget } },
        };
        window.Show();
        return (window, widget);
    }

    [AvaloniaTest]
    public void Desktop_ShowsFullBar()
    {
        var (window, widget) = Show(1200);

        Assert.That(widget.FindControl<Grid>("DesktopBar")!.IsVisible, Is.True);
        Assert.That(widget.FindControl<Grid>("MobileBar")!.IsVisible, Is.False);

        Assert.That(widget.FindControl<Grid>("ProgressGroup")!.IsEffectivelyVisible, Is.True);
        Assert.That(widget.FindControl<StackPanel>("VolumeGroup")!.IsEffectivelyVisible, Is.True);
        Assert.That(widget.FindControl<Button>("SettingsButton")!.IsEffectivelyVisible, Is.True);
        Assert.That(widget.FindControl<Button>("BackButton")!.IsEffectivelyVisible, Is.True);
        Assert.That(widget.FindControl<Button>("ForwardButton")!.IsEffectivelyVisible, Is.True);

        var play = widget.FindControl<Button>("PlayButton")!;
        Assert.That(play.Bounds.Width, Is.EqualTo(48));
        Assert.That(play.Command, Is.SameAs(((MainViewModel)widget.DataContext!).TogglePlayCommand));

        var back = widget.FindControl<Button>("BackButton")!;
        Assert.That(back.Command, Is.SameAs(((MainViewModel)widget.DataContext!).SkipToPreviousCommand));
        var forward = widget.FindControl<Button>("ForwardButton")!;
        Assert.That(forward.Command, Is.SameAs(((MainViewModel)widget.DataContext!).SkipToNextCommand));

        window.Close();
    }

    [AvaloniaTest]
    public void Desktop_ShowsCoverOnly_AndSeekButtonsHaveNoText()
    {
        var (window, widget) = Show(1200);

        Assert.That(widget.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Cosmic Journey"), Is.False);
        Assert.That(widget.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Space Explorer"), Is.False);
        Assert.That(widget.FindControl<Button>("SkipBackButton")!.GetVisualDescendants().OfType<TextBlock>(), Is.Empty);
        Assert.That(widget.FindControl<Button>("SkipForwardButton")!.GetVisualDescendants().OfType<TextBlock>(), Is.Empty);

        window.Close();
    }

    [AvaloniaTest]
    public void Mobile_ShowsCompactBar()
    {
        var (window, widget) = Show(420);

        Assert.That(widget.FindControl<Grid>("DesktopBar")!.IsVisible, Is.False);
        Assert.That(widget.FindControl<Grid>("MobileBar")!.IsVisible, Is.True);

        // Bootstrap's <768px rules hide seek, volume and settings.
        Assert.That(widget.FindControl<Grid>("ProgressGroup")!.IsEffectivelyVisible, Is.False);
        Assert.That(widget.FindControl<StackPanel>("VolumeGroup")!.IsEffectivelyVisible, Is.False);
        Assert.That(widget.FindControl<Button>("SettingsButton")!.IsEffectivelyVisible, Is.False);
        Assert.That(widget.FindControl<Button>("BackButton")!.IsEffectivelyVisible, Is.False);
        Assert.That(widget.FindControl<Button>("ForwardButton")!.IsEffectivelyVisible, Is.False);

        var play = widget.FindControl<Button>("MobilePlayButton")!;
        Assert.That(play.Bounds.Width, Is.EqualTo(44));

        window.Close();
    }

    [AvaloniaTest]
    public void FrostedBackdrop_UsesBlurredVisualBrush()
    {
        var (window, widget) = Show(1200);

        var frost = widget.FindControl<Border>("FrostLayer")!;
        Assert.That(frost.Effect, Is.TypeOf<BlurEffect>());
        Assert.That(frost.Background, Is.TypeOf<VisualBrush>());

        // The backdrop layer must stay out of the layout measure; otherwise the
        // brush stretches the bar over the whole content area.
        var root = widget.FindControl<Border>("RootBorder")!;
        Assert.That(root.Bounds.Height, Is.LessThan(150));

        window.Close();
    }

    [AvaloniaTest]
    public void VolumeSlider_BindsToViewModel()
    {
        var (window, widget) = Show(1200);

        var slider = widget.FindControl<Slider>("VolumeSlider")!;
        Assert.That(slider.Minimum, Is.EqualTo(0));
        Assert.That(slider.Maximum, Is.EqualTo(100));

        slider.Value = 40;
        Assert.That(((MainViewModel)widget.DataContext!).Volume, Is.EqualTo(40));

        window.Close();
    }

    [AvaloniaTest]
    public void ProgressSlider_UsesSeekMaximumAndPosition()
    {
        var (window, widget) = Show(1200, seekMaximum: 300, position: 120);

        var slider = widget.FindControl<Slider>("ProgressSlider")!;
        Assert.That(slider.Maximum, Is.EqualTo(300));
        Assert.That(slider.Value, Is.EqualTo(120));

        window.Close();
    }
}
