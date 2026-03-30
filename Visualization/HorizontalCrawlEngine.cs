using Avalonia;
using Avalonia.Media;
using Singularidi.Midi;
using Singularidi.Themes;

namespace Singularidi.Visualization;

public sealed class HorizontalCrawlEngine : IVisualizationEngine
{
    public string Name => "Horizontal Crawl";

    private readonly PianoLayout _layout = new();

    private IBrush _backgroundBrush = null!;
    private IPen _guidePen = null!;
    private IBrush _whiteKeyBrush = null!;
    private IBrush _blackKeyBrush = null!;
    private IPen _whiteKeyBorderPen = null!;
    private Color[] _channelColors = null!;
    private Color[] _trackColors = null!;
    private NoteColorMode _colorMode;
    private Dictionary<int, Color>? _noteColorOverrides;
    private Dictionary<int, Color>? _keyColorOverrides;
    private IVisualTheme? _cachedTheme;

    // ── Configurable layout ─────────────────────────────────────────────

    /// <summary>Fraction of vertical space above the horizon (sky). Default 0.20 (20%).</summary>
    public double SkyFraction { get; set; } = 0.20;

    /// <summary>Fraction of the road (from piano to vanishing point) at which guide lines stop.
    /// 1.0 = lines reach the vanishing point; 0.85 = lines stop at 85% of the way. Default 0.90.</summary>
    public double HorizonDepth { get; set; } = 0.90;

    /// <summary>Fraction of the road that the piano keys occupy at the bottom. Default 0.10.</summary>
    public double PianoDepthFraction { get; set; } = 0.10;

    /// <summary>Power curve exponent for perspective acceleration. Values &gt; 1 make notes
    /// crawl slowly at the horizon and accelerate toward the viewer. Default 2.0.</summary>
    public double PerspectivePower { get; set; } = 2.0;

