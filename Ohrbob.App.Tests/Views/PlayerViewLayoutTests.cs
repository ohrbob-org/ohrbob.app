using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using Ohrbob.App.Models;
using Ohrbob.App.Services;
using Ohrbob.App.ViewModels;
using Ohrbob.App.Views;

namespace Ohrbob.App.Tests.Views;

[TestFixture]
public class PlayerViewLayoutTests
{
    /// <summary>Isolates tests from the user's real saved connection.</summary>
    private static MainViewModel CreateViewModel() =>
        new(new ConfigService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")), null);

    [AvaloniaTest]
    public void Portrait_MenuButton_IsFullyOnScreen()
    {
        var vm = CreateViewModel();
        vm.IsPortrait = true;
        vm.IsSidebarOpen = false;
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
    public void ScrollingRefreshesFrostSnapshot()
    {
        var vm = CreateViewModel();
        vm.IsPortrait = false;
        vm.IsSidebarOpen = false;
        var view = new PlayerView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var scroll = view.FindControl<ScrollViewer>("EpisodeScroll")!;
        var widget = view.GetVisualDescendants().OfType<AudioWidget>().Single();
        var brush = (VisualBrush)widget.FindControl<Border>("FrostLayer")!.Background!;

        var visualChanges = 0;
        brush.PropertyChanged += (_, e) =>
        {
            if (e.Property == VisualBrush.VisualProperty) visualChanges++;
        };

        scroll.RaiseEvent(new ScrollChangedEventArgs(new Vector(), new Vector(0, 100), new Vector()));
        Dispatcher.UIThread.RunJobs();

        Assert.That(visualChanges, Is.GreaterThan(0),
            "scrolling must re-assign the brush visual so the blur follows the content");
        window.Hide();
    }

    [AvaloniaTest]
    public void WidgetFloatsOverScrollContent()
    {
        var vm = CreateViewModel();
        vm.IsPortrait = false;
        vm.IsSidebarOpen = false;
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

    [AvaloniaTest]
    public void SettingsDialog_OpensAndShowsPerStationTargets()
    {
        var vm = CreateViewModel();
        vm.IsSettingsOpen = true;
        vm.Stations.Add(new StationWithFeedsDto { Station = new Station { Id = "s1", Name = "One" } });
        vm.SelectedStation = vm.Stations[0];
        var view = new MainView { DataContext = vm };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var dialog = view.FindControl<Border>("SettingsDialog")!;
        Assert.That(dialog.IsVisible, Is.True);
        var options = view.FindControl<ComboBox>("BufferOptionsBox")!;
        Assert.That(options.ItemsSource, Is.Not.Null);
        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Settings — One"), Is.True,
            "the dialog title must name the active station");
        window.Hide();
    }

    [AvaloniaTest]
    public void UpNextPanel_ShowsDownloadStatePublishedDateAndClosesByCommand()
    {
        var vm = CreateViewModel();
        vm.IsUpNextOpen = true;
        vm.IsDownloadEnabled = true;
        vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = "e1", Title = "Current", IsCurrent = true, DownloadState = DownloadState.Downloaded, PublishedAt = new DateTime(2026, 11, 1) });
        vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = "e2", Title = "Next", DownloadState = DownloadState.NotDownloaded, PublishedAt = new DateTime(2026, 11, 2) });
        var view = new PlayerView { DataContext = vm };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var panel = view.FindControl<Border>("UpNextPanel")!;
        Assert.That(panel.IsVisible, Is.True);
        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Downloaded"), Is.True);
        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Not downloaded"), Is.True);
        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Nov 2, 2026"), Is.True);

        vm.CloseUpNextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(panel.IsVisible, Is.False);
        window.Hide();
    }

    [AvaloniaTest]
    public void UpNextPanel_DisabledDownloads_HidesPerItemBadgesAndShowsHostPacedReason()
    {
        var vm = CreateViewModel();
        vm.IsUpNextOpen = true;
        vm.IsDownloadEnabled = false;
        vm.DownloadBlockedReason = "Downloads are off: episodes unlock when the host has listened to them.";
        vm.DownloadStatusMessage = "Downloads are off: episodes unlock when the host has listened to them.";
        vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = "e1", Title = "Current", IsCurrent = true, DownloadState = DownloadState.NotDownloaded, ShowDownloadStateLabel = false });
        vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = "e2", Title = "Next", DownloadState = DownloadState.NotDownloaded, ShowDownloadStateLabel = false });
        var view = new PlayerView { DataContext = vm };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Downloads are off: episodes unlock when the host has listened to them."), Is.True);
        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Not downloaded" && t.IsVisible), Is.False,
            "per-item badges must be hidden when downloads are disabled");
        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Playing"), Is.True);
        window.Hide();
    }

    [AvaloniaTest]
    public void UpNextPanel_AgingStation_ShowsExactReason()
    {
        var vm = CreateViewModel();
        vm.IsUpNextOpen = true;
        vm.IsDownloadEnabled = false;
        vm.DownloadBlockedReason = "Downloads are off: this station only rotates the newest episode of each feed.";
        vm.DownloadStatusMessage = "Downloads are off: this station only rotates the newest episode of each feed.";
        vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = "e1", Title = "Current", IsCurrent = true, DownloadState = DownloadState.NotDownloaded });
        var view = new PlayerView { DataContext = vm };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Downloads are off: this station only rotates the newest episode of each feed."), Is.True);
        window.Hide();
    }

    [AvaloniaTest]
    public void SwipeRecognizer_IsAttachedToPlayerScroll()
    {
        var view = new PlayerView { DataContext = CreateViewModel() };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var scroll = view.FindControl<ScrollViewer>("EpisodeScroll")!;
        Assert.That(scroll.GestureRecognizers.OfType<SwipeGestureRecognizer>().Any(), Is.True);
        window.Hide();
    }

    [AvaloniaTest]
    public void OfflineBadges_AreRemoved_AndSettingsGearIsInTopBar()
    {
        var vm = CreateViewModel();
        vm.IsOffline = true;
        vm.Stations.Add(new StationWithFeedsDto
        {
            Station = new Station { Id = "s1", Name = "One", IsAvailableOffline = true }
        });
        vm.SelectedStation = vm.Stations[0];
        var view = new PlayerView { DataContext = vm };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Offline" && t.IsVisible), Is.False,
            "offline badges were removed from the player");
        var settings = view.FindControl<Button>("SettingsButton")!;
        Assert.That(Grid.GetRow(settings), Is.EqualTo(0));
        window.Hide();
    }
}
