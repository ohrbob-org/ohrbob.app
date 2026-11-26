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
    public void PortraitStationsHeader_ShowsDisconnectAndUsesPullRefresh()
    {
        var vm = CreateViewModel();
        vm.IsNowPlayingOpen = false;
        var view = new MetroShellView { DataContext = vm };
        var window = new Window { Width = 420, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.That(view.FindControl<Button>("DisconnectIconButton")!.IsEffectivelyVisible, Is.True,
            "disconnect must remain available in portrait");
        Assert.That(view.FindControl<Button>("DisconnectButton")!.IsEffectivelyVisible, Is.False,
            "portrait uses the compact disconnect icon");

        var refresh = view.FindControl<RefreshContainer>("StationsRefresh");
        Assert.That(refresh, Is.Not.Null);
        Assert.That(refresh!.PullDirection, Is.EqualTo(PullDirection.TopToBottom));
        Assert.That(view.GetVisualDescendants().OfType<Button>().Any(b => ReferenceEquals(b.Command, vm.RefreshAllCommand)),
            Is.False, "the header refresh button was removed");
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
