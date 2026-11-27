using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ohrbob.App.Models;
using Ohrbob.App.ViewModels;
using Serilog;

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
    private bool _startingFromMediaId;
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
        _service.PlayFromMediaIdRequested += OnPlayFromMediaIdRequested;

        Publish();
    }

    public MediaSessionInfo BuildInfo() => new(
        Title: string.IsNullOrWhiteSpace(_viewModel.CurrentEpisodeTitle) ? "Ohrbob" : _viewModel.CurrentEpisodeTitle,
        Artist: _viewModel.SelectedStation?.Station?.Name ?? "Ohrbob",
        CoverUrl: _viewModel.CurrentEpisodeCoverUrl,
        IsPlaying: _viewModel.IsPlaying,
        PositionSeconds: _viewModel.PositionSeconds,
        DurationSeconds: _viewModel.DurationSeconds,
        IsBuffering: _startingFromMediaId || _viewModel.IsBuffering || _viewModel.IsStationLoading);

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
            case nameof(MainViewModel.IsBuffering):
            case nameof(MainViewModel.IsStationLoading):
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

    private void OnPreviousRequested(object? sender, EventArgs e) =>
        _dispatch(() => _viewModel.SkipToPreviousCommand.Execute(null));

    private void OnSeekToRequested(object? sender, int seconds) =>
        _dispatch(() => _viewModel.PositionSeconds = Clamp(seconds));

    private void OnSeekByRequested(object? sender, int offset) =>
        _dispatch(() => _viewModel.PositionSeconds = Clamp(_viewModel.PositionSeconds + offset));

    private void OnPlayFromMediaIdRequested(object? sender, string? mediaId) =>
        _dispatch(() => _ = PlayFromMediaIdAsync(mediaId));

    private async Task PlayFromMediaIdAsync(string? mediaId)
    {
        if (string.IsNullOrEmpty(mediaId)) return;

        var mediaIdText = mediaId.Trim();
        var station = _viewModel.Stations.FirstOrDefault(s => MatchesStation(s, mediaIdText));
        if (station is null)
        {
            Log.Warning("PlayFromMediaId: no station matches {MediaId}", mediaId);
            return;
        }

        // Show buffering immediately, before the first round trip, so the
        // head unit does not look frozen while the station switches.
        _startingFromMediaId = true;
        Publish();

        try
        {
            // Route through the same station-open path as a UI tap so the station
            // switch, playlist and now-playing state stay consistent.
            _viewModel.OpenStationCommand.Execute(station);

            // Wait only for the new station's status (which carries the stream
            // URI), not for the slower playlist refresh. Otherwise Android Auto
            // stays silent until the whole queue has been fetched.
            try
            {
                await _viewModel.StationPlaybackReadyTask;
            }
            catch
            {
                // The switch failed; let the current content resume instead of
                // leaving the car controls inert.
            }

            _viewModel.IsPlaying = true;
        }
        finally
        {
            _startingFromMediaId = false;
            Publish();
        }
    }

    private static bool MatchesStation(StationWithFeedsDto station, string mediaId)
    {
        if (string.Equals(station.Station?.Id, mediaId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (station.Station?.Name is not { } name)
            return false;

        // Media browsers sometimes pass the display name instead of the id,
        // and Unicode normalization can differ (for example "ö" vs "o" + "¨").
        return string.Equals(
            name.Normalize(NormalizationForm.FormC),
            mediaId.Normalize(NormalizationForm.FormC),
            StringComparison.OrdinalIgnoreCase);
    }

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
        _service.PlayFromMediaIdRequested -= OnPlayFromMediaIdRequested;
    }
}
