using Avalonia.Media;
using Singularidi.Midi;
using Singularidi.Themes;

namespace Singularidi.Visualization;

public interface IVisualizationEngine
{
    string Name { get; }

    /// <summary>Hit-tester for clicking piano keys in this view. Geometry is engine-specific.</summary>
    IPianoHitTester HitTester { get; }

    void OnSizeChanged(double width, double height);

    void Render(
        DrawingContext ctx,
        double width,
        double height,
        IReadOnlyList<NoteEvent> notes,
        double currentTimeSeconds,
        IVisualTheme theme,
        bool highlightActiveNotes,
        int[] activeKeyChannel,
        int[] activeKeyTrack);
}
