using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
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
        Assert.That(Canvas.GetLeft(playlist), Is.EqualTo(now.Bounds.Width).Within(2));
        Assert.That(Canvas.GetLeft(playlist), Is.LessThan(viewport.Bounds.Width),
            "the next Metro page must peek in from the right");
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

        Assert.That(transform.X, Is.EqualTo(-(viewport.Bounds.Width - 64)).Within(2),
            "opening the playlist should snap the panorama to the second page");
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
