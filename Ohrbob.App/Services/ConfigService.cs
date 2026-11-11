using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Ohrbob.App.Services;

public class ConfigService
{
    private static readonly int[] AllowedBufferMinutes = { 0, 10, 15, 30, 60, 120 };

    public string? ServerUrl { get; set; }
    public string? StreamToken { get; set; }
    public bool DoNotAutoPlay { get; set; }

    private Dictionary<string, int> _bufferMinutesByStation = new();
    private readonly string? _customPath;

    public ConfigService()
    {
        Load();
    }

    public ConfigService(string customPath)
    {
        _customPath = customPath;
        Load();
    }

    private string GetConfigFilePath()
    {
        if (_customPath != null)
        {
            return _customPath;
        }
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "Ohrbob");
            return Path.Combine(dir, "config.json");
        }
        catch
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }
    }

    public void Load()
    {
        var path = GetConfigFilePath();
        if (!File.Exists(path))
        {
            // Fallback next to the executable
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (!File.Exists(path))
            {
                return;
            }
        }

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<ConfigData>(json);
            if (data != null)
            {
                ServerUrl = data.ServerUrl;
                StreamToken = data.StreamToken;
                DoNotAutoPlay = data.DoNotAutoPlay;
                _bufferMinutesByStation = data.BufferMinutesByStation ?? new();
            }
        }
        catch
        {
            // Fallback or handle load error gracefully
        }
    }

    public int GetBufferMinutes(string stationId) =>
        _bufferMinutesByStation.TryGetValue(stationId, out var minutes) &&
        AllowedBufferMinutes.Contains(minutes)
            ? minutes
            : 0;

    public void SetBufferMinutes(string stationId, int minutes)
    {
        if (string.IsNullOrEmpty(stationId)) return;
        var normalized = AllowedBufferMinutes.Contains(minutes) ? minutes : 0;
        if (normalized == 0) _bufferMinutesByStation.Remove(stationId);
        else _bufferMinutesByStation[stationId] = normalized;
        Persist();
    }

    public void Save(string serverUrl, string streamToken, bool? doNotAutoPlay = null)
    {
        ServerUrl = serverUrl;
        StreamToken = streamToken;
        if (doNotAutoPlay.HasValue) DoNotAutoPlay = doNotAutoPlay.Value;
        Persist();
    }

    private void Persist()
    {
        var path = GetConfigFilePath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = Serialize();
            File.WriteAllText(path, json);
        }
        catch
        {
            // Try to write to the fallback path in base directory if AppData is unavailable/unwritable
            try
            {
                var fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                File.WriteAllText(fallbackPath, Serialize());
            }
            catch
            {
                // Ignore save errors
            }
        }
    }

    private string Serialize()
    {
        var data = new ConfigData
        {
            ServerUrl = ServerUrl,
            StreamToken = StreamToken,
            DoNotAutoPlay = DoNotAutoPlay,
            BufferMinutesByStation = _bufferMinutesByStation,
        };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    private class ConfigData
    {
        public string? ServerUrl { get; set; }
        public string? StreamToken { get; set; }
        public bool DoNotAutoPlay { get; set; }
        public Dictionary<string, int>? BufferMinutesByStation { get; set; }
    }
}
