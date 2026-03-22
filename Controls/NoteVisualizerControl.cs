using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Singularidi.Midi;
using Singularidi.Themes;

namespace Singularidi.Controls;

public sealed class NoteVisualizerControl : Control
{
    // ── Styled Properties ────────────────────────────────────────────────

    public static readonly StyledProperty<IVisualTheme> VisualThemeProperty =
        AvaloniaProperty.Register<NoteVisualizerControl, IVisualTheme>(
            nameof(VisualTheme), defaultValue: BuiltInThemes.Dark());

    public static readonly StyledProperty<bool> HighlightActiveNotesProperty =
        AvaloniaProperty.Register<NoteVisualizerControl, bool>(
            nameof(HighlightActiveNotes), defaultValue: true);

    public static readonly StyledProperty<MidiPlaybackEngine?> EngineProperty =
        AvaloniaProperty.Register<NoteVisualizerControl, MidiPlaybackEngine?>(
            nameof(Engine));

    public IVisualTheme VisualTheme
    {
        get => GetValue(VisualThemeProperty);
        set => SetValue(VisualThemeProperty, value);
    }

    public bool HighlightActiveNotes
    {
        get => GetValue(HighlightActiveNotesProperty);
        set => SetValue(HighlightActiveNotesProperty, value);
    }

    public MidiPlaybackEngine? Engine
    {
        get => GetValue(EngineProperty);
        set => SetValue(EngineProperty, value);
    }

    // ── Static layout data ───────────────────────────────────────────────

    // Black key pattern per semitone in octave: true = black key
    private static readonly bool[] IsBlackKey =
    [
        false, true, false, true, false, false, true, false, true, false, true, false
    ];

    // x-center offset in units of whiteKeyWidth within one octave
    private static readonly double[] InOctaveOffset =
    [
        0.5, 1.0, 1.5, 2.0, 2.5,  // C, C#, D, D#, E
        3.5, 4.0, 4.5, 5.0, 5.5, 6.0, 6.5  // F, F#, G, G#, A, A#, B
    ];

    private const int TotalWhiteKeys = 75;
    private const double LookAheadSeconds = 4.0;
    private const double PianoHeightFraction = 0.15;

    // Per-note layout cache
    private readonly double[] _xCenter = new double[128];
    private readonly double[] _noteWidth = new double[128];
    private double _cachedWidth = -1;
    private double _whiteKeyWidth;
    private double _blackKeyWidth;

    // Active keys: note number → channel
    private readonly int[] _activeKeyChannel = new int[128];

    private readonly DispatcherTimer _renderTimer;

    // Cached brushes/pens rebuilt when theme changes
    private IBrush _backgroundBrush = null!;
    private IPen _guidePen = null!;
    private IBrush _whiteKeyBrush = null!;
    private IBrush _blackKeyBrush = null!;
    private IPen _whiteKeyBorderPen = null!;
    private SolidColorBrush[] _channelBrushes = null!;
    private Color[] _channelColors = null!;
    private Dictionary<int, Color>? _noteColorOverrides;
    private Dictionary<int, Color>? _keyColorOverrides;

    // ── Constructor ─────────────────────────────────────────────────────

    public NoteVisualizerControl()
    {
        Array.Fill(_activeKeyChannel, -1);
        InvalidateThemeCaches();
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();
    }

    static NoteVisualizerControl()
    {
        VisualThemeProperty.Changed.AddClassHandler<NoteVisualizerControl>(
            (c, _) => c.InvalidateThemeCaches());
    }

    private void InvalidateThemeCaches()
    {
        var theme = VisualTheme;
        _backgroundBrush = new SolidColorBrush(theme.BackgroundColor);
        _guidePen = new Pen(new SolidColorBrush(theme.GuideLineColor), 1);
        _whiteKeyBrush = new SolidColorBrush(theme.WhiteKeyColor);
        _blackKeyBrush = new SolidColorBrush(theme.BlackKeyColor);
        _whiteKeyBorderPen = new Pen(Brushes.Black, 0.5);
        _channelColors = theme.ChannelColors;
        _channelBrushes = _channelColors.Select(c => new SolidColorBrush(c)).ToArray();
        _noteColorOverrides = theme.NoteColorOverrides;
        _keyColorOverrides = theme.KeyColorOverrides;
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        Engine?.UpdateNoteEvents();
        UpdateActiveKeys();
        InvalidateVisual();
    }

    private void UpdateActiveKeys()
    {
        Array.Fill(_activeKeyChannel, -1);
        var engine = Engine;
        if (engine == null) return;
        var now = engine.CurrentTime.TotalSeconds;
        foreach (var note in engine.Notes)
        {
            if (note.StartSeconds <= now && note.EndSeconds >= now)
                _activeKeyChannel[note.NoteNumber] = note.Channel;
        }
    }

