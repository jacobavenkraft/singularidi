using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Singularidi.Themes;
using Singularidi.ViewModels;

namespace Singularidi.Views;

public partial class ThemeEditorWindow : Window
{
    private readonly ThemeEditorViewModel _vm;
    private readonly ColorPicker[] _channelPickers = new ColorPicker[16];

    public ThemeEditorWindow(ThemeData source)
    {
        InitializeComponent();
        _vm = new ThemeEditorViewModel(source);

        // Theme name
        TxtThemeName.Text = _vm.Name;

        // Background & guides
        PickerBackground.Color = _vm.Background;
        PickerGuideLine.Color = _vm.GuideLine;

        // Note shape
        RadioRectangular.IsChecked = _vm.NoteShape == NoteShape.Rectangular;
        RadioDotBlock.IsChecked = _vm.NoteShape == NoteShape.DotBlock;

        // Channel colors — 16 color pickers in a 4x4 grid
        for (int i = 0; i < 16; i++)
        {
            var picker = new ColorPicker
            {
                Color = _vm.ChannelColors[i],
                Width = 140,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 8),
            };
            var label = new TextBlock
            {
                Text = $"Ch {i}",
                Foreground = new SolidColorBrush(Color.Parse("#999999")),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2),
            };
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(label);
            stack.Children.Add(picker);
            ChannelColorsPanel.Children.Add(stack);
            _channelPickers[i] = picker;
        }

        // Piano keys
        PickerWhiteKey.Color = _vm.WhiteKey;
        PickerBlackKey.Color = _vm.BlackKey;

        // Active highlight
        PickerActiveHighlight.Color = _vm.ActiveHighlight;
        SliderNoteBlend.Value = _vm.ActiveNoteBlend;
        SliderWhiteKeyBlend.Value = _vm.ActiveWhiteKeyBlend;
        SliderBlackKeyBlend.Value = _vm.ActiveBlackKeyBlend;

        // Note overrides
        foreach (var entry in _vm.NoteOverrides)
            AddOverrideRow(NoteOverridesPanel, entry, true);

        // Key overrides
        foreach (var entry in _vm.KeyOverrides)
            AddOverrideRow(KeyOverridesPanel, entry, false);

        BtnAddNoteOverride.Click += (_, _) =>
        {
            _vm.AddNoteOverride();
            var entry = _vm.NoteOverrides[^1];
            AddOverrideRow(NoteOverridesPanel, entry, true);
        };

        BtnAddKeyOverride.Click += (_, _) =>
        {
            _vm.AddKeyOverride();
            var entry = _vm.KeyOverrides[^1];
            AddOverrideRow(KeyOverridesPanel, entry, false);
        };
    }

    // Parameterless constructor for AXAML designer
    public ThemeEditorWindow() : this(BuiltInThemes.Dark()) { }

    private void AddOverrideRow(StackPanel panel, ColorOverrideEntry entry, bool isNoteOverride)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        var numBox = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 127,
            Value = entry.NoteNumber,
            Width = 80,
            FormatString = "0",
        };
        numBox.ValueChanged += (_, e) =>
        {
            if (e.NewValue.HasValue)
                entry.NoteNumber = (int)e.NewValue.Value;
        };

        var picker = new ColorPicker
        {
            Color = entry.Color,
            Width = 140,
            Height = 32,
        };
        picker.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(ColorPicker.Color))
                entry.Color = picker.Color;
        };

        var removeBtn = new Button { Content = "Remove", Padding = new Thickness(8, 4) };
        removeBtn.Click += (_, _) =>
        {
            if (isNoteOverride)
                _vm.RemoveNoteOverride(entry);
            else
                _vm.RemoveKeyOverride(entry);
            panel.Children.Remove(row);
        };

        var noteLabel = new TextBlock
        {
            Text = "Note:",
            Foreground = new SolidColorBrush(Color.Parse("#999999")),
            VerticalAlignment = VerticalAlignment.Center,
        };

        row.Children.Add(noteLabel);
        row.Children.Add(numBox);
        row.Children.Add(picker);
        row.Children.Add(removeBtn);
        panel.Children.Add(row);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Collect values from controls back into the VM
        _vm.Name = TxtThemeName.Text?.Trim() ?? "Custom";
        if (string.IsNullOrWhiteSpace(_vm.Name))
            _vm.Name = "Custom";

        _vm.Background = PickerBackground.Color;
        _vm.GuideLine = PickerGuideLine.Color;
        _vm.NoteShape = RadioDotBlock.IsChecked == true ? NoteShape.DotBlock : NoteShape.Rectangular;

        for (int i = 0; i < 16; i++)
            _vm.ChannelColors[i] = _channelPickers[i].Color;

        _vm.WhiteKey = PickerWhiteKey.Color;
        _vm.BlackKey = PickerBlackKey.Color;

        _vm.ActiveHighlight = PickerActiveHighlight.Color;
        _vm.ActiveNoteBlend = (float)SliderNoteBlend.Value;
        _vm.ActiveWhiteKeyBlend = (float)SliderWhiteKeyBlend.Value;
        _vm.ActiveBlackKeyBlend = (float)SliderBlackKeyBlend.Value;

        Close(_vm.ToThemeData());
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
