using System.Numerics;
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
    private readonly Piano3DRenderer _pianoRenderer = new();

    private IBrush _backgroundBrush = null!;
    private IPen _guidePen = null!;
    private Color[] _channelColors = null!;
    private Color[] _trackColors = null!;
    private NoteColorMode _colorMode;
    private Dictionary<int, Color>? _noteColorOverrides;
    private IVisualTheme? _cachedTheme;

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
        _colorMode = theme.ColorMode;
        _channelColors = theme.ChannelColors;
        _trackColors = theme.TrackColors;
        _noteColorOverrides = theme.NoteColorOverrides;
    }

    /// <summary>
    /// Projects a world-space point at (worldX, worldZ) onto the screen using 1/z perspective.
    /// </summary>
    private static (double screenX, double screenY) Project3D(
        double worldX, double worldZ,
        double vanishX, double vanishY, double roadBottom)
    {
        double zClamped = Math.Max(worldZ, 0.001);
        double scale = Znear / zClamped;
        double screenX = vanishX + (worldX - vanishX) * scale;
        double screenY = vanishY + (roadBottom - vanishY) * scale;
        return (screenX, screenY);
    }

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
        double roadFraction = 1.0 - SkyFraction;
        double Zpiano = Znear / (1.0 - PianoScreenFraction / roadFraction);
        Zpiano = Math.Clamp(Zpiano, Znear + 0.01, Zhorizon * 0.5);

        // 1. Background
        ctx.DrawRectangle(_backgroundBrush, null, new Rect(0, 0, w, h));

        // 2. Perspective guide lines
        DrawPerspectiveGuideLines(ctx, vanishX, vanishY, roadBottom, Znear, Zhorizon);

        // 3. Notes on the road
        double now = currentTimeSeconds;
        double noteZrange = Zhorizon - Zpiano;

        var visibleNotes = new List<(NoteEvent note, double zNear, double zFar)>();
        foreach (var note in notes)
        {
            if (note.StartSeconds - now > PianoLayout.LookAheadSeconds) break;
            if (note.EndSeconds < now - 0.5) continue;

            double tNear = (note.StartSeconds - now) / PianoLayout.LookAheadSeconds;
            double tFar = (note.EndSeconds - now) / PianoLayout.LookAheadSeconds;

            double zNear2 = Zpiano + tNear * noteZrange;
            double zFar2 = Zpiano + tFar * noteZrange;

            if (zFar2 < Znear * 0.5) continue;
            visibleNotes.Add((note, zNear2, zFar2));
        }

        visibleNotes.Sort((a, b) => b.zNear.CompareTo(a.zNear));

        foreach (var (note, rawZnear, rawZfar) in visibleNotes)
        {
            double zN = Math.Clamp(rawZnear, Znear, Zhorizon);
            double zF = Math.Clamp(rawZfar, Znear, Zhorizon);
            if (Math.Abs(zN - zF) < 0.001) zF = Math.Min(zN + 0.01, Zhorizon);

            double noteX = _layout.XCenter[note.NoteNumber];
            double nw = _layout.NoteWidth[note.NoteNumber];

            var (cxNear, cyNear) = Project3D(noteX, zN, vanishX, vanishY, roadBottom);
            var (cxFar, cyFar) = Project3D(noteX, zF, vanishX, vanishY, roadBottom);

            double halfWNear = nw * WidthScale3D(zN) / 2;
            double halfWFar = nw * WidthScale3D(zF) / 2;

            Color baseColor = ColorHelper.ResolveNoteColor(note, _colorMode, _channelColors, _trackColors, _noteColorOverrides);

            bool isActive = highlightActiveNotes && (
                _colorMode == NoteColorMode.Track
                    ? activeKeyTrack[note.NoteNumber] == note.Track
                    : activeKeyChannel[note.NoteNumber] == note.Channel);
            var fillColor = isActive
                ? ColorHelper.LerpToColor(baseColor, theme.ActiveHighlightColor, theme.ActiveNoteBlend)
                : baseColor;

            double fadeT = Math.Clamp((zN - Zpiano) / noteZrange, 0, 1);
            byte alpha = (byte)(255 * (1.0 - fadeT * 0.6));
            fillColor = Color.FromArgb(alpha, fillColor.R, fillColor.G, fillColor.B);

            var brush = new SolidColorBrush(fillColor);

            double cr = theme.NoteShape == NoteShape.Rectangular
                ? theme.NoteCornerRadius : 0;

            var geo = new StreamGeometry();
            using (var sgCtx = geo.Open())
            {
                if (cr <= 0.5 || theme.NoteShape == NoteShape.DotBlock)
                {
                    sgCtx.BeginFigure(new Point(cxNear - halfWNear, cyNear), true);
                    sgCtx.LineTo(new Point(cxNear + halfWNear, cyNear));
                    sgCtx.LineTo(new Point(cxFar + halfWFar, cyFar));
                    sgCtx.LineTo(new Point(cxFar - halfWFar, cyFar));
                    sgCtx.EndFigure(true);
                }
                else
                {
                    double scaleNear = WidthScale3D(zN);
                    double scaleFar = WidthScale3D(zF);
                    double rNear = Math.Min(cr * scaleNear, halfWNear);
                    double rFar = Math.Min(cr * scaleFar, halfWFar);

                    double nL = cxNear - halfWNear, nR = cxNear + halfWNear;
                    double fL = cxFar - halfWFar, fR = cxFar + halfWFar;
                    double nY = cyNear, fY = cyFar;
                    double nearH = Math.Abs(nY - fY);
                    rNear = Math.Min(rNear, nearH / 2);
                    rFar = Math.Min(rFar, nearH / 2);

                    var sizeNear = new Size(rNear, rNear);
                    var sizeFar = new Size(rFar, rFar);

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

        // 4. Piano keyboard — 3D rendered (perspective projection matching note 1/z system)
        double roadH = roadBottom - vanishY;

        _pianoRenderer.ProjectionMode = PianoProjectionMode.Perspective;
        _pianoRenderer.Persp_VanishX = vanishX;
        _pianoRenderer.Persp_VanishY = vanishY;
        _pianoRenderer.Persp_RoadBottom = roadBottom;
        _pianoRenderer.Persp_Znear = Znear;
        _pianoRenderer.Persp_Zpiano = Zpiano;
        _pianoRenderer.Persp_HeightScale = 1.5;  // world Y (pixels) → screen pixel offset at Znear
        _pianoRenderer.LightDirection = Vector3.Normalize(new Vector3(-0.5f, 0.8f, -0.3f));
        _pianoRenderer.AmbientIntensity = 0.35f;
        _pianoRenderer.WhitePivotAngle = 0.025f;
        _pianoRenderer.BlackPivotAngle = 0.04f;

        _pianoRenderer.Render(ctx, _layout, theme, activeKeyChannel, activeKeyTrack);
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
                double leftEdge = _layout.WhiteKeyBottomLeft[0] >= 0 ? _layout.WhiteKeyBottomLeft[0] : 0;
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
}