    private void RebuildLayout(double width)
    {
        if (Math.Abs(width - _cachedWidth) < 0.001) return;
        _cachedWidth = width;
        _whiteKeyWidth = width / TotalWhiteKeys;
        _blackKeyWidth = _whiteKeyWidth * 0.60;

        for (int note = 0; note < 128; note++)
        {
            int octave = note / 12;
            int semitone = note % 12;
            _xCenter[note] = octave * 7 * _whiteKeyWidth + InOctaveOffset[semitone] * _whiteKeyWidth;
            _noteWidth[note] = IsBlackKey[semitone] ? _blackKeyWidth : _whiteKeyWidth;
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        double w = bounds.Width;
        double h = bounds.Height;
        if (w <= 0 || h <= 0) return;

        RebuildLayout(w);

        double pianoHeight = h * PianoHeightFraction;
        double vizHeight = h - pianoHeight;
        var theme = VisualTheme;
        var engine = Engine;

        // 1. Background
        ctx.DrawRectangle(_backgroundBrush, null, new Rect(0, 0, w, h));

        // 2. Note lane guides
        for (int note = 0; note < 128; note++)
        {
            ctx.DrawLine(_guidePen,
                new Point(_xCenter[note], 0),
                new Point(_xCenter[note], vizHeight));
        }

        // 3. Falling notes
        if (engine != null)
        {
            double now = engine.CurrentTime.TotalSeconds;
            foreach (var note in engine.Notes)
            {
                if (note.StartSeconds - now > LookAheadSeconds) break;
                if (note.EndSeconds < now - 0.1) continue;

                double yBottom = vizHeight - (note.StartSeconds - now) / LookAheadSeconds * vizHeight;
                double yTop    = vizHeight - (note.EndSeconds   - now) / LookAheadSeconds * vizHeight;
                yTop    = Math.Clamp(yTop,    0, vizHeight);
                yBottom = Math.Clamp(yBottom, 0, vizHeight);

                double rectH = yBottom - yTop;
                if (rectH < 1) rectH = 1;

                // Resolve note color: per-note override → channel color
                Color baseColor;
                if (_noteColorOverrides != null && _noteColorOverrides.TryGetValue(note.NoteNumber, out var overrideColor))
                    baseColor = overrideColor;
                else
                    baseColor = _channelColors[note.Channel % 16];

                bool isActive = HighlightActiveNotes && _activeKeyChannel[note.NoteNumber] == note.Channel;
                var fillColor = isActive ? LerpToColor(baseColor, theme.ActiveHighlightColor, theme.ActiveNoteBlend) : baseColor;
                var brush = new SolidColorBrush(fillColor);

                double nw = _noteWidth[note.NoteNumber];
                double x = _xCenter[note.NoteNumber] - nw / 2;
                var rect = new Rect(x, yTop, nw, rectH);

                double cornerRadius = theme.NoteShape == NoteShape.DotBlock ? nw / 2 : 2;
                ctx.DrawRectangle(brush, null, rect, cornerRadius, cornerRadius);
            }
        }

        // 4. Piano keyboard
        double pianoY = vizHeight;

        // White keys first
        for (int note = 0; note < 128; note++)
        {
            if (IsBlackKey[note % 12]) continue;
            int channel = _activeKeyChannel[note];
            IBrush keyBrush;
            if (channel >= 0)
            {
                var keyBase = _channelColors[channel % 16];
                keyBrush = new SolidColorBrush(LerpToColor(keyBase, theme.ActiveHighlightColor, theme.ActiveWhiteKeyBlend));
            }
            else if (_keyColorOverrides != null && _keyColorOverrides.TryGetValue(note, out var keyOverride))
            {
                keyBrush = new SolidColorBrush(keyOverride);
            }
            else
            {
                keyBrush = _whiteKeyBrush;
            }

            double x = _xCenter[note] - _whiteKeyWidth / 2;
            var keyRect = new Rect(x, pianoY, _whiteKeyWidth - 1, pianoHeight);
            ctx.DrawRectangle(keyBrush, _whiteKeyBorderPen, keyRect, 0, 3);
        }

        // Black keys on top
        for (int note = 0; note < 128; note++)
        {
            if (!IsBlackKey[note % 12]) continue;
            int channel = _activeKeyChannel[note];
            IBrush keyBrush;
            if (channel >= 0)
            {
                var keyBase = _channelColors[channel % 16];
                keyBrush = new SolidColorBrush(LerpToColor(keyBase, theme.ActiveHighlightColor, theme.ActiveBlackKeyBlend));
            }
            else if (_keyColorOverrides != null && _keyColorOverrides.TryGetValue(note, out var keyOverride))
            {
                keyBrush = new SolidColorBrush(keyOverride);
            }
            else
            {
                keyBrush = _blackKeyBrush;
            }

            double x = _xCenter[note] - _blackKeyWidth / 2;
            double bh = pianoHeight * 0.65;
            var keyRect = new Rect(x, pianoY, _blackKeyWidth, bh);
            ctx.DrawRectangle(keyBrush, null, keyRect, 0, 2);
        }
    }

    private static Color LerpToColor(Color c, Color target, float t)
    {
        byte r = (byte)(c.R + (target.R - c.R) * t);
        byte g = (byte)(c.G + (target.G - c.G) * t);
        byte b = (byte)(c.B + (target.B - c.B) * t);
        return Color.FromRgb(r, g, b);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _renderTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
