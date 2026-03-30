using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Singularidi.Themes;
using Singularidi.ViewModels;
using Singularidi.Views;

namespace Singularidi.Services;

public sealed class DialogService : IDialogService
{
    private readonly Func<Window> _getWindow;

    public DialogService(Func<Window> getWindow)
    {
        _getWindow = getWindow;
    }

    public async Task<string?> OpenMidiFileAsync()
    {
        var window = _getWindow();
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open MIDI File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("MIDI Files") { Patterns = new[] { "*.mid", "*.midi" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> OpenSoundFontAsync()
    {
        var window = _getWindow();
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select SoundFont",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SoundFont Files") { Patterns = new[] { "*.sf2" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<ThemeData?> ShowThemeEditorAsync(ThemeData source)
    {
        var window = _getWindow();
        var editor = new ThemeEditorWindow();
        var vm = new ThemeEditorViewModel(source, result => editor.Close(result));
        editor.DataContext = vm;
        return await editor.ShowDialog<ThemeData?>(window);
    }

    public async Task<string?> SaveMp4FileAsync()
    {
        var window = _getWindow();
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to MP4",
            DefaultExtension = "mp4",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("MP4 Video") { Patterns = new[] { "*.mp4" } },
            }
        });
        return file?.Path.LocalPath;
    }
}
