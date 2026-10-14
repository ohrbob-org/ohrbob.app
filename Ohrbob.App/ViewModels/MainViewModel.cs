using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ohrbob.App.Services;

namespace Ohrbob.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly OhrbobClient _client;
    private DispatcherTimer? _playbackTimer;
    private DispatcherTimer? _syncTimer;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _serverUrlInput = "http://localhost:5002";

    [ObservableProperty]
    private string _tokenInput = "";

    [ObservableProperty]
    private string _deepLinkInput = "";

    [ObservableProperty]
    private string? _connectionErrorMessage;

    [ObservableProperty]
    private ObservableCollection<Station> _stations = new();

    [ObservableProperty]
    private Station? _selectedStation;

    [ObservableProperty]
    private string _currentEpisodeTitle = "No Track Selected";

    [ObservableProperty]
    private string _currentEpisodeDescription = "Please choose a station to start listening.";

    [ObservableProperty]
    private string? _currentEpisodeCoverUrl;

    [ObservableProperty]
    private string? _currentEpisodeStreamUri;

    [ObservableProperty]
    private string? _nextEpisodeTitle;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int _positionSeconds;

    [ObservableProperty]
    private int _durationSeconds = 1800; // default 30 minutes

    [ObservableProperty]
    private string _positionText = "00:00";

    [ObservableProperty]
    private string _durationText = "30:00";

    public MainViewModel()
    {
        _configService = new ConfigService();
        _configService.Load();
        _client = new OhrbobClient(_configService);

        if (!string.IsNullOrEmpty(_configService.ServerUrl) && !string.IsNullOrEmpty(_configService.StreamToken))
        {
            ServerUrlInput = _configService.ServerUrl;
            TokenInput = _configService.StreamToken;
            IsConnected = true;
            _ = InitializeAppAsync();
        }

        SetupTimers();
    }

    private void SetupTimers()
    {
        // Timer to increment the playhead position (simulating active playback)
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _playbackTimer.Tick += (s, e) =>
        {
            if (IsPlaying)
            {
                PositionSeconds++;
                if (PositionSeconds >= DurationSeconds)
                {
                    _ = SkipToNextAsync();
                }
            }
        };

        // Timer to periodically sync our current playback state back to the Ohrbob server
        _syncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _syncTimer.Tick += async (s, e) =>
        {
            if (IsConnected && IsPlaying && SelectedStation != null && !string.IsNullOrEmpty(CurrentEpisodeStreamUri))
            {
                try
                {
                    // Call the API sync endpoint
                    await _client.SyncStatusAsync(CurrentEpisodeTitle, SelectedStation.Id, PositionSeconds);
                }
                catch
                {
                    // Ignore transient syncing errors
                }
            }
        };
    }

    partial void OnPositionSecondsChanged(int value)
    {
        PositionText = FormatTime(value);
    }

    partial void OnDurationSecondsChanged(int value)
    {
        DurationText = FormatTime(value);
    }

    private string FormatTime(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        ConnectionErrorMessage = null;

        var server = ServerUrlInput?.Trim();
        var token = TokenInput?.Trim();

        // Check if deep link is provided
        if (!string.IsNullOrWhiteSpace(DeepLinkInput))
        {
            try
            {
                var uri = new Uri(DeepLinkInput.Trim());
                if (uri.Scheme.Equals("ohrbob", StringComparison.OrdinalIgnoreCase))
                {
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    var qServer = query["server"];
                    var qToken = query["token"];

                    if (!string.IsNullOrEmpty(qServer) && !string.IsNullOrEmpty(qToken))
                    {
                        server = qServer;
                        token = qToken;
                    }
                }
            }
            catch (Exception ex)
            {
                ConnectionErrorMessage = $"Invalid deep link URL: {ex.Message}";
                return;
            }
        }

        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(token))
        {
            ConnectionErrorMessage = "Please provide both a server URL and an API StreamToken.";
            return;
        }

        try
        {
            _configService.Save(server, token);
            IsConnected = true;
            await InitializeAppAsync();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionErrorMessage = $"Failed to connect to the server: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _playbackTimer?.Stop();
        IsPlaying = false;
        IsConnected = false;
        _configService.Save("", "");
        Stations.Clear();
        SelectedStation = null;
        CurrentEpisodeTitle = "No Track Selected";
        CurrentEpisodeDescription = "Please choose a station to start listening.";
        CurrentEpisodeCoverUrl = null;
        CurrentEpisodeStreamUri = null;
        NextEpisodeTitle = null;
    }

    private async Task InitializeAppAsync()
    {
        try
        {
            var stationsList = await _client.GetStationsAsync();
            Stations.Clear();
            foreach (var station in stationsList)
            {
                Stations.Add(station);
            }

            await RefreshStatusAsync();
            _playbackTimer?.Start();
        }
        catch (Exception ex)
        {
            ConnectionErrorMessage = $"Initialization error: {ex.Message}";
            Disconnect();
        }
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var status = await _client.GetStatusAsync();
            if (status.ValueKind == JsonValueKind.Object)
            {
                if (status.TryGetProperty("stationId", out var stationIdProp))
                {
                    var stationId = stationIdProp.GetString();
                    if (!string.IsNullOrEmpty(stationId) && (SelectedStation == null || SelectedStation.Id != stationId))
                    {
                        SelectedStation = Stations.FirstOrDefault(s => s.Id == stationId);
                    }
                }

                if (status.TryGetProperty("current", out var currentProp) && currentProp.ValueKind == JsonValueKind.Object)
                {
                    if (currentProp.TryGetProperty("title", out var titleProp))
                        CurrentEpisodeTitle = titleProp.GetString() ?? "Unknown Title";

                    if (currentProp.TryGetProperty("description", out var descProp))
                        CurrentEpisodeDescription = descProp.GetString() ?? "";

                    if (currentProp.TryGetProperty("coverUrl", out var coverProp))
                        CurrentEpisodeCoverUrl = coverProp.GetString();

                    if (currentProp.TryGetProperty("streamUri", out var uriProp))
                        CurrentEpisodeStreamUri = uriProp.GetString();

                    if (currentProp.TryGetProperty("playheadPositionSeconds", out var posProp) && posProp.TryGetInt32(out var pos))
                        PositionSeconds = pos;
                }
                else
                {
                    CurrentEpisodeTitle = "No Active Episode";
                    CurrentEpisodeDescription = "No episodes currently in this station's queue.";
                    CurrentEpisodeCoverUrl = null;
                    CurrentEpisodeStreamUri = null;
                    PositionSeconds = 0;
                }

                if (status.TryGetProperty("next", out var nextProp) && nextProp.ValueKind == JsonValueKind.Object)
                {
                    if (nextProp.TryGetProperty("title", out var nextTitleProp))
                        NextEpisodeTitle = nextTitleProp.GetString();
                }
                else
                {
                    NextEpisodeTitle = null;
                }
            }
        }
        catch
        {
            // Fail silently or retain current status
        }
    }

    partial void OnSelectedStationChanged(Station? value)
    {
        if (value != null)
        {
            _ = SwitchStationAsync(value.Id);
        }
    }

    private async Task SwitchStationAsync(string stationId)
    {
        try
        {
            await _client.PlayStationAsync(stationId);
            await RefreshStatusAsync();
        }
        catch
        {
            // Handle error switching station
        }
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (string.IsNullOrEmpty(CurrentEpisodeStreamUri)) return;
        IsPlaying = !IsPlaying;
    }

    [RelayCommand]
    private void SeekBack()
    {
        PositionSeconds = Math.Max(0, PositionSeconds - 15);
    }

    [RelayCommand]
    private void SeekForward()
    {
        PositionSeconds = Math.Min(DurationSeconds, PositionSeconds + 30);
    }

    [RelayCommand]
    private async Task SkipToNextAsync()
    {
        try
        {
            await _client.NextAsync();
            await RefreshStatusAsync();
        }
        catch
        {
            // Handle network skip error
        }
    }
}
