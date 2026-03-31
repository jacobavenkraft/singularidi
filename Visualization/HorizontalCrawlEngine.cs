using Avalonia;
using Avalonia.Media;
using Singularidi.Midi;
using Singularidi.Themes;

namespace Singularidi.Visualization;

/// <summary>
/// Guitar Hero-style 3D perspective visualization.
///
/// Uses true 1/z perspective projection. Notes exist on a flat ground plane in world space
/// and travel at constant world-space velocity toward the camera. The 1/z divide naturally
/// produces correct perspective acceleration (slow at horizon, fast near camera).
///
/// Conceptually identical to the VerticalFall view but with the camera tilted from
/// top-down to an angled view looking down the road toward the horizon.
/// </summary>
public sealed class HorizontalCrawlEngine : IVisualizationEngine
{
    public string Name => "Horizontal Crawl";

    public GuideLineStyle GuideLineStyle { get; set; } = GuideLineStyle.KeyWidthCentered;

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

    // 3D piano key cached brushes
    private LinearGradientBrush _whiteKeyTopGradient = null!;
    private LinearGradientBrush _blackKeyTopGradient = null!;
    private SolidColorBrush _whiteKeyFrontBrush = null!;
    private SolidColorBrush _blackKeyFrontBrush = null!;

    // ── Configurable layout ─────────────────────────────────────────────

    /// <summary>Fraction of vertical space above the vanishing point (sky). Default 0.20 (20%).</summary>
    public double SkyFraction { get; set; } = 0.20;

    /// <summary>Ratio of the horizon distance to the piano distance in world space.
    /// Higher values = more dramatic perspective convergence. Default 12.0.</summary>
    public double DepthRatio { get; set; } = 12.0;

    /// <summary>Desired fraction of screen height the piano keys occupy. Default 0.12 (12%).
    /// The world-space Z for the piano's far edge is back-computed so the keys take up
    /// exactly this much vertical screen space regardless of DepthRatio.</summary>
    public double PianoScreenFraction { get; set; } = 0.12;

    /// <summary>Controls how aggressively guide lines fade to prevent moiré.
    /// Range 0.0–1.0. At 0.0 no fading is applied (all lines fully visible).
    /// At 1.0 maximum fading — lines disappear early. Default 0.5.</summary>
    public double GuideLineFade { get; set; } = 0.9;

    // ── 3D projection constants ─────────────────────────────────────────
    // World space: Z = distance from camera along viewing axis.
    //   Znear = piano distance from camera (fixed at 1.0)
    //   Zhorizon = Znear * DepthRatio (where guide lines end / notes appear)
    // Notes move at constant velocity in world Z, from Zhorizon toward Znear.
    // Screen projection uses 1/z: screenPos = vanish + (worldX - vanish) * Znear / worldZ

    private const double Znear = 1.0;

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

