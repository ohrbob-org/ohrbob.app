using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
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
}
