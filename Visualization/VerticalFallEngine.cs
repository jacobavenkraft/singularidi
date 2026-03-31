using Avalonia;
using Avalonia.Media;
using Singularidi.Midi;
using Singularidi.Themes;

namespace Singularidi.Visualization;

public sealed class VerticalFallEngine : IVisualizationEngine
{
    public string Name => "Vertical Fall";

    public GuideLineStyle GuideLineStyle { get; set; } = GuideLineStyle.KeyWidthCentered;

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

    // 3D piano key cached brushes
    private LinearGradientBrush _whiteKeyGradient = null!;
    private LinearGradientBrush _blackKeyGradient = null!;
    private SolidColorBrush _whiteKeyFrontBrush = null!;
    private LinearGradientBrush _blackKeyShadowLBrush = null!;  // shadow on left side
    private LinearGradientBrush _blackKeyShadowRBrush = null!;  // shadow on right side

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

        // 3D piano key brushes
        _whiteKeyGradient = MakeWhiteKeyGradient(theme.WhiteKeyColor);
        _blackKeyGradient = MakeBlackKeyGradient(theme.BlackKeyColor);
        _whiteKeyFrontBrush = new SolidColorBrush(ColorHelper.Darken(theme.WhiteKeyColor, 0.25));
        _blackKeyShadowLBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(45, 0, 0, 0), 0.0),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0),
            }
        };
        _blackKeyShadowRBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0),
                new GradientStop(Color.FromArgb(45, 0, 0, 0), 1.0),
            }
        };
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
        switch (GuideLineStyle)
        {
            case GuideLineStyle.KeyWidthCentered:
                for (int note = 0; note < 128; note++)
                {
                    ctx.DrawLine(_guidePen,
                        new Point(_layout.XCenter[note], 0),
                        new Point(_layout.XCenter[note], vizHeight));
                }
                break;

            case GuideLineStyle.UniformCentered:
                for (int note = 0; note < 128; note++)
                {
                    ctx.DrawLine(_guidePen,
                        new Point(_layout.GuideXUniform[note], 0),
                        new Point(_layout.GuideXUniform[note], vizHeight));
                }
                break;

            case GuideLineStyle.Octave:
                foreach (double x in _layout.OctaveBoundaryX)
                {
                    ctx.DrawLine(_guidePen, new Point(x, 0), new Point(x, vizHeight));
                }
                break;
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

            double cornerRadius = theme.NoteShape == NoteShape.DotBlock
                ? nw / 2
                : Math.Min(theme.NoteCornerRadius, nw / 2);
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
        double sw = _layout.SlotWidth;
        double gap = Math.Max(sw * 0.06, 0.5);
        double blackKeyH = pianoHeight * PianoLayout.BlackKeyHeightFraction;
        double dividerY = pianoY + blackKeyH;
        double pressDepth = pianoHeight * 0.02;
        double thicknessH = pianoHeight * 0.025;

        // === White keys — shaped polygons with gradient fill ===
        for (int note = 0; note < 128; note++)
        {
            if (PianoLayout.IsBlackKey[note % 12]) continue;
            int channel = activeKeyChannel[note];
            bool isActive = channel >= 0;

            IBrush keyBrush;
            if (isActive)
            {
                var activeColor = ColorHelper.ResolveActiveKeyColor(
                    note, activeKeyChannel, activeKeyTrack,
                    _colorMode, _channelColors, _trackColors,
                    theme.ActiveHighlightColor, theme.ActiveWhiteKeyBlend);
                keyBrush = MakeWhiteKeyGradient(activeColor);
            }
            else if (_keyColorOverrides != null && _keyColorOverrides.TryGetValue(note, out var keyOverride))
            {
                keyBrush = MakeWhiteKeyGradient(keyOverride);
            }
            else
            {
                keyBrush = _whiteKeyGradient;
            }

            double topLeft = _layout.KeyTopLeft[note] + gap / 2;
            double topRight = _layout.KeyTopRight[note] - gap / 2;
            double botLeft = _layout.WhiteKeyBottomLeft[note] + gap / 2;
            double botRight = _layout.WhiteKeyBottomRight[note] - gap / 2;
            double botY = pianoY + pianoHeight;

            // Depression: shift top edge down for active keys
            double keyTopY = isActive ? pianoY + pressDepth : pianoY;
            double keyDivY = isActive ? dividerY + pressDepth * 0.5 : dividerY;

            var geo = new StreamGeometry();
            using (var sgCtx = geo.Open())
            {
                sgCtx.BeginFigure(new Point(topLeft, keyTopY), true);
                sgCtx.LineTo(new Point(topRight, keyTopY));
                sgCtx.LineTo(new Point(topRight, keyDivY));
                sgCtx.LineTo(new Point(botRight, keyDivY));
                sgCtx.LineTo(new Point(botRight, botY));
                sgCtx.LineTo(new Point(botLeft, botY));
                sgCtx.LineTo(new Point(botLeft, keyDivY));
                sgCtx.LineTo(new Point(topLeft, keyDivY));
                sgCtx.EndFigure(true);
            }
            ctx.DrawGeometry(keyBrush, _whiteKeyBorderPen, geo);

            // Front face strip at bottom
            double frontH = isActive ? thicknessH * 0.6 : thicknessH;
            var frontBrush = isActive
                ? new SolidColorBrush(ColorHelper.Darken(
                    ColorHelper.ResolveActiveKeyColor(note, activeKeyChannel, activeKeyTrack,
                        _colorMode, _channelColors, _trackColors,
                        theme.ActiveHighlightColor, theme.ActiveWhiteKeyBlend), 0.25))
                : _whiteKeyFrontBrush;
            ctx.DrawRectangle(frontBrush, null,
                new Rect(botLeft, botY, botRight - botLeft, frontH));
        }

        // === Shadow overlays from black keys onto white keys ===
        for (int note = 0; note < 128; note++)
        {
            if (PianoLayout.IsBlackKey[note % 12]) continue;
            double topLeft = _layout.KeyTopLeft[note] + gap / 2;
            double topRight = _layout.KeyTopRight[note] - gap / 2;
            double shadowW = _layout.BlackKeyWidth * 0.35;

            // Check left neighbor for black key
            if (note > 0 && PianoLayout.IsBlackKey[(note - 1) % 12])
            {
                ctx.DrawRectangle(_blackKeyShadowLBrush, null,
                    new Rect(topLeft, pianoY, shadowW, blackKeyH));
            }
            // Check right neighbor for black key
            if (note < 127 && PianoLayout.IsBlackKey[(note + 1) % 12])
            {
                ctx.DrawRectangle(_blackKeyShadowRBrush, null,
                    new Rect(topRight - shadowW, pianoY, shadowW, blackKeyH));
            }
        }

        // === Black keys on top with glossy gradient ===
        for (int note = 0; note < 128; note++)
        {
            if (!PianoLayout.IsBlackKey[note % 12]) continue;
            int channel = activeKeyChannel[note];
            bool isActive = channel >= 0;

            IBrush keyBrush;
            if (isActive)
            {
                var activeColor = ColorHelper.ResolveActiveKeyColor(
                    note, activeKeyChannel, activeKeyTrack,
                    _colorMode, _channelColors, _trackColors,
                    theme.ActiveHighlightColor, theme.ActiveBlackKeyBlend);
                keyBrush = MakeBlackKeyGradient(activeColor);
            }
            else if (_keyColorOverrides != null && _keyColorOverrides.TryGetValue(note, out var keyOverride))
            {
                keyBrush = MakeBlackKeyGradient(keyOverride);
            }
            else
            {
                keyBrush = _blackKeyGradient;
            }

            double x = _layout.KeyTopLeft[note] + gap / 2;
            double keyW = _layout.KeyTopRight[note] - _layout.KeyTopLeft[note] - gap;

            // Depression: shift top down, reduce height
            double blackTopY = isActive ? pianoY + blackKeyH * 0.04 : pianoY;
            double blackH = isActive ? blackKeyH * 0.96 : blackKeyH;

            var keyRect = new Rect(x, blackTopY, keyW, blackH);
            ctx.DrawRectangle(keyBrush, null, keyRect, 0, 2);
        }
    }

    private static LinearGradientBrush MakeWhiteKeyGradient(Color baseColor)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.3, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ColorHelper.Lighten(baseColor, 0.08), 0.0),
                new GradientStop(baseColor, 0.7),
                new GradientStop(ColorHelper.Darken(baseColor, 0.10), 1.0),
            }
        };
    }

    private static LinearGradientBrush MakeBlackKeyGradient(Color baseColor)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ColorHelper.Lighten(baseColor, 0.25), 0.0),
                new GradientStop(ColorHelper.Lighten(baseColor, 0.08), 0.12),
                new GradientStop(baseColor, 0.35),
                new GradientStop(ColorHelper.Darken(baseColor, 0.05), 1.0),
            }
        };
    }
}
