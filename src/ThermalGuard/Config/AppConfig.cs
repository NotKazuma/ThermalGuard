using System.Text.Json;
using ThermalGuard.Core;

namespace ThermalGuard.Config;

public sealed class AppConfig
{
    public List<Profile> Profiles { get; set; } = new();
    public Profile Default { get; set; } = new() { DisplayName = "Default (auto-detected)" };
    public bool AutoDetect { get; set; } = true;
    public int AutoDetectSeconds { get; set; } = 10; // fullscreen must hold this long before we act
    public List<string> Ignored { get; set; } = new();
    public bool ShowFpsOverlay { get; set; } = true;

    public static AppConfig Seed() => new()
    {
        Profiles =
        {
            new Profile { DisplayName = "Assassin's Creed IV Black Flag", ExeName = "ACBlackFlag" },
        },
    };
}

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private readonly string _path;
    public ProfileStore(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ThermalGuard", "config.json");

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path)) ?? AppConfig.Seed();
        }
        catch (JsonException) { Backup(); } // keep the broken file instead of silently overwriting it
        catch (IOException) { }
        var seed = AppConfig.Seed();
        Save(seed);
        return seed;
    }

    public void Save(AppConfig c)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(c, Opts));
        File.Move(tmp, _path, overwrite: true);
    }

    private void Backup()
    {
        try { File.Move(_path, _path + ".bad-" + DateTime.Now.ToString("yyyyMMddHHmmss")); } catch (IOException) { }
    }
}
