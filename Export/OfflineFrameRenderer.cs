using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Singularidi.Midi;
using Singularidi.Themes;
using Singularidi.Visualization;

namespace Singularidi.Export;

/// <summary>
/// Renders visualization frames to raw pixel data for video export.
/// Must be called on the Avalonia UI thread.
/// </summary>
public sealed class OfflineFrameRenderer
{
    private readonly RenderHelper _renderHelper;
    private Avalonia.Media.Imaging.RenderTargetBitmap _rtb;
    private byte[]? _pixelBuffer;

    public OfflineFrameRenderer(int width, int height)
    {
        _renderHelper = new RenderHelper(width, height);
        _rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height));
    }

    /// <summary>
    /// Renders a single frame and returns raw BGRA pixel data.
    /// Must be called on the UI thread.
    /// </summary>
    public byte[] RenderFrame(
        IVisualizationEngine engine,
        IReadOnlyList<NoteEvent> notes,
        double timeSeconds,
        IVisualTheme theme,
        bool highlightActiveNotes,
        int[] activeKeyChannel,
        int[] activeKeyTrack)
    {
        int w = (int)_rtb.PixelSize.Width;
        int h = (int)_rtb.PixelSize.Height;

        _renderHelper.Configure(engine, notes, timeSeconds, theme, highlightActiveNotes,
            activeKeyChannel, activeKeyTrack);

        _renderHelper.Measure(new Size(w, h));
        _renderHelper.Arrange(new Rect(0, 0, w, h));
        _rtb.Render(_renderHelper);

        // Extract pixel data
        int stride = w * 4;
        int bufferSize = stride * h;
        _pixelBuffer ??= new byte[bufferSize];

        var handle = GCHandle.Alloc(_pixelBuffer, GCHandleType.Pinned);
        try
        {
            _rtb.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), bufferSize, stride);
        }
        finally
        {
            handle.Free();
        }

        return _pixelBuffer;
    }

    /// <summary>
    /// Helper control that delegates rendering to an IVisualizationEngine.
    /// </summary>
    private sealed class RenderHelper : Control
    {
        private IVisualizationEngine? _engine;
        private IReadOnlyList<NoteEvent> _notes = Array.Empty<NoteEvent>();
        private double _timeSeconds;
        private IVisualTheme _theme = null!;
        private bool _highlightActiveNotes;
        private int[] _activeKeyChannel = Array.Empty<int>();
        private int[] _activeKeyTrack = Array.Empty<int>();

        public RenderHelper(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public void Configure(
            IVisualizationEngine engine,
            IReadOnlyList<NoteEvent> notes,
            double timeSeconds,
            IVisualTheme theme,
            bool highlightActiveNotes,
            int[] activeKeyChannel,
            int[] activeKeyTrack)
        {
            _engine = engine;
            _notes = notes;
            _timeSeconds = timeSeconds;
            _theme = theme;
            _highlightActiveNotes = highlightActiveNotes;
            _activeKeyChannel = activeKeyChannel;
            _activeKeyTrack = activeKeyTrack;
        }

        public override void Render(DrawingContext context)
        {
            if (_engine == null || _theme == null) return;
            _engine.Render(context, Bounds.Width, Bounds.Height,
                _notes, _timeSeconds, _theme, _highlightActiveNotes,
                _activeKeyChannel, _activeKeyTrack);
        }
    }
}
