using Avalonia;
using Avalonia.Media;
using Singularidi.Midi;
using Singularidi.Themes;

namespace Singularidi.Visualization;

public sealed class VerticalFallEngine : IVisualizationEngine
{
    public string Name => "Vertical Fall";

    private readonly PianoLayout _layout = new();

    // Cached brushes/pens rebuilt when theme changes
    private IBrush _backgroundBrush = null!;
    private IPen _guidePen = null!;
    private IBrush _whiteKeyBrush = null!;
    private IBrush _blackKeyBrush = null!;
    private IPen _whiteKeyBorderPen = null!;
    private SolidColorBrush[] _channelBrushes = null!;
    private Color[] _channelColors = null!;
    private Color[] _trackColors = null!;
    private NoteColorMode _colorMode;
    private Dictionary<int, Color>? _noteColorOverrides;
    private Dictionary<int, Color>? _keyColorOverrides;
    private IVisualTheme? _cachedTheme;

    public void OnSizeChanged(double width, double height)
    {
        _layout.RebuildIfNeeded(width);
    }

    private void EnsureThemeCaches(IVisualTheme theme)
    {
        if (ReferenceEquals(theme, _cachedTheme)) return;
        _cachedTheme = theme;
        _backgroundBrush = new SolidColorBrush(theme.BackgroundColor);
        _guidePen = new Pen(new SolidColorBrush(theme.GuideLineColor), 1);
        _whiteKeyBrush = new SolidColorBrush(theme.WhiteKeyColor);
        _blackKeyBrush = new SolidColorBrush(theme.BlackKeyColor);
        _whiteKeyBorderPen = new Pen(Brushes.Black, 0.5);
        _colorMode = theme.ColorMode;
        _channelColors = theme.ChannelColors;
        _channelBrushes = _channelColors.Select(c => new SolidColorBrush(c)).ToArray();
        _trackColors = theme.TrackColors;
        _noteColorOverrides = theme.NoteColorOverrides;
        _keyColorOverrides = theme.KeyColorOverrides;
    }

    public void Render(
        DrawingContext ctx,
        double w,
        double h,
        IReadOnlyList<NoteEvent> notes,
        double currentTimeSeconds,
        IVisualTheme theme,
        bool highlightActiveNotes,
        int[] activeKeyChannel,
        int[] activeKeyTrack)
    {
        _layout.RebuildIfNeeded(w);
        EnsureThemeCaches(theme);

        double pianoHeight = h * PianoLayout.PianoHeightFraction;
        double vizHeight = h - pianoHeight;

        // 1. Background
        ctx.DrawRectangle(_backgroundBrush, null, new Rect(0, 0, w, h));

        // 2. Note lane guides
        for (int note = 0; note < 128; note++)
        {
            ctx.DrawLine(_guidePen,
                new Point(_layout.XCenter[note], 0),
                new Point(_layout.XCenter[note], vizHeight));
        }

        // 3. Falling notes
        double now = currentTimeSeconds;
        foreach (var note in notes)
        {
            if (note.StartSeconds - now > PianoLayout.LookAheadSeconds) break;
            if (note.EndSeconds < now - 0.1) continue;

            double yBottom = vizHeight - (note.StartSeconds - now) / PianoLayout.LookAheadSeconds * vizHeight;
            double yTop    = vizHeight - (note.EndSeconds   - now) / PianoLayout.LookAheadSeconds * vizHeight;
            yTop    = Math.Clamp(yTop,    0, vizHeight);
            yBottom = Math.Clamp(yBottom, 0, vizHeight);

            double rectH = yBottom - yTop;
            if (rectH < 1) rectH = 1;

            Color baseColor = ColorHelper.ResolveNoteColor(note, _colorMode, _channelColors, _trackColors, _noteColorOverrides);

            bool isActive = highlightActiveNotes && (
                _colorMode == NoteColorMode.Track
                    ? activeKeyTrack[note.NoteNumber] == note.Track
                    : activeKeyChannel[note.NoteNumber] == note.Channel);
            var fillColor = isActive ? ColorHelper.LerpToColor(baseColor, theme.ActiveHighlightColor, theme.ActiveNoteBlend) : baseColor;
            var brush = new SolidColorBrush(fillColor);

            double nw = _layout.NoteWidth[note.NoteNumber];
            double x = _layout.XCenter[note.NoteNumber] - nw / 2;
            var rect = new Rect(x, yTop, nw, rectH);

            double cornerRadius = theme.NoteShape == NoteShape.DotBlock ? nw / 2 : 2;
            ctx.DrawRectangle(brush, null, rect, cornerRadius, cornerRadius);
        }

        // 4. Piano keyboard
        double pianoY = vizHeight;
        DrawPianoKeyboard(ctx, theme, pianoY, pianoHeight, highlightActiveNotes, activeKeyChannel, activeKeyTrack);
    }

    private void DrawPianoKeyboard(
        DrawingContext ctx,
        IVisualTheme theme,
        double pianoY,
        double pianoHeight,
        bool highlightActiveNotes,
        int[] activeKeyChannel,
        int[] activeKeyTrack)
    {
        // White keys first
        for (int note = 0; note < 128; note++)
        {
            if (PianoLayout.IsBlackKey[note % 12]) continue;
            int channel = activeKeyChannel[note];
            IBrush keyBrush;
            if (channel >= 0)
            {
                var activeColor = ColorHelper.ResolveActiveKeyColor(
                    note, activeKeyChannel, activeKeyTrack,
                    _colorMode, _channelColors, _trackColors,
                    theme.ActiveHighlightColor, theme.ActiveWhiteKeyBlend);
                keyBrush = new SolidColorBrush(activeColor);
            }
            else if (_keyColorOverrides != null && _keyColorOverrides.TryGetValue(note, out var keyOverride))
            {
                keyBrush = new SolidColorBrush(keyOverride);
            }
            else
            {
                keyBrush = _whiteKeyBrush;
            }

            double x = _layout.XCenter[note] - _layout.WhiteKeyWidth / 2;
            var keyRect = new Rect(x, pianoY, _layout.WhiteKeyWidth - 1, pianoHeight);
            ctx.DrawRectangle(keyBrush, _whiteKeyBorderPen, keyRect, 0, 3);
        }

        // Black keys on top
        for (int note = 0; note < 128; note++)
        {
            if (!PianoLayout.IsBlackKey[note % 12]) continue;
            int channel = activeKeyChannel[note];
            IBrush keyBrush;
            if (channel >= 0)
            {
                var activeColor = ColorHelper.ResolveActiveKeyColor(
                    note, activeKeyChannel, activeKeyTrack,
                    _colorMode, _channelColors, _trackColors,
                    theme.ActiveHighlightColor, theme.ActiveBlackKeyBlend);
                keyBrush = new SolidColorBrush(activeColor);
            }
            else if (_keyColorOverrides != null && _keyColorOverrides.TryGetValue(note, out var keyOverride))
            {
                keyBrush = new SolidColorBrush(keyOverride);
            }
            else
            {
                keyBrush = _blackKeyBrush;
            }

            double x = _layout.XCenter[note] - _layout.BlackKeyWidth / 2;
            double bh = pianoHeight * 0.65;
            var keyRect = new Rect(x, pianoY, _layout.BlackKeyWidth, bh);
            ctx.DrawRectangle(keyBrush, null, keyRect, 0, 2);
        }
    }
}
