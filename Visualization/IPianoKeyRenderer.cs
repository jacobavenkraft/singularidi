using System.Numerics;
using Avalonia.Media;
using Singularidi.Themes;

namespace Singularidi.Visualization;

/// <summary>
/// Interface for pluggable piano keyboard rendering backends.
/// The software backend draws via DrawingContext; the GPU backend renders
/// via an OpenGL control and treats the DrawingContext call as a no-op.
/// </summary>
public interface IPianoKeyRenderer
{
    string Name { get; }

    // Projection
    PianoProjectionMode ProjectionMode { get; set; }

    // TopDown parameters
    double TopDown_PianoY { get; set; }
    double TopDown_PianoHeight { get; set; }
    double TopDown_HeightScale { get; set; }

    // Perspective parameters
    double Persp_VanishX { get; set; }
    double Persp_VanishY { get; set; }
    double Persp_RoadBottom { get; set; }
    double Persp_Znear { get; set; }
    double Persp_Zpiano { get; set; }
    double Persp_HeightScale { get; set; }

    // Lighting
    Vector3 LightDirection { get; set; }
    float AmbientIntensity { get; set; }

    // Pivot depression
    float WhitePivotAngle { get; set; }
    float BlackPivotAngle { get; set; }

    // Shadow
    float ShadowFracNormal { get; set; }
    float ShadowFracPressed { get; set; }

    void Render(
        DrawingContext ctx,
        PianoLayout layout,
        IVisualTheme theme,
        int[] activeKeyChannel, int[] activeKeyTrack);
}
