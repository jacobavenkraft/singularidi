using System.Text.Json;

namespace Singularidi.Config;

public class ConfigService : IConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string GetConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Singularidi", "config.json");
    }

    public AppConfig Load()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
        {
            var cfg = new AppConfig();
            Save(cfg);
            return cfg;
        }
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig cfg)
    {
        var path = GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOptions));
    }
}
