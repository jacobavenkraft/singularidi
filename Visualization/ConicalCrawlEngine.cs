using Avalonia;
using Avalonia.Media;
using Singularidi.Midi;
using Singularidi.Themes;

namespace Singularidi.Visualization;

public sealed class ConicalCrawlEngine : IVisualizationEngine
{
    public string Name => "Conical Crawl";

    private readonly CircularPianoLayout _circLayout = new();

    private IBrush _backgroundBrush = null!;
    private IPen _guidePen = null!;
    private Color[] _channelColors = null!;
    private Color[] _trackColors = null!;
    private NoteColorMode _colorMode;
    private Dictionary<int, Color>? _noteColorOverrides;
    private IVisualTheme? _cachedTheme;

    private const double LookAheadSeconds = 4.0;

    public void OnSizeChanged(double width, double height)
    {
        _circLayout.RebuildIfNeeded(width, height);
    }

    private void EnsureThemeCaches(IVisualTheme theme)
    {
        if (ReferenceEquals(theme, _cachedTheme)) return;
        _cachedTheme = theme;
        _backgroundBrush = new SolidColorBrush(theme.BackgroundColor);
        _guidePen = new Pen(new SolidColorBrush(theme.GuideLineColor), 1) { DashStyle = DashStyle.Dot };
        _colorMode = theme.ColorMode;
        _channelColors = theme.ChannelColors;
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
        _circLayout.RebuildIfNeeded(w, h);
        EnsureThemeCaches(theme);

        double vanishX = _circLayout.CenterX;
        double vanishY = _circLayout.CenterY;

        // 1. Background
        ctx.DrawRectangle(_backgroundBrush, null, new Rect(0, 0, w, h));

        // 2. Radial guide lines from center to every 4th key
        for (int note = 0; note < 128; note += 4)
        {
            ctx.DrawLine(_guidePen,
                new Point(vanishX, vanishY),
                new Point(_circLayout.X[note], _circLayout.Y[note]));
        }

        // 3. Notes — collect visible, sort back-to-front
        double now = currentTimeSeconds;
        var visibleNotes = new List<(NoteEvent note, double depth)>();
        foreach (var note in notes)
        {
            if (note.StartSeconds - now > LookAheadSeconds) break;
            if (note.EndSeconds < now - 0.1) continue;

            double depth = (note.StartSeconds - now) / LookAheadSeconds;
            if (depth < -0.025) continue;
            visibleNotes.Add((note, depth));
        }

        visibleNotes.Sort((a, b) => b.depth.CompareTo(a.depth));

        foreach (var (note, rawDepth) in visibleNotes)
        {
            double depth = Math.Clamp(rawDepth, 0, 1);

            // Scale: 0 at apex (depth=1), 1 at rim (depth=0)
            double scale = 1.0 - depth;

            // Rim position for this note's key
            double rimX = _circLayout.X[note.NoteNumber];
            double rimY = _circLayout.Y[note.NoteNumber];

            // Interpolate from center to rim
            double projX = vanishX + (rimX - vanishX) * scale;
            double projY = vanishY + (rimY - vanishY) * scale;

            // Note size scales with distance from center
            double baseSize = _circLayout.KeySize[note.NoteNumber];
            double noteSize = Math.Max(baseSize * scale, 2);

            // Duration affects the note size slightly (longer notes = slightly larger)
            double durationFactor = Math.Clamp((note.EndSeconds - note.StartSeconds) / 0.5, 0.5, 2.0);
            double noteW = noteSize * durationFactor;
            double noteH = noteSize;

            // Note color
            Color baseColor = ColorHelper.ResolveNoteColor(note, _colorMode, _channelColors, _trackColors, _noteColorOverrides);

            bool isActive = highlightActiveNotes && (
                _colorMode == NoteColorMode.Track
                    ? activeKeyTrack[note.NoteNumber] == note.Track
                    : activeKeyChannel[note.NoteNumber] == note.Channel);
            var fillColor = isActive
                ? ColorHelper.LerpToColor(baseColor, theme.ActiveHighlightColor, theme.ActiveNoteBlend)
                : baseColor;

            // Alpha fade toward center
            byte alpha = (byte)(255 * Math.Clamp(scale, 0.1, 1.0));
            fillColor = Color.FromArgb(alpha, fillColor.R, fillColor.G, fillColor.B);

            var brush = new SolidColorBrush(fillColor);

            // Draw as ellipse for cone aesthetic
            if (theme.NoteShape == NoteShape.DotBlock)
            {
                ctx.DrawEllipse(brush, null, new Point(projX, projY), noteW / 2, noteH / 2);
            }
            else
            {
                var rect = new Rect(projX - noteW / 2, projY - noteH / 2, noteW, noteH);
                ctx.DrawRectangle(brush, null, rect, 3 * scale, 3 * scale);
            }
        }

        // 4. Circular piano keys
        DrawCircularPiano(ctx, theme, activeKeyChannel, activeKeyTrack);
    }

    private void DrawCircularPiano(
        DrawingContext ctx,
        IVisualTheme theme,
        int[] activeKeyChannel,
        int[] activeKeyTrack)
    {
        // Draw white keys first (outer ring), then black keys (inner ring)
        for (int pass = 0; pass < 2; pass++)
        {
            bool drawBlack = pass == 1;
            for (int note = 0; note < 128; note++)
            {
                if (PianoLayout.IsBlackKey[note % 12] != drawBlack) continue;

                double kx = _circLayout.X[note];
                double ky = _circLayout.Y[note];
                double ks = _circLayout.KeySize[note];

                int channel = activeKeyChannel[note];
                Color keyColor;
                if (channel >= 0)
                {
                    float blend = drawBlack ? theme.ActiveBlackKeyBlend : theme.ActiveWhiteKeyBlend;
                    keyColor = ColorHelper.ResolveActiveKeyColor(
                        note, activeKeyChannel, activeKeyTrack,
                        _colorMode, _channelColors, _trackColors,
                        theme.ActiveHighlightColor, blend);
                }
                else
                {
                    keyColor = drawBlack ? theme.BlackKeyColor : theme.WhiteKeyColor;
                }

                var brush = new SolidColorBrush(keyColor);
                var pen = drawBlack ? null : new Pen(Brushes.Black, 0.5);

                // Draw key as a small rounded rectangle at its circle position
                var rect = new Rect(kx - ks / 2, ky - ks / 2, ks, ks);
                ctx.DrawRectangle(brush, pen, rect, ks * 0.2, ks * 0.2);
            }
        }
    }
}
