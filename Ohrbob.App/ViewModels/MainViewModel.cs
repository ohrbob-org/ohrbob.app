using System;
using System.Collections.Generic;
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
    public sealed record BufferOption(int Minutes, string Label);

    /// <summary>The hosted ohrbob.org page that starts the connect flow.</summary>
    public const string AppConnectPath = "/connect/app";

    private readonly ConfigService _configService;
    private readonly OhrbobClient _client;
    private readonly IAudioPlayer _player;
    private readonly IUriLauncher _uriLauncher;
    private readonly Action<Action> _dispatch;
    private string? _pendingServerUrl;
    private string? _pendingStreamToken;
    private string? _loadedStreamUri;
    private string? _loadingStreamUri;
    private int _lastSyncedPosition;
    private DispatcherTimer? _syncTimer;
    private DispatcherTimer? _refreshIndicatorTimer;
    private DateTime _lastRefreshAt = DateTime.MinValue;
    private static readonly TimeSpan RefreshCooldown = TimeSpan.FromSeconds(10);
    private bool _isUpdatingPositionFromPlayer;
    private bool _isUpdatingStateFromPlayer;
    private bool _isSkipping;
    private bool _suppressBufferSave;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isAdvancedMode;

    [ObservableProperty]
    private bool _isReplaceConfirmationVisible;

#if DEBUG
    [ObservableProperty]
    private string _serverUrlInput = "http://10.0.2.2:5002";

    [ObservableProperty]
    private string _tokenInput = "00ef0bbf-8177-4f5d-b308-b2843ab09d0c";
#else
    [ObservableProperty]
    private string _serverUrlInput = "https://player.ohrbob.org";

    [ObservableProperty]
    private string _tokenInput = "";
