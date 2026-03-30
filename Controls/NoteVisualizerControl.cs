using Avalonia;
using Avalonia.Controls;
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

    // ── Constructor ─────────────────────────────────────────────────────

    public NoteVisualizerControl()
    {
        Array.Fill(_activeKeyChannel, -1);
        Array.Fill(_activeKeyTrack, -1);
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();
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
        Array.Fill(_activeKeyTrack, -1);
        var engine = Engine;
        if (engine == null) return;
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
}
