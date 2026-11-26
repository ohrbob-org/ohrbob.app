using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Threading;
using Serilog;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Components;
using SoundFlow.Providers;
using SoundFlow.Interfaces;
using SoundFlow.Structs;
using SoundFlow.Enums;
using SoundFlow.Abstracts.Devices;
using Timer = System.Timers.Timer;

namespace Ohrbob.App.Services;

public class SoundFlowAudioPlayer : IAudioPlayer
{
    private MiniAudioEngine? _engine;
    private AudioPlaybackDevice? _device;
    private SoundPlayer? _player;
    private StreamDataProvider? _provider;
    private HttpBufferingStream? _bufferStream;
    private FileStream? _localStream;
    private string? _tempFilePath;
    private CancellationTokenSource? _cts;
    private Timer _timer;

    // A Stop can arrive while SoundFlow is still constructing a provider from a
    // source stream. The stream must stay alive until that initialization either
    // adopts it or abandons it, otherwise metadata parsing hits a closed file.
    private readonly object _lifecycleLock = new();
    private int _loadVersion;
    private int _initializingCount;

    private bool _isPlaying;
    public bool IsPlaying => _isPlaying;
    public int PositionSeconds => _player != null ? (int)_player.Time : 0;
    public int DurationSeconds => _player != null ? (int)_player.Duration : 0;
    public string? CurrentUrl { get; private set; }

