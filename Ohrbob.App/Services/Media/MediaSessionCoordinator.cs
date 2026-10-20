using System;
using System.ComponentModel;
using Avalonia.Threading;
using Ohrbob.App.ViewModels;

namespace Ohrbob.App.Services.Media;

/// <summary>
/// Bridges the main view model and an <see cref="IMediaSessionService"/>: pushes the
/// current playback state to the OS media controls and translates the controls'
/// commands back into view model changes.
/// </summary>
public sealed class MediaSessionCoordinator : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly IMediaSessionService _service;
    private readonly Action<Action> _dispatch;
    private bool _disposed;

    public MediaSessionCoordinator(
        MainViewModel viewModel,
        IMediaSessionService service,
        Action<Action>? dispatch = null)
    {
        _viewModel = viewModel;
        _service = service;
        _dispatch = dispatch ?? (action => Dispatcher.UIThread.Post(action));

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _service.PlayRequested += OnPlayRequested;
        _service.PauseRequested += OnPauseRequested;
        _service.TogglePlayPauseRequested += OnTogglePlayPauseRequested;
        _service.NextRequested += OnNextRequested;
        _service.PreviousRequested += OnPreviousRequested;
        _service.SeekToRequested += OnSeekToRequested;
        _service.SeekByRequested += OnSeekByRequested;

        Publish();
    }

    public MediaSessionInfo BuildInfo() => new(
        Title: string.IsNullOrWhiteSpace(_viewModel.CurrentEpisodeTitle) ? "Ohrbob" : _viewModel.CurrentEpisodeTitle,
        Artist: _viewModel.SelectedStation?.Station?.Name ?? "Ohrbob",
        CoverUrl: _viewModel.CurrentEpisodeCoverUrl,
        IsPlaying: _viewModel.IsPlaying,
        PositionSeconds: _viewModel.PositionSeconds,
        DurationSeconds: _viewModel.DurationSeconds);

    private void Publish() => _service.Update(BuildInfo());

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CurrentEpisodeTitle):
            case nameof(MainViewModel.CurrentEpisodeCoverUrl):
            case nameof(MainViewModel.SelectedStation):
            case nameof(MainViewModel.IsPlaying):
            case nameof(MainViewModel.PositionSeconds):
            case nameof(MainViewModel.DurationSeconds):
                Publish();
                break;
        }
    }

    private void OnPlayRequested(object? sender, EventArgs e) =>
        _dispatch(() => _viewModel.IsPlaying = true);

    private void OnPauseRequested(object? sender, EventArgs e) =>
        _dispatch(() => _viewModel.IsPlaying = false);

    private void OnTogglePlayPauseRequested(object? sender, EventArgs e) =>
        _dispatch(() => _viewModel.TogglePlayCommand.Execute(null));

    private void OnNextRequested(object? sender, EventArgs e) =>
        _dispatch(() => _viewModel.SkipToNextCommand.Execute(null));

    private void OnPreviousRequested(object? sender, EventArgs e)
    {
        // The server exposes no previous-episode command yet.
    }

    private void OnSeekToRequested(object? sender, int seconds) =>
        _dispatch(() => _viewModel.PositionSeconds = Clamp(seconds));

    private void OnSeekByRequested(object? sender, int offset) =>
        _dispatch(() => _viewModel.PositionSeconds = Clamp(_viewModel.PositionSeconds + offset));

    private int Clamp(int seconds)
    {
        var max = _viewModel.DurationSeconds > 0 ? _viewModel.DurationSeconds : int.MaxValue;
        return Math.Clamp(seconds, 0, max);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _service.PlayRequested -= OnPlayRequested;
        _service.PauseRequested -= OnPauseRequested;
        _service.TogglePlayPauseRequested -= OnTogglePlayPauseRequested;
        _service.NextRequested -= OnNextRequested;
        _service.PreviousRequested -= OnPreviousRequested;
        _service.SeekToRequested -= OnSeekToRequested;
        _service.SeekByRequested -= OnSeekByRequested;
    }
}
