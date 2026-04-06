using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Singularidi.Themes;

namespace Singularidi.Visualization;

public enum PianoProjectionMode
{
    /// <summary>Top-down view: X = PianoLayout screen X, Z maps to screen Y, height = vertical offset.</summary>
    TopDown,
    /// <summary>Perspective: X/Z use 1/z projection matching HorizontalCrawlEngine, height = vertical offset.</summary>
    Perspective,
}

/// <summary>
/// Software 3D renderer for the piano keyboard. Generates lit 3D geometry and
/// projects it using engine-specific projection (not a generic 3D camera) so that
/// the keyboard aligns perfectly with the existing note/guideline rendering.
/// </summary>
public sealed class Piano3DRenderer
{
    private readonly Piano3DGeometry _geometry = new();

    // Projection
    public PianoProjectionMode ProjectionMode { get; set; }

    // TopDown parameters
    public double TopDown_PianoY { get; set; }       // screen Y where piano starts (front of key)
    public double TopDown_PianoHeight { get; set; }   // screen height of piano region
    public double TopDown_HeightScale { get; set; }   // how much worldY offsets screen Y (pixels per world unit)

    // Perspective parameters (matching HorizontalCrawlEngine's 1/z system)
    public double Persp_VanishX { get; set; }
    public double Persp_VanishY { get; set; }
    public double Persp_RoadBottom { get; set; }
    public double Persp_Znear { get; set; }
    public double Persp_Zpiano { get; set; }
    public double Persp_HeightScale { get; set; }     // world Y → screen pixel offset at Znear

