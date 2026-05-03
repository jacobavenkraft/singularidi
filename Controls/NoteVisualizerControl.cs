using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Singularidi.Midi;
using Singularidi.Themes;
using Singularidi.Visualization;

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

    public static readonly StyledProperty<IVisualizationEngine?> VisualizationEngineProperty =
        AvaloniaProperty.Register<NoteVisualizerControl, IVisualizationEngine?>(
            nameof(VisualizationEngine));

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

    public IVisualizationEngine? VisualizationEngine
    {
        get => GetValue(VisualizationEngineProperty);
        set => SetValue(VisualizationEngineProperty, value);
    }

    // Active keys: note number → channel / track
    private readonly int[] _activeKeyChannel = new int[128];
    private readonly int[] _activeKeyTrack = new int[128];

    private readonly DispatcherTimer _renderTimer;

    // GPU overlay
    private PianoGlControl? _glOverlay;

    // Click-to-play state: the note currently being held under the pointer.
    private int? _heldNote;
    // Last screen-space pointer position, used to sweep a line segment between
    // pointer-move events so fast drags don't skip over keys.
    private Point? _lastPointerPos;
    // Notes whose pointer-up has been deferred until they've sounded long enough to be audible.
    // Key = MIDI note number, value = absolute time (DateTime.UtcNow ticks) when release becomes allowed.
    private readonly Dictionary<int, long> _pendingReleaseTicks = new();
    // Notes that are currently sounding (pointer-down sent, pointer-up not yet sent).
    private readonly HashSet<int> _soundingNotes = new();
    // Minimum time each note must remain on once triggered, so glissando notes are audible.
    private static readonly TimeSpan MinNoteHoldDuration = TimeSpan.FromMilliseconds(60);
    // Maximum number of substeps when sampling along a fast drag.
    private const int MaxSweepSamples = 64;

    /// <summary>Raised when the user presses a piano key (pointer-down or glissando into a new key).</summary>
    public event Action<int>? KeyPressed;

    /// <summary>Raised when the user releases a piano key (pointer-up, capture lost, or glissando out of the key).</summary>
    public event Action<int>? KeyReleased;

    // ── Constructor ─────────────────────────────────────────────────────

    public NoteVisualizerControl()
    {
        Array.Fill(_activeKeyChannel, -1);
        Array.Fill(_activeKeyTrack, -1);
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();
    }

    /// <summary>
    /// Attach or detach the GPU piano overlay control.
    /// When attached, the GL control renders piano keys via OpenGL on top of the 2D visualization.
    /// </summary>
    public void SetGlOverlay(PianoGlControl? glControl)
    {
        if (_glOverlay == glControl) return;

        if (_glOverlay != null)
        {
            VisualChildren.Remove(_glOverlay);
            LogicalChildren.Remove(_glOverlay);
        }

        _glOverlay = glControl;

        if (_glOverlay != null)
        {
            LogicalChildren.Add(_glOverlay);
            VisualChildren.Add(_glOverlay);
        }

        InvalidateArrange();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _glOverlay?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _glOverlay?.Measure(availableSize);
        return availableSize;
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        Engine?.UpdateNoteEvents();
        FlushPendingReleases();
        UpdateActiveKeys();
        InvalidateVisual();
    }

    /// <summary>
    /// Releases notes whose minimum hold time has elapsed and whose pointer is no longer over them.
    /// </summary>
    private void FlushPendingReleases()
    {
        if (_pendingReleaseTicks.Count == 0) return;
        long nowTicks = DateTime.UtcNow.Ticks;
        List<int>? doneNotes = null;
        foreach (var (note, releaseAt) in _pendingReleaseTicks)
        {
            if (nowTicks >= releaseAt)
            {
                (doneNotes ??= new List<int>()).Add(note);
            }
        }
        if (doneNotes == null) return;
        foreach (int note in doneNotes)
        {
            _pendingReleaseTicks.Remove(note);
            ReleaseNote(note);
        }
    }

    private void UpdateActiveKeys()
    {
        Array.Fill(_activeKeyChannel, -1);
        Array.Fill(_activeKeyTrack, -1);
        var engine = Engine;
        if (engine != null)
        {
            var now = engine.CurrentTime.TotalSeconds;
            foreach (var note in engine.Notes)
            {
                if (note.StartSeconds <= now && note.EndSeconds >= now)
                {
                    _activeKeyChannel[note.NoteNumber] = note.Channel;
                    _activeKeyTrack[note.NoteNumber] = note.Track;
                }
            }
        }

        // Click-triggered keys participate in the active-key state so the renderer
        // applies pivot depression / highlight blending exactly as for MIDI notes.
        // Channel 0 matches MidiPlaybackEngine.PlayKey. Don't overwrite a real MIDI note
        // that's already lighting up this key.
        foreach (int note in _soundingNotes)
        {
            if (_activeKeyChannel[note] < 0)
            {
                _activeKeyChannel[note] = 0;
                _activeKeyTrack[note] = 0;
            }
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        double w = bounds.Width;
        double h = bounds.Height;
        if (w <= 0 || h <= 0) return;

        var vizEngine = VisualizationEngine;
        if (vizEngine == null) return;

        var engine = Engine;
        double currentTime = engine?.CurrentTime.TotalSeconds ?? 0;
        IReadOnlyList<NoteEvent> notes = engine?.Notes ?? Array.Empty<NoteEvent>();

        vizEngine.Render(ctx, w, h, notes, currentTime,
            VisualTheme, HighlightActiveNotes,
            _activeKeyChannel, _activeKeyTrack);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _renderTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    // ── Click-to-play pointer handlers ───────────────────────────────────

    private int? HitTestAt(Point pos)
    {
        var hitTester = VisualizationEngine?.HitTester;
        if (hitTester == null) return null;
        return hitTester.HitTest(pos.X, pos.Y, Bounds.Width, Bounds.Height);
    }

    /// <summary>
    /// Trigger a note. If it's already sounding (e.g., a real MIDI note is also playing it,
    /// or the user re-entered the same key), this is a no-op except for refreshing the held state.
    /// </summary>
    private void TriggerNote(int note)
    {
        if (_soundingNotes.Add(note))
        {
            KeyPressed?.Invoke(note);
        }
        // Cancel any pending release — pointer is back over the note.
        _pendingReleaseTicks.Remove(note);
    }

    /// <summary>
    /// Schedule a note to release once it's been sounding for at least MinNoteHoldDuration.
    /// If the note hasn't reached that age yet, the actual NoteOff is deferred to the render tick.
    /// </summary>
    private void ScheduleRelease(int note, long pressTicks)
    {
        if (!_soundingNotes.Contains(note)) return;
        long earliest = pressTicks + MinNoteHoldDuration.Ticks;
        long nowTicks = DateTime.UtcNow.Ticks;
        if (nowTicks >= earliest)
        {
            ReleaseNote(note);
        }
        else
        {
            // If multiple events queue a release for the same note, keep the latest deadline.
            if (_pendingReleaseTicks.TryGetValue(note, out var existing) && existing >= earliest)
                return;
            _pendingReleaseTicks[note] = earliest;
        }
    }

    private void ReleaseNote(int note)
    {
        if (_soundingNotes.Remove(note))
        {
            KeyReleased?.Invoke(note);
        }
    }

    /// <summary>
    /// Records when each currently-sounding note was first triggered, so we can enforce
    /// MinNoteHoldDuration on quick glissando drags.
    /// </summary>
    private readonly Dictionary<int, long> _notePressTicks = new();

    /// <summary>
    /// Sample the line segment between two screen points and trigger every key crossed.
    /// The previous held note is released at the end if the pointer ended on a different key.
    /// </summary>
    private void SweepFromTo(Point from, Point to)
    {
        // Choose a step count proportional to distance so we don't skip narrow black keys.
        // Layout-derived minimum key width is about (control width / 75) for white keys,
        // narrower for black keys. Sample at ~1/3 of the white key width.
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        double stepPx = Math.Max(2.0, Bounds.Width / 75.0 / 3.0);
        int steps = (int)Math.Ceiling(dist / stepPx);
        if (steps < 1) steps = 1;
        if (steps > MaxSweepSamples) steps = MaxSweepSamples;

        long nowTicks = DateTime.UtcNow.Ticks;
        int? lastNote = _heldNote;

        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            var p = new Point(from.X + dx * t, from.Y + dy * t);
            int? hit = HitTestAt(p);

            if (hit == lastNote) continue;

            // Releasing the previous note: defer if it was just triggered.
            if (lastNote.HasValue)
            {
                long pressed = _notePressTicks.TryGetValue(lastNote.Value, out var pt) ? pt : nowTicks;
                ScheduleRelease(lastNote.Value, pressed);
            }

            if (hit.HasValue)
            {
                TriggerNote(hit.Value);
                _notePressTicks[hit.Value] = nowTicks;
            }
            lastNote = hit;
        }

        _heldNote = lastNote;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(this);
        _lastPointerPos = pos;

        var note = HitTestAt(pos);
        if (note == null) return;

        _heldNote = note;
        TriggerNote(note.Value);
        _notePressTicks[note.Value] = DateTime.UtcNow.Ticks;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var pos = e.GetPosition(this);
        if (_lastPointerPos == null || _heldNote == null)
        {
            _lastPointerPos = pos;
            return;
        }

        SweepFromTo(_lastPointerPos.Value, pos);
        _lastPointerPos = pos;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ReleaseAllPointerNotes();
        _lastPointerPos = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ReleaseAllPointerNotes();
        _lastPointerPos = null;
    }

    /// <summary>
    /// Release every pointer-triggered note, honoring the minimum hold duration.
    /// Notes that haven't sounded long enough get deferred to the render tick.
    /// </summary>
    private void ReleaseAllPointerNotes()
    {
        if (_soundingNotes.Count == 0) { _heldNote = null; return; }
        var snapshot = new int[_soundingNotes.Count];
        _soundingNotes.CopyTo(snapshot);
        foreach (int note in snapshot)
        {
            long pressed = _notePressTicks.TryGetValue(note, out var pt) ? pt : 0;
            ScheduleRelease(note, pressed);
        }
        _heldNote = null;
    }
}
