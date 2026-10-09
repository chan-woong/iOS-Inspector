using System.Text.Json;
using IosInspector.Models;

namespace IosInspector.Services;

public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IosInspector", "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
