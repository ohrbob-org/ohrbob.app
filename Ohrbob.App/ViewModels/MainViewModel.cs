using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Ohrbob.App.Services;
using Ohrbob.App.Models;
using Serilog;

namespace Ohrbob.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly OhrbobClient _client;
    private readonly IAudioPlayer _player;
    private string? _loadedStreamUri;
    private int _lastSyncedPosition;
    private DispatcherTimer? _syncTimer;
    private bool _isUpdatingPositionFromPlayer;
    private bool _isUpdatingStateFromPlayer;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _serverUrlInput = "http://10.0.2.2:5002";

    [ObservableProperty]
    private string _tokenInput = "00ef0bbf-8177-4f5d-b308-b2843ab09d0c";

    [ObservableProperty]
    private string _deepLinkInput = "";

    [ObservableProperty]
    private string? _connectionErrorMessage;

    [ObservableProperty]
    private ObservableCollection<StationWithFeedsDto> _stations = new();

    [ObservableProperty]
    private StationWithFeedsDto? _selectedStation;

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
    private bool _isBuffering;

    [ObservableProperty]
    private int _positionSeconds;

    [ObservableProperty]
    private int _durationSeconds = 1800; // default 30 minutes

    [ObservableProperty]
    private string _positionText = "00:00";

    [ObservableProperty]
    private string _durationText = "30:00";

    [ObservableProperty]
    private bool _isPortrait;

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    public MainViewModel() : this( new ConfigService(), null)
    {
    }

    public MainViewModel(ConfigService configService, OhrbobClient? client)
    {
        _configService = configService;
        var handler = new HttpClientHandler();

        // Only bypass if running locally or in a specific dev environment
        bool isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        #if DEBUG
        
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
        #endif

        
        _client = client ?? new OhrbobClient(_configService, handler);

        _player = new SoundFlowAudioPlayer();
        _player.PositionChanged += OnPlayerOnPositionChanged;
        _player.DurationChanged += OnPlayerOnDurationChanged;
        _player.PlaybackStateChanged += OnPlayerOnPlaybackStateChanged;

        if (Avalonia.Controls.Design.IsDesignMode)
            return;

        if (!string.IsNullOrEmpty(_configService.ServerUrl) && !string.IsNullOrEmpty(_configService.StreamToken))
        {
            ServerUrlInput = _configService.ServerUrl;
            TokenInput = _configService.StreamToken;
            IsConnected = true;
            _ = InitializeAppAsync();
        }

        SetupTimers();
    }

    private void OnPlayerOnPositionChanged(object? s, int pos)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isUpdatingPositionFromPlayer = true;
            try
            {
                PositionSeconds = pos;
            }
            finally
            {
                _isUpdatingPositionFromPlayer = false;
            }

            if (IsPlaying && pos > 0 && DurationSeconds > 0 && pos >= DurationSeconds)
            {
                _ = SkipToNextAsync();
            }
        });
    }

    private void OnPlayerOnDurationChanged(object? s, int dur)
    {
        Dispatcher.UIThread.Post(() => { DurationSeconds = dur; });
    }

    private void OnPlayerOnPlaybackStateChanged(object? s, bool state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isUpdatingStateFromPlayer = true;
            try
            {
                IsPlaying = state;
            }
            finally
            {
                _isUpdatingStateFromPlayer = false;
            }
        });
    }

    private void SetupTimers()
    {
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
                    if (SelectedStation?.Station?.Id != null)
                    {
                        await _client.SyncStatusAsync(CurrentEpisodeTitle, SelectedStation.Station.Id, PositionSeconds);
                    }
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

        if (!_isUpdatingPositionFromPlayer)
        {
            try
            {
                if (Math.Abs(_player.PositionSeconds - value) > 2)
                {
                    _player.Seek(value);
                }
            }
            catch
            {
                // Ignore player exceptions
            }
        }

        _lastSyncedPosition = value;
    }

    private async Task LoadAudioUrlAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            IsBuffering = true;
            _player.Stop();
            await _player.Load(url);
            _loadedStreamUri = url;
            
            if (PositionSeconds > 0)
            {
                _player.Seek(PositionSeconds);
            }
            
            if (!_configService.DoNotAutoPlay)
            {
                _player.Play();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load audio stream: {ex.Message}");
        }
        finally
        {
            IsBuffering = false;
        }
    }

    partial void OnIsPlayingChanged(bool value)
    {
        if (_isUpdatingStateFromPlayer) return;

        try
        {
            if (value)
            {
                if (!string.IsNullOrEmpty(CurrentEpisodeStreamUri))
                {
                    if (_loadedStreamUri != CurrentEpisodeStreamUri)
                    {
                        _ = LoadAudioUrlAsync(CurrentEpisodeStreamUri);
                    }
                    else
                    {
                        _player.Play();
                    }
                }
            }
            else
            {
                _player.Pause();
            }
        }
        catch
        {
            // Safe fallback
        }
    }

    partial void OnCurrentEpisodeStreamUriChanged(string? value)
    {
        try
        {
            _player.Stop();
            _loadedStreamUri = null;
            if (!string.IsNullOrEmpty(value))
            {
                bool shouldPlay = !_configService.DoNotAutoPlay;
                if (IsPlaying != shouldPlay)
                {
                    IsPlaying = shouldPlay;
                }
                else
                {
                    _ = LoadAudioUrlAsync(value);
                }
            }
        }
        catch
        {
            // Safe fallback
        }
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
            ServerUrlInput = server;
            TokenInput = token;
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

        try
        {
            _player.Stop();
            _loadedStreamUri = null;
        }
        catch
        {
            // Safe fallback
        }
    }

    private async Task InitializeAppAsync()
    {
        try
        {
            Log.Debug("Initializing app...");
            var stationsList = await _client.GetStationsAsync();
            Stations.Clear();
            foreach (var station in stationsList)
            {
                Stations.Add(station);
            }

            await RefreshStatusAsync();
            _syncTimer?.Start();
        }
        catch (Exception ex)
        {
            ConnectionErrorMessage = $"Initialization error: {ex.Message}";
            Log.Error($"Initialization error: {ex.Message}");
            Log.Fatal(ex, "App initialization failed");
            
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
                    if (!string.IsNullOrEmpty(stationId) && (SelectedStation == null || SelectedStation.Station?.Id != stationId))
                    {
                        SelectedStation = Stations.FirstOrDefault(s => s.Station?.Id == stationId);
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

    partial void OnSelectedStationChanged(StationWithFeedsDto? value)
    {
        if (value != null && value.Station?.Id != null)
        {
            if (IsPortrait) IsSidebarOpen = false;
            _ = SwitchStationAsync(value.Station.Id);
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
        _configService.Save(_configService.ServerUrl ?? "", _configService.StreamToken ?? "", doNotAutoPlay: !IsPlaying);
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
