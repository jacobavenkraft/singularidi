using System.Numerics;
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
    private readonly Piano3DRenderer _pianoRenderer = new();

    // Cached brushes/pens rebuilt when theme changes
    private IBrush _backgroundBrush = null!;
    private IPen _guidePen = null!;
    private SolidColorBrush[] _channelBrushes = null!;
    private Color[] _channelColors = null!;
    private Color[] _trackColors = null!;
    private NoteColorMode _colorMode;
    private Dictionary<int, Color>? _noteColorOverrides;
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
        _colorMode = theme.ColorMode;
        _channelColors = theme.ChannelColors;
        _channelBrushes = _channelColors.Select(c => new SolidColorBrush(c)).ToArray();
        _trackColors = theme.TrackColors;
        _noteColorOverrides = theme.NoteColorOverrides;
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

        // 4. Piano keyboard — 3D rendered (top-down projection)
        double pianoY = vizHeight;

        _pianoRenderer.ProjectionMode = PianoProjectionMode.TopDown;
        _pianoRenderer.TopDown_PianoY = pianoY;
        _pianoRenderer.TopDown_PianoHeight = pianoHeight;
        _pianoRenderer.TopDown_HeightScale = 0.6;  // world Y (pixels) → screen Y offset (subtle in top-down)
        _pianoRenderer.LightDirection = Vector3.Normalize(new Vector3(-0.4f, 1.0f, -0.6f));
        _pianoRenderer.AmbientIntensity = 0.4f;
        _pianoRenderer.WhitePivotAngle = 0.02f;
        _pianoRenderer.BlackPivotAngle = 0.035f;

        _pianoRenderer.Render(ctx, _layout, theme, activeKeyChannel, activeKeyTrack);
    }
}