    // ── Derived layout values (recalculated per frame from the configurables) ──
    // vanishX, vanishY = the perspective vanishing point
    // roadBottom = Y coordinate of the bottom of the road (top of screen = 0)
    // These are set at the start of Render().

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
        _trackColors = theme.TrackColors;
        _noteColorOverrides = theme.NoteColorOverrides;
        _keyColorOverrides = theme.KeyColorOverrides;
    }

    /// <summary>
    /// Projects a point onto the road surface using simple linear interpolation
    /// along the line from (noteX, roadBottom) to (vanishX, vanishY).
    /// depth=0 → at the piano (bottom), depth=1 → at the vanishing point.
    /// This guarantees notes lie exactly on their guide lines.
    /// </summary>
    private static (double x, double y) Project(
        double noteX, double depth,
        double vanishX, double vanishY, double roadBottom)
    {
        double d = Math.Clamp(depth, 0, 1);
        double x = noteX + (vanishX - noteX) * d;
        double y = roadBottom + (vanishY - roadBottom) * d;
        return (x, y);
    }

    /// <summary>Width scale factor at a given depth. 1.0 at bottom, 0.0 at vanishing point.</summary>
    private static double WidthScale(double depth) => 1.0 - Math.Clamp(depth, 0, 1);

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

        // Layout: sky occupies the top SkyFraction, road occupies the rest
        double vanishX = w / 2;
        double vanishY = h * SkyFraction; // vanishing point at the horizon
        double roadBottom = h;            // road extends to the very bottom

        // 1. Background
        ctx.DrawRectangle(_backgroundBrush, null, new Rect(0, 0, w, h));

        // 2. Perspective guide lines — one per note, from road bottom to the horizon line
        for (int note = 0; note < 128; note++)
        {
            double noteX = _layout.XCenter[note];
            var (hx, hy) = Project(noteX, HorizonDepth, vanishX, vanishY, roadBottom);
            ctx.DrawLine(_guidePen, new Point(noteX, roadBottom), new Point(hx, hy));
        }

        // 3. Notes on the road — collect visible, sort back-to-front
        //
        // Depth mapping: the "strike line" is at PianoDepthFraction (the far edge of the piano).
        // A note hits the strike line exactly when StartSeconds == now, then continues
        // through the piano area toward depth=0 as it sustains.
        //   depth = PianoDepthFraction + (timeAhead / LookAheadSeconds) * (HorizonDepth - PianoDepthFraction)
        // So: timeAhead=0 → depth=PianoDepthFraction (strike), timeAhead=LookAhead → depth=HorizonDepth (horizon)
        double now = currentTimeSeconds;
        double roadRange = HorizonDepth - PianoDepthFraction; // depth range from strike line to horizon
        var visibleNotes = new List<(NoteEvent note, double depthNear, double depthFar)>();
        foreach (var note in notes)
        {
            if (note.StartSeconds - now > PianoLayout.LookAheadSeconds) break;
            if (note.EndSeconds < now - 0.5) continue;

            // Apply power curve: t^PerspectivePower makes notes slow at horizon, fast near piano
            double tNear = Math.Clamp((note.StartSeconds - now) / PianoLayout.LookAheadSeconds, 0, 1);
            double tFar = Math.Clamp((note.EndSeconds - now) / PianoLayout.LookAheadSeconds, 0, 1);
            double depthNear = PianoDepthFraction + Math.Pow(tNear, 1.0 / PerspectivePower) * roadRange;
            double depthFar = PianoDepthFraction + Math.Pow(tFar, 1.0 / PerspectivePower) * roadRange;

            // For notes currently playing (past the strike line), use linear depth into the piano
            if (note.StartSeconds < now)
                depthNear = PianoDepthFraction + (note.StartSeconds - now) / PianoLayout.LookAheadSeconds * roadRange;
            if (note.EndSeconds < now)
                depthFar = PianoDepthFraction + (note.EndSeconds - now) / PianoLayout.LookAheadSeconds * roadRange;

            if (depthFar < 0) continue; // fully past the piano
            visibleNotes.Add((note, depthNear, depthFar));
        }

        // Sort back-to-front (farthest near-edge first)
        visibleNotes.Sort((a, b) => b.depthNear.CompareTo(a.depthNear));

        foreach (var (note, rawDepthNear, rawDepthFar) in visibleNotes)
        {
            // Clamp: notes appear at horizon, travel to piano, then through piano to depth=0
            double dNear = Math.Clamp(rawDepthNear, 0, HorizonDepth);
            double dFar = Math.Clamp(rawDepthFar, 0, HorizonDepth);
            if (Math.Abs(dNear - dFar) < 0.001) dFar = Math.Min(dNear + 0.005, HorizonDepth);

            double noteX = _layout.XCenter[note.NoteNumber];
            double nw = _layout.NoteWidth[note.NoteNumber];

            // Project near and far edges — these lie exactly on the guide line for noteX
            var (cxNear, cyNear) = Project(noteX, dNear, vanishX, vanishY, roadBottom);
            var (cxFar, cyFar) = Project(noteX, dFar, vanishX, vanishY, roadBottom);

            double halfWNear = nw * WidthScale(dNear) / 2;
            double halfWFar = nw * WidthScale(dFar) / 2;

            // Note color
            Color baseColor = ColorHelper.ResolveNoteColor(note, _colorMode, _channelColors, _trackColors, _noteColorOverrides);

            bool isActive = highlightActiveNotes && (
                _colorMode == NoteColorMode.Track
                    ? activeKeyTrack[note.NoteNumber] == note.Track
                    : activeKeyChannel[note.NoteNumber] == note.Channel);
            var fillColor = isActive
                ? ColorHelper.LerpToColor(baseColor, theme.ActiveHighlightColor, theme.ActiveNoteBlend)
                : baseColor;

            // Alpha fade for distant notes (normalized to road range: strike line → horizon)
            double fadeT = Math.Clamp((dNear - PianoDepthFraction) / roadRange, 0, 1);
            byte alpha = (byte)(255 * (1.0 - fadeT * 0.6));
            fillColor = Color.FromArgb(alpha, fillColor.R, fillColor.G, fillColor.B);

            var brush = new SolidColorBrush(fillColor);

            // Draw trapezoid: near edge (bottom, wider) → far edge (top, narrower)
            var geo = new StreamGeometry();
            using (var sgCtx = geo.Open())
            {
                sgCtx.BeginFigure(new Point(cxNear - halfWNear, cyNear), true);
                sgCtx.LineTo(new Point(cxNear + halfWNear, cyNear));
                sgCtx.LineTo(new Point(cxFar + halfWFar, cyFar));
                sgCtx.LineTo(new Point(cxFar - halfWFar, cyFar));
                sgCtx.EndFigure(true);
            }
            ctx.DrawGeometry(brush, null, geo);
        }

        // 4. Piano keys — perspective trapezoids lying flat on the road
        DrawPerspectivePiano(ctx, theme, vanishX, vanishY, roadBottom, activeKeyChannel, activeKeyTrack);
    }

    private void DrawPerspectivePiano(
        DrawingContext ctx,
        IVisualTheme theme,
        double vanishX,
        double vanishY,
        double roadBottom,
        int[] activeKeyChannel,
        int[] activeKeyTrack)
    {
        // White keys span from the very bottom (dNear=0) to PianoDepthFraction.
        // Black keys sit at the far end of white keys (further from viewer).
        double whiteNear = 0;
        double whiteFar = PianoDepthFraction;
        double blackNear = PianoDepthFraction * 0.40; // start partway into the white key
        double blackFar = PianoDepthFraction;          // end at the same far edge

        // White keys first (drawn underneath)
        for (int note = 0; note < 128; note++)
        {
            if (PianoLayout.IsBlackKey[note % 12]) continue;

            double noteX = _layout.XCenter[note];
            IBrush keyBrush = ResolveKeyBrush(note, false, theme, activeKeyChannel, activeKeyTrack);
            DrawPerspectiveKey(ctx, noteX, _layout.WhiteKeyWidth, whiteNear, whiteFar,
                vanishX, vanishY, roadBottom, keyBrush, _whiteKeyBorderPen);
        }

        // Black keys on top — at the far portion of the piano (further from viewer)
        for (int note = 0; note < 128; note++)
        {
            if (!PianoLayout.IsBlackKey[note % 12]) continue;

            double noteX = _layout.XCenter[note];
            IBrush keyBrush = ResolveKeyBrush(note, true, theme, activeKeyChannel, activeKeyTrack);
            DrawPerspectiveKey(ctx, noteX, _layout.BlackKeyWidth, blackNear, blackFar,
                vanishX, vanishY, roadBottom, keyBrush, null);
        }
    }

    private void DrawPerspectiveKey(
        DrawingContext ctx,
        double noteX,
        double keyWidth,
        double dNear,
        double dFar,
        double vanishX,
        double vanishY,
        double roadBottom,
        IBrush brush,
        IPen? pen)
    {
        var (cxNear, cyNear) = Project(noteX, dNear, vanishX, vanishY, roadBottom);
        var (cxFar, cyFar) = Project(noteX, dFar, vanishX, vanishY, roadBottom);

        double halfWNear = keyWidth * WidthScale(dNear) / 2;
        double halfWFar = keyWidth * WidthScale(dFar) / 2;

        var geo = new StreamGeometry();
        using (var sgCtx = geo.Open())
        {
            sgCtx.BeginFigure(new Point(cxNear - halfWNear, cyNear), true);
            sgCtx.LineTo(new Point(cxNear + halfWNear, cyNear));
            sgCtx.LineTo(new Point(cxFar + halfWFar, cyFar));
            sgCtx.LineTo(new Point(cxFar - halfWFar, cyFar));
            sgCtx.EndFigure(true);
        }
        ctx.DrawGeometry(brush, pen, geo);
    }

    private IBrush ResolveKeyBrush(
        int note,
        bool isBlack,
        IVisualTheme theme,
        int[] activeKeyChannel,
        int[] activeKeyTrack)
    {
        int channel = activeKeyChannel[note];
        if (channel >= 0)
        {
            float blend = isBlack ? theme.ActiveBlackKeyBlend : theme.ActiveWhiteKeyBlend;
            var activeColor = ColorHelper.ResolveActiveKeyColor(
                note, activeKeyChannel, activeKeyTrack,
                _colorMode, _channelColors, _trackColors,
                theme.ActiveHighlightColor, blend);
            return new SolidColorBrush(activeColor);
        }

        if (_keyColorOverrides != null && _keyColorOverrides.TryGetValue(note, out var keyOverride))
            return new SolidColorBrush(keyOverride);

        return isBlack ? _blackKeyBrush : _whiteKeyBrush;
    }
}
