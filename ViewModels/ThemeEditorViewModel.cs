using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Singularidi.Themes;

namespace Singularidi.ViewModels;

public sealed partial class ThemeEditorViewModel : ObservableObject
{
    private readonly Action<ThemeData?> _close;

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

    [ObservableProperty]
    private double _noteCornerRadius;

    [ObservableProperty]
    private NoteColorMode _colorMode;

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

    public ObservableCollection<ChannelColorViewModel> ChannelColorEntries { get; } = new();
    public ObservableCollection<TrackColorViewModel> TrackColorEntries { get; } = new();
    public ObservableCollection<ColorOverrideEntry> NoteOverrides { get; } = new();
    public ObservableCollection<ColorOverrideEntry> KeyOverrides { get; } = new();

    public ThemeEditorViewModel(ThemeData source, Action<ThemeData?> close)
    {
        _close = close;
        _name = source.Name;
        _background = Color.Parse(source.Background);
        _guideLine = Color.Parse(source.GuideLine);
        _noteShape = source.NoteShape;
        _noteCornerRadius = source.NoteCornerRadius;
        _colorMode = source.ColorMode;
        _whiteKey = Color.Parse(source.WhiteKey);
        _blackKey = Color.Parse(source.BlackKey);
        _activeHighlight = Color.Parse(source.ActiveHighlight);
        _activeNoteBlend = source.ActiveNoteBlend;
        _activeWhiteKeyBlend = source.ActiveWhiteKeyBlend;
        _activeBlackKeyBlend = source.ActiveBlackKeyBlend;

        for (int i = 0; i < 16; i++)
            ChannelColorEntries.Add(new ChannelColorViewModel(i, Color.Parse(source.ChannelColorValues[i])));

        if (source.TrackColorValues != null)
            for (int i = 0; i < source.TrackColorValues.Count; i++)
                TrackColorEntries.Add(new TrackColorViewModel(i, Color.Parse(source.TrackColorValues[i])));

        if (source.NoteColorOverrideValues != null)
            foreach (var kv in source.NoteColorOverrideValues)
                NoteOverrides.Add(new ColorOverrideEntry { NoteNumber = (decimal)kv.Key, Color = Color.Parse(kv.Value) });

        if (source.KeyColorOverrideValues != null)
            foreach (var kv in source.KeyColorOverrideValues)
                KeyOverrides.Add(new ColorOverrideEntry { NoteNumber = (decimal)kv.Key, Color = Color.Parse(kv.Value) });
    }

    // Parameterless for designer support
    public ThemeEditorViewModel() : this(BuiltInThemes.Dark(), _ => { }) { }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
            Name = "Custom";

        _close(ToThemeData());
    }

    [RelayCommand]
    private void Cancel() => _close(null);

    partial void OnColorModeChanged(NoteColorMode value)
    {
        if (value == NoteColorMode.Track && TrackColorEntries.Count == 0)
        {
            for (int i = 0; i < ChannelColorEntries.Count; i++)
                TrackColorEntries.Add(new TrackColorViewModel(i, ChannelColorEntries[i].Color));
        }
    }

    [RelayCommand]
    private void AddTrackColor()
    {
        int index = TrackColorEntries.Count;
        var defaultColor = ChannelColorEntries[index % 16].Color;
        TrackColorEntries.Add(new TrackColorViewModel(index, defaultColor));
    }

    [RelayCommand]
    private void RemoveTrackColor(TrackColorViewModel entry)
    {
        TrackColorEntries.Remove(entry);
        // Re-index remaining entries
        var items = TrackColorEntries.Select(e => e.Color).ToList();
        TrackColorEntries.Clear();
        for (int i = 0; i < items.Count; i++)
            TrackColorEntries.Add(new TrackColorViewModel(i, items[i]));
    }

    [RelayCommand]
    private void AddNoteOverride() =>
        NoteOverrides.Add(new ColorOverrideEntry { NoteNumber = 60m, Color = Colors.White });

    [RelayCommand]
    private void RemoveNoteOverride(ColorOverrideEntry entry) =>
        NoteOverrides.Remove(entry);

    [RelayCommand]
    private void AddKeyOverride() =>
        KeyOverrides.Add(new ColorOverrideEntry { NoteNumber = 60m, Color = Colors.White });

    [RelayCommand]
    private void RemoveKeyOverride(ColorOverrideEntry entry) =>
        KeyOverrides.Remove(entry);

    public ThemeData ToThemeData()
    {
        var data = new ThemeData
        {
            Name = Name,
            Background = FormatColor(Background),
            GuideLine = FormatColor(GuideLine),
            NoteShape = NoteShape,
            NoteCornerRadius = NoteCornerRadius,
            ColorMode = ColorMode,
            ChannelColorValues = ChannelColorEntries.Select(e => FormatColor(e.Color)).ToArray(),
            TrackColorValues = TrackColorEntries.Count > 0
                ? TrackColorEntries.Select(e => FormatColor(e.Color)).ToList()
                : null,
            WhiteKey = FormatColor(WhiteKey),
            BlackKey = FormatColor(BlackKey),
            ActiveHighlight = FormatColor(ActiveHighlight),
            ActiveNoteBlend = ActiveNoteBlend,
            ActiveWhiteKeyBlend = ActiveWhiteKeyBlend,
            ActiveBlackKeyBlend = ActiveBlackKeyBlend,
        };

        if (NoteOverrides.Count > 0)
            data.NoteColorOverrideValues = NoteOverrides.ToDictionary(e => (int)e.NoteNumber, e => FormatColor(e.Color));

        if (KeyOverrides.Count > 0)
            data.KeyColorOverrideValues = KeyOverrides.ToDictionary(e => (int)e.NoteNumber, e => FormatColor(e.Color));

        return data;
    }

    private static string FormatColor(Color c) =>
        c.A < 255 ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

public partial class ChannelColorViewModel : ObservableObject
{
    public int ChannelIndex { get; }

    public string Label => $"Ch {ChannelIndex}";

    [ObservableProperty]
    private Color _color;

    public ChannelColorViewModel(int channelIndex, Color color)
    {
        ChannelIndex = channelIndex;
        _color = color;
    }
}

public partial class TrackColorViewModel : ObservableObject
{
    public int TrackIndex { get; }
    public string Label => $"Track {TrackIndex}";

    [ObservableProperty]
    private Color _color;

    public TrackColorViewModel(int trackIndex, Color color)
    {
        TrackIndex = trackIndex;
        _color = color;
    }
}

public partial class ColorOverrideEntry : ObservableObject
{
    [ObservableProperty]
    private decimal _noteNumber;

    [ObservableProperty]
    private Color _color = Colors.White;

    partial void OnNoteNumberChanging(decimal value)
    {
        _noteNumber = Math.Clamp(value, 0, 127);
    }
}