        // 3D piano key brushes
        _whiteKeyTopGradient = MakeWhiteKeyTopGradient(theme.WhiteKeyColor);
        _blackKeyTopGradient = MakeBlackKeyTopGradient(theme.BlackKeyColor);
        _whiteKeyFrontBrush = new SolidColorBrush(ColorHelper.Darken(theme.WhiteKeyColor, 0.15));
        _blackKeyFrontBrush = new SolidColorBrush(ColorHelper.Darken(theme.BlackKeyColor, 0.20));
    }

    /// <summary>
    /// Projects a world-space point at (worldX, worldZ) onto the screen using 1/z perspective.
    /// worldX is the note's horizontal position (same as PianoLayout.XCenter).
    /// worldZ is the distance from the camera (Znear = at the piano, larger = further away).
    /// </summary>
    private static (double screenX, double screenY) Project3D(
        double worldX, double worldZ,
        double vanishX, double vanishY, double roadBottom)
    {
        double zClamped = Math.Max(worldZ, 0.001); // avoid division by zero
        double scale = Znear / zClamped;           // 1/z perspective divide
        double screenX = vanishX + (worldX - vanishX) * scale;
        double screenY = vanishY + (roadBottom - vanishY) * scale;
        return (screenX, screenY);
    }

    /// <summary>Width scale at a given worldZ. Full width at Znear, shrinks with distance.</summary>
    private static double WidthScale3D(double worldZ) => Znear / Math.Max(worldZ, 0.001);

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

        double vanishX = w / 2;
        double vanishY = h * SkyFraction;
        double roadBottom = h;

        double Zhorizon = Znear * DepthRatio;

        // Back-compute Zpiano so the piano occupies exactly PianoScreenFraction of screen height.
        // Screen Y at Zpiano = vanishY + roadHeight * (Znear / Zpiano)
        // Piano screen height = roadBottom - screenY(Zpiano) = roadHeight * (1 - Znear / Zpiano)
        // We want: roadHeight * (1 - Znear / Zpiano) = PianoScreenFraction * h
        // => Znear / Zpiano = 1 - PianoScreenFraction / (1 - SkyFraction)
        // => Zpiano = Znear / (1 - PianoScreenFraction / (1 - SkyFraction))
        double roadFraction = 1.0 - SkyFraction;
        double Zpiano = Znear / (1.0 - PianoScreenFraction / roadFraction);
        Zpiano = Math.Clamp(Zpiano, Znear + 0.01, Zhorizon * 0.5); // safety clamp

        // 1. Background
        ctx.DrawRectangle(_backgroundBrush, null, new Rect(0, 0, w, h));

        // 2. Perspective guide lines
        DrawPerspectiveGuideLines(ctx, vanishX, vanishY, roadBottom, Znear, Zhorizon);

        // 3. Notes on the road
        //
        // Time-to-Z mapping (linear in world space, constant velocity):
        //   worldZ = Zpiano + (timeAhead / LookAheadSeconds) * (Zhorizon - Zpiano)
        // At timeAhead=0: worldZ = Zpiano (note hits the strike line / far edge of piano)
        // At timeAhead=LookAhead: worldZ = Zhorizon (note appears at the horizon)
        // For sustaining notes (timeAhead < 0): worldZ < Zpiano (inside the piano area)
        double now = currentTimeSeconds;
        double noteZrange = Zhorizon - Zpiano; // world Z range for the note runway

        var visibleNotes = new List<(NoteEvent note, double zNear, double zFar)>();
        foreach (var note in notes)
        {
            if (note.StartSeconds - now > PianoLayout.LookAheadSeconds) break;
            if (note.EndSeconds < now - 0.5) continue;

            double tNear = (note.StartSeconds - now) / PianoLayout.LookAheadSeconds;
            double tFar = (note.EndSeconds - now) / PianoLayout.LookAheadSeconds;

            double zNear = Zpiano + tNear * noteZrange;
            double zFar = Zpiano + tFar * noteZrange;

            if (zFar < Znear * 0.5) continue; // fully past the piano
            visibleNotes.Add((note, zNear, zFar));
        }

        // Sort back-to-front (farthest first)
        visibleNotes.Sort((a, b) => b.zNear.CompareTo(a.zNear));

        foreach (var (note, rawZnear, rawZfar) in visibleNotes)
        {
            // Clamp to visible range
            double zN = Math.Clamp(rawZnear, Znear, Zhorizon);
            double zF = Math.Clamp(rawZfar, Znear, Zhorizon);
            if (Math.Abs(zN - zF) < 0.001) zF = Math.Min(zN + 0.01, Zhorizon);

            double noteX = _layout.XCenter[note.NoteNumber];
            double nw = _layout.NoteWidth[note.NoteNumber];

            // Project both edges — 1/z guarantees they lie on the guide line
            var (cxNear, cyNear) = Project3D(noteX, zN, vanishX, vanishY, roadBottom);
            var (cxFar, cyFar) = Project3D(noteX, zF, vanishX, vanishY, roadBottom);

            double halfWNear = nw * WidthScale3D(zN) / 2;
            double halfWFar = nw * WidthScale3D(zF) / 2;

            // Note color
            Color baseColor = ColorHelper.ResolveNoteColor(note, _colorMode, _channelColors, _trackColors, _noteColorOverrides);

            bool isActive = highlightActiveNotes && (
                _colorMode == NoteColorMode.Track
                    ? activeKeyTrack[note.NoteNumber] == note.Track
                    : activeKeyChannel[note.NoteNumber] == note.Channel);
            var fillColor = isActive
                ? ColorHelper.LerpToColor(baseColor, theme.ActiveHighlightColor, theme.ActiveNoteBlend)
                : baseColor;

            // Alpha fade based on world distance (normalized 0–1 over the runway)
            double fadeT = Math.Clamp((zN - Zpiano) / noteZrange, 0, 1);
            byte alpha = (byte)(255 * (1.0 - fadeT * 0.6));
            fillColor = Color.FromArgb(alpha, fillColor.R, fillColor.G, fillColor.B);

            var brush = new SolidColorBrush(fillColor);

            // Draw trapezoid (with optional corner radius for Rectangular shape)
            double cr = theme.NoteShape == NoteShape.Rectangular
                ? theme.NoteCornerRadius : 0;

            var geo = new StreamGeometry();
            using (var sgCtx = geo.Open())
            {
                if (cr <= 0.5 || theme.NoteShape == NoteShape.DotBlock)
                {
                    // Sharp corners
                    sgCtx.BeginFigure(new Point(cxNear - halfWNear, cyNear), true);
                    sgCtx.LineTo(new Point(cxNear + halfWNear, cyNear));
                    sgCtx.LineTo(new Point(cxFar + halfWFar, cyFar));
                    sgCtx.LineTo(new Point(cxFar - halfWFar, cyFar));
                    sgCtx.EndFigure(true);
                }
                else
                {
                    // Rounded corners — radius scaled by perspective at each end
                    double scaleNear = WidthScale3D(zN);
                    double scaleFar = WidthScale3D(zF);
                    double rNear = Math.Min(cr * scaleNear, halfWNear);
                    double rFar = Math.Min(cr * scaleFar, halfWFar);

                    // Near edge (bottom, closest): left-bottom to right-bottom
                    double nL = cxNear - halfWNear, nR = cxNear + halfWNear;
                    // Far edge (top, farthest): left-top to right-top
                    double fL = cxFar - halfWFar, fR = cxFar + halfWFar;
                    double nY = cyNear, fY = cyFar;
                    double nearH = Math.Abs(nY - fY);
                    rNear = Math.Min(rNear, nearH / 2);
                    rFar = Math.Min(rFar, nearH / 2);

                    var sizeNear = new Size(rNear, rNear);
                    var sizeFar = new Size(rFar, rFar);

                    // Start at near-left + rNear offset, go clockwise
                    sgCtx.BeginFigure(new Point(nL + rNear, nY), true);
                    sgCtx.LineTo(new Point(nR - rNear, nY));
                    sgCtx.ArcTo(new Point(nR, nY - rNear), sizeNear, 0, false, SweepDirection.CounterClockwise);
                    sgCtx.LineTo(new Point(fR, fY + rFar));
                    sgCtx.ArcTo(new Point(fR - rFar, fY), sizeFar, 0, false, SweepDirection.CounterClockwise);
                    sgCtx.LineTo(new Point(fL + rFar, fY));
                    sgCtx.ArcTo(new Point(fL, fY + rFar), sizeFar, 0, false, SweepDirection.CounterClockwise);
                    sgCtx.LineTo(new Point(nL, nY - rNear));
                    sgCtx.ArcTo(new Point(nL + rNear, nY), sizeNear, 0, false, SweepDirection.CounterClockwise);
                    sgCtx.EndFigure(true);
                }
            }
            ctx.DrawGeometry(brush, null, geo);
        }

        // 4. Piano keys — perspective trapezoids
        DrawPerspectivePiano(ctx, theme, vanishX, vanishY, roadBottom, Zpiano, activeKeyChannel, activeKeyTrack);
    }

    private void DrawPerspectivePiano(
        DrawingContext ctx,
        IVisualTheme theme,
        double vanishX,
        double vanishY,
        double roadBottom,
        double Zpiano,
        int[] activeKeyChannel,
        int[] activeKeyTrack)
    {
        double roadH = roadBottom - vanishY;

        // Block heights (screen space at Znear for white, blackZnear for black)
        double whiteFrontH = roadH * 0.020;    // white key block height
        double blackFrontH = roadH * 0.014;    // black key elevation ABOVE white key
        double blackTaper = roadH * 0.002;

        // surfaceBottom = where white key top surface projects at Znear (above roadBottom)
        double surfaceBottom = roadBottom - whiteFrontH;

        // Black key near Z (40% from near to far)
        double blackZnear = Znear + (Zpiano - Znear) * 0.40;
        double scaleRatio = WidthScale3D(Zpiano) / WidthScale3D(blackZnear);

        // Black key elevation at near and far edges (perspective-scaled)
        double blackElevNear = blackFrontH;
        double blackElevFar = blackFrontH * scaleRatio;

        // ── Pivot model ──
        // Keys pivot at the far end (Zpiano). When pressed:
        //   sinkAtZ = maxSink * (Zpiano - Z) / (Zpiano - keyNearZ)
        // i.e., full sink at near edge, zero at far edge.
        double whiteMaxSink = whiteFrontH * 0.65;  // near edge sinks 65% of white block height
        // Black key pressed: front sinks to white key surface level (full elevation gone)
        // but back barely sinks.

        // === Pass 1: White key front faces ===
        for (int note = 0; note < 128; note++)
        {
            if (PianoLayout.IsBlackKey[note % 12]) continue;
            bool isActive = activeKeyChannel[note] >= 0;
            double noteX = _layout.XCenter[note];
            double kw = _layout.NoteWidth[note];
            var (cx, _) = Project3D(noteX, Znear, vanishX, vanishY, surfaceBottom);
            double halfW = kw * WidthScale3D(Znear) / 2;

            // Pivot: near edge sinks most, far edge doesn't sink
            double sinkNear = isActive ? whiteMaxSink : 0;
            double frontTop = surfaceBottom + sinkNear;
            double frontBot = roadBottom;

            IBrush frontBrush;
            if (isActive)
            {
                var activeColor = ResolveActiveKeyColor(note, theme, activeKeyChannel, activeKeyTrack, false);
                frontBrush = new SolidColorBrush(ColorHelper.Darken(activeColor, 0.15));
            }
            else
            {
                frontBrush = _whiteKeyFrontBrush;
            }

            var geo = new StreamGeometry();
            using (var sgCtx = geo.Open())
            {
                sgCtx.BeginFigure(new Point(cx - halfW, frontTop), true);
                sgCtx.LineTo(new Point(cx + halfW, frontTop));
                sgCtx.LineTo(new Point(cx + halfW, frontBot));
                sgCtx.LineTo(new Point(cx - halfW, frontBot));
                sgCtx.EndFigure(true);
            }
            ctx.DrawGeometry(frontBrush, _whiteKeyBorderPen, geo);
        }

        // === Pass 2: White key top surfaces (pivot tilt) ===
        for (int note = 0; note < 128; note++)
        {
            if (PianoLayout.IsBlackKey[note % 12]) continue;
            bool isActive = activeKeyChannel[note] >= 0;
            double noteX = _layout.XCenter[note];
            double kw = _layout.NoteWidth[note];

            // Pivot: sinkNear at front, sinkFar ≈ 0 at back
            double sinkNear = isActive ? whiteMaxSink : 0;
            double sinkFar = isActive ? whiteMaxSink * 0.05 : 0;

            IBrush topBrush;
            if (isActive)
            {
                var activeColor = ResolveActiveKeyColor(note, theme, activeKeyChannel, activeKeyTrack, false);
                topBrush = MakeWhiteKeyTopGradient(activeColor);
            }
            else if (_keyColorOverrides != null && _keyColorOverrides.TryGetValue(note, out var ov))
            {
                topBrush = MakeWhiteKeyTopGradient(ov);
            }
            else
            {
                topBrush = _whiteKeyTopGradient;
            }

            DrawPerspectiveKey(ctx, noteX, kw, Znear, Zpiano,
                vanishX, vanishY, surfaceBottom, topBrush, _whiteKeyBorderPen,
                sinkNear, sinkFar);
        }

        // === Pass 3: Shadow overlays — light from left, shadows fall to the RIGHT ===
        // Shadows elongate when the receiving white key is pressed (surface drops, more black key exposed)
        for (int note = 0; note < 128; note++)
        {
            if (PianoLayout.IsBlackKey[note % 12]) continue;
            if (note > 0 && PianoLayout.IsBlackKey[(note - 1) % 12])
            {
                bool whiteIsActive = activeKeyChannel[note] >= 0;
                // Wider shadow when white key is pressed (more black key block exposed)
                double shadowFrac = whiteIsActive ? 0.50 : 0.35;
                byte shadowAlpha = whiteIsActive ? (byte)50 : (byte)35;
                var sBrush = new SolidColorBrush(Color.FromArgb(shadowAlpha, 0, 0, 0));
                DrawShadowOverlay(ctx, noteX: _layout.XCenter[note],
                    keyWidth: _layout.NoteWidth[note],
                    zNear: blackZnear, zFar: Zpiano,
                    vanishX, vanishY, surfaceBottom, sBrush, shadowFrac);
            }
        }

        // === Pass 4: Black key front faces ===
        // Top = elevated surface. Base = white key surface.
        // Pressed: front sinks to white key level (elev → 0), pivots at back.
        for (int note = 0; note < 128; note++)
        {
            if (!PianoLayout.IsBlackKey[note % 12]) continue;
            bool isActive = activeKeyChannel[note] >= 0;
            double noteX = _layout.XCenter[note];
            double kw = _layout.NoteWidth[note];
            var (cx, cy) = Project3D(noteX, blackZnear, vanishX, vanishY, surfaceBottom);
            double halfW = kw * WidthScale3D(blackZnear) / 2;

            // Pressed: front drops to white key level (zero elevation at near edge)
            double elevAtFront = isActive ? 0 : blackElevNear;
            double taper = blackTaper * (elevAtFront / Math.Max(blackElevNear, 0.001));

            if (elevAtFront < 0.5) continue; // front face invisible when pressed

            double frontTop = cy - elevAtFront;
            double frontBot = cy;

            IBrush frontBrush;
            if (isActive)
            {
                var activeColor = ResolveActiveKeyColor(note, theme, activeKeyChannel, activeKeyTrack, true);
                frontBrush = new SolidColorBrush(ColorHelper.Darken(activeColor, 0.20));
            }
            else
            {
                frontBrush = _blackKeyFrontBrush;
            }

            var geo = new StreamGeometry();
            using (var sgCtx = geo.Open())
            {
                sgCtx.BeginFigure(new Point(cx - halfW, frontTop), true);
                sgCtx.LineTo(new Point(cx + halfW, frontTop));
                sgCtx.LineTo(new Point(cx + halfW + taper, frontBot));
                sgCtx.LineTo(new Point(cx - halfW - taper, frontBot));
                sgCtx.EndFigure(true);
            }
            ctx.DrawGeometry(frontBrush, null, geo);
        }

        // === Pass 5: Black key side faces ===
        // Top = elevated surface (with pivot tilt when pressed).
        // Bottom = white key surface level.
        for (int note = 0; note < 128; note++)
        {
            if (!PianoLayout.IsBlackKey[note % 12]) continue;
            bool isActive = activeKeyChannel[note] >= 0;
            double noteX = _layout.XCenter[note];
            double kw = _layout.NoteWidth[note];

            var (cxNear, cyNear) = Project3D(noteX, blackZnear, vanishX, vanishY, surfaceBottom);
            var (cxFar, cyFar) = Project3D(noteX, Zpiano, vanishX, vanishY, surfaceBottom);
            double halfWNear = kw * WidthScale3D(blackZnear) / 2;
            double halfWFar = kw * WidthScale3D(Zpiano) / 2;

            // Pivot: near edge drops to white key level, far edge barely moves
            double elevNear = isActive ? 0 : blackElevNear;
            double elevFar = isActive ? blackElevFar * 0.85 : blackElevFar;
            double taperNear = blackTaper * (elevNear / Math.Max(blackElevNear, 0.001));
            double taperFar = blackTaper * scaleRatio;

            double topNearY = cyNear - elevNear;
            double topFarY = cyFar - elevFar;
            double botNearY = cyNear;
            double botFarY = cyFar;

            IBrush sideBrush;
            if (isActive)
            {
                var activeColor = ResolveActiveKeyColor(note, theme, activeKeyChannel, activeKeyTrack, true);
                sideBrush = new SolidColorBrush(ColorHelper.Darken(activeColor, 0.30));
            }
            else
            {
                sideBrush = new SolidColorBrush(ColorHelper.Darken(theme.BlackKeyColor, 0.30));
            }

            bool showRightSide = cxNear < vanishX;
            bool showLeftSide = cxNear > vanishX;

            if (showRightSide)
            {
                var geo = new StreamGeometry();
                using (var sgCtx = geo.Open())
                {
                    sgCtx.BeginFigure(new Point(cxNear + halfWNear, topNearY), true);
                    sgCtx.LineTo(new Point(cxFar + halfWFar, topFarY));
                    sgCtx.LineTo(new Point(cxFar + halfWFar + taperFar, botFarY));
                    sgCtx.LineTo(new Point(cxNear + halfWNear + taperNear, botNearY));
                    sgCtx.EndFigure(true);
                }
                ctx.DrawGeometry(sideBrush, null, geo);
            }

            if (showLeftSide)
            {
                var geo = new StreamGeometry();
                using (var sgCtx = geo.Open())
                {
                    sgCtx.BeginFigure(new Point(cxNear - halfWNear, topNearY), true);
                    sgCtx.LineTo(new Point(cxFar - halfWFar, topFarY));
                    sgCtx.LineTo(new Point(cxFar - halfWFar - taperFar, botFarY));
                    sgCtx.LineTo(new Point(cxNear - halfWNear - taperNear, botNearY));
                    sgCtx.EndFigure(true);
                }
                ctx.DrawGeometry(sideBrush, null, geo);
            }
        }

        // === Pass 6: Black key top surfaces (pivot tilt when pressed) ===
        for (int note = 0; note < 128; note++)
        {
            if (!PianoLayout.IsBlackKey[note % 12]) continue;
            bool isActive = activeKeyChannel[note] >= 0;
            double noteX = _layout.XCenter[note];
            double kw = _layout.NoteWidth[note];

            var (cxNear, cyNear) = Project3D(noteX, blackZnear, vanishX, vanishY, surfaceBottom);
            var (cxFar, cyFar) = Project3D(noteX, Zpiano, vanishX, vanishY, surfaceBottom);
            double halfWNear = kw * WidthScale3D(blackZnear) / 2;
            double halfWFar = kw * WidthScale3D(Zpiano) / 2;

            // Pivot: near drops to white level, far barely moves
            double elevNear = isActive ? 0 : blackElevNear;
            double elevFar = isActive ? blackElevFar * 0.85 : blackElevFar;

            double topNearY = cyNear - elevNear;
            double topFarY = cyFar - elevFar;

            IBrush topBrush;
            if (isActive)
            {
                var activeColor = ResolveActiveKeyColor(note, theme, activeKeyChannel, activeKeyTrack, true);
                topBrush = MakeBlackKeyTopGradient(activeColor);
            }
            else if (_keyColorOverrides != null && _keyColorOverrides.TryGetValue(note, out var ov))
            {
                topBrush = MakeBlackKeyTopGradient(ov);
            }
            else
            {
                topBrush = _blackKeyTopGradient;
            }

            var geo = new StreamGeometry();
            using (var sgCtx = geo.Open())
            {
                sgCtx.BeginFigure(new Point(cxNear - halfWNear, topNearY), true);
                sgCtx.LineTo(new Point(cxNear + halfWNear, topNearY));
                sgCtx.LineTo(new Point(cxFar + halfWFar, topFarY));
                sgCtx.LineTo(new Point(cxFar - halfWFar, topFarY));
                sgCtx.EndFigure(true);
            }
            ctx.DrawGeometry(topBrush, null, geo);
        }
    }

    private void DrawPerspectiveKey(
        DrawingContext ctx,
        double noteX,
        double keyWidth,
        double zNear,
        double zFar,
        double vanishX,
        double vanishY,
        double roadBottom,
        IBrush brush,
        IPen? pen,
        double sinkNear = 0,
        double sinkFar = 0)
    {
        var (cxNear, cyNear) = Project3D(noteX, zNear, vanishX, vanishY, roadBottom);
        var (cxFar, cyFar) = Project3D(noteX, zFar, vanishX, vanishY, roadBottom);

        cyNear += sinkNear;
        cyFar += sinkFar;

        double halfWNear = keyWidth * WidthScale3D(zNear) / 2;
        double halfWFar = keyWidth * WidthScale3D(zFar) / 2;

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

    /// <summary>
    /// Draws a shadow on the LEFT edge of a white key (cast by a black key to its left).
    /// Light source is to the left, so shadows fall rightward.
    /// </summary>
    private void DrawShadowOverlay(
        DrawingContext ctx,
        double noteX,
        double keyWidth,
        double zNear,
        double zFar,
        double vanishX,
        double vanishY,
        double roadBottom,
        IBrush shadowBrush,
        double shadowFrac = 0.35)
    {
        var (cxNear, cyNear) = Project3D(noteX, zNear, vanishX, vanishY, roadBottom);
        var (cxFar, cyFar) = Project3D(noteX, zFar, vanishX, vanishY, roadBottom);

        double halfWNear = keyWidth * WidthScale3D(zNear) / 2;
        double halfWFar = keyWidth * WidthScale3D(zFar) / 2;

        // Shadow covers the left portion of the white key's top surface
        double nL = cxNear - halfWNear;
        double nR = cxNear - halfWNear + halfWNear * 2 * shadowFrac;
        double fL = cxFar - halfWFar;
        double fR = cxFar - halfWFar + halfWFar * 2 * shadowFrac;

        var geo = new StreamGeometry();
        using (var sgCtx = geo.Open())
        {
            sgCtx.BeginFigure(new Point(nL, cyNear), true);
            sgCtx.LineTo(new Point(nR, cyNear));
            sgCtx.LineTo(new Point(fR, cyFar));
            sgCtx.LineTo(new Point(fL, cyFar));
            sgCtx.EndFigure(true);
        }
        ctx.DrawGeometry(shadowBrush, null, geo);
    }

    private Color ResolveActiveKeyColor(
        int note, IVisualTheme theme,
        int[] activeKeyChannel, int[] activeKeyTrack,
        bool isBlack)
    {
        float blend = isBlack ? theme.ActiveBlackKeyBlend : theme.ActiveWhiteKeyBlend;
        return ColorHelper.ResolveActiveKeyColor(
            note, activeKeyChannel, activeKeyTrack,
            _colorMode, _channelColors, _trackColors,
            theme.ActiveHighlightColor, blend);
    }

    private void DrawPerspectiveGuideLines(
        DrawingContext ctx,
        double vanishX, double vanishY, double roadBottom,
        double Znear, double Zhorizon)
    {
        const int guideSegments = 40;
        double moireFadeMinGap = GuideLineFade * 6.0;
        double moireFadeMaxGap = GuideLineFade * 16.0;
        var guideLineColor = _guidePen.Brush is SolidColorBrush sb ? sb.Color : Colors.Gray;

        // Collect the X positions to draw guide lines at
        var guidePositions = new List<(double x, double neighborX)>();

        switch (GuideLineStyle)
        {
            case GuideLineStyle.KeyWidthCentered:
                for (int note = 0; note < 128; note++)
                {
                    double nx = _layout.XCenter[note];
                    double neighbor = note < 127 ? _layout.XCenter[note + 1] : _layout.XCenter[note - 1];
                    guidePositions.Add((nx, neighbor));
                }
                break;

            case GuideLineStyle.UniformCentered:
                for (int note = 0; note < 128; note++)
                {
                    double nx = _layout.GuideXUniform[note];
                    double neighbor = note < 127 ? _layout.GuideXUniform[note + 1] : _layout.GuideXUniform[note - 1];
                    guidePositions.Add((nx, neighbor));
                }
                break;

            case GuideLineStyle.Octave:
            {
                // Left edge of keyboard
                double leftEdge = _layout.WhiteKeyBottomLeft[0] >= 0 ? _layout.WhiteKeyBottomLeft[0] : 0;
                // Right edge of keyboard — find last white key
                double rightEdge = leftEdge;
                for (int n = 127; n >= 0; n--)
                {
                    if (_layout.WhiteKeyBottomRight[n] >= 0) { rightEdge = _layout.WhiteKeyBottomRight[n]; break; }
                }

                var allPositions = new List<double> { leftEdge };
                allPositions.AddRange(_layout.OctaveBoundaryX);
                allPositions.Add(rightEdge);

                for (int i = 0; i < allPositions.Count; i++)
                {
                    double neighbor = i < allPositions.Count - 1
                        ? allPositions[i + 1]
                        : allPositions[i - 1];
                    guidePositions.Add((allPositions[i], neighbor));
                }
                break;
            }
        }

        // Draw each guide line with perspective moiré fade
        foreach (var (guideX, neighborX) in guidePositions)
        {
            Point? prevPoint = null;
            for (int seg = 0; seg <= guideSegments; seg++)
            {
                double t = (double)seg / guideSegments;
                double z = Znear + t * (Zhorizon - Znear);

                var (sx, sy) = Project3D(guideX, z, vanishX, vanishY, roadBottom);
                var (nsx, _) = Project3D(neighborX, z, vanishX, vanishY, roadBottom);

                double gap = Math.Abs(nsx - sx);
                double alpha = moireFadeMaxGap > 0
                    ? Math.Clamp((gap - moireFadeMinGap) / (moireFadeMaxGap - moireFadeMinGap), 0, 1)
                    : 1.0;

                var pt = new Point(sx, sy);
                if (prevPoint.HasValue && alpha > 0.01)
                {
                    byte a = (byte)(alpha * guideLineColor.A);
                    var fadedColor = Color.FromArgb(a, guideLineColor.R, guideLineColor.G, guideLineColor.B);
                    var fadedPen = new Pen(new SolidColorBrush(fadedColor), _guidePen.Thickness);
                    ctx.DrawLine(fadedPen, prevPoint.Value, pt);
                }
                prevPoint = pt;
            }
        }
    }

    private static LinearGradientBrush MakeWhiteKeyTopGradient(Color baseColor)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ColorHelper.Lighten(baseColor, 0.06), 0.0),
                new GradientStop(baseColor, 0.4),
                new GradientStop(ColorHelper.Darken(baseColor, 0.12), 1.0),
            }
        };
    }

    private static LinearGradientBrush MakeBlackKeyTopGradient(Color baseColor)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ColorHelper.Lighten(baseColor, 0.15), 0.0),
                new GradientStop(baseColor, 0.3),
                new GradientStop(ColorHelper.Darken(baseColor, 0.08), 1.0),
            }
        };
    }
}
