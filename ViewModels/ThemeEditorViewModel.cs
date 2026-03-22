using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Singularidi.Themes;

namespace Singularidi.ViewModels;

public sealed class ThemeEditorViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _name;
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

    private Color _background;
    public Color Background { get => _background; set { _background = value; OnPropertyChanged(); } }

    private Color _guideLine;
    public Color GuideLine { get => _guideLine; set { _guideLine = value; OnPropertyChanged(); } }

    private NoteShape _noteShape;
    public NoteShape NoteShape { get => _noteShape; set { _noteShape = value; OnPropertyChanged(); } }

    public bool IsRectangular
    {
        get => _noteShape == NoteShape.Rectangular;
        set { if (value) NoteShape = NoteShape.Rectangular; OnPropertyChanged(); OnPropertyChanged(nameof(IsDotBlock)); }
    }
    public bool IsDotBlock
    {
        get => _noteShape == NoteShape.DotBlock;
        set { if (value) NoteShape = NoteShape.DotBlock; OnPropertyChanged(); OnPropertyChanged(nameof(IsRectangular)); }
    }

    private Color _whiteKey;
    public Color WhiteKey { get => _whiteKey; set { _whiteKey = value; OnPropertyChanged(); } }

    private Color _blackKey;
    public Color BlackKey { get => _blackKey; set { _blackKey = value; OnPropertyChanged(); } }

    private Color _activeHighlight;
    public Color ActiveHighlight { get => _activeHighlight; set { _activeHighlight = value; OnPropertyChanged(); } }

    private float _activeNoteBlend;
    public float ActiveNoteBlend { get => _activeNoteBlend; set { _activeNoteBlend = value; OnPropertyChanged(); } }

    private float _activeWhiteKeyBlend;
    public float ActiveWhiteKeyBlend { get => _activeWhiteKeyBlend; set { _activeWhiteKeyBlend = value; OnPropertyChanged(); } }

    private float _activeBlackKeyBlend;
    public float ActiveBlackKeyBlend { get => _activeBlackKeyBlend; set { _activeBlackKeyBlend = value; OnPropertyChanged(); } }

    public Color[] ChannelColors { get; } = new Color[16];

    public ObservableCollection<ColorOverrideEntry> NoteOverrides { get; } = new();
    public ObservableCollection<ColorOverrideEntry> KeyOverrides { get; } = new();

    public ThemeEditorViewModel(ThemeData source)
    {
        _name = source.Name;
        _background = Color.Parse(source.Background);
        _guideLine = Color.Parse(source.GuideLine);
        _noteShape = source.NoteShape;
        _whiteKey = Color.Parse(source.WhiteKey);
        _blackKey = Color.Parse(source.BlackKey);
        _activeHighlight = Color.Parse(source.ActiveHighlight);
        _activeNoteBlend = source.ActiveNoteBlend;
        _activeWhiteKeyBlend = source.ActiveWhiteKeyBlend;
        _activeBlackKeyBlend = source.ActiveBlackKeyBlend;

        for (int i = 0; i < 16; i++)
            ChannelColors[i] = Color.Parse(source.ChannelColorValues[i]);

        if (source.NoteColorOverrideValues != null)
            foreach (var kv in source.NoteColorOverrideValues)
                NoteOverrides.Add(new ColorOverrideEntry { NoteNumber = kv.Key, Color = Color.Parse(kv.Value) });

        if (source.KeyColorOverrideValues != null)
            foreach (var kv in source.KeyColorOverrideValues)
                KeyOverrides.Add(new ColorOverrideEntry { NoteNumber = kv.Key, Color = Color.Parse(kv.Value) });
    }

    public ThemeData ToThemeData()
    {
        var data = new ThemeData
        {
            Name = Name,
            Background = FormatColor(Background),
            GuideLine = FormatColor(GuideLine),
            NoteShape = NoteShape,
            ChannelColorValues = ChannelColors.Select(FormatColor).ToArray(),
            WhiteKey = FormatColor(WhiteKey),
            BlackKey = FormatColor(BlackKey),
            ActiveHighlight = FormatColor(ActiveHighlight),
            ActiveNoteBlend = ActiveNoteBlend,
            ActiveWhiteKeyBlend = ActiveWhiteKeyBlend,
            ActiveBlackKeyBlend = ActiveBlackKeyBlend,
        };

        if (NoteOverrides.Count > 0)
            data.NoteColorOverrideValues = NoteOverrides.ToDictionary(e => e.NoteNumber, e => FormatColor(e.Color));

        if (KeyOverrides.Count > 0)
            data.KeyColorOverrideValues = KeyOverrides.ToDictionary(e => e.NoteNumber, e => FormatColor(e.Color));

        return data;
    }

    public void AddNoteOverride() =>
        NoteOverrides.Add(new ColorOverrideEntry { NoteNumber = 60, Color = Colors.White });

    public void RemoveNoteOverride(ColorOverrideEntry entry) =>
        NoteOverrides.Remove(entry);

    public void AddKeyOverride() =>
        KeyOverrides.Add(new ColorOverrideEntry { NoteNumber = 60, Color = Colors.White });

    public void RemoveKeyOverride(ColorOverrideEntry entry) =>
        KeyOverrides.Remove(entry);

    private static string FormatColor(Color c) =>
        c.A < 255 ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ColorOverrideEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private int _noteNumber;
    public int NoteNumber
    {
        get => _noteNumber;
        set { _noteNumber = Math.Clamp(value, 0, 127); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoteNumber))); }
    }

    private Color _color = Colors.White;
    public Color Color
    {
        get => _color;
        set { _color = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color))); }
    }
}