    private float _volume = 1f;
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_player != null) _player.Volume = _volume;
        }
    }
    
    // We don't need LibVlc anymore, this is for backwards compatibility with the interface
    public bool IsLibVlcInstalled => true; 

    public event EventHandler<int>? PositionChanged;
    public event EventHandler<int>? DurationChanged;
    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler? PlaybackEnded;

    public SoundFlowAudioPlayer()
    {
        _timer = new Timer(500);
        _timer.Elapsed += Timer_Elapsed;
        _timer.Start();
    }

    private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (_player != null && _isPlaying)
        {
            PositionChanged?.Invoke(this, PositionSeconds);
            DurationChanged?.Invoke(this, DurationSeconds);
        }
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        // SoundFlow stops decoding at EOF but keeps reporting the final position.
        // Without clearing the playing flag the position timer would keep firing at
        // the end of the stream forever.
        _isPlaying = false;
        PlaybackStateChanged?.Invoke(this, false);
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    public async Task Load(string url)
    {
        Stop(); // Cleans up previous streams
        int version;
        lock (_lifecycleLock)
        {
            version = ++_loadVersion;
        }
        CurrentUrl = url;

        try
        {
            if (_engine == null)
            {
                _engine = new MiniAudioEngine();
            }

            var cts = new CancellationTokenSource();
            lock (_lifecycleLock)
            {
                if (version != _loadVersion)
                {
                    cts.Dispose();
                    return;
                }
                _cts = cts;
            }

            var ct = cts.Token;

            // Cached episode: the file is already complete, no buffering stream needed.
            if (File.Exists(url))
            {
                var localStream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read);
                lock (_lifecycleLock)
                {
                    if (version != _loadVersion)
                    {
                        localStream.Dispose();
                        return;
                    }

                    _localStream = localStream;
                    _initializingCount++;
                }

                await InitializePlaybackAsync(localStream, ct, version, tempFilePath: null);
                return;
            }

            var tempFilePath = Path.GetTempFileName() + ".mp3";
            File.WriteAllBytes(tempFilePath, Array.Empty<byte>());

            bool isReady = false;
            int maxRetries = 3;
            long contentLength = 100 * 1024 * 1024;
            TaskCompletionSource? downloadDone = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var client = new HttpClient();
                    var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    contentLength = response.Content.Headers.ContentLength ?? 100 * 1024 * 1024;
                    var attemptDone = downloadDone = new TaskCompletionSource();

                    // Start background download stream. Use the captured path: a newer
                    // Load may replace the field while this download is still running.
                    _ = Task.Run(async () => {
                        try {
                            using var stream = await response.Content.ReadAsStreamAsync(ct);
                            using var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                            byte[] buffer = new byte[81920];
                            int read;
                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0) {
                                await fs.WriteAsync(buffer, 0, read, ct);
                                await fs.FlushAsync(ct);
                            }
                        } catch { /* Ignore cancellation or network errors in background */ }
                        finally {
                            attemptDone.TrySetResult();
                        }
                    }, ct);

                    // Wait until we have enough data (at least 1KB) to ensure headers exist for SoundFlow
                    var fileInfo = new FileInfo(tempFilePath);
                    int retry = 0;
                    while (fileInfo.Length < 1000 && !attemptDone.Task.IsCompleted && retry < 50 && !ct.IsCancellationRequested)
                    {
                        await Task.Delay(100, ct);
                        fileInfo.Refresh();
                        retry++;
                    }

                    if (fileInfo.Length >= 1000 || attemptDone.Task.IsCompleted)
                    {
                        isReady = true;
                        break;
                    }

                    // If we didn't get enough data and it's not complete, retry
                    cts.Cancel();
                    cts = new CancellationTokenSource();
                    lock (_lifecycleLock)
                    {
                        if (version != _loadVersion)
                        {
                            cts.Dispose();
                            return;
                        }
                        _cts = cts;
                    }
                    ct = cts.Token;
                }
                catch (Exception) when (attempt < maxRetries)
                {
                    await Task.Delay(1000, ct);
                }
            }

            if (!isReady)
            {
                TryDeleteFile(tempFilePath);
                return; // Failed to stream after retries
            }

            var bufferStream = new HttpBufferingStream(tempFilePath, contentLength);

            // The download may have finished before the buffer stream existed; in that
            // case nobody can call MarkComplete later, and reads would spin forever at EOF.
            if (downloadDone!.Task.IsCompleted)
            {
                bufferStream.MarkComplete();
            }
            else
            {
                _ = downloadDone.Task.ContinueWith(_ => bufferStream.MarkComplete(), TaskScheduler.Default);
            }

            lock (_lifecycleLock)
            {
                if (version != _loadVersion)
                {
                    bufferStream.Dispose();
                    TryDeleteFile(tempFilePath);
                    return;
                }

                _bufferStream = bufferStream;
                _tempFilePath = tempFilePath;
                _initializingCount++;
            }

            // Execute SoundFlow initialization on a background thread.
            await InitializePlaybackAsync(bufferStream, ct, version, tempFilePath);
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer load; no error.
        }
        catch(Exception e)
        {
            Log.Error(e, "Error loading SoundFlow audio: {Message}", e.Message);
        }
    }

    /// <summary>Shared provider/device setup for streamed and local-file sources.</summary>
    private async Task InitializePlaybackAsync(
        Stream source,
        CancellationToken ct,
        int version,
        string? tempFilePath)
    {
        StreamDataProvider? provider = null;
        AudioPlaybackDevice? device = null;
        SoundPlayer? player = null;
        var adopted = false;
        var countReleased = false;

        try
        {
            (provider, device, player) = await Task.Run(() =>
            {
                var p = new StreamDataProvider(_engine!, source, new SoundFlow.Metadata.Models.ReadOptions());
                var format = new AudioFormat {
                    Format = p.SampleFormat == SampleFormat.Unknown ? SampleFormat.F32 : p.SampleFormat,
                    Channels = p.FormatInfo?.ChannelCount ?? 2,
                    SampleRate = p.FormatInfo?.SampleRate ?? 44100
                };

                var d = _engine!.InitializePlaybackDevice(null, format, new MiniAudioDeviceConfig());
                var sp = new SoundPlayer(_engine, format, p);
                return (p, d, sp);
            }, ct);

            lock (_lifecycleLock)
            {
                if (!ct.IsCancellationRequested && version == _loadVersion)
                {
                    _provider = provider;
                    _device = device;
                    _player = player;

                    player!.Volume = _volume;
                    player.PlaybackEnded += OnPlaybackEnded;
                    device!.MasterMixer.AddComponent(player);
                    device.Start();
                    adopted = true;
                }

                _initializingCount--;
                countReleased = true;

                if (!adopted)
                    ClearSourceFields(source, tempFilePath);
            }

            if (!adopted)
            {
                Cleanup(source, tempFilePath, provider, device, player);
                return;
            }

            // Fire an initial duration and position update so UI is correct even if paused
            DurationChanged?.Invoke(this, DurationSeconds);
            PositionChanged?.Invoke(this, PositionSeconds);
        }
        catch
        {
            lock (_lifecycleLock)
            {
                if (!countReleased)
                {
                    _initializingCount--;
                    countReleased = true;
                }

                if (ReferenceEquals(_provider, provider)) _provider = null;
                if (ReferenceEquals(_device, device)) _device = null;
                if (ReferenceEquals(_player, player)) _player = null;
                ClearSourceFields(source, tempFilePath);
            }

            Cleanup(source, tempFilePath, provider, device, player);
            throw;
        }
    }

    private void ClearSourceFields(Stream source, string? tempFilePath)
    {
        if (ReferenceEquals(_bufferStream, source)) _bufferStream = null;
        if (ReferenceEquals(_localStream, source)) _localStream = null;
        if (tempFilePath != null && _tempFilePath == tempFilePath) _tempFilePath = null;
    }

    private static void Cleanup(
        Stream source,
        string? tempFilePath,
        StreamDataProvider? provider,
        AudioPlaybackDevice? device,
        SoundPlayer? player)
    {
        player?.Dispose();
        device?.Dispose();
        provider?.Dispose();
        source.Dispose();
        if (tempFilePath != null) TryDeleteFile(tempFilePath);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort only.
        }
    }

    public void Play()
    {
        if (_player != null && !_isPlaying)
        {
            _isPlaying = true;
            _player.Play();
            PlaybackStateChanged?.Invoke(this, true);
        }
    }

    public void Pause()
    {
        if (_player != null && _isPlaying)
        {
            _isPlaying = false;
            _player.Pause();
            PlaybackStateChanged?.Invoke(this, false);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        SoundPlayer? player = null;
        AudioPlaybackDevice? device = null;
        StreamDataProvider? provider = null;
        HttpBufferingStream? bufferStream = null;
        FileStream? localStream = null;
        string? tempFilePath = null;
        bool disposeLifecycle;

        lock (_lifecycleLock)
        {
            _loadVersion++;
            cts = _cts;
            _cts = null;

            // An initialization in progress owns its source and will clean up when
            // it observes the cancelled version. Disposing it here would close the
            // file while SoundFlow is still parsing metadata.
            disposeLifecycle = _initializingCount == 0;
            if (disposeLifecycle)
            {
                player = _player;
                device = _device;
                provider = _provider;
                bufferStream = _bufferStream;
                localStream = _localStream;
                tempFilePath = _tempFilePath;

                _player = null;
                _device = null;
                _provider = null;
                _bufferStream = null;
                _localStream = null;
                _tempFilePath = null;
            }
        }

        cts?.Cancel();

        if (disposeLifecycle)
        {
            if (player != null)
            {
                player.PlaybackEnded -= OnPlaybackEnded;
                player.Dispose();
            }
            device?.Dispose();
            provider?.Dispose();
            bufferStream?.Dispose();
            localStream?.Dispose();
            if (tempFilePath != null) TryDeleteFile(tempFilePath);
        }

        CurrentUrl = null;
        _isPlaying = false;
        PlaybackStateChanged?.Invoke(this, false);
    }

    public void Seek(int positionSeconds)
    {
        if (_player != null)
        {
            _player.Seek((float)positionSeconds);
            PositionChanged?.Invoke(this, PositionSeconds);
        }
    }

    public void Dispose()
    {
        Stop();
        _timer?.Dispose();
    }
}