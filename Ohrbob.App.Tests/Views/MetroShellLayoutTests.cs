using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
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
    public void Landscape_OnlyShowsThePlaylistSettingsCog()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        var playlist = view.GetVisualDescendants().OfType<PlaylistView>().Single();

        Assert.That(nowPlaying.FindControl<Button>("StationSettingsButton")!.IsEffectivelyVisible, Is.False,
            "the player-side cog is redundant when both panes are visible");
        Assert.That(playlist.FindControl<Button>("PlaylistSettingsButton")!.IsEffectivelyVisible, Is.True,
            "the playlist cog remains the single settings entry in landscape");
        window.Close();
    }

    [AvaloniaTest]
    public void NarrowLayout_KeepsThePlayerSettingsCog()
    {
        var vm = CreateViewModel();
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var nowPlaying = view.GetVisualDescendants().OfType<NowPlayingView>().Single();
        Assert.That(nowPlaying.FindControl<Button>("StationSettingsButton")!.IsEffectivelyVisible, Is.True);
        window.Close();
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
        Assert.That(playlistSettings, Is.Not.Null);
        Assert.That(playlistSettings!.Command, Is.SameAs(vm.OpenSettingsCommand));
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
        Assert.That(NowPlayingView.RubberBandOffset(0, 500, 80), Is.GreaterThan(0),
            "dragging down at the top should bounce");
        Assert.That(NowPlayingView.RubberBandOffset(500, 500, -80), Is.LessThan(0),
            "dragging up at the bottom should bounce");
        Assert.That(NowPlayingView.RubberBandOffset(250, 500, 80), Is.EqualTo(0),
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
        window.Close();
    }
}
