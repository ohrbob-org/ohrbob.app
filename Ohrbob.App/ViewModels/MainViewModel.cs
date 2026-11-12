using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
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
    private readonly EpisodeDownloadService _downloads;
    private readonly OfflineStateService _offlineState;
    private readonly SemaphoreSlim _downloadSyncGate = new(1, 1);
    private readonly object _downloadSyncLock = new();
    private OfflineSnapshot? _snapshot;
    private bool _isRestoringSnapshot;
    private StationWithFeedsDto? _previousSelectedStation;
    private Task? _downloadSyncTask;
    private string? _lastDownloadSignature;
    private int _playlistGeneration;
    private string? _playlistStationId;
    private bool _playlistHostPaceEnabled;
    private bool _playlistIsAging;
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
    private bool _isOffline;

    [ObservableProperty]
    private string? _offlineMessage;

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
    private void OpenSettings()
    {
        IsUpNextOpen = false;
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void ToggleUpNext()
    {
        IsUpNextOpen = !IsUpNextOpen;
        if (IsUpNextOpen) IsSettingsOpen = false;
    }

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

    public MainViewModel(ConfigService configService, OhrbobClient? client, IUriLauncher? uriLauncher = null, IAudioPlayer? player = null, Action<Action>? dispatch = null,
        EpisodeDownloadService? downloads = null, OfflineStateService? offlineState = null)
    {
        _configService = configService;
        _uriLauncher = uriLauncher ?? new AppUriLauncher();
        _dispatch = dispatch ?? (action => Dispatcher.UIThread.Post(action));
        _downloads = downloads ?? new EpisodeDownloadService();
        _offlineState = offlineState ?? new OfflineStateService();
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

            // Restore the cached player before the network call so an unreachable
            // server still shows the last station instead of an empty player.
            try
            {
                _snapshot = _offlineState.Load();
                if (_snapshot != null) ApplySnapshot(_snapshot);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to restore the offline snapshot");
            }

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

        if (IsOffline)
        {
            if (AdvanceOffline()) return;
        }
        else if (await SkipAsync(_client.NextAsync))
        {
            // If the server moved us on to another stream, start it. Otherwise stop
            // here: the old stream has ended and there is nothing left to play.
            IsPlaying = !string.IsNullOrEmpty(CurrentEpisodeStreamUri) && CurrentEpisodeStreamUri != previousUri;
            return;
        }
        else if (AdvanceOffline())
        {
            return;
        }

        IsPlaying = false;
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
            // Keep the local resume point current even when the server is gone.
            SaveOfflineSnapshot();

            if (IsConnected && !IsOffline && IsPlaying && SelectedStation != null && !string.IsNullOrEmpty(CurrentEpisodeStreamUri))
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

            var downloadsBlocked = DownloadsBlocked;
            var localPath = ActiveStationId is { } stationId && !downloadsBlocked && !string.IsNullOrEmpty(CurrentEpisodeId)
                ? _downloads.GetDownloadedPath(stationId, CurrentEpisodeId)
                : null;

            await _player.Load(localPath ?? url);
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
    private async Task OpenLink(Uri? uri)
    {
        if (uri is null) return;
        if (uri.Scheme is not ("http" or "https" or "mailto")) return;
        await _uriLauncher.LaunchAsync(uri);
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
        _downloads.Cancel();
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

            // If we were playing offline, push the local episode and position back to
            // the server before the refresh overwrites them with stale server state.
            if (IsOffline && SelectedStation?.Station?.Id is { } offlineStationId && !string.IsNullOrEmpty(CurrentEpisodeId))
            {
                try
                {
                    await _client.SyncStatusAsync(CurrentEpisodeId, offlineStationId, PositionSeconds);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to push offline playback state before reconnect");
                }
            }

            var stationsList = await _client.GetStationsAsync();
            var activeStationId = SelectedStation?.Station?.Id;
            Stations.Clear();
            foreach (var station in stationsList)
            {
                Stations.Add(station);
            }

            RefreshStationOfflineBadges();

            // A snapshot may have restored an object that is no longer in the list;
            // point at the server's fresh copy instead (without a PlayStation call).
            if (activeStationId is not null)
            {
                _isRestoringSnapshot = true;
                try
                {
                    SelectedStation = Stations.FirstOrDefault(s => s.Station?.Id == activeStationId);
                }
                finally
                {
                    _isRestoringSnapshot = false;
                }
            }

            await RefreshStatusAsync();
            await RefreshPlaylistAsync();
            _syncTimer?.Start();
            ConnectionErrorMessage = null;
            IsOffline = false;
            OfflineMessage = null;
            SaveOfflineSnapshot();
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

            // With a snapshot the player stays usable from disk instead of going blank.
            if (_snapshot is { StationStates.Count: > 0 })
            {
                IsOffline = true;
                OfflineMessage = "Server unavailable. Playing downloaded episodes.";
            }

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
                        CurrentEpisodeTitle = HtmlText.ToPlainText(titleProp.GetString()) is { Length: > 0 } title
                            ? title
                            : "Unknown Title";

                    if (currentProp.TryGetProperty("description", out var descProp))
                        CurrentEpisodeDescription = HtmlText.ToPlainText(descProp.GetString());

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
                        NextEpisodeTitle = HtmlText.ToPlainText(nextTitleProp.GetString());
                }
                else
                {
                    NextEpisodeTitle = null;
                }
            }

            SaveOfflineSnapshot();
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
        try
        {
            await RefreshPlaylistAsync();
        }
        finally
        {
            // Pruning must run even if the playlist refresh failed (e.g. the user
            // lowered the buffer to Off while the server is unreachable).
            TriggerDownloadSync();
        }
    }

    /// <summary>
    /// Brings the active station's cache in line with its buffer. Serialized so two
    /// refreshes cannot run two queues at once; a newer sync cancels the previous one.
    /// </summary>
    public async Task SyncDownloadsAsync()
    {
        if (!IsConnected || ActiveStationId is not { } stationId) return;

        await _downloadSyncGate.WaitAsync();
        try
        {
            // A queued sync may have outlived its station after a switch.
            if (!IsConnected || ActiveStationId != stationId) return;

            // An unknown playlist (older server / failed fetch) must not prune the cache.
            // An empty but available playlist still syncs, so a blocked station is pruned.
            if (IsPlaylistUnavailable) return;

            var blocked = DownloadsBlocked;
            var minutes = _configService.GetBufferMinutes(stationId);
            // Host-paced/aging files must never survive, so the current episode is
            // protected only while its station still allows downloads.
            await _downloads.SyncAsync(stationId, PlaylistItems, minutes, !blocked && minutes > 0,
                PositionSeconds, CancellationToken.None, blocked ? null : CurrentEpisodeId);
            SaveOfflineSnapshot();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Episode download sync failed");
        }
        finally
        {
            try
            {
                // A concurrent playlist refresh may have replaced PlaylistItems
                // while the sync updated the old items. Reconcile the visible list.
                if (ActiveStationId == stationId)
                    RefreshDownloadStates(stationId);

                // The badge reflects disk state, so update it once the sync is done.
                if (SelectedStation?.Station?.Id == stationId)
                    SelectedStation.Station.IsAvailableOffline = PlaylistItems.Any(i => i.DownloadState == DownloadState.Downloaded);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Episode download state refresh failed");
            }
            finally
            {
                _downloadSyncGate.Release();
            }
        }
    }

    private void RefreshDownloadStates(string stationId)
    {
        foreach (var item in PlaylistItems)
        {
            var path = _downloads.GetDownloadedPath(stationId, item.EpisodeId);
            // Keep a failed attempt visible until the next sync retries it.
            if (path is null && item.DownloadState == DownloadState.Failed)
                continue;

            item.DownloadState = path is null ? DownloadState.NotDownloaded : DownloadState.Downloaded;
        }
    }

    private void RefreshStationOfflineBadges()
    {
        _snapshot ??= _offlineState.Load();
        if (_snapshot is null) return;

        foreach (var wrapper in Stations)
        {
            if (wrapper.Station?.Id is not { } stationId) continue;
            wrapper.Station.IsAvailableOffline = HasDownloadedContent(stationId, _snapshot);
        }
    }

    /// <summary>Fire-and-forget kickoff; never awaited by navigation or refresh.</summary>
    public void TriggerDownloadSync()
    {
        // Triggers arrive from UI, navigation and thread-pool continuations. The
        // lock keeps the signature check and task swap atomic so two triggers
        // cannot both decide to restart (and cancel) the same running sync.
        lock (_downloadSyncLock)
        {
            // The initial load and the fire-and-forget station switch both refresh
            // the same playlist. Do not cancel and restart a sync that is already
            // running for identical content; otherwise the episode is fetched twice.
            var signature = DownloadSignature();
            if (signature == _lastDownloadSignature && _downloadSyncTask is { IsCompleted: false })
                return;

            _lastDownloadSignature = signature;
            _downloads.Cancel();
            _downloadSyncTask = SyncDownloadsAsync();
        }
    }

    public async Task WaitForDownloadsAsync()
    {
        // A refresh that finishes after the caller started waiting replaces the
        // task; keep waiting until the task we observed is still the newest.
        while (true)
        {
            Task task;
            lock (_downloadSyncLock)
                task = _downloadSyncTask ?? Task.CompletedTask;

            await task;

            lock (_downloadSyncLock)
                if (ReferenceEquals(task, _downloadSyncTask)) return;
        }
    }

    private string? DownloadSignature()
    {
        if (ActiveStationId is not { } stationId) return null;

        var minutes = _configService.GetBufferMinutes(stationId);
        var episodes = string.Join("\n", PlaylistItems.Select(p => $"{p.EpisodeId}:{p.StreamUri}:{p.IsCurrent}"));
        return $"{stationId}|{minutes}|{episodes}";
    }

    /// <summary>
    /// The station whose playlist is on screen. Usually the selected station; the
    /// playlist is the only source when the server status omits its station id.
    /// </summary>
    private string? ActiveStationId => SelectedStation?.Station?.Id ?? _playlistStationId;

    private bool DownloadsBlocked => SelectedStation?.Station is { } station
        ? station.HostPaceEnabled || station.IsAging
        : _playlistHostPaceEnabled || _playlistIsAging;

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

    private string? DownloadedPath(string stationId, string? episodeId) =>
        string.IsNullOrEmpty(episodeId) ? null : _downloads.GetDownloadedPath(stationId, episodeId);

    private bool HasDownloadedContent(string? stationId, OfflineSnapshot snapshot) =>
        stationId is not null &&
        snapshot.StationStates.TryGetValue(stationId, out var state) &&
        HasDownloadedContent(stationId, state);

    private bool HasDownloadedContent(string stationId, StationOfflineState state)
    {
        // Host-paced/aging files must never play, so they do not count as offline content.
        if (state.HostPaceEnabled || state.IsAging) return false;

        return state.Playlist.Any(episode => DownloadedPath(stationId, episode.EpisodeId) is not null);
    }

    private void ApplySnapshot(OfflineSnapshot snapshot)
    {
        _isRestoringSnapshot = true;
        try
        {
            Stations.Clear();
            foreach (var station in snapshot.Stations)
            {
                if (station.Station is { } model)
                {
                    model.Name = HtmlText.ToPlainText(model.Name);
                    model.IsAvailableOffline = HasDownloadedContent(model.Id, snapshot);
                }
                Stations.Add(station);
            }

            ProfileName = snapshot.ProfileName ?? "";
            SelectedStation = Stations.FirstOrDefault(s => s.Station?.Id == snapshot.ActiveStationId)
                ?? Stations.FirstOrDefault();

            if (snapshot.ActiveStationId is { } stationId &&
                snapshot.StationStates.TryGetValue(stationId, out var state))
            {
                RestoreBufferOption(stationId);
                ApplyOfflineState(stationId, state);
            }
        }
        finally
        {
            _isRestoringSnapshot = false;
        }
    }

    /// <summary>Shows a station from its snapshot, preferring the downloaded file.</summary>
    private void ApplyOfflineState(string stationId, StationOfflineState state)
    {
        PlaylistItems = new ObservableCollection<PlaylistItem>(
            state.Playlist.Select(episode => new PlaylistItem
            {
                EpisodeId = episode.EpisodeId ?? "",
                Title = HtmlText.ToPlainText(episode.Title),
                Description = HtmlText.ToPlainText(episode.Description),
                Artist = HtmlText.ToPlainText(episode.Artist),
                CoverUrl = episode.CoverUrl,
                StreamUri = episode.StreamUri,
                PublishedAt = episode.PublishedAt,
                IsCurrent = episode.IsCurrent,
                DownloadState = DownloadedPath(stationId, episode.EpisodeId) is null
                    ? DownloadState.NotDownloaded
                    : DownloadState.Downloaded,
            }));

        var current = state.Playlist.FirstOrDefault(e => e.EpisodeId == state.CurrentEpisodeId);
        var position = (int)state.PositionSeconds;
        if (current is null || DownloadedPath(stationId, current.EpisodeId) is null)
        {
            // The saved episode is gone from disk; resume at the first one still there.
            var downloaded = state.Playlist.FirstOrDefault(e => DownloadedPath(stationId, e.EpisodeId) is not null);
            if (downloaded is not null)
            {
                current = downloaded;
                position = 0;
            }
        }

        // The saved current episode may have been pruned; keep the UI marker on
        // whatever is actually playing.
        foreach (var item in PlaylistItems)
            item.IsCurrent = current is not null && item.EpisodeId == current.EpisodeId;

        if (current is not null)
        {
            CurrentEpisodeId = current.EpisodeId ?? "";
            CurrentEpisodeTitle = current.Title ?? "";
            CurrentEpisodeDescription = current.Description ?? "";
            CurrentEpisodeCoverUrl = current.CoverUrl;
            NextEpisodeTitle = null;
        }
        else
        {
            CurrentEpisodeId = state.CurrentEpisodeId ?? "";
            CurrentEpisodeStreamUri = null;
        }

        PositionSeconds = position;
        if (current?.StreamUri is { } uri)
            CurrentEpisodeStreamUri = uri;
    }

    internal void SaveOfflineSnapshot()
    {
        if (!IsConnected || SelectedStation?.Station?.Id is not { } stationId) return;

        var snapshot = _snapshot ?? new OfflineSnapshot();
        snapshot.SavedAt = DateTimeOffset.UtcNow;
        snapshot.ActiveStationId = stationId;
        snapshot.ProfileName = ProfileName;

        // Preserve stations that still have downloaded content even when the server
        // temporarily returns an empty list, so offline selection stays usable.
        var mergedStations = Stations.ToList();
        if (_snapshot != null)
        {
            foreach (var cached in _snapshot.Stations)
            {
                if (cached.Station?.Id is not { } cachedId) continue;
                if (mergedStations.Any(s => s.Station?.Id == cachedId)) continue;
                if (_snapshot.StationStates.TryGetValue(cachedId, out var state) && HasDownloadedContent(cachedId, state))
                    mergedStations.Add(cached);
            }
        }
        snapshot.Stations = mergedStations;

        snapshot.StationStates[stationId] = new StationOfflineState
        {
            StationId = stationId,
            HostPaceEnabled = SelectedStation.Station.HostPaceEnabled,
            IsAging = SelectedStation.Station.IsAging,
            CurrentEpisodeId = CurrentEpisodeId,
            PositionSeconds = PositionSeconds,
            Playlist = PlaylistItems.Select(item => new PlaylistEpisodeDto
            {
                EpisodeId = item.EpisodeId,
                Title = item.Title,
                Description = item.Description,
                Artist = item.Artist,
                CoverUrl = item.CoverUrl,
                StreamUri = item.StreamUri,
                PublishedAt = item.PublishedAt,
                IsCurrent = item.IsCurrent,
            }).ToList(),
        };
        _snapshot = snapshot;

        try
        {
            _offlineState.Save(snapshot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Failed to save the offline snapshot");
        }
    }

    private bool TrySwitchOffline(string stationId)
    {
        _snapshot ??= _offlineState.Load();
        if (_snapshot is null ||
            !_snapshot.StationStates.TryGetValue(stationId, out var state) ||
            !HasDownloadedContent(stationId, state))
            return false;

        // Clear first so the new station's file is loaded even when the stream URI
        // is the same as the previous station's.
        CurrentEpisodeStreamUri = null;
        ApplyOfflineState(stationId, state);
        SaveOfflineSnapshot();
        IsPlaying = true;
        return true;
    }

    private bool AdvanceOffline()
    {
        if (DownloadsBlocked || ActiveStationId is not { } stationId) return false;

        var start = 0;
        for (var i = 0; i < PlaylistItems.Count; i++)
        {
            if (PlaylistItems[i].EpisodeId == CurrentEpisodeId)
            {
                start = i + 1;
                break;
            }
        }

        for (var i = start; i < PlaylistItems.Count; i++)
        {
            var item = PlaylistItems[i];
            if (DownloadedPath(stationId, item.EpisodeId) is null) continue;

            foreach (var playlistItem in PlaylistItems)
                playlistItem.IsCurrent = false;
            item.IsCurrent = true;

            PositionSeconds = 0;
            CurrentEpisodeId = item.EpisodeId;
            CurrentEpisodeTitle = item.Title;
            CurrentEpisodeDescription = item.Description;
            CurrentEpisodeCoverUrl = item.CoverUrl;
            CurrentEpisodeStreamUri = item.StreamUri;
            SaveOfflineSnapshot();
            IsPlaying = true;
            return true;
        }

        return false;
    }

    private async Task RefreshPlaylistAsync()
    {
        if (!IsConnected) return;

        // Playlist refreshes may overlap (initial load and the station switch).
        // Only the latest one may apply its result and start downloads.
        var generation = ++_playlistGeneration;

        IsPlaylistLoading = true;
        try
        {
            var playlist = await _client.GetPlaylistAsync();
            if (generation != _playlistGeneration) return;

            if (playlist == null)
            {
                IsPlaylistUnavailable = true;
                PlaylistItems.Clear();
                _playlistStationId = null;
            }
            else
            {
                IsPlaylistUnavailable = false;
                _playlistStationId = playlist.StationId;
                _playlistHostPaceEnabled = playlist.HostPaceEnabled;
                _playlistIsAging = playlist.IsAging;
                var existingStates = PlaylistItems
                    .GroupBy(p => p.EpisodeId)
                    .ToDictionary(g => g.Key, g => g.First().DownloadState);
                PlaylistItems = new ObservableCollection<PlaylistItem>(
                    playlist.Episodes.Select(e => new PlaylistItem
                    {
                        EpisodeId = e.EpisodeId ?? "",
                        Title = HtmlText.ToPlainText(e.Title),
                        Description = HtmlText.ToPlainText(e.Description),
                        Artist = HtmlText.ToPlainText(e.Artist),
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

            if (playlist != null)
            {
                // A failed/absent playlist must not replace the cached one.
                SaveOfflineSnapshot();
                TriggerDownloadSync();
            }
        }
        catch
        {
            if (generation != _playlistGeneration) return;

            IsPlaylistUnavailable = true;
            PlaylistItems.Clear();
            _playlistStationId = null;
            UpdateDownloadState(null);
        }
        finally
        {
            if (generation == _playlistGeneration)
            {
                IsPlaylistLoading = false;
                IsPlaylistEmpty = PlaylistItems.Count == 0 && !IsPlaylistUnavailable;
            }
        }
    }

    private void UpdateDownloadState(PlaylistDto? playlist)
    {
        var station = SelectedStation?.Station;
        if (station?.HostPaceEnabled == true || playlist?.HostPaceEnabled == true)
            DownloadBlockedReason = "Downloads are off: episodes unlock when the host has listened to them.";
        else if (station?.IsAging == true || playlist?.IsAging == true)
            DownloadBlockedReason = "Downloads are off: this station only rotates the newest episode of each feed.";
        else
            DownloadBlockedReason = null;

        var minutes = station?.Id is { } stationId ? _configService.GetBufferMinutes(stationId) : 0;
        DownloadStatusMessage = DownloadBlockedReason
            ?? (minutes == 0 ? "Set a buffer in Settings to download episodes." : null);
        IsDownloadEnabled = DownloadBlockedReason == null && minutes > 0;
        IsBufferSelectionEnabled = DownloadBlockedReason == null;

        foreach (var item in PlaylistItems)
            item.ShowDownloadStateLabel = IsDownloadEnabled;
    }

    partial void OnSelectedStationChanging(StationWithFeedsDto? value) =>
        _previousSelectedStation = SelectedStation;

    partial void OnSelectedStationChanged(StationWithFeedsDto? value)
    {
        // Restoring a snapshot must not hit the server or the offline guard.
        if (_isRestoringSnapshot) return;

        // Leaving a station drops its queue (files stay). Bumping the generation
        // also prevents an in-flight refresh for the old station from applying.
        _downloads.Cancel();
        _playlistGeneration++;
        _lastDownloadSignature = null;

        if (value?.Station?.Id is not { } stationId) return;

        if (IsOffline)
        {
            if (!TrySwitchOffline(stationId))
            {
                _isRestoringSnapshot = true;
                try
                {
                    SelectedStation = _previousSelectedStation;
                }
                finally
                {
                    _isRestoringSnapshot = false;
                }

                ConnectionErrorMessage = "This station is not available offline.";
                return;
            }

            ConnectionErrorMessage = null;
        }
        else
        {
            _ = SwitchStationAsync(stationId);
        }

        RestoreBufferOption(stationId);
        if (IsPortrait) IsSidebarOpen = false;
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

    private async Task<bool> SkipAsync(Func<Task> navigate)
    {
        // Playback-end, buttons and media controls can all ask for a skip; only
        // one request at a time may run, otherwise a failed skip can pile up
        // overlapping network calls and audio loads.
        if (_isSkipping) return false;
        _isSkipping = true;

        try
        {
            await navigate();
            await RefreshStatusAsync();
            await RefreshPlaylistAsync();
            return true;
        }
        catch
        {
            // Handle network skip error; the caller may fall back to local playback.
            return false;
        }
        finally
        {
            _isSkipping = false;
        }
    }
}
