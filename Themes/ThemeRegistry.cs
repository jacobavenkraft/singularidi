namespace Singularidi.Themes;

public class ThemeRegistry
{
    private readonly Dictionary<string, IVisualTheme> _themes = new();

    public ThemeRegistry(List<ThemeData>? customThemes = null)
    {
        _themes["Dark"] = BuiltInThemes.Dark();
        _themes["Light"] = BuiltInThemes.Light();

        if (customThemes != null)
        {
            foreach (var t in customThemes)
                _themes[t.Name] = t;
        }
    }

    public IVisualTheme Get(string name) =>
        _themes.TryGetValue(name, out var theme) ? theme : _themes["Dark"];

    public void AddOrUpdate(ThemeData theme) =>
        _themes[theme.Name] = theme;

    public IReadOnlyCollection<string> AvailableThemes => _themes.Keys;
}
