using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using Ohrbob.App.ViewModels;
using Ohrbob.App.Views;

namespace Ohrbob.App.Tests.Views;

[TestFixture]
public class PlayerViewLayoutTests
{
    [AvaloniaTest]
    public void Portrait_MenuButton_IsFullyOnScreen()
    {
        var vm = new MainViewModel { IsPortrait = true, IsSidebarOpen = false };
        var view = new PlayerView { DataContext = vm };
        var window = new Window { Width = 420, Height = 800, Content = view };
        window.Show();

        var button = view.FindControl<Button>("MenuButton")!;
        Assert.That(button.IsVisible, Is.True);

        var topLeft = button.TranslatePoint(new Point(0, 0), window);
        Assert.That(topLeft, Is.Not.Null);
        Assert.That(topLeft!.Value.X, Is.GreaterThanOrEqualTo(0), "menu button clipped on the left");
        Assert.That(topLeft.Value.Y, Is.GreaterThanOrEqualTo(0), "menu button clipped on the top");
        Assert.That(topLeft.Value.X + button.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width));
        window.Close();
    }

    [AvaloniaTest]
    public void WidgetFloatsOverScrollContent()
    {
        var vm = new MainViewModel { IsPortrait = false, IsSidebarOpen = false };
        var view = new PlayerView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var scroll = view.FindControl<ScrollViewer>("EpisodeScroll")!;
        var widget = view.GetVisualDescendants().OfType<AudioWidget>().Single();

        // The widget overlays the episode scroll instead of occupying its own row.
        Assert.That(widget.BackdropSource, Is.SameAs(scroll));
        var frost = widget.FindControl<Border>("FrostLayer")!;
        Assert.That(frost.Background, Is.TypeOf<VisualBrush>());
        Assert.That(((VisualBrush)frost.Background!).Visual, Is.SameAs(scroll),
            "the frost must paint the content behind it, not a stale empty snapshot");
        Assert.That(widget.VerticalAlignment, Is.EqualTo(Avalonia.Layout.VerticalAlignment.Bottom));
        Assert.That(scroll.Padding.Bottom, Is.GreaterThan(80), "content needs room to scroll above the bar");

        window.Hide();
    }
}