    // Lighting
    public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(-0.3f, 1f, -0.5f));
    public float AmbientIntensity { get; set; } = 0.35f;

    // Pivot depression
    public float WhitePivotAngle { get; set; } = 0.025f;
    public float BlackPivotAngle { get; set; } = 0.04f;

    // Shadow
    public float ShadowFracNormal { get; set; } = 0.35f;
    public float ShadowFracPressed { get; set; } = 0.50f;

    // Reusable buffers
    private readonly List<(Face3D face, float depth, Color color, IPen? pen)> _sortBuffer = new();

    public void Render(
        DrawingContext ctx,
        PianoLayout layout,
        IVisualTheme theme,
        int[] activeKeyChannel, int[] activeKeyTrack)
    {
        _geometry.RebuildIfNeeded(layout);
        _geometry.AddShadowQuads(layout, activeKeyChannel, ShadowFracNormal, ShadowFracPressed);

        var borderPen = new Pen(Brushes.Black, 0.3);

        _sortBuffer.Clear();

        foreach (var face in _geometry.Faces)
        {
            // Skip BlackLower faces — they're hidden between white keys and cause slivers
            if (face.Part == FacePart.BlackLower) continue;

            int key = face.KeyIndex;
            bool isActive = activeKeyChannel[key] >= 0;

            // Apply pivot depression to active keys
            Vector3[] verts;
            Vector3 normal;

            if (isActive)
            {
                float pivotZ = PianoLayout.IsBlackKey[key % 12]
                    ? _geometry.BlackKeyLength
                    : _geometry.KeyLength;
                float angle = PianoLayout.IsBlackKey[key % 12]
                    ? BlackPivotAngle
                    : WhitePivotAngle;

                verts = ApplyPivot(face.Vertices, pivotZ, angle);
                // Rotate the face normal by the same pivot angle (don't recompute from
                // winding order, which may disagree with the manually-assigned normal)
                normal = RotateNormalByPivot(face.Normal, angle);
            }
            else
            {
                verts = face.Vertices;
                normal = face.Normal;
            }

            // Back-face culling.
            // TopDown: fixed view direction works because the camera is directly above.
            // Perspective: a fixed view direction is incorrect — the actual view ray varies
            // across the screen, so faces at the edges/bottom get incorrectly culled.
            // Skip culling for perspective; the painter's sort handles draw order and the
            // face count is small enough that rendering back-faces costs nothing.
            if (ProjectionMode == PianoProjectionMode.TopDown)
            {
                Vector3 viewDir = new Vector3(0, -1, 0);
                if (Vector3.Dot(normal, viewDir) > 0.1f) continue;
            }

            // Project to screen
            float centerX = (float)layout.XCenter[key];
            var screenVerts = new Point[verts.Length];
            float avgDepth = 0;
            bool allValid = true;

            for (int i = 0; i < verts.Length; i++)
            {
                var sv = ProjectToScreen(verts[i], centerX);
                if (float.IsNaN(sv.X) || float.IsNaN(sv.Y)) { allValid = false; break; }
                screenVerts[i] = new Point(sv.X, sv.Y);
                avgDepth += sv.Z;
            }

            if (!allValid) continue;
            avgDepth /= verts.Length;

            // Compute face color
            Color color;
            IPen? pen = null;

            if (face.Part == FacePart.Shadow)
            {
                color = Color.FromArgb(50, 0, 0, 0);
            }
            else
            {
                Color baseColor = GetBaseColor(face, theme, key, isActive,
                    activeKeyChannel, activeKeyTrack);

                float ndotl = Math.Max(0, Vector3.Dot(normal, LightDirection));
                float intensity = AmbientIntensity + (1f - AmbientIntensity) * ndotl;
                intensity = Math.Clamp(intensity, 0f, 1f);

                color = ApplyIntensity(baseColor, intensity);
                pen = (face.Part == FacePart.WhiteIvory || face.Part == FacePart.WhiteWood)
                    ? borderPen : null;
            }

            _sortBuffer.Add((face, avgDepth, color, pen));
        }

        // Painter's algorithm: back-to-front with height tiebreaker.
        // Primary: depth (quantized so faces in the same Z band are treated as equal).
        // Secondary: max vertex Y — the camera looks slightly downward, so taller
        // faces are physically closer and must paint last. This correctly handles
        // black key side faces vs white key side faces in the same Z band.
        _sortBuffer.Sort((a, b) =>
        {
            int da = (int)(a.depth * 200);
            int db = (int)(b.depth * 200);
            if (da != db) return db.CompareTo(da); // back to front
            float maxYa = MaxY(a.face.Vertices);
            float maxYb = MaxY(b.face.Vertices);
            return maxYa.CompareTo(maxYb); // lower faces first, taller faces last
        });

        // Draw all faces
        foreach (var (face, _, color, pen) in _sortBuffer)
        {
            int key = face.KeyIndex;
            bool isActive = activeKeyChannel[key] >= 0;
            float centerX = (float)layout.XCenter[key];

            Vector3[] verts;
            if (isActive)
            {
                float pivotZ = PianoLayout.IsBlackKey[key % 12]
                    ? _geometry.BlackKeyLength
                    : _geometry.KeyLength;
                float angle = PianoLayout.IsBlackKey[key % 12]
                    ? BlackPivotAngle
                    : WhitePivotAngle;
                verts = ApplyPivot(face.Vertices, pivotZ, angle);
            }
            else
            {
                verts = face.Vertices;
            }

            var screenVerts = new Point[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var sv = ProjectToScreen(verts[i], centerX);
                screenVerts[i] = new Point(sv.X, sv.Y);
            }

            var brush = new SolidColorBrush(color);
            var geo = new StreamGeometry();
            using (var sgCtx = geo.Open())
            {
                sgCtx.BeginFigure(screenVerts[0], true);
                for (int i = 1; i < screenVerts.Length; i++)
                    sgCtx.LineTo(screenVerts[i]);
                sgCtx.EndFigure(true);
            }
            ctx.DrawGeometry(brush, pen, geo);
        }
    }

    /// <summary>
    /// Project a 3D world vertex to screen coordinates.
    /// worldX in the geometry is relative to the key's PianoLayout X position.
    /// centerX is layout.XCenter[key] — the screen-space center of this key.
    /// </summary>
    private Vector3 ProjectToScreen(Vector3 v, float centerX)
    {
        // v.X = absolute screen-space X (from PianoLayout coordinates in geometry)
        // v.Y = height above base plane
        // v.Z = depth (0 = front of key, keyLength = back)

        if (ProjectionMode == PianoProjectionMode.TopDown)
        {
            float keyLength = _geometry.KeyLength;

            // X: direct from world (already in screen coords from PianoLayout)
            float sx = v.X;

            // Y: front of key (Z=0) at bottom of piano region, back (Z=keyLength) at top
            // pianoY is the TOP of the piano region on screen, pianoY + pianoHeight is the bottom
            float zFrac = v.Z / keyLength; // 0=front, 1=back
            float sy = (float)(TopDown_PianoY + TopDown_PianoHeight * (1.0 - zFrac));

            // Height lifts things up on screen (negative Y direction)
            sy -= v.Y * (float)TopDown_HeightScale;

            // Depth for sorting: higher Z = farther from camera (back of key)
            return new Vector3(sx, sy, v.Z);
        }
        else // Perspective
        {
            float keyLength = _geometry.KeyLength;

            // Map worldZ (0=front, keyLength=back) to 1/z projection Z space
            // Znear = piano front edge, Zpiano = piano back edge
            double zFrac = v.Z / keyLength;
            double projZ = Persp_Znear + zFrac * (Persp_Zpiano - Persp_Znear);
            projZ = Math.Max(projZ, 0.001);

            // 1/z projection (matching HorizontalCrawlEngine exactly)
            double scale = Persp_Znear / projZ;
            float sx = (float)(Persp_VanishX + (v.X - Persp_VanishX) * scale);
            float sy = (float)(Persp_VanishY + (Persp_RoadBottom - Persp_VanishY) * scale);

            // Height lifts up on screen, scaled by perspective
            sy -= v.Y * (float)(Persp_HeightScale * scale);

            return new Vector3(sx, sy, (float)projZ);
        }
    }

    private Color GetBaseColor(Face3D face, IVisualTheme theme, int key, bool isActive,
        int[] activeKeyChannel, int[] activeKeyTrack)
    {
        bool isBlack = PianoLayout.IsBlackKey[key % 12];
        Color baseColor;

        if (isBlack)
        {
            baseColor = theme.BlackKeyColor;
            if (theme.KeyColorOverrides != null && theme.KeyColorOverrides.TryGetValue(key, out var ko))
                baseColor = ko;
        }
        else
        {
            baseColor = theme.WhiteKeyColor;
            if (theme.KeyColorOverrides != null && theme.KeyColorOverrides.TryGetValue(key, out var ko))
                baseColor = ko;

            if (face.Part == FacePart.WhiteWood)
                baseColor = ColorHelper.Darken(baseColor, 0.15);
        }

        if (isActive)
        {
            float blend = isBlack ? theme.ActiveBlackKeyBlend : theme.ActiveWhiteKeyBlend;
            var activeColor = ColorHelper.ResolveActiveKeyColor(
                key, activeKeyChannel, activeKeyTrack,
                theme.ColorMode,
                theme.ChannelColors, theme.TrackColors,
                theme.ActiveHighlightColor, blend);
            baseColor = activeColor;
        }

        return baseColor;
    }

    private static Color ApplyIntensity(Color c, float intensity)
    {
        byte r = (byte)(c.R * intensity);
        byte g = (byte)(c.G * intensity);
        byte b = (byte)(c.B * intensity);
        return Color.FromArgb(c.A, r, g, b);
    }

    /// <summary>
    /// Apply pivot depression: rotate around X-axis at Z=pivotZ.
    /// Front of key sinks down (negative Y), back stays fixed.
    /// </summary>
    private static Vector3[] ApplyPivot(Vector3[] verts, float pivotZ, float angle)
    {
        var result = new Vector3[verts.Length];
        float cosA = MathF.Cos(angle);
        float sinA = MathF.Sin(angle);

        for (int i = 0; i < verts.Length; i++)
        {
            var v = verts[i];
            float dz = v.Z - pivotZ;
            float dy = v.Y;

            // Clockwise rotation around X-axis at Z=pivotZ:
            // front (dz < 0) sinks down, back (dz = 0) stays fixed.
            float newY = dy * cosA + dz * sinA;
            float newZ = -dy * sinA + dz * cosA;

            result[i] = new Vector3(v.X, newY, newZ + pivotZ);
        }

        return result;
    }

    /// <summary>
    /// Rotate a normal vector by the same pivot angle used for vertices.
    /// The pivot rotates around the X-axis, so only Y and Z components change.
    /// No translation (normals are direction vectors).
    /// </summary>
    private static Vector3 RotateNormalByPivot(Vector3 n, float angle)
    {
        float cosA = MathF.Cos(angle);
        float sinA = MathF.Sin(angle);
        // Same clockwise rotation as ApplyPivot (no translation for normals)
        float newY = n.Y * cosA + n.Z * sinA;
        float newZ = -n.Y * sinA + n.Z * cosA;
        return Vector3.Normalize(new Vector3(n.X, newY, newZ));
    }

    private static float MaxY(Vector3[] verts)
    {
        float max = verts[0].Y;
        for (int i = 1; i < verts.Length; i++)
            if (verts[i].Y > max) max = verts[i].Y;
        return max;
    }
}
