using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using Ohrbob.App.Models;
using Ohrbob.App.Services;
using Ohrbob.App.Tests.ViewModels;
using Ohrbob.App.ViewModels;
using Ohrbob.App.Views;

namespace Ohrbob.App.Tests.Views;

[TestFixture]
public class MetroShellLayoutTests
{
    private static MainViewModel CreateViewModel()
    {
        var configPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var offlinePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "offline.json");
        var vm = new MainViewModel(
            new ConfigService(configPath),
            null,
            null,
            new TestAudioPlayer(),
            action => action(),
            null,
            new OfflineStateService(offlinePath));

        vm.IsConnected = true;
        vm.ProfileName = "Kai";
        vm.Stations.Add(new StationWithFeedsDto
        {
            Station = new Station { Id = "s1", Name = "Morning Brief" },
        });
        vm.SelectedStation = vm.Stations[0];
        vm.CurrentEpisodeTitle = "The Last Train to Anywhere";
        vm.IsNowPlayingOpen = true;
        return vm;
    }

    [AvaloniaTest]
    public void WideLayout_ShowsNowPlayingAndPlaylistSideBySide()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var now = view.FindControl<Border>("NowPlayingPage")!;
        var playlist = view.FindControl<Border>("PlaylistPage")!;
        var playlistLeft = Canvas.GetLeft(playlist);

        Assert.That(view.IsNarrow, Is.False);
        Assert.That(now.Bounds.Width, Is.EqualTo(696).Within(2),
            "the player should use 58% of a wide window");
        Assert.That(playlistLeft, Is.EqualTo(now.Bounds.Width).Within(2));
        Assert.That(now.Bounds.Width + playlist.Bounds.Width, Is.EqualTo(1200).Within(3),
            "both Metro pages must be fully visible on desktop/tablet landscape");
        window.Close();
    }

    [AvaloniaTest]
    public void LandscapeTablet_AlsoShowsBothPages()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var now = view.FindControl<Border>("NowPlayingPage")!;
        var playlist = view.FindControl<Border>("PlaylistPage")!;

        Assert.That(view.IsNarrow, Is.False,
            "a wide-aspect tablet should show the now-playing and playlist columns together");
        Assert.That(now.Bounds.Width + playlist.Bounds.Width, Is.EqualTo(800).Within(3));
        window.Close();
    }

    [AvaloniaTest]
    public void NarrowLayout_LeavesAPlaylistPeek()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewport = view.FindControl<Panel>("PanoramaViewport")!;
        var now = view.FindControl<Border>("NowPlayingPage")!;
        var playlist = view.FindControl<Border>("PlaylistPage")!;

        Assert.That(view.IsNarrow, Is.True);
        Assert.That(now.Bounds.Width, Is.EqualTo(viewport.Bounds.Width - 64).Within(2));
        Assert.That(playlist.Bounds.Width, Is.EqualTo(viewport.Bounds.Width - 64).Within(2));
        Assert.That(Canvas.GetLeft(playlist), Is.EqualTo(now.Bounds.Width).Within(2));
        Assert.That(Canvas.GetLeft(playlist), Is.LessThan(viewport.Bounds.Width),
            "the next Metro page must peek in from the right");
        window.Close();
    }

    [AvaloniaTest]
    public void PlaylistOpen_LeavesNowPlayingPeekingOnTheLeft()
    {
        var vm = CreateViewModel();
        vm.IsPlaylistOpen = true;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewport = view.FindControl<Panel>("PanoramaViewport")!;
        var now = view.FindControl<Border>("NowPlayingPage")!;
        var playlist = view.FindControl<Border>("PlaylistPage")!;

        var nowRight = now.TranslatePoint(new Point(now.Bounds.Width, 0), viewport)!.Value.X;
        var playlistLeft = playlist.TranslatePoint(new Point(0, 0), viewport)!.Value.X;

        Assert.That(nowRight, Is.EqualTo(64).Within(2),
            "the player page should remain visible as a peeking strip on the left");
        Assert.That(playlistLeft, Is.EqualTo(64).Within(2));
        Assert.That(playlist.TranslatePoint(new Point(playlist.Bounds.Width, 0), viewport)!.Value.X,
            Is.EqualTo(viewport.Bounds.Width).Within(2),
            "the playlist should fill the remainder without clipping its right edge");
        window.Close();
    }

    [AvaloniaTest]
    public void NarrowSwipeState_SlidesTheWholePlaylistIntoView()
    {
        var vm = CreateViewModel();
        vm.IsPlaylistOpen = true;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewport = view.FindControl<Panel>("PanoramaViewport")!;
        var canvas = view.FindControl<Canvas>("PanoramaCanvas")!;
        var transform = (Avalonia.Media.TranslateTransform)canvas.RenderTransform!;

        Assert.That(transform.X, Is.EqualTo(-(viewport.Bounds.Width - 128)).Within(2),
            "opening the playlist should leave the player peeking on the left");
        window.Close();
    }

    [AvaloniaTest]
    public async Task StationZoom_CachesBothLayersOnlyDuringTheTransition()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var stations = view.FindControl<Border>("StationsLayer")!;
        var player = view.FindControl<Border>("PlayerLayer")!;
        Assert.That(stations.CacheMode, Is.Null);
        Assert.That(player.CacheMode, Is.Null);

        vm.IsNowPlayingOpen = true;
        Dispatcher.UIThread.RunJobs();

        Assert.That(stations.CacheMode, Is.TypeOf<BitmapCache>(),
            "the station grid must be rasterized for the zoom");
        Assert.That(player.CacheMode, Is.TypeOf<BitmapCache>(),
            "the player layer must be rasterized for the zoom");

        await Task.Delay(900);
        Dispatcher.UIThread.RunJobs();

        Assert.That(stations.CacheMode, Is.Null,
            "the zoom cache must be released after the transition");
        Assert.That(player.CacheMode, Is.Null,
            "the zoom cache must be released after the transition");
        window.Close();
    }

    [AvaloniaTest]
    public void StationTiles_UseTightMetroGutters()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tile = view.GetVisualDescendants().OfType<Button>()
            .First(button => button.Classes.Contains("stationTile"));
        var gap = tile.GetVisualDescendants().OfType<Border>()
            .First(border => border.BorderThickness.Right > 0);

        Assert.That(tile.Margin, Is.EqualTo(default(Thickness)),
            "the tile itself must not add an outer gap");
        Assert.That(gap.BorderThickness, Is.EqualTo(new Thickness(0, 0, 8, 8)),
            "station tiles use a tight Metro gutter");
        window.Close();
    }

    [AvaloniaTest]
    public void PlaylistRows_UseTightGuttersWithoutDividers()
    {
        var vm = CreateViewModel();
        vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = "e1", Title = "Episode" });
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();
        var row = playlist.GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("episodeRow"));

        Assert.That(row.BorderThickness, Is.EqualTo(default(Thickness)),
            "playlist rows no longer draw a divider");
        Assert.That(row.Padding.Top, Is.EqualTo(8).Within(0.1),
            "playlist rows use a tight vertical gutter");
        window.Close();
    }

    [AvaloniaTest]
    public void MainView_UsesTheMetroShellInsteadOfTheOldSidebar()
    {
        var vm = CreateViewModel();
        var view = new MainView { DataContext = vm };
        var window = new Window { Width = 1100, Height = 760, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.That(view.GetVisualDescendants().OfType<MetroShellView>().Any(), Is.True);
        Assert.That(view.GetVisualDescendants().OfType<PlayerView>().Any(), Is.False,
            "the legacy Bootstrap-style player should not be in the live visual tree");
        window.Close();
    }

    [AvaloniaTest]
    public void StationTiles_UseASingleLineMarqueeOnlyWhenTheNameOverflows()
    {
        var vm = CreateViewModel();
        var longName = "A station name that is much too long for a single tile";
        vm.Stations[0].Station!.Name = longName;
        vm.Stations.Add(new StationWithFeedsDto
        {
            Station = new Station { Id = "s2", Name = "Short" },
        });
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var longTitle = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == longName);
        var shortTitle = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Short");
        var longPanel = (StackPanel)longTitle.Parent!;
        var shortPanel = (StackPanel)shortTitle.Parent!;

        Assert.That(longTitle.TextWrapping, Is.EqualTo(TextWrapping.NoWrap));
        Assert.That(longTitle.MaxLines, Is.EqualTo(1));
        Assert.That(longPanel.HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Left),
            "the marquee must start at the first character");
        Assert.That(MarqueeText.GetIsEnabled(longPanel), Is.True);
        Assert.That(MarqueeText.MeasureOverflow(longPanel), Is.GreaterThan(0),
            "a long station name should be prepared for marquee scrolling");
        Assert.That(longPanel.Children.OfType<TextBlock>().Count(), Is.EqualTo(2),
            "the marquee needs a second copy to loop seamlessly");
        Assert.That(longPanel.Children.OfType<TextBlock>().Last().IsVisible, Is.True);
        Assert.That(MarqueeText.MeasureOverflow(shortPanel), Is.EqualTo(0),
            "short station names should stay still");
        Assert.That(shortPanel.Children.OfType<TextBlock>().Last().IsVisible, Is.False,
            "the duplicate title should be hidden when nothing overflows");
        window.Close();
    }

    [AvaloniaTest]
    public void StationsList_BouncesAtBothEdgesAndKeepsPullToRefresh()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var scroll = view.FindControl<ScrollViewer>("StationsScroll")!;
        var content = view.FindControl<Panel>("StationsContent")!;
        var refresh = view.FindControl<RefreshContainer>("StationsRefresh")!;

        Assert.That(content.RenderTransform, Is.TypeOf<TranslateTransform>());
        Assert.That(refresh.Content, Is.SameAs(scroll),
            "pull-to-refresh must stay wrapped around the stations scroll");
        Assert.That(refresh.PullDirection, Is.EqualTo(PullDirection.TopToBottom));
        Assert.That(RubberBand.Offset(0, 500, 80), Is.GreaterThan(0),
            "dragging down at the top of the stations should bounce");
        Assert.That(RubberBand.Offset(500, 500, -80), Is.LessThan(0),
            "dragging up at the bottom of the stations should bounce");
        Assert.That(RubberBand.Offset(250, 500, 80), Is.EqualTo(0),
            "dragging in the middle should scroll normally, not bounce");
        window.Close();
    }

    [AvaloniaTest]
    public void DraggingTheStationListPastTheTopBounces()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var scroll = view.FindControl<ScrollViewer>("StationsScroll")!;
        var content = view.FindControl<Panel>("StationsContent")!;
        var bounce = (TranslateTransform)content.RenderTransform!;
        Assert.That(scroll.Bounds.Height, Is.GreaterThan(100), "the stations list must be laid out");
        var start = scroll.TranslatePoint(new Point(60, 60), window)!.Value;

        // Small pull: enough to see the bounce without crossing the
        // pull-to-refresh threshold and starting a network reload.
        var pointer = window.TouchBegin(start, RawInputModifiers.None);
        window.TouchMove(pointer, start + new Point(0, 20), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.That(scroll.Offset.Y, Is.LessThanOrEqualTo(0.5),
            "the list must still be at the top while it bounces");
        Assert.That(bounce.Y, Is.GreaterThan(0),
            "a downward drag at the top should move the station content");

        window.TouchEnd(pointer, start + new Point(0, 20), RawInputModifiers.None);
        window.Close();
    }

    [AvaloniaTest]
    public void TriggeringARefresh_ReleasesTheStationBounce()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var scroll = view.FindControl<ScrollViewer>("StationsScroll")!;
        var content = view.FindControl<Panel>("StationsContent")!;
        var bounce = (TranslateTransform)content.RenderTransform!;
        var refresh = view.FindControl<RefreshContainer>("StationsRefresh")!;
        var start = scroll.TranslatePoint(new Point(60, 60), window)!.Value;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(0, 40), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        Assert.That(bounce.Y, Is.GreaterThan(0), "sanity: dragging down at the top bounces");

        refresh.RequestRefresh();
        Dispatcher.UIThread.RunJobs();
        Assert.That(IsStationsDragActive(view), Is.False,
            "a refresh interrupts the drag and must stop following the pointer");

        window.MouseMove(start + new Point(0, 10));
        Dispatcher.UIThread.RunJobs();
        Assert.That(IsStationsDragActive(view), Is.False,
            "after the refresh, plain mouse moves must not restart the drag");

        window.MouseUp(start + new Point(0, 10), MouseButton.Left);
        window.Close();
    }

    private static bool IsStationsDragActive(MetroShellView view) =>
        (bool)typeof(MetroShellView)
            .GetField("_trackingStationsDrag",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(view)!;

    private static (MetroShellView View, Window Window, ScrollViewer Scroll, TranslateTransform Bounce)
        CreatePlaylistWindow()
    {
        var vm = CreateViewModel();
        for (var i = 0; i < 20; i++)
            vm.PlaylistItems.Add(new PlaylistItem
            {
                EpisodeId = $"e{i}",
                Title = $"Episode {i}",
                IsCurrent = i == 0,
            });

        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();
        var scroll = playlist.FindControl<ListBox>("PlaylistList")!
            .GetVisualDescendants().OfType<ScrollViewer>().Single();
        var content = (Control)scroll.Content!;
        var bounce = (TranslateTransform)content.RenderTransform!;
        return (view, window, scroll, bounce);
    }

    [AvaloniaTest]
    public void PlaylistMouseDrag_ScrollsAndRubberBandsAtTheTop()
    {
        var (_, window, scroll, bounce) = CreatePlaylistWindow();
        var start = scroll.TranslatePoint(new Point(200, 100), window)!.Value;

        // At the top, drag down past the edge: it should rubber-band.
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(0, 60), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        Assert.That(scroll.Offset.Y, Is.LessThanOrEqualTo(0.5),
            "the list must stay at the top while it bounces");
        Assert.That(bounce.Y, Is.GreaterThan(0),
            "dragging down at the top must rubber-band");
        window.MouseUp(start + new Point(0, 60), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // Drag up: the list follows the mouse.
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start - new Point(0, 120), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        Assert.That(scroll.Offset.Y, Is.GreaterThan(50),
            "dragging with the mouse must pan the playlist");
        Assert.That(bounce.Y, Is.EqualTo(0));
        window.MouseUp(start - new Point(0, 120), MouseButton.Left);
        window.Close();
    }

    [AvaloniaTest]
    public void PlaylistMouseDrag_StartsInertiaAfterRelease()
    {
        var (view, window, scroll, _) = CreatePlaylistWindow();
        var start = scroll.TranslatePoint(new Point(200, 500), window)!.Value;

        window.MouseDown(start, MouseButton.Left);

        // Space the moves out so the tracker sees a real velocity; the
        // headless clock otherwise reports zero elapsed time between events.
        Thread.Sleep(20);
        window.MouseMove(start - new Point(0, 60), RawInputModifiers.LeftMouseButton);
        Thread.Sleep(20);
        window.MouseMove(start - new Point(0, 140), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        var atRelease = scroll.Offset.Y;
        Assert.That(atRelease, Is.GreaterThan(50));

        window.MouseUp(start - new Point(0, 140), MouseButton.Left);

        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();
        var timer = (DispatcherTimer?)typeof(PlaylistView)
            .GetField("_inertiaTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(playlist);
        Assert.That(timer!.IsEnabled, Is.True,
            "releasing a fast drag must start the coasting timer");

        timer.Stop();
        window.Close();
    }

    [Test]
    public void ScrollInertia_DeceleratesAndStopsAtTheEdges()
    {
        var middle = ScrollInertia.Step(100, 1000, 0.1, 1000);
        Assert.That(middle.Offset, Is.GreaterThan(100));
        Assert.That(middle.Velocity, Is.GreaterThan(0).And.LessThan(1000));
        Assert.That(middle.Stop, Is.EqualTo(ScrollInertia.StopReason.None));

        var top = ScrollInertia.Step(0, -1000, 0.1, 1000);
        Assert.That(top.Offset, Is.EqualTo(0));
        Assert.That(top.Stop, Is.EqualTo(ScrollInertia.StopReason.Top));

        var bottom = ScrollInertia.Step(990, 1000, 0.1, 1000);
        Assert.That(bottom.Offset, Is.EqualTo(1000));
        Assert.That(bottom.Stop, Is.EqualTo(ScrollInertia.StopReason.Bottom));

        var slow = ScrollInertia.Step(500, 20, 0.1, 1000);
        Assert.That(slow.Stop, Is.EqualTo(ScrollInertia.StopReason.Slow));
        Assert.That(slow.Offset, Is.EqualTo(502).Within(0.01));
    }

    [AvaloniaTest]
    public void EveryScrollViewer_HasInertiaEnabled()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = true;
        for (var i = 0; i < 20; i++)
            vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = $"e{i}", Title = $"Episode {i}" });

        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var scrollViewers = view.GetVisualDescendants().OfType<ScrollViewer>().ToList();
        Assert.That(scrollViewers, Is.Not.Empty);
        Assert.That(scrollViewers, Has.All.Matches<ScrollViewer>(s => s.IsScrollInertiaEnabled),
            "every scrollable must keep inertia enabled");
        window.Close();
    }

    [AvaloniaTest]
    public void StationLoading_CoversTheOldPlayerAndPlaylistContent()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = true;
        vm.IsStationLoading = true;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var player = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();
        var playerOverlay = player.FindControl<Border>("StationLoadingOverlay")!;
        var playlistOverlay = playlist.FindControl<Border>("StationLoadingOverlay")!;

        Assert.That(playerOverlay.IsEffectivelyVisible, Is.True);
        Assert.That(playerOverlay.Bounds.Height, Is.GreaterThan(100),
            "the loader covers the player content below its header");
        Assert.That(playlistOverlay.IsEffectivelyVisible, Is.True,
            "the old queue is hidden while the new one loads");

        vm.IsStationLoading = false;
        Dispatcher.UIThread.RunJobs();
        Assert.That(playerOverlay.IsEffectivelyVisible, Is.False);
        Assert.That(playlistOverlay.IsEffectivelyVisible, Is.False);
        window.Close();
    }

    [AvaloniaTest]
    public void PlaylistCurrentEpisode_UsesOnlyTheOrangeIndicator()
    {
        var vm = CreateViewModel();
        vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = "e1", Title = "Current", IsCurrent = true });
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();
        Assert.That(playlist.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text == "NOW" && t.IsEffectivelyVisible), Is.False,
            "the clipped NOW label is redundant with the left indicator");
        Assert.That(playlist.GetVisualDescendants().OfType<Border>().Any(b =>
                b.Width == 4 && b.IsEffectivelyVisible &&
                b.Background is ISolidColorBrush { Color: var c } && c == Color.Parse("#FC5D00")),
            Is.True, "the orange current-episode indicator must remain");
        window.Close();
    }

    [AvaloniaTest]
    public void PortraitStationsHeader_ProfileMenuHoldsManageAndDisconnect()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.That(view.FindControl<Button>("ProfileMenuButton"), Is.Not.Null,
            "the profile avatar opens the menu");
        Assert.That(view.FindControl<Button>("DisconnectButton"), Is.Null,
            "the standalone disconnect button moved into the profile menu");
        Assert.That(view.FindControl<Button>("DisconnectIconButton"), Is.Null,
            "the compact standalone disconnect icon moved into the profile menu");

        var refresh = view.FindControl<RefreshContainer>("StationsRefresh");
        Assert.That(refresh, Is.Not.Null);
        Assert.That(refresh!.PullDirection, Is.EqualTo(PullDirection.TopToBottom));
        Assert.That(view.GetVisualDescendants().OfType<Button>().Any(b => ReferenceEquals(b.Command, vm.RefreshAllCommand)),
            Is.False, "the header refresh button was removed");
        window.Close();
    }

    [AvaloniaTest]
    public void StationTile_TouchTap_OpensStation()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        vm.SelectedStation = null;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var tile = view.GetVisualDescendants().OfType<Button>()
            .First(button => button.Classes.Contains("stationTile"));
        var start = tile.TranslatePoint(
            new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;

        var pointer = window.TouchBegin(start, RawInputModifiers.None);
        window.TouchEnd(pointer, start, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.SelectedStation, Is.SameAs(vm.Stations[0]),
            "a touch tap must open the station");
        window.Close();
    }

    [AvaloniaTest]
    public void StationTile_TouchTapWithSmallMovement_OpensStation()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        vm.SelectedStation = null;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var tile = view.GetVisualDescendants().OfType<Button>()
            .First(button => button.Classes.Contains("stationTile"));
        var start = tile.TranslatePoint(
            new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;

        var pointer = window.TouchBegin(start, RawInputModifiers.None);
        window.TouchMove(pointer, start + new Point(0, 10), RawInputModifiers.None);
        window.TouchEnd(pointer, start + new Point(0, 10), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.SelectedStation, Is.SameAs(vm.Stations[0]),
            "a few pixels of finger drift must still count as a tap");
        window.Close();
    }

    [AvaloniaTest]
    public void StationTile_TouchDrag_DoesNotOpenStation()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        vm.SelectedStation = null;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var tile = view.GetVisualDescendants().OfType<Button>()
            .First(button => button.Classes.Contains("stationTile"));
        var start = tile.TranslatePoint(
            new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;

        var pointer = window.TouchBegin(start, RawInputModifiers.None);
        window.TouchMove(pointer, start + new Point(0, 60), RawInputModifiers.None);
        window.TouchEnd(pointer, start + new Point(0, 60), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.SelectedStation, Is.Null,
            "a real drag must scroll/bounce instead of opening the station");
        window.Close();
    }

    [AvaloniaTest]
    public void ProfileMenu_ListsManageAboveDisconnect()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var trigger = view.FindControl<Button>("ProfileMenuButton")!;
        var flyout = trigger.Flyout as Flyout;
        Assert.That(flyout, Is.Not.Null, "the profile avatar must open a flyout menu");

        flyout!.ShowAt(trigger);
        Dispatcher.UIThread.RunJobs();

        var content = (Control)flyout.Content!;
        var buttons = content.GetVisualDescendants().OfType<Button>().ToList();
        var manage = buttons.Single(button => Equals(button.Content, "manage"));
        var disconnect = buttons.Single(button => Equals(button.Content, "disconnect"));

        Assert.That(manage.Command, Is.SameAs(vm.OpenProfileSettingsCommand),
            "manage keeps the previous avatar action");
        Assert.That(disconnect.Command, Is.SameAs(vm.DisconnectCommand),
            "disconnect is available from the profile menu");

        var managePoint = manage.TranslatePoint(default, content)!.Value;
        var disconnectPoint = disconnect.TranslatePoint(default, content)!.Value;
        Assert.That(disconnectPoint.Y, Is.GreaterThan(managePoint.Y),
            "the menu options are stacked vertically");
        Assert.That(disconnectPoint.X, Is.EqualTo(managePoint.X).Within(0.5));

        manage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.That(flyout.IsOpen, Is.False, "choosing a menu action closes the overlay");

        window.Close();
    }

    [AvaloniaTest]
    public void BufferingUsesASmallChipAndDoesNotCoverNavigation()
    {
        var vm = CreateViewModel();
        vm.IsBuffering = true;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var bufferingLabels = nowPlaying.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == "BUFFERING" && t.IsEffectivelyVisible)
            .ToList();

        Assert.That(bufferingLabels, Has.Count.EqualTo(1),
            "only the compact chip should remain; the full-page blocking veil was removed");
        var chip = nowPlaying.FindControl<Border>("BufferingChip")!;
        Assert.That(chip.IsHitTestVisible, Is.False);
        Assert.That(nowPlaying.FindControl<Button>("BackToStations")!.IsEffectivelyVisible, Is.True);
        window.Close();
    }

    [AvaloniaTest]
    public void Landscape_ShowsThePlayerCogAndLeavesThePlaylistHeaderEmpty()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();

        Assert.That(nowPlaying.FindControl<Button>("StationSettingsButton")!.IsEffectivelyVisible, Is.True,
            "the player header always owns the settings cog");
        Assert.That(playlist.FindControl<Button>("PlaylistSettingsButton"), Is.Null,
            "the playlist header has no actions and stays empty");
        window.Close();
    }

    [AvaloniaTest]
    public void NarrowLayout_OnlyShowsThePlayerSettingsCog()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();

        Assert.That(nowPlaying.FindControl<Button>("StationSettingsButton")!.IsEffectivelyVisible, Is.True,
            "the player header owns the settings cog");
        Assert.That(playlist.FindControl<Button>("PlaylistSettingsButton"), Is.Null,
            "the playlist header has no actions in portrait either");
        window.Close();
    }

    [AvaloniaTest]
    public void PortraitVolumeFlyout_DoesNotClipTheThumb()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var player = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var button = player.FindControl<Button>("PortraitVolumeButton")!;
        var flyout = (Flyout)button.Flyout!;
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();

        var content = (Control)flyout.Content!;
        var slider = content.GetVisualDescendants().OfType<Slider>().Single();
        var thumb = slider.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single();

        Assert.That(thumb.Name, Is.EqualTo("SliderThumb"),
            "the vertical template names its thumb SliderThumb");
        Assert.That(thumb.Bounds.Width, Is.EqualTo(14).Within(0.5));
        Assert.That(thumb.Bounds.Height, Is.EqualTo(14).Within(0.5));
        Assert.That(((ISolidColorBrush)thumb.Background!).Color, Is.EqualTo(Colors.White),
            "the flyout thumb should not fall back to the blue Fluent accent");

        foreach (var volume in new[] { 100.0, 50.0, 0.0 })
        {
            vm.Volume = (int)volume;
            Dispatcher.UIThread.RunJobs();

            var thumbTopLeft = thumb.TranslatePoint(default, slider)!.Value;
            Assert.That(thumbTopLeft.X, Is.GreaterThanOrEqualTo(0),
                $"thumb at volume {volume} must not clip on the left");
            Assert.That(thumbTopLeft.X + thumb.Bounds.Width,
                Is.LessThanOrEqualTo(slider.Bounds.Width + 0.5),
                $"thumb at volume {volume} must not clip on the right");
            Assert.That(thumbTopLeft.Y, Is.GreaterThanOrEqualTo(0),
                $"thumb at volume {volume} must not clip at the top");
            Assert.That(thumbTopLeft.Y + thumb.Bounds.Height,
                Is.LessThanOrEqualTo(slider.Bounds.Height + 0.5),
                $"thumb at volume {volume} must not clip at the bottom");
            Assert.That(thumbTopLeft.X,
                Is.EqualTo((slider.Bounds.Width - thumb.Bounds.Width) / 2).Within(0.5),
                "the vertical track must be centred in the slider");
        }

        var popupTopLeft = content.TranslatePoint(default, window)!.Value;
        Assert.That(popupTopLeft.X, Is.GreaterThanOrEqualTo(0));
        Assert.That(popupTopLeft.Y, Is.GreaterThanOrEqualTo(0));
        Assert.That(popupTopLeft.X + content.Bounds.Width,
            Is.LessThanOrEqualTo(window.Bounds.Width + 0.5),
            "the flyout must stay inside the window");
        Assert.That(popupTopLeft.Y + content.Bounds.Height,
            Is.LessThanOrEqualTo(window.Bounds.Height + 0.5),
            "the flyout must stay inside the window");

        flyout.Hide();
        window.Close();
    }

    [AvaloniaTest]
    public void EveryLayout_ShowsExactlyOneVolumeControl()
    {
        var sizes = new (double Width, double Height)[]
        {
            (420, 820),   // phone portrait
            (760, 1000),  // portrait tablet: narrow shell, wide player pane
            (900, 1600),  // portrait tablet: wide player pane
            (800, 600),   // small wide window: wide shell, narrow player pane
            (1000, 700),  // desktop: wide shell, narrow player pane
            (1200, 800),  // desktop: wide shell, wide player pane
        };

        foreach (var (width, height) in sizes)
        {
            var vm = CreateViewModel();
            var view = new MetroShellView { DataContext = vm };
            var window = new Window { Width = width, Height = height, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var player = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
            var buttonVisible = player.FindControl<Button>("PortraitVolumeButton")!.IsEffectivelyVisible;
            var sliderVisible = player.FindControl<StackPanel>("VolumeGroup")!.IsEffectivelyVisible;

            Assert.That(new[] { buttonVisible, sliderVisible }.Count(v => v), Is.EqualTo(1),
                $"{width}x{height} (IsNarrow={view.IsNarrow}, player width={player.Bounds.Width:0}) " +
                "must show exactly one volume control, not both or neither");
            window.Close();
        }
    }

    [AvaloniaTest]
    public void ResizingTheWindow_KeepsExactlyOneVolumeControl()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var sizes = new (double Width, double Height)[]
        {
            (1200, 800), (1000, 700), (800, 600), (760, 1000), (420, 820), (900, 1600),
        };

        foreach (var (width, height) in sizes)
        {
            window.Width = width;
            window.Height = height;
            Dispatcher.UIThread.RunJobs();

            var player = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
            var buttonVisible = player.FindControl<Button>("PortraitVolumeButton")!.IsEffectivelyVisible;
            var sliderVisible = player.FindControl<StackPanel>("VolumeGroup")!.IsEffectivelyVisible;

            Assert.That(new[] { buttonVisible, sliderVisible }.Count(v => v), Is.EqualTo(1),
                $"after resizing to {width}x{height} (IsNarrow={view.IsNarrow}, " +
                $"player width={player.Bounds.Width:0}) exactly one volume control must remain");
        }

        window.Close();
    }

    [AvaloniaTest]
    public void Portrait_HasAVolumeFlyout_AndLandscapeKeepsTheInlineSlider()
    {
        var portraitVm = CreateViewModel();
        var portrait = new MetroShellView { DataContext = portraitVm };
        var portraitWindow = new Window { Width = 420, Height = 820, Content = portrait };
        portraitWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var portraitPlayer = portrait.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var volumeButton = portraitPlayer.FindControl<Button>("PortraitVolumeButton")!;
        Assert.That(volumeButton.IsEffectivelyVisible, Is.True);
        Assert.That(portraitPlayer.FindControl<StackPanel>("VolumeGroup")!.IsEffectivelyVisible, Is.False);

        var flyout = volumeButton.Flyout as Flyout;
        Assert.That(flyout, Is.Not.Null);
        Assert.That(flyout!.Placement, Is.EqualTo(PlacementMode.TopEdgeAlignedRight));
        var flyoutBorder = flyout.Content as Border;
        Assert.That(flyoutBorder, Is.Not.Null);
        Assert.That(flyoutBorder!.BorderThickness, Is.EqualTo(default(Thickness)),
            "the flyout should have no outline");
        Assert.That(((ISolidColorBrush)flyoutBorder.Background!).Color,
            Is.EqualTo(Color.Parse("#FC5D00")), "the flyout should use the brand highlight");
        var flyoutStack = flyoutBorder.Child as StackPanel;
        Assert.That(flyoutStack!.Children.OfType<TextBlock>(), Is.Empty,
            "the VOLUME label was removed");
        var portraitSlider = flyoutStack.Children.OfType<Slider>().Single();
        Assert.That(portraitSlider.Orientation, Is.EqualTo(Avalonia.Layout.Orientation.Vertical));

        flyout.ShowAt(volumeButton);
        Dispatcher.UIThread.RunJobs();
        portraitVm.Volume = 37;
        Dispatcher.UIThread.RunJobs();
        Assert.That(portraitSlider.Value, Is.EqualTo(37),
            "the portrait slider must bind to the player volume");
        flyout.Hide();
        portraitWindow.Close();

        var landscapeVm = CreateViewModel();
        var landscape = new MetroShellView { DataContext = landscapeVm };
        var landscapeWindow = new Window { Width = 1200, Height = 800, Content = landscape };
        landscapeWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var landscapePlayer = landscape.GetVisualDescendants().OfType<NowPlayingView>().Single();
        Assert.That(landscapePlayer.FindControl<Button>("PortraitVolumeButton")!.IsEffectivelyVisible, Is.False);
        Assert.That(landscapePlayer.FindControl<StackPanel>("VolumeGroup")!.IsEffectivelyVisible, Is.True);
        landscapeWindow.Close();
    }

    [AvaloniaTest]
    public void PlaylistHeadline_HasRoomForDescenders()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();
        var title = playlist.FindControl<TextBlock>("PlaylistTitle")!;

        Assert.That(title.LineHeight, Is.GreaterThanOrEqualTo(title.FontSize + 8),
            "the playlist line box must leave room for the p/y descenders");
        window.Close();
    }

    [AvaloniaTest]
    public void PlayerHasNoShellHeader_AndTheBackButtonLivesInTheCoverHeader()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = true;
        vm.SelectedStation!.Station!.Name = "Die Maus zum Hören";
        vm.ProfileName = "Kai Wegner";
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.That(view.FindControl<Border>("PlayerHeader"), Is.Null,
            "the player no longer has a separate app header");

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var back = nowPlaying.FindControl<Button>("BackToStations");
        Assert.That(back, Is.Not.Null);
        Assert.That(back!.Command, Is.SameAs(vm.ShowStationsCommand));
        Assert.That(nowPlaying.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Kai Wegner"),
            Is.False);
        window.Close();
    }

    [AvaloniaTest]
    public void PlayerPages_ExposeStationSettingsCogInsteadOfPlaylistShortcut()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();

        Assert.That(nowPlaying.FindControl<Button>("OpenPlaylistButton"), Is.Null,
            "the content-level playlist shortcut was removed");
        var nowPlayingSettings = nowPlaying.FindControl<Button>("StationSettingsButton");
        Assert.That(nowPlayingSettings, Is.Not.Null);
        Assert.That(nowPlayingSettings!.Command, Is.SameAs(vm.OpenSettingsCommand));
        Assert.That(nowPlaying.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "station settings"),
            Is.False, "the settings link at the bottom was removed");

        var playlistSettings = playlist.FindControl<Button>("PlaylistSettingsButton");
        Assert.That(playlistSettings, Is.Null,
            "the playlist header is empty, so settings live only in the player header");
        window.Close();
    }

    [AvaloniaTest]
    public void LongNowPlayingMetadata_ScrollsTitleDateAndDescription()
    {
        var vm = CreateViewModel();
        vm.CurrentEpisodeTitle = string.Join(" ", Enumerable.Repeat("A very long episode title", 6));
        vm.CurrentEpisodeDescription = string.Join(" ", Enumerable.Repeat(
            "This is a long description that needs to be reachable by scrolling.", 20));
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 600, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var scroll = nowPlaying.FindControl<ScrollViewer>("NowPlayingDetailsScroll")!;
        var title = nowPlaying.FindControl<TextBlock>("NowPlayingTitle")!;
        var description = nowPlaying.FindControl<LinkTextBlock>("NowPlayingDescription")!;

        Assert.That(scroll.VerticalScrollBarVisibility, Is.EqualTo(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden),
            "the player uses rubber-band scrolling instead of a visible scrollbar");
        Assert.That(title.GetVisualAncestors().Contains(scroll), Is.True,
            "the title must scroll with the description");
        Assert.That(description.GetVisualAncestors().Contains(scroll), Is.True,
            "the description must be inside the same scroller");
        Assert.That(scroll.Extent.Height, Is.GreaterThan(scroll.Viewport.Height),
            "long metadata must actually overflow the available space");

        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        Assert.That(scroll.Offset.Y, Is.GreaterThan(0),
            "the metadata area must scroll down to the description");
        window.Close();
    }

    [AvaloniaTest]
    public void NowPlayingMetadata_BouncesOnlyAtTheScrollEdges()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var content = nowPlaying.FindControl<Grid>("NowPlayingDetailsContent")!;
        var scroll = nowPlaying.FindControl<ScrollViewer>("NowPlayingDetailsScroll")!;

        Assert.That(content.RenderTransform, Is.TypeOf<TranslateTransform>());
        Assert.That(RubberBand.Offset(0, 500, 80), Is.GreaterThan(0),
            "dragging down at the top should bounce");
        Assert.That(RubberBand.Offset(500, 500, -80), Is.LessThan(0),
            "dragging up at the bottom should bounce");
        Assert.That(RubberBand.Offset(250, 500, 80), Is.EqualTo(0),
            "dragging in the middle should scroll normally, not bounce");
        Assert.That(scroll.VerticalScrollBarVisibility,
            Is.EqualTo(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden));
        window.Close();
    }

    [AvaloniaTest]
    public void ShortNowPlayingMetadata_StaysBottomAnchored()
    {
        var vm = CreateViewModel();
        vm.CurrentEpisodeTitle = "Short title";
        vm.CurrentEpisodeDescription = "Short description.";
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var scroll = nowPlaying.FindControl<ScrollViewer>("NowPlayingDetailsScroll")!;
        var title = nowPlaying.FindControl<TextBlock>("NowPlayingTitle")!;
        var titlePoint = title.TranslatePoint(new Point(0, 0), scroll);

        Assert.That(titlePoint, Is.Not.Null);
        Assert.That(titlePoint!.Value.Y, Is.GreaterThan(scroll.Bounds.Height / 2),
            "short metadata should sit at the bottom of the available space, not at the top");
        window.Close();
    }

    [AvaloniaTest]
    public void NarrowPanorama_HorizontalDragOwnsTheGestureFromTheDetailsScroller()
    {
        var vm = CreateViewModel();
        vm.CurrentEpisodeTitle = "A long title that makes the details area scrollable";
        vm.CurrentEpisodeDescription = string.Join(" ", Enumerable.Repeat(
            "Long description that makes the details area scrollable.", 20));
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var scroll = nowPlaying.FindControl<ScrollViewer>("NowPlayingDetailsScroll")!;
        var details = nowPlaying.FindControl<Grid>("NowPlayingDetailsContent")!;
        var bounce = (TranslateTransform)details.RenderTransform!;
        var canvas = view.FindControl<Canvas>("PanoramaCanvas")!;
        var transform = (TranslateTransform)canvas.RenderTransform!;
        var start = scroll.TranslatePoint(
            new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), window)!.Value;
        var offset = scroll.Offset.Y;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start - new Point(60, 0), RawInputModifiers.LeftMouseButton);
        window.MouseMove(start - new Point(80, 100), RawInputModifiers.LeftMouseButton);
        window.MouseMove(start - new Point(80, -80), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        Assert.That(transform.X, Is.LessThan(-10),
            "the horizontal drag should move the panorama");
        Assert.That(scroll.Offset.Y, Is.EqualTo(offset).Within(1),
            "the details scroller must not take over a horizontal drag");
        Assert.That(bounce.Y, Is.EqualTo(0).Within(0.01),
            "the details rubber-band must not react once the panorama owns the pointer");

        window.MouseUp(start - new Point(80, -80), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.IsPlaylistOpen, Is.False,
            "a short drag should settle back on the player");
        window.Close();
    }

    [AvaloniaTest]
    public void PlayerHeaderButtons_StillReceiveTaps()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = true;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var settings = nowPlaying.FindControl<Button>("StationSettingsButton")!;
        var settingsPoint = settings.TranslatePoint(
            new Point(settings.Bounds.Width / 2, settings.Bounds.Height / 2), window)!.Value;

        window.MouseDown(settingsPoint, MouseButton.Left);
        window.MouseUp(settingsPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.IsSettingsOpen, Is.True,
            "the settings cog must still receive taps through the panorama handlers");

        vm.CloseSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var back = nowPlaying.FindControl<Button>("BackToStations")!;
        var backPoint = back.TranslatePoint(
            new Point(back.Bounds.Width / 2, back.Bounds.Height / 2), window)!.Value;

        window.MouseDown(backPoint, MouseButton.Left);
        window.MouseUp(backPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.IsNowPlayingOpen, Is.False,
            "the back button must still receive taps through the panorama handlers");
        window.Close();
    }

    [AvaloniaTest]
    public void NarrowPanorama_SwipesToThePlaylistAndBack()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        view.SwipeForTests(-300);
        Dispatcher.UIThread.RunJobs();
        Assert.That(vm.IsPlaylistOpen, Is.True, "a left swipe should reveal the playlist");

        view.SwipeForTests(300);
        Dispatcher.UIThread.RunJobs();
        Assert.That(vm.IsPlaylistOpen, Is.False, "a right swipe should return to now playing");
        window.Close();
    }

    [AvaloniaTest]
    public void NarrowPanorama_InterruptedSwipeSettlesOnAPage()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewport = view.FindControl<Panel>("PanoramaViewport")!;
        var canvas = view.FindControl<Canvas>("PanoramaCanvas")!;
        var transform = (TranslateTransform)canvas.RenderTransform!;

        // Simulate the transform being left halfway with no active pan flag,
        // as happens when a snap is interrupted and the release is missed.
        transform.Transitions = null;
        transform.X = -(viewport.Bounds.Width - 128) / 2;
        view.SettlePanoramaForTests();
        Dispatcher.UIThread.RunJobs();

        Assert.That(transform.X, Is.EqualTo(0).Within(1),
            "an interrupted swipe on the player must settle back to the player");

        vm.IsPlaylistOpen = true;
        Dispatcher.UIThread.RunJobs();
        transform.Transitions = null;
        transform.X = -(viewport.Bounds.Width - 128) / 2;
        view.SettlePanoramaForTests();
        Dispatcher.UIThread.RunJobs();

        Assert.That(transform.X, Is.EqualTo(-(viewport.Bounds.Width - 128)).Within(1),
            "an interrupted swipe on the playlist must settle back to the playlist");
        window.Close();
    }

    [AvaloniaTest]
    public void NarrowStationsHeader_RemovesPlayerLabelAndTightensGutters()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 360, Height = 780, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var header = view.FindControl<Grid>("StationsHeader")!;
        var grid = view.FindControl<ItemsControl>("StationGrid")!;

        Assert.That(header.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "player"),
            Is.False, "the redundant 'player' label was removed from the wordmark");
        Assert.That(header.Margin.Left, Is.EqualTo(16).Within(0.1),
            "portrait should use the tighter header gutter");
        Assert.That(grid.Margin.Left, Is.EqualTo(16).Within(0.1),
            "portrait should use the tighter content gutter");
        Assert.That(grid.Margin.Right, Is.EqualTo(16).Within(0.1));
        window.Close();
    }

    [AvaloniaTest]
    public void PlayerLayer_IsImmersiveButOtherScreensKeepSafeAreaPadding()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MainView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.That(TopLevel.GetAutoSafeAreaPadding(view), Is.True,
            "the station screen should stay inside the safe area");

        vm.IsNowPlayingOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.That(TopLevel.GetAutoSafeAreaPadding(view), Is.False,
            "the player should paint behind the status and navigation bars");

        vm.IsNowPlayingOpen = false;
        Dispatcher.UIThread.RunJobs();
        Assert.That(TopLevel.GetAutoSafeAreaPadding(view), Is.True,
            "leaving the player should restore the safe-area padding");
        window.Close();
    }

    [AvaloniaTest]
    public void NarrowStationsGrid_TilesFillTheAvailableWidth()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 360, Height = 780, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grid = view.FindControl<ItemsControl>("StationGrid")!;
        var wrap = grid.GetVisualDescendants().OfType<WrapPanel>().Single();
        var tile = grid.GetVisualDescendants().OfType<Button>()
            .First(button => button.Classes.Contains("stationTile"));

        Assert.That(view.StationTileWidth, Is.EqualTo(328).Within(1),
            $"computed slot width (WrapPanel={wrap.ItemWidth}, tile={tile.Bounds.Width})");
        Assert.That(wrap.ItemWidth, Is.EqualTo(view.StationTileWidth).Within(0.5),
            "the WrapPanel should take its slot width from the shell");
        Assert.That(tile.Bounds.Width, Is.GreaterThan(280),
            "the station tile should grow into the tighter portrait gutters");
        Assert.That(tile.Bounds.Width, Is.LessThanOrEqualTo(view.Bounds.Width - 32),
            "the tile must stay inside the portrait gutters");
        window.Close();
    }

    [AvaloniaTest]
    public void NarrowStationsHeader_DoesNotSqueezeTheHeadline()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        vm.ProfileName = "Kai Wegner";
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 360, Height = 780, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var title = view.FindControl<TextBlock>("StationsTitle")!;
        var point = title.TranslatePoint(new Point(0, 0), view);

        Assert.That(title.Bounds.Width, Is.GreaterThanOrEqualTo(title.DesiredSize.Width - 1),
            "the stations headline must get its full width instead of being clipped by the profile actions");
        Assert.That(point!.Value.X + title.Bounds.Width, Is.LessThanOrEqualTo(view.Bounds.Width + 1));
        window.Close();
    }

    [AvaloniaTest]
    public void NarrowTransport_KeepsForwardButtonInsideThePane()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 360, Height = 780, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlayingView = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var forward = nowPlayingView.FindControl<Button>("NextButton")!;
        var point = forward.TranslatePoint(new Avalonia.Point(0, 0), nowPlayingView);

        Assert.That(point, Is.Not.Null);
        Assert.That(point!.Value.X + forward.Bounds.Width,
            Is.LessThanOrEqualTo(nowPlayingView.Bounds.Width + 1),
            "the forward transport button must not be clipped on narrow screens");
        window.Close();
    }

    [AvaloniaTest]
    public void SettingsAndEpisodeModals_FitNarrowPortraitScreens()
    {
        var vm = CreateViewModel();
        vm.OpenStationSettingsForCommand.Execute(vm.Stations[0]);
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var settings = view.FindControl<Panel>("SettingsOverlay")!.GetVisualDescendants()
            .OfType<Border>().First(b => b.Classes.Contains("modalCard"));
        Assert.That(settings.Bounds.Width, Is.LessThanOrEqualTo(420 - 39),
            "the settings modal must fit inside the phone viewport with its 20px margins");

        vm.CloseSettingsCommand.Execute(null);
        vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = "e1", Title = "An episode with a long title" });
        vm.OpenEpisodeActionsCommand.Execute(vm.PlaylistItems[0]);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var episode = view.FindControl<Panel>("EpisodeActionsOverlay")!.GetVisualDescendants()
            .OfType<Border>().First(b => b.Classes.Contains("modalCard"));
        Assert.That(episode.Bounds.Width, Is.LessThanOrEqualTo(420 - 39),
            "the episode modal must fit inside the phone viewport with its 20px margins");
        window.Close();
    }

    [AvaloniaTest]
    public void StationSettingsModal_IsPartOfTheShell()
    {
        var vm = CreateViewModel();
        var station = vm.Stations[0];
        vm.OpenStationSettingsForCommand.Execute(station);
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1100, Height = 760, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var overlay = view.FindControl<Panel>("SettingsOverlay")!;
        Assert.That(overlay.IsVisible, Is.True);
        Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Morning Brief"), Is.True);

        Assert.That(overlay.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "×"), Is.False,
            "the settings modal no longer has a close X");
        Assert.That(overlay.GetVisualDescendants().OfType<Button>()
                .Any(b => Equals(b.Content, "more settings")), Is.True,
            "the external settings action is labelled 'more settings'");
        Assert.That(overlay.GetVisualDescendants().OfType<Button>()
                .Any(b => Equals(b.Content, "done")), Is.True,
            "the done button remains as the close action");
        window.Close();
    }

    [AvaloniaTest]
    public async Task StationLongPress_OpensSettingsWithoutStartingPlayback()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var tile = view.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("stationTile"));
        var start = tile.TranslatePoint(
            new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;

        var pointer = window.TouchBegin(start, RawInputModifiers.None);

        // Hold past the 520 ms threshold. Awaiting returns to the dispatcher so
        // the long-press timer can tick.
        await Task.Delay(700);
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.IsSettingsOpen, Is.True,
            "holding a station tile must open that station's settings");
        Assert.That(vm.SelectedStation, Is.SameAs(vm.Stations[0]),
            "opening settings must not switch playback");
        Assert.That(vm.SettingsStation, Is.SameAs(vm.Stations[0]));

        window.TouchEnd(pointer, start, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.That(vm.SelectedStation, Is.SameAs(vm.Stations[0]),
            "releasing after a long press must not start playback");
        window.Close();
    }

    [AvaloniaTest]
    public async Task StationLongPress_WithMouse_OpensSettings()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var tile = view.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("stationTile"));
        var start = tile.TranslatePoint(
            new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;

        window.MouseDown(start, MouseButton.Left);
        await Task.Delay(700);
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.IsSettingsOpen, Is.True,
            "holding a station tile with the mouse must open its settings");
        Assert.That(vm.SettingsStation, Is.SameAs(vm.Stations[0]));

        window.MouseUp(start, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.That(vm.SelectedStation, Is.SameAs(vm.Stations[0]),
            "releasing after a long press must not switch playback");
        window.Close();
    }

    [AvaloniaTest]
    public void StationsHeader_ShowsTheProfileInitialInItsColor()
    {
        var vm = CreateViewModel();
        vm.ProfileName = "kai";
        vm.ProfileColor = "#3b82f6";
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var avatar = view.FindControl<Border>("ProfileAvatar")!;
        var initial = view.FindControl<TextBlock>("ProfileInitialText")!;
        var header = view.FindControl<Grid>("StationsHeader")!;

        Assert.That(initial.Text, Is.EqualTo("K"),
            "the profile avatar shows only the first letter");
        Assert.That(avatar.Background, Is.InstanceOf<ISolidColorBrush>());
        Assert.That(((ISolidColorBrush)avatar.Background!).Color, Is.EqualTo(Color.Parse("#3B82F6")),
            "the avatar uses the profile's color");
        Assert.That(header.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "kai"), Is.False,
            "the full profile name is no longer shown in the header");
        window.Close();
    }

    [AvaloniaTest]
    public void PlayerHeader_UsesTheStationNameAsItsPageTitle()
    {
        var vm = CreateViewModel();
        vm.SelectedStation!.Station!.Name = "Die Maus zum Hören";
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var title = nowPlaying.FindControl<TextBlock>("NowPlayingStationTitle")!;
        var back = nowPlaying.FindControl<Button>("BackToStations")!;
        var settings = nowPlaying.FindControl<Button>("StationSettingsButton")!;

        Assert.That(title.Text, Is.EqualTo("Die Maus zum Hören"));
        Assert.That(title.IsEffectivelyVisible, Is.True,
            "the station name belongs in the page title slot");
        Assert.That(nowPlaying.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text == "now playing" && t.IsEffectivelyVisible), Is.False,
            "the now playing eyebrow was dropped");
        Assert.That(back.IsEffectivelyVisible, Is.True);
        Assert.That(settings.IsEffectivelyVisible, Is.True);
        Assert.That(back.TranslatePoint(default, nowPlaying)!.Value.Y,
            Is.LessThan(title.TranslatePoint(default, nowPlaying)!.Value.Y),
            "back and settings occupy the header row above the title");
        window.Close();
    }

    [AvaloniaTest]
    public void PlaylistHeader_MatchesThePlayerTitleAndHasNoActions()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();
        var playerTitle = nowPlaying.FindControl<TextBlock>("NowPlayingStationTitle")!;
        var playlistTitle = playlist.FindControl<TextBlock>("PlaylistTitle")!;

        Assert.That(playlistTitle.Text, Is.EqualTo("playlist"));
        Assert.That(playlistTitle.FontFamily!.Name, Is.EqualTo(playerTitle.FontFamily!.Name),
            "both page titles use the Metro display face");
        Assert.That(playlistTitle.FontSize, Is.EqualTo(playerTitle.FontSize).Within(0.5),
            "both page titles use the same display size");
        Assert.That(playlistTitle.TranslatePoint(default, view)!.Value.Y,
            Is.EqualTo(playerTitle.TranslatePoint(default, view)!.Value.Y).Within(1.5),
            "the playlist title sits in the same row as the player title");
        Assert.That(playlist.FindControl<StackPanel>("PlaylistHeader")!
                .GetVisualDescendants().OfType<Button>(), Is.Empty,
            "the playlist header control row is empty");
        window.Close();
    }

    [AvaloniaTest]
    public void StationTitle_CollapsesWhileTheListScrollsBehindIt()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        for (var i = 0; i < 24; i++)
            vm.Stations.Add(new StationWithFeedsDto
            {
                Station = new Station { Id = $"s{i}", Name = $"Station {i}" },
            });

        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var scroll = view.FindControl<ScrollViewer>("StationsScroll")!;
        var title = view.FindControl<TextBlock>("StationsTitle")!;
        var titleHost = view.FindControl<MetroTitleHost>("StationsTitleHost")!;
        var transform = (ScaleTransform)title.RenderTransform!;
        var titleTop = title.TranslatePoint(default, view)!.Value.Y;
        var scrollTop = scroll.TranslatePoint(default, view)!.Value.Y;
        var expandedHostHeight = titleHost.Bounds.Height;
        var expandedInset = scroll.Padding.Top;

        Assert.That(scroll.Padding.Top, Is.GreaterThan(40),
            "the list must reserve room for the title at rest");
        Assert.That(scrollTop, Is.LessThan(titleTop),
            "the list paints underneath the header");
        Assert.That(transform.ScaleX, Is.EqualTo(1).Within(0.01));

        scroll.Offset = new Vector(0, 240);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.That(transform.ScaleX, Is.LessThan(0.8),
            "scrolling the stations must shrink the title");
        Assert.That(titleHost.Bounds.Height, Is.LessThan(expandedHostHeight * 0.8),
            "the title host must shrink with the title");
        Assert.That(titleHost.Bounds.Height,
            Is.EqualTo(title.LineHeight * 0.45).Within(1),
            "the host must take exactly the scaled line height");
        Assert.That(title.Bounds.Height, Is.EqualTo(title.LineHeight).Within(1),
            "the title must keep its natural layout height so glyphs are not clipped");
        Assert.That(scroll.Padding.Top, Is.LessThan(expandedInset - 20),
            "the shrunken header must release space to the list");
        Assert.That(title.TranslatePoint(default, view)!.Value.Y,
            Is.EqualTo(titleTop).Within(0.5),
            "the title stays put while the content scrolls behind it");
        window.Close();
    }

    [AvaloniaTest]
    public void PlaylistTitle_CollapsesWhileTheListScrollsBehindIt()
    {
        var vm = CreateViewModel();
        for (var i = 0; i < 24; i++)
            vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = $"e{i}", Title = $"Episode {i}" });

        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();
        var scroll = playlist.FindControl<ListBox>("PlaylistList")!
            .GetVisualDescendants().OfType<ScrollViewer>().Single();
        var title = playlist.FindControl<TextBlock>("PlaylistTitle")!;
        var titleHost = playlist.FindControl<MetroTitleHost>("PlaylistTitleHost")!;
        var transform = (ScaleTransform)title.RenderTransform!;
        var titleTop = title.TranslatePoint(default, view)!.Value.Y;
        var expandedHostHeight = titleHost.Bounds.Height;
        var expandedInset = scroll.Padding.Top;

        Assert.That(scroll.Padding.Top, Is.GreaterThan(40),
            "the list must reserve room for the header");
        Assert.That(transform.ScaleX, Is.EqualTo(1).Within(0.01));

        scroll.Offset = new Vector(0, 200);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.That(transform.ScaleX, Is.LessThan(0.8),
            "scrolling the playlist must shrink its title");
        Assert.That(titleHost.Bounds.Height, Is.LessThan(expandedHostHeight * 0.8),
            "the title host must shrink with the title");
        Assert.That(titleHost.Bounds.Height,
            Is.EqualTo(title.LineHeight * 0.45).Within(1),
            "the host must take exactly the scaled line height");
        Assert.That(title.Bounds.Height, Is.EqualTo(title.LineHeight).Within(1),
            "the title must keep its natural layout height so glyphs are not clipped");
        Assert.That(scroll.Padding.Top, Is.LessThan(expandedInset - 20),
            "the shrunken header must release space to the list");
        Assert.That(title.TranslatePoint(default, view)!.Value.Y,
            Is.EqualTo(titleTop).Within(0.5));
        window.Close();
    }

    [AvaloniaTest]
    public void LongPlayerMetadata_StartsBelowTheHeaderAndFadesBehindIt()
    {
        var vm = CreateViewModel();
        vm.CurrentEpisodeDescription = string.Join(" ", Enumerable.Repeat(
            "Long description that makes the details area scrollable.", 100));
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var header = nowPlaying.FindControl<Grid>("NowPlayingHeader")!;
        var content = nowPlaying.FindControl<Grid>("NowPlayingDetailsContent")!;
        var fade = nowPlaying.FindControl<Border>("NowPlayingHeaderFade");

        Assert.That(header.Bounds.Height, Is.GreaterThan(40));
        Assert.That(content.Margin.Top, Is.GreaterThanOrEqualTo(header.Bounds.Height),
            "long metadata must start below the page header, not underneath it");
        Assert.That(fade, Is.Not.Null,
            "the player needs a fade layer between the scrolling details and the header");
        Assert.That(fade!.IsHitTestVisible, Is.False);
        Assert.That(fade.Height, Is.GreaterThanOrEqualTo(header.Bounds.Height),
            "the fade must cover the header");

        var descendants = nowPlaying.GetVisualDescendants().ToList();
        var scroll = nowPlaying.FindControl<ScrollViewer>("NowPlayingDetailsScroll")!;
        Assert.That(descendants.IndexOf(fade), Is.GreaterThan(descendants.IndexOf(scroll)),
            "the fade paints above the scrolling details");
        Assert.That(descendants.IndexOf(fade), Is.LessThan(descendants.IndexOf(header)),
            "the header text paints above the fade");
        window.Close();
    }

    [AvaloniaTest]
    public void PlayerTitle_CollapsesWhenLongMetadataScrolls()
    {
        var vm = CreateViewModel();
        vm.CurrentEpisodeDescription = string.Join(" ", Enumerable.Repeat(
            "Long description that makes the details area scrollable.", 100));
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var scroll = nowPlaying.FindControl<ScrollViewer>("NowPlayingDetailsScroll")!;
        var title = nowPlaying.FindControl<TextBlock>("NowPlayingStationTitle")!;
        var titleHost = nowPlaying.FindControl<MetroTitleHost>("NowPlayingStationTitleHost")!;
        var header = nowPlaying.FindControl<Grid>("NowPlayingHeader")!;
        var content = nowPlaying.FindControl<Grid>("NowPlayingDetailsContent")!;
        var transform = (ScaleTransform)title.RenderTransform!;
        var expandedHostHeight = titleHost.Bounds.Height;
        var expandedHeaderHeight = header.Bounds.Height;
        var expandedInset = content.Margin.Top;

        Assert.That(scroll.Extent.Height, Is.GreaterThan(scroll.Viewport.Height),
            "long metadata must be scrollable for the title to collapse");
        Assert.That(transform.ScaleX, Is.EqualTo(1).Within(0.01));

        scroll.Offset = new Vector(0, 160);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.That(transform.ScaleX, Is.LessThan(0.8),
            "scrolling the player details must shrink the station title");
        Assert.That(titleHost.Bounds.Height, Is.LessThan(expandedHostHeight * 0.8),
            "the title host must shrink with the title");
        Assert.That(titleHost.Bounds.Height,
            Is.EqualTo(title.LineHeight * 0.5).Within(1),
            "the host must take exactly the scaled line height");
        Assert.That(title.Bounds.Height, Is.EqualTo(title.LineHeight).Within(1),
            "the title must keep its natural layout height so glyphs are not clipped");
        Assert.That(header.Bounds.Height, Is.LessThan(expandedHeaderHeight - 10),
            "the header must collapse as its title shrinks");
        Assert.That(content.Margin.Top, Is.LessThan(expandedInset),
            "the details must move up into the space released by the header");
        window.Close();
    }

    [AvaloniaTest]
    public void PlayerHeaderFade_UsesTheSameGradientAsThePlaylist()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();
        var playerFade = nowPlaying.FindControl<Border>("NowPlayingHeaderFade")!;
        var playlistFade = playlist.FindControl<Border>("PlaylistHeaderFade")!;
        var stationsFade = view.FindControl<Border>("StationsHeaderFade")!;

        Assert.That(playerFade.Height, Is.EqualTo(playlistFade.Height).Within(0.5),
            "the player header fade must use the same height as the playlist");
        Assert.That(ReferenceEquals(playerFade.Background, playlistFade.Background), Is.True,
            "the player header fade must use the same brush as the playlist");
        Assert.That(ReferenceEquals(playerFade.Background, stationsFade.Background), Is.True,
            "all three page headers share one fade brush");
        window.Close();
    }

    [AvaloniaTest]
    public void SelectingAStation_ResetsTheDetailsScrollAndRestoresTheFullTitle()
    {
        var vm = CreateViewModel();
        vm.Stations.Add(new StationWithFeedsDto
        {
            Station = new Station { Id = "s2", Name = "Morning Briefing" },
        });
        vm.CurrentEpisodeDescription = string.Join(" ", Enumerable.Repeat(
            "Long description that makes the details area scrollable.", 100));
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var scroll = nowPlaying.FindControl<ScrollViewer>("NowPlayingDetailsScroll")!;
        var title = nowPlaying.FindControl<TextBlock>("NowPlayingStationTitle")!;
        var titleHost = nowPlaying.FindControl<MetroTitleHost>("NowPlayingStationTitleHost")!;
        var transform = (ScaleTransform)title.RenderTransform!;

        scroll.Offset = new Vector(0, 200);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.That(transform.ScaleX, Is.LessThan(0.8), "sanity: the title is collapsed");
        Assert.That(titleHost.Bounds.Height, Is.LessThan(title.LineHeight * 0.8));

        vm.SelectedStation = vm.Stations[1];
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.That(scroll.Offset.Y, Is.EqualTo(0).Within(0.5),
            "a newly selected station must start at the top");
        Assert.That(transform.ScaleX, Is.EqualTo(1).Within(0.01),
            "the station title must expand again for the new station");
        Assert.That(titleHost.Bounds.Height, Is.EqualTo(title.LineHeight).Within(1),
            "the title host must return to the full line height");
        Assert.That(title.IsEffectivelyVisible, Is.True);
        window.Close();
    }

    [AvaloniaTest]
    public void PlayerHeaderTitle_PassesScrollGesturesToTheDetails()
    {
        var vm = CreateViewModel();
        vm.CurrentEpisodeDescription = string.Join(" ", Enumerable.Repeat(
            "Long description that makes the details area scrollable.", 100));
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var scroll = nowPlaying.FindControl<ScrollViewer>("NowPlayingDetailsScroll")!;
        var title = nowPlaying.FindControl<TextBlock>("NowPlayingStationTitle")!;
        var start = title.TranslatePoint(
            new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), window)!.Value;

        window.MouseWheel(start, new Vector(0, -3));
        Dispatcher.UIThread.RunJobs();

        Assert.That(scroll.Offset.Y, Is.GreaterThan(20),
            "a wheel over the page title must still scroll the content behind it");
        window.Close();
    }

    [AvaloniaTest]
    public void PlaylistHeader_PassesScrollGesturesToTheList()
    {
        var vm = CreateViewModel();
        for (var i = 0; i < 24; i++)
            vm.PlaylistItems.Add(new PlaylistItem { EpisodeId = $"e{i}", Title = $"Episode {i}" });

        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();
        var scroll = playlist.FindControl<ListBox>("PlaylistList")!
            .GetVisualDescendants().OfType<ScrollViewer>().Single();
        var title = playlist.FindControl<TextBlock>("PlaylistTitle")!;
        var start = title.TranslatePoint(
            new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), window)!.Value;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start - new Point(0, 160), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();

        Assert.That(scroll.Offset.Y, Is.GreaterThan(20),
            "a drag over the playlist title must still scroll the list behind it");
        window.MouseUp(start - new Point(0, 160), MouseButton.Left);
        window.Close();
    }

    [AvaloniaTest]
    public void NowPlayingHeader_AlignsBackButtonStationTitleAndMetadata()
    {
        var vm = CreateViewModel();
        vm.PlaylistItems.Add(new PlaylistItem
        {
            EpisodeId = "e1",
            Title = "Episode",
            Artist = "Artist",
            PublishedAt = new DateTime(2026, 11, 26),
        });
        vm.CurrentEpisodeId = "e1";
        vm.CurrentEpisodeDescription = "A description.";

        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var backIcon = nowPlaying.FindControl<Button>("BackToStations")!
            .GetVisualDescendants().OfType<PathIcon>().Single();
        var stationTitle = nowPlaying.FindControl<TextBlock>("NowPlayingStationTitle")!;
        var episodeTitle = nowPlaying.FindControl<TextBlock>("NowPlayingTitle")!;
        var date = nowPlaying.FindControl<TextBlock>("NowPlayingDate")!;
        var description = nowPlaying.FindControl<LinkTextBlock>("NowPlayingDescription")!;

        double Left(Control control) => control.TranslatePoint(default, view)!.Value.X;
        var gutter = Left(stationTitle);

        Assert.Multiple(() =>
        {
            Assert.That(gutter, Is.EqualTo(24).Within(0.5),
                "the now-playing page uses a 24px left gutter");
            Assert.That(nowPlaying.FindControl<Border>("TransportDeck")!.Padding.Left,
                Is.EqualTo(24).Within(0.5),
                "the transport deck must share the same left gutter");
            Assert.That(Left(backIcon), Is.EqualTo(gutter).Within(0.5),
                "the back arrow must share the page gutter");
            Assert.That(Left(episodeTitle), Is.EqualTo(gutter).Within(0.5),
                "the episode title must share the page gutter");
            Assert.That(Left(date), Is.EqualTo(gutter).Within(0.5),
                "the episode date must share the page gutter");
            Assert.That(Left(description), Is.EqualTo(gutter).Within(0.5),
                "the episode description must share the page gutter");
        });

        window.Close();
    }

    [AvaloniaTest]
    public void PlaylistCoverArt_AlignsWithTheHeaderGutter()
    {
        var vm = CreateViewModel();
        vm.DownloadStatusMessage = "Set a buffer in Settings to download episodes.";
        vm.PlaylistItems.Add(new PlaylistItem
        {
            EpisodeId = "e1",
            Title = "Current episode",
            CoverUrl = "cover.png",
            IsCurrent = true,
        });
        vm.PlaylistItems.Add(new PlaylistItem
        {
            EpisodeId = "e2",
            Title = "Next episode",
            CoverUrl = "cover.png",
        });

        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();
        var header = playlist.FindControl<TextBlock>("PlaylistTitle")!;
        var covers = playlist.GetVisualDescendants().OfType<Border>()
            .Where(border => Math.Abs(border.Width - 58) < 0.1)
            .ToList();

        Assert.That(covers, Has.Count.EqualTo(2),
            "both the current and the next episode should render a cover");

        double Left(Control control) => control.TranslatePoint(default, view)!.Value.X;
        var gutter = Left(header);
        Assert.Multiple(() =>
        {
            foreach (var cover in covers)
                Assert.That(Left(cover), Is.EqualTo(gutter).Within(0.5),
                    "every episode cover must line up with the playlist header");
        });

        window.Close();
    }
}