#endif

    [ObservableProperty]
    private string _deepLinkInput = "";

    [ObservableProperty]
    private string? _connectionErrorMessage;

    [ObservableProperty]
    private ObservableCollection<StationWithFeedsDto> _stations = new();

    [ObservableProperty]
    private StationWithFeedsDto? _selectedStation;

    [ObservableProperty]
    private string _currentEpisodeId = "";

    [ObservableProperty]
    private string _currentEpisodeTitle = "No Track Selected";

    [ObservableProperty]
    private string _profileName = "";

    [ObservableProperty]
    private string _currentEpisodeDescription = "Please choose a station to start listening.";

    [ObservableProperty]
    private string? _currentEpisodeCoverUrl;

    [ObservableProperty]
    private string? _currentEpisodeStreamUri;

    [ObservableProperty]
    private string? _nextEpisodeTitle;

    private static readonly IReadOnlyList<BufferOption> AllBufferOptions = new[]
    {
        new BufferOption(0, "Off"),
        new BufferOption(10, "10 min"),
        new BufferOption(15, "15 min"),
        new BufferOption(30, "30 min"),
        new BufferOption(60, "60 min"),
        new BufferOption(120, "120 min"),
    };

    public IReadOnlyList<BufferOption> BufferOptions => AllBufferOptions;

    [ObservableProperty]
    private BufferOption _selectedBufferOption = AllBufferOptions[0];

    [ObservableProperty]
    private ObservableCollection<PlaylistItem> _playlistItems = new();

    [ObservableProperty]
    private string? _downloadBlockedReason;

    [ObservableProperty]
    private string? _downloadStatusMessage;

    [ObservableProperty]
    private bool _isDownloadEnabled;

    [ObservableProperty]
    private bool _isBufferSelectionEnabled;

    [ObservableProperty]
    private bool _isPlaylistUnavailable;

    [ObservableProperty]
    private bool _isPlaylistLoading;

    [ObservableProperty]
    private bool _isPlaylistEmpty;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _isUpNextOpen;

    [ObservableProperty]
    private bool _isRefreshIndicatorVisible;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isBuffering;

    [ObservableProperty]
    private int _positionSeconds;

    // 0..100 for the slider; the player expects 0..1.
    [ObservableProperty]
    private int _volume = 100;

    partial void OnVolumeChanged(int value) => _player.Volume = Math.Clamp(value, 0, 100) / 100f;

    [ObservableProperty]
    private int _durationSeconds;

    // The slider's upper bound. It may exceed DurationSeconds while the player
    // reports a position past a stale/unknown duration; clamping there would
    // write the clamped value back into PositionSeconds and seek in a loop.
    [ObservableProperty]
    private int _seekMaximum;

    [ObservableProperty]
    private string _positionText = "00:00";

    [ObservableProperty]
    private string _durationText = "00:00";

    [ObservableProperty]
    private bool _isPortrait;

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void ToggleUpNext() => IsUpNextOpen = !IsUpNextOpen;

    [RelayCommand]
    private void CloseUpNext() => IsUpNextOpen = false;

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    public MainViewModel() : this(new ConfigService(), null)
    {
    }

    public MainViewModel(ConfigService configService, OhrbobClient? client)
        : this(configService, client, null)
    {
    }

    public MainViewModel(ConfigService configService, OhrbobClient? client, IUriLauncher? uriLauncher = null, IAudioPlayer? player = null, Action<Action>? dispatch = null)
    {
        _configService = configService;
        _uriLauncher = uriLauncher ?? new AppUriLauncher();
        _dispatch = dispatch ?? (action => Dispatcher.UIThread.Post(action));
        var handler = new HttpClientHandler();

        // Only bypass if running locally or in a specific dev environment
        bool isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        #if DEBUG
        
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
        #endif

        
        _client = client ?? new OhrbobClient(_configService, handler);

        _player = player ?? new SoundFlowAudioPlayer();
        _player.PositionChanged += OnPlayerOnPositionChanged;
        _player.DurationChanged += OnPlayerOnDurationChanged;
        _player.PlaybackStateChanged += OnPlayerOnPlaybackStateChanged;
        _player.PlaybackEnded += OnPlayerPlaybackEnded;

        // Platform heads publish incoming ohrbob:// links here; marshal onto the
        // UI thread because they arrive from non-UI sources (argv, intents).
        DeepLinkRouter.Subscribe(uri => Dispatcher.UIThread.Post(() => ApplyDeepLink(uri)));

        if (Avalonia.Controls.Design.IsDesignMode)
            return;

        if (!string.IsNullOrEmpty(_configService.ServerUrl) && !string.IsNullOrEmpty(_configService.StreamToken))
        {
            ServerUrlInput = _configService.ServerUrl;
            TokenInput = _configService.StreamToken;
            IsConnected = true;
            _ = InitializeAppAsync(keepConnectedOnFailure: true);
        }

        SetupTimers();
    }

    private void OnPlayerOnPositionChanged(object? s, int pos)
    {
        _dispatch(() =>
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
        });
    }

    private void OnPlayerOnDurationChanged(object? s, int dur)
    {
        _dispatch(() => { DurationSeconds = dur; });
    }

    private void OnPlayerOnPlaybackStateChanged(object? s, bool state)
    {
        _dispatch(() =>
        {
            if (IsBuffering && !state) return; // Ignore internal stops during load

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

    private void OnPlayerPlaybackEnded(object? s, EventArgs e)
    {
        _dispatch(() => _ = ContinueAfterEndAsync());
    }

    private async Task ContinueAfterEndAsync()
    {
        var previousUri = CurrentEpisodeStreamUri;
        await SkipToNextAsync();

        // If the server moved us on to another stream, start it. Otherwise stop
        // here: the old stream has ended and there is nothing left to play.
        IsPlaying = !string.IsNullOrEmpty(CurrentEpisodeStreamUri) && CurrentEpisodeStreamUri != previousUri;
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
                    if (SelectedStation?.Station?.Id != null && !string.IsNullOrEmpty(CurrentEpisodeId))
                    {
                        await _client.SyncStatusAsync(CurrentEpisodeId, SelectedStation.Station.Id, PositionSeconds);
                    }
                }
                catch
                {
                    // Ignore transient syncing errors
                }
            }
        };

        _refreshIndicatorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshIndicatorTimer.Tick += (s, e) =>
        {
            IsRefreshIndicatorVisible = false;
            _refreshIndicatorTimer.Stop();
        };
    }

    partial void OnPositionSecondsChanged(int value)
    {
        PositionText = FormatTime(value);
        SeekMaximum = Math.Max(DurationSeconds, value);

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
        if (string.IsNullOrEmpty(url) || _loadingStreamUri == url) return;

        _loadingStreamUri = url;
        try
        {
            IsBuffering = true;
            _player.Stop();
            await _player.Load(url);
            _loadedStreamUri = url;
            
            if (PositionSeconds > 0)
            {
                Log.Information("Seeking player to saved position: {PositionSeconds}", PositionSeconds);
                _player.Seek(PositionSeconds);
            }
            
            if (IsPlaying)
            {
                _player.Play();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load audio stream: {ex.Message}");
            if (_loadingStreamUri == url) IsPlaying = false;
        }
        finally
        {
            if (_loadingStreamUri == url) _loadingStreamUri = null;
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
            DurationSeconds = 0; // Clear duration to prevent stale time display

            if (!string.IsNullOrEmpty(value))
            {
                // We keep the global IsPlaying state. Load the audio.
                // It will auto-play if IsPlaying is true.
                _ = LoadAudioUrlAsync(value);
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
        SeekMaximum = Math.Max(value, PositionSeconds);
    }

    private string FormatTime(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    /// <summary>
    /// Parses a fixed-shape connect deep link:
    /// <c>ohrbob://connect?server=&lt;base-url&gt;&amp;token=&lt;token&gt;</c>.
    /// Any other scheme/host or a missing part is rejected.
    /// </summary>
    public static bool TryParseConnectDeepLink(string uri, out string server, out string token)
    {
        server = "";
        token = "";

        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        if (!Uri.TryCreate(uri.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!parsed.Scheme.Equals("ohrbob", StringComparison.OrdinalIgnoreCase) ||
            !parsed.Host.Equals("connect", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = System.Web.HttpUtility.ParseQueryString(parsed.Query);
        var qServer = query["server"];
        var qToken = query["token"];

        if (string.IsNullOrEmpty(qServer) || string.IsNullOrEmpty(qToken))
        {
            return false;
        }

        server = qServer;
        token = qToken;
        return true;
    }

    /// <summary>
    /// Deterministic entry point for an incoming <c>ohrbob://</c> link. Applies
    /// the connection silently when disconnected; when already connected it
    /// parks the link and asks for confirmation before replacing (see
    /// <see cref="ConfirmReplaceCommand"/> / <see cref="CancelReplaceCommand"/>).
    /// </summary>
    public void ApplyDeepLink(string uri)
    {
        if (!TryParseConnectDeepLink(uri, out var server, out var token))
        {
            return;
        }

        if (IsConnected)
        {
            _pendingServerUrl = server;
            _pendingStreamToken = token;
            IsReplaceConfirmationVisible = true;
            return;
        }

        ApplyConnection(server, token);
    }

    private void ApplyConnection(string server, string token)
    {
        ConnectionErrorMessage = null;

        try
        {
            _configService.Save(server, token);
            ServerUrlInput = server;
            TokenInput = token;
            IsConnected = true;
            _ = InitializeAppAsync(keepConnectedOnFailure: true);
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionErrorMessage = $"Failed to connect to the server: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenHostedConnectPage()
    {
        var hostName = Environment.MachineName;
        var uriBuilder = new UriBuilder(new Uri("https://ohrbob.org"));
        uriBuilder.Path = AppConnectPath;
        uriBuilder.Query = $"name={Uri.EscapeDataString(hostName)}";
        await _uriLauncher.LaunchAsync(uriBuilder.Uri);
    }

    [RelayCommand]
    private void ToggleAdvancedMode()
    {
        IsAdvancedMode = !IsAdvancedMode;
    }

    [RelayCommand]
    private void ConfirmReplace()
    {
        IsReplaceConfirmationVisible = false;

        if (_pendingServerUrl is null || _pendingStreamToken is null)
        {
            return;
        }

        var server = _pendingServerUrl;
        var token = _pendingStreamToken;
        _pendingServerUrl = null;
        _pendingStreamToken = null;

        ApplyConnection(server, token);
    }

    [RelayCommand]
    private void CancelReplace()
    {
        _pendingServerUrl = null;
        _pendingStreamToken = null;
        IsReplaceConfirmationVisible = false;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        ConnectionErrorMessage = null;

        var server = ServerUrlInput?.Trim();
        var token = TokenInput?.Trim();

        // Advanced mode allows pasting a deep link instead of entering fields.
        if (!string.IsNullOrWhiteSpace(DeepLinkInput))
        {
            if (TryParseConnectDeepLink(DeepLinkInput, out var linkServer, out var linkToken))
            {
                server = linkServer;
                token = linkToken;
            }
            else
            {
                ConnectionErrorMessage = "Invalid connect link. Expected ohrbob://connect?server=…&token=…";
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
        CurrentEpisodeId = "";
        ProfileName = "";
        CurrentEpisodeTitle = "No Track Selected";
        CurrentEpisodeDescription = "Please choose a station to start listening.";
        CurrentEpisodeCoverUrl = null;
        CurrentEpisodeStreamUri = null;
        NextEpisodeTitle = null;
        PlaylistItems.Clear();
        IsPlaylistUnavailable = false;

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

    private async Task InitializeAppAsync(bool keepConnectedOnFailure = false)
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
            await RefreshPlaylistAsync();
            _syncTimer?.Start();
            ConnectionErrorMessage = null;
        }
        catch (Exception ex)
        {
            ConnectionErrorMessage = $"Initialization error: {ex.Message}";
            Log.Error($"Initialization error: {ex.Message}");
            Log.Fatal(ex, "App initialization failed");

            // A transient failure (network not up yet, server briefly unavailable)
            // must not discard the saved connection: the user would have to connect
            // again on every start. Only an explicit Disconnect clears the config.
            if (!keepConnectedOnFailure) IsConnected = false;
            _syncTimer?.Stop();
        }
    }

    /// <summary>
    /// Retries the initial sync without leaving the player. Used when Android Auto
    /// starts the app before the network is up and later asks for content again.
    /// </summary>
    public Task RetryConnectionAsync() => InitializeAppAsync(keepConnectedOnFailure: true);

    /// <summary>Reloads stations and the current playback state from the server.</summary>
    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        if (DateTime.UtcNow - _lastRefreshAt < RefreshCooldown) return;
        _lastRefreshAt = DateTime.UtcNow;

        await InitializeAppAsync(keepConnectedOnFailure: true);

        if (string.IsNullOrEmpty(ConnectionErrorMessage))
        {
            IsRefreshIndicatorVisible = true;
            _refreshIndicatorTimer?.Stop();
            _refreshIndicatorTimer?.Start();
        }
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var status = await _client.GetStatusAsync();
            if (status.ValueKind == JsonValueKind.Object)
            {
                if (status.TryGetProperty("profileName", out var profileNameProp))
                    ProfileName = profileNameProp.GetString() ?? "";

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
                    if (currentProp.TryGetProperty("episodeId", out var idProp))
                        CurrentEpisodeId = idProp.GetString() ?? "";

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

    partial void OnSelectedBufferOptionChanged(BufferOption value)
    {
        if (_suppressBufferSave) return;
        _ = ApplySelectedBufferAsync();
    }

    /// <summary>Persists the station's buffer and refreshes its playlist.</summary>
    public async Task ApplySelectedBufferAsync()
    {
        if (SelectedStation?.Station?.Id is not { } stationId) return;
        _configService.SetBufferMinutes(stationId, SelectedBufferOption.Minutes);
        await RefreshPlaylistAsync();
    }

    private void RestoreBufferOption(string stationId)
    {
        var minutes = _configService.GetBufferMinutes(stationId);
        _suppressBufferSave = true;
        try
        {
            SelectedBufferOption = BufferOptions.FirstOrDefault(o => o.Minutes == minutes) ?? BufferOptions[0];
        }
        finally
        {
            _suppressBufferSave = false;
        }
    }

    private async Task RefreshPlaylistAsync()
    {
        if (!IsConnected) return;

        IsPlaylistLoading = true;
        try
        {
            var playlist = await _client.GetPlaylistAsync();
            if (playlist == null)
            {
                IsPlaylistUnavailable = true;
                PlaylistItems.Clear();
            }
            else
            {
                IsPlaylistUnavailable = false;
                var existingStates = PlaylistItems
                    .GroupBy(p => p.EpisodeId)
                    .ToDictionary(g => g.Key, g => g.First().DownloadState);
                PlaylistItems = new ObservableCollection<PlaylistItem>(
                    playlist.Episodes.Select(e => new PlaylistItem
                    {
                        EpisodeId = e.EpisodeId ?? "",
                        Title = e.Title ?? "",
                        Description = e.Description ?? "",
                        Artist = e.Artist ?? "",
                        CoverUrl = e.CoverUrl,
                        StreamUri = e.StreamUri,
                        PublishedAt = e.PublishedAt,
                        IsCurrent = e.IsCurrent,
                        DownloadState = existingStates.TryGetValue(e.EpisodeId ?? "", out var state)
                            ? state
                            : DownloadState.NotDownloaded,
                    }));
            }

            UpdateDownloadState(playlist);
        }
        catch
        {
            IsPlaylistUnavailable = true;
            PlaylistItems.Clear();
            UpdateDownloadState(null);
        }
        finally
        {
            IsPlaylistLoading = false;
            IsPlaylistEmpty = PlaylistItems.Count == 0 && !IsPlaylistUnavailable;
        }
    }

    private void UpdateDownloadState(PlaylistDto? playlist)
    {
        var station = SelectedStation?.Station;
        if (station?.HostPaceEnabled == true || playlist?.HostPaceEnabled == true)
            DownloadBlockedReason = "Downloads are unavailable for host-paced stations.";
        else if (station?.IsAging == true || playlist?.IsAging == true)
            DownloadBlockedReason = "Downloads are unavailable for aging stations.";
        else
            DownloadBlockedReason = null;

        var minutes = station?.Id is { } stationId ? _configService.GetBufferMinutes(stationId) : 0;
        DownloadStatusMessage = DownloadBlockedReason
            ?? (minutes == 0 ? "Set a buffer in Settings to download episodes." : null);
        IsDownloadEnabled = DownloadBlockedReason == null && minutes > 0;
        IsBufferSelectionEnabled = DownloadBlockedReason == null;
    }

    partial void OnSelectedStationChanged(StationWithFeedsDto? value)
    {
        if (value != null && value.Station?.Id != null)
        {
            RestoreBufferOption(value.Station.Id);
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
            await RefreshPlaylistAsync();
        }
        catch
        {
            // Handle error switching station
        }
    }

    [RelayCommand]
    private async Task OpenStationSettingsAsync()
    {
        if (SelectedStation?.Station?.Id == null) return;
        var webUri = await _client.GetWebUriAsync();
        if (!string.IsNullOrEmpty(webUri))
        {
            var url = $"{webUri.TrimEnd('/')}/Stations/Edit/{SelectedStation.Station.Id}";
            await _uriLauncher.LaunchAsync(new Uri(url));
        }
    }

    [RelayCommand]
    private async Task OpenProfileSettingsAsync()
    {
        var webUri = await _client.GetWebUriAsync();
        if (!string.IsNullOrEmpty(webUri))
        {
            var url = $"{webUri.TrimEnd('/')}/Profiles";
            await _uriLauncher.LaunchAsync(new Uri(url));
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
        // Duration is 0 until the stream has been probed; don't clamp a forward
        // seek to the start in that window.
        PositionSeconds = DurationSeconds > 0
            ? Math.Min(DurationSeconds, PositionSeconds + 30)
            : PositionSeconds + 30;
    }

    [RelayCommand]
    private Task SkipToNextAsync() => SkipAsync(_client.NextAsync);

    [RelayCommand]
    private Task SkipToPreviousAsync() => SkipAsync(_client.PreviousAsync);

    private async Task SkipAsync(Func<Task> navigate)
    {
        // Playback-end, buttons and media controls can all ask for a skip; only
        // one request at a time may run, otherwise a failed skip can pile up
        // overlapping network calls and audio loads.
        if (_isSkipping) return;
        _isSkipping = true;

        try
        {
            await navigate();
            await RefreshStatusAsync();
        }
        catch
        {
            // Handle network skip error
        }
        finally
        {
            _isSkipping = false;
        }
    }
}
