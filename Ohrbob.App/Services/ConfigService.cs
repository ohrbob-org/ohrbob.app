using System;
using System.IO;
using System.Text.Json;

namespace Ohrbob.App.Services;

public class ConfigService
{
    public string? ServerUrl { get; set; }
    public string? StreamToken { get; set; }

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
            }
        }
        catch
        {
            // Fallback or handle load error gracefully
        }
    }

    public void Save(string serverUrl, string streamToken)
    {
        ServerUrl = serverUrl;
        StreamToken = streamToken;

        var path = GetConfigFilePath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var data = new ConfigData { ServerUrl = ServerUrl, StreamToken = StreamToken };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Try to write to the fallback path in base directory if AppData is unavailable/unwritable
            try
            {
                var fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                var data = new ConfigData { ServerUrl = ServerUrl, StreamToken = StreamToken };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(fallbackPath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
    }

    private class ConfigData
    {
        public string? ServerUrl { get; set; }
        public string? StreamToken { get; set; }
    }
}
