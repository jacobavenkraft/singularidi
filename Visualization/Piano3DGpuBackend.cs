using System.Numerics;
using Avalonia.Media;
using Singularidi.Themes;

namespace Singularidi.Visualization;

/// <summary>
/// GPU-accelerated piano key renderer. Delegates actual rendering to PianoGlControl
/// which renders via OpenGL in the Avalonia visual tree. The Render() method updates
/// state but does not draw to DrawingContext (the GL control handles rendering).
/// </summary>
public sealed class Piano3DGpuBackend : IPianoKeyRenderer
{
    private readonly Piano3DGeometry _geometry = new();

    public string Name => "GPU";

    public PianoGlControl GlControl { get; } = new();

    // IPianoKeyRenderer properties — stored here and forwarded to GlControl each frame
    public PianoProjectionMode ProjectionMode { get; set; }
    public double TopDown_PianoY { get; set; }
    public double TopDown_PianoHeight { get; set; }
    public double TopDown_HeightScale { get; set; }
    public double Persp_VanishX { get; set; }
    public double Persp_VanishY { get; set; }
    public double Persp_RoadBottom { get; set; }
    public double Persp_Znear { get; set; }
    public double Persp_Zpiano { get; set; }
    public double Persp_HeightScale { get; set; }
    public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(-0.3f, 1f, -0.5f));
    public float AmbientIntensity { get; set; } = 0.35f;
    public float WhitePivotAngle { get; set; } = 0.025f;
    public float BlackPivotAngle { get; set; } = 0.04f;
    public float ShadowFracNormal { get; set; } = 0.35f;
    public float ShadowFracPressed { get; set; } = 0.50f;

    /// <summary>
    /// Whether the GPU backend is available (GL initialized successfully).
    /// If false, callers should fall back to the software renderer.
    /// </summary>
    public bool IsAvailable => GlControl.IsInitialized && !GlControl.InitFailed;

    public void Render(
        DrawingContext ctx,
        PianoLayout layout,
        IVisualTheme theme,
        int[] activeKeyChannel, int[] activeKeyTrack)
    {
        // If GL isn't ready, skip (caller should fall back to software)
        if (GlControl.InitFailed) return;

        // Rebuild geometry if needed
        _geometry.RebuildIfNeeded(layout);

        // Forward all projection/lighting parameters to the GL control
        GlControl.ProjectionMode = ProjectionMode;
        GlControl.TD_PianoY = TopDown_PianoY;
        GlControl.TD_PianoHeight = TopDown_PianoHeight;
        GlControl.TD_HeightScale = TopDown_HeightScale;
        GlControl.P_VanishX = Persp_VanishX;
        GlControl.P_VanishY = Persp_VanishY;
        GlControl.P_RoadBottom = Persp_RoadBottom;
        GlControl.P_Znear = Persp_Znear;
        GlControl.P_Zpiano = Persp_Zpiano;
        GlControl.P_HeightScale = Persp_HeightScale;
        GlControl.LightDirection = LightDirection;
        GlControl.AmbientIntensity = AmbientIntensity;
        GlControl.WhitePivotAngle = WhitePivotAngle;
        GlControl.BlackPivotAngle = BlackPivotAngle;
        GlControl.ShadowFracNormal = ShadowFracNormal;
        GlControl.ShadowFracPressed = ShadowFracPressed;

        // Update per-frame data (key colors, active state, mesh)
        GlControl.UpdateFrame(layout, _geometry, theme, activeKeyChannel, activeKeyTrack);

        // Request the GL control to render on the next composition frame
        GlControl.RequestNextFrameRendering();
    }
}
