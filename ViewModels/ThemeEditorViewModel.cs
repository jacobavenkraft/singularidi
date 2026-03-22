using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Singularidi.Themes;

namespace Singularidi.ViewModels;

public sealed partial class ThemeEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private Color _background;

    [ObservableProperty]
    private Color _guideLine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRectangular))]
    [NotifyPropertyChangedFor(nameof(IsDotBlock))]
    private NoteShape _noteShape;

    public bool IsRectangular
    {
        get => NoteShape == NoteShape.Rectangular;
        set { if (value) NoteShape = NoteShape.Rectangular; }
    }

    public bool IsDotBlock
    {
        get => NoteShape == NoteShape.DotBlock;
        set { if (value) NoteShape = NoteShape.DotBlock; }
    }

    [ObservableProperty]
    private Color _whiteKey;

    [ObservableProperty]
    private Color _blackKey;

    [ObservableProperty]
    private Color _activeHighlight;

    [ObservableProperty]
    private float _activeNoteBlend;

    [ObservableProperty]
    private float _activeWhiteKeyBlend;

    [ObservableProperty]
    private float _activeBlackKeyBlend;

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
}

public partial class ColorOverrideEntry : ObservableObject
{
    [ObservableProperty]
    private int _noteNumber;

    [ObservableProperty]
    private Color _color = Colors.White;

    partial void OnNoteNumberChanging(int value)
    {
        _noteNumber = Math.Clamp(value, 0, 127);
    }
}
