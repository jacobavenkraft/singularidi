using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Singularidi.Midi;

namespace Singularidi.Controls;

public sealed class NoteVisualizerControl : Control
{
    private static readonly Color[] ChannelColors =
    [
        Color.Parse("#FF5050"), Color.Parse("#FF9040"), Color.Parse("#FFD740"), Color.Parse("#70E050"),
        Color.Parse("#40D4D4"), Color.Parse("#4080FF"), Color.Parse("#A050FF"), Color.Parse("#FF50C8"),
        Color.Parse("#FF8080"), Color.Parse("#80FF80"), Color.Parse("#80FFFF"), Color.Parse("#8080FF"),
        Color.Parse("#FF80C0"), Color.Parse("#FFC080"), Color.Parse("#C0FF80"), Color.Parse("#C080FF"),
    ];

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#0d0d0d"));
    private static readonly IBrush WhiteKeyInactiveBrush = new SolidColorBrush(Color.FromRgb(240, 240, 240));
    private static readonly IBrush BlackKeyInactiveBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26));
    private static readonly IBrush GuideBrush = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
    private static readonly IPen GuidePen = new Pen(GuideBrush, 1);
    private static readonly IPen BlackKeyBorderPen = new Pen(Brushes.Black, 0.5);

    // Per-note layout cache
    private readonly double[] _xCenter = new double[128];
    private readonly double[] _noteWidth = new double[128];
    private double _cachedWidth = -1;
    private double _whiteKeyWidth;
    private double _blackKeyWidth;

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

    public bool HighlightActiveNotes { get; set; } = true;

    private MidiPlaybackEngine? _engine;
    private readonly DispatcherTimer _renderTimer;

    // Active keys: note number → channel
    private readonly int[] _activeKeyChannel = new int[128];

    public NoteVisualizerControl()
    {
        Array.Fill(_activeKeyChannel, -1);
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();
    }

    public void SetEngine(MidiPlaybackEngine engine)
    {
        _engine = engine;
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        _engine?.UpdateNoteEvents();
        UpdateActiveKeys();
        InvalidateVisual();
    }

    private void UpdateActiveKeys()
    {
        Array.Fill(_activeKeyChannel, -1);
        if (_engine == null) return;
        var now = _engine.CurrentTime.TotalSeconds;
        foreach (var note in _engine.Notes)
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

        // 1. Background
        ctx.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));

        // 2. Note lane guides
        for (int note = 0; note < 128; note++)
        {
            ctx.DrawLine(GuidePen,
                new Point(_xCenter[note], 0),
                new Point(_xCenter[note], vizHeight));
        }

        // 3. Falling notes
        if (_engine != null)
        {
            double now = _engine.CurrentTime.TotalSeconds;
            foreach (var note in _engine.Notes)
            {
                if (note.StartSeconds - now > LookAheadSeconds) break;
                if (note.EndSeconds < now - 0.1) continue;

                double yBottom = vizHeight - (note.StartSeconds - now) / LookAheadSeconds * vizHeight;
                double yTop    = vizHeight - (note.EndSeconds   - now) / LookAheadSeconds * vizHeight;
                yTop    = Math.Clamp(yTop,    0, vizHeight);
                yBottom = Math.Clamp(yBottom, 0, vizHeight);

                double rectH = yBottom - yTop;
                if (rectH < 1) rectH = 1;

                var baseColor = ChannelColors[note.Channel % 16];
                bool isActive = HighlightActiveNotes && _activeKeyChannel[note.NoteNumber] == note.Channel;
                var fillColor = isActive ? LerpToWhite(baseColor, 0.4f) : baseColor;
                var brush = new SolidColorBrush(fillColor);

                double nw = _noteWidth[note.NoteNumber];
                double x = _xCenter[note.NoteNumber] - nw / 2;
                var rect = new Rect(x, yTop, nw, rectH);
                ctx.DrawRectangle(brush, null, rect, 2, 2);
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
                keyBrush = new SolidColorBrush(LerpToWhite(ChannelColors[channel % 16], 0.5f));
            else
                keyBrush = WhiteKeyInactiveBrush;

            double x = _xCenter[note] - _whiteKeyWidth / 2;
            var keyRect = new Rect(x, pianoY, _whiteKeyWidth - 1, pianoHeight);
            ctx.DrawRectangle(keyBrush, BlackKeyBorderPen, keyRect, 0, 3);
        }

        // Black keys on top
        for (int note = 0; note < 128; note++)
        {
            if (!IsBlackKey[note % 12]) continue;
            int channel = _activeKeyChannel[note];
            IBrush keyBrush;
            if (channel >= 0)
                keyBrush = new SolidColorBrush(LerpToWhite(ChannelColors[channel % 16], 0.3f));
            else
                keyBrush = BlackKeyInactiveBrush;

            double x = _xCenter[note] - _blackKeyWidth / 2;
            double bh = pianoHeight * 0.65;
            var keyRect = new Rect(x, pianoY, _blackKeyWidth, bh);
            ctx.DrawRectangle(keyBrush, null, keyRect, 0, 2);
        }
    }

    private static Color LerpToWhite(Color c, float t)
    {
        float r = c.R / 255f + (1f - c.R / 255f) * t;
        float g = c.G / 255f + (1f - c.G / 255f) * t;
        float b = c.B / 255f + (1f - c.B / 255f) * t;
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _renderTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
