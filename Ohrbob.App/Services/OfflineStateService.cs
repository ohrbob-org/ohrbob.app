using System;
using System.IO;
using System.Text.Json;
using Ohrbob.App.Models;

namespace Ohrbob.App.Services;

public class OfflineStateService
{
    private readonly string _path;

    public OfflineStateService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ohrbob",
            "offline-state.json");
    }

    public OfflineSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            return JsonSerializer.Deserialize<OfflineSnapshot>(File.ReadAllText(_path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(OfflineSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempPath, _path, overwrite: true);
    }
}
