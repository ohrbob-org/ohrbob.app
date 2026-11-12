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
    public void PlayerContent_StacksStationCoverTitleAndDescription()
    {
        var vm = CreateViewModel();
        vm.CurrentEpisodeTitle = "Episode";
        vm.CurrentEpisodeDescription = "Description";
        var view = new PlayerView { DataContext = vm };
        var window = new Window { Width = 900, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var station = view.FindControl<TextBlock>("StationTitle")!;
        var cover = view.FindControl<Border>("CoverArt")!;
        var title = view.FindControl<TextBlock>("EpisodeTitle")!;
        var description = view.FindControl<TextBlock>("EpisodeDescription")!;

        var stationY = station.TranslatePoint(new Point(0, 0), view)!.Value.Y;
        var coverY = cover.TranslatePoint(new Point(0, 0), view)!.Value.Y;
        var titleY = title.TranslatePoint(new Point(0, 0), view)!.Value.Y;
        var descriptionY = description.TranslatePoint(new Point(0, 0), view)!.Value.Y;

        Assert.That(stationY, Is.LessThan(coverY));
        Assert.That(coverY, Is.LessThan(titleY));
        Assert.That(titleY, Is.LessThan(descriptionY));
        window.Hide();
    }

    [AvaloniaTest]
    public void SettingsPanel_OpensFullHeightFromTheRight()
    {
        var vm = CreateViewModel();
        vm.IsSettingsOpen = true;
        vm.Stations.Add(new StationWithFeedsDto { Station = new Station { Id = "s1", Name = "One" } });
        vm.SelectedStation = vm.Stations[0];
        var view = new PlayerView { DataContext = vm };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var panel = view.FindControl<Border>("SettingsPanel")!;
        var overlay = view.FindControl<Panel>("SettingsOverlay")!;
        Assert.That(overlay.IsHitTestVisible, Is.True);
        Assert.That(panel.HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Right));
        var host = view.FindControl<Panel>("PlayerOverlayHost")!;
        Assert.That(panel.Bounds.Height, Is.EqualTo(host.Bounds.Height).Within(1),
            "settings should stretch to the full height of the player content");
        var options = view.FindControl<ComboBox>("BufferOptionsBox")!;
        Assert.That(options.ItemsSource, Is.Not.Null);
        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "One"), Is.True,
            "the settings header must name the active station");
        window.Hide();
    }

    [AvaloniaTest]
    public void Overlays_FitNarrowPlayer_WithoutCloseButtons()
    {
        var vm = CreateViewModel();
        vm.Stations.Add(new StationWithFeedsDto { Station = new Station { Id = "s1", Name = "One" } });
        vm.SelectedStation = vm.Stations[0];
        vm.IsSettingsOpen = true;
        var view = new PlayerView { DataContext = vm };
        var window = new Window { Width = 360, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var settings = view.FindControl<Border>("SettingsPanel")!;
        Assert.That(settings.Bounds.Width, Is.LessThanOrEqualTo(view.Bounds.Width + 1),
            "the settings overlay must not overflow a narrow window");
        Assert.That(view.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Close"), Is.False,
            "right-side overlays close via the scrim, not a Close button");
        window.Hide();
    }

    [AvaloniaTest]
    public void UpNextPanel_FitsNarrowPlayer_WithoutCloseButton()
    {
        var vm = CreateViewModel();
        vm.IsUpNextOpen = true;
        var view = new PlayerView { DataContext = vm };
        var window = new Window { Width = 360, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var upNext = view.FindControl<Border>("UpNextPanel")!;
        Assert.That(upNext.Bounds.Width, Is.LessThanOrEqualTo(view.Bounds.Width + 1),
            "the Up next overlay must not overflow a narrow window");
        Assert.That(view.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Close"), Is.False);
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

        var overlay = view.FindControl<Panel>("UpNextOverlay")!;
        Assert.That(overlay.IsHitTestVisible, Is.True);
        var downloadedLabel = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Downloaded");
        var notDownloadedLabel = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Not downloaded");
        Assert.That(((ISolidColorBrush)downloadedLabel.Foreground!).Color, Is.EqualTo(Color.Parse("#198754")),
            "a successful download must read as green");
        Assert.That(((ISolidColorBrush)notDownloadedLabel.Foreground!).Color, Is.EqualTo(Color.Parse("#6C757D")),
            "not-downloaded must read as muted grey");
        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Nov 2, 2026"), Is.True);

        vm.CloseUpNextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(overlay.IsHitTestVisible, Is.False);
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
