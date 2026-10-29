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
    private string? _tempFilePath;
    private CancellationTokenSource? _cts;
    private Timer _timer;

    private bool _isPlaying;
    public bool IsPlaying => _isPlaying;
    public int PositionSeconds => _player != null ? (int)_player.Time : 0;
    public int DurationSeconds => _player != null ? (int)_player.Duration : 0;
    public string? CurrentUrl { get; private set; }
    
    // We don't need LibVlc anymore, this is for backwards compatibility with the interface
    public bool IsLibVlcInstalled => true; 

    public event EventHandler<int>? PositionChanged;
    public event EventHandler<int>? DurationChanged;
    public event EventHandler<bool>? PlaybackStateChanged;

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

    public async Task Load(string url)
    {
        CurrentUrl = url;
        Stop(); // Cleans up previous streams
        
        try
        {
            if (_engine == null)
            {
                _engine = new MiniAudioEngine();
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            _tempFilePath = Path.GetTempFileName() + ".mp3";
            File.WriteAllBytes(_tempFilePath, Array.Empty<byte>());
            
            bool isReady = false;
            int maxRetries = 3;
            long contentLength = 100 * 1024 * 1024;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var client = new HttpClient();
                    var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();
                    
                    contentLength = response.Content.Headers.ContentLength ?? 100 * 1024 * 1024;
                    bool downloadComplete = false;
                    
                    // Start background download stream
                    var backgroundDownload = Task.Run(async () => {
                        try {
                            using var stream = await response.Content.ReadAsStreamAsync(ct);
                            using var fs = new FileStream(_tempFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                            byte[] buffer = new byte[81920];
                            int read;
                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0) {
                                await fs.WriteAsync(buffer, 0, read, ct);
                                await fs.FlushAsync(ct);
                            }
                        } catch { /* Ignore cancellation or network errors in background */ }
                        finally {
                            downloadComplete = true;
                            _bufferStream?.MarkComplete();
                        }
                    }, ct);

                    // Wait until we have enough data (at least 1KB) to ensure headers exist for SoundFlow
                    var fileInfo = new FileInfo(_tempFilePath);
                    int retry = 0;
                    while (fileInfo.Length < 1000 && !downloadComplete && retry < 50 && !ct.IsCancellationRequested)
                    {
                        await Task.Delay(100, ct);
                        fileInfo.Refresh();
                        retry++;
                    }

                    if (fileInfo.Length >= 1000 || downloadComplete)
                    {
                        isReady = true;
                        break;
                    }
                    
                    // If we didn't get enough data and it's not complete, retry
                    _cts.Cancel();
                    _cts = new CancellationTokenSource();
                    ct = _cts.Token;
                }
                catch (Exception) when (attempt < maxRetries)
                {
                    await Task.Delay(1000, ct);
                }
            }

            if (!isReady)
            {
                return; // Failed to stream after retries
            }

            _bufferStream = new HttpBufferingStream(_tempFilePath, contentLength);
            
            // Execute SoundFlow initialization on a background thread.
            await Task.Run(() => 
            {
                _provider = new StreamDataProvider(_engine, _bufferStream, new SoundFlow.Metadata.Models.ReadOptions());
                
                var format = new AudioFormat {
                    Format = _provider.SampleFormat == SampleFormat.Unknown ? SampleFormat.F32 : _provider.SampleFormat,
                    Channels = _provider.FormatInfo?.ChannelCount ?? 2,
                    SampleRate = _provider.FormatInfo?.SampleRate ?? 44100
                };

                _device = _engine.InitializePlaybackDevice(null, format, new MiniAudioDeviceConfig());
                _player = new SoundPlayer(_engine, format, _provider);
                
                _device.MasterMixer.AddComponent(_player);
                _device.Start();
                
            }, ct);
            
            // Fire an initial duration and position update so UI is correct even if paused
            DurationChanged?.Invoke(this, DurationSeconds);
            PositionChanged?.Invoke(this, PositionSeconds);
        }
        catch(Exception e)
        {
            Log.Fatal(e,$"Error loading SoundFlow audio: {e}");
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
        _cts?.Cancel();
        _player?.Dispose();
        _player = null;
        _device?.Dispose();
        _device = null;
        _provider?.Dispose();
        _provider = null;
        _bufferStream?.Dispose();
        _bufferStream = null;
        
        if (_tempFilePath != null && File.Exists(_tempFilePath))
        {
            try { File.Delete(_tempFilePath); } catch { }
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