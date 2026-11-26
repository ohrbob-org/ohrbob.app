using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Ohrbob.App.Models;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Views;

public partial class PlaylistView : UserControl
{
    private readonly DispatcherTimer _longPressTimer;
    private PlaylistItem? _pressedItem;

    public PlaylistView()
    {
        InitializeComponent();

        _longPressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(520),
        };
        _longPressTimer.Tick += OnLongPressElapsed;
    }

    private void OnEpisodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PlaylistItem item }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _pressedItem = item;
        _longPressTimer.Stop();
        _longPressTimer.Start();
    }

    private void OnEpisodePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _longPressTimer.Stop();
        _pressedItem = null;
    }

    private void OnEpisodePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _longPressTimer.Stop();
        _pressedItem = null;
    }

    private void OnLongPressElapsed(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        if (_pressedItem is not { } item) return;

        _pressedItem = null;
        if (DataContext is MainViewModel vm)
            vm.OpenEpisodeActionsCommand.Execute(item);
    }
}
