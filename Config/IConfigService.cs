namespace Singularidi.Config;

public interface IConfigService
{
    AppConfig Load();
    void Save(AppConfig cfg);
}
