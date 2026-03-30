using Singularidi.Themes;

namespace Singularidi.Services;

public interface IDialogService
{
    Task<string?> OpenMidiFileAsync();
    Task<string?> OpenSoundFontAsync();
    Task<ThemeData?> ShowThemeEditorAsync(ThemeData source);
    Task<string?> SaveMp4FileAsync();
}
