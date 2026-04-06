using System.Numerics;

namespace Singularidi.Visualization;

public enum FacePart
{
    WhiteWood,
    WhiteIvory,
    BlackUpper,
    BlackLower,
    Shadow,
}

public struct Face3D
{
    public Vector3[] Vertices;   // 3-6 vertices, wound CCW when viewed from outside
    public Vector3 Normal;       // outward-facing normal
    public int KeyIndex;         // MIDI note number (0-127)
    public FacePart Part;
}

/// <summary>
/// Generates 3D mesh data for a full 128-key piano keyboard.
/// World space: X = left-right, Y = up, Z = front(0) to back(+).
/// All dimensions are proportional to PianoLayout.WhiteKeyWidth.
/// </summary>
public sealed class Piano3DGeometry
{
    // Proportional dimensions (multiples of WhiteKeyWidth)
    private const float KeyLengthRatio = 6.5f;       // Z depth of white key
    private const float WoodHeightRatio = 0.52f;      // Y height of wood block
    private const float IvoryThicknessRatio = 0.087f;  // Y thickness of ivory cap
    private const float IvoryOverhangRatio = 0.065f;   // Z overhang at front only
    private const float BlackTotalHeightRatio = 0.87f;  // Y total height of black key
    private const float BlackUpperHeightRatio = 0.35f;  // Y height above white key surface
    private const float BlackUpperTaper = 0.06f;        // fraction inset per side at top
    private const float BlackKeyLengthRatio = 0.60f;    // black key is shorter than white key
    private const float IvoryCornerRadius = 0.04f;      // corner radius as fraction of key width

    private readonly List<Face3D> _faces = new();
    private float _cachedWhiteKeyWidth = -1;

    public IReadOnlyList<Face3D> Faces => _faces;

    // Absolute dimensions after rebuild
    public float KeyLength { get; private set; }
    public float WoodHeight { get; private set; }
    public float IvoryThickness { get; private set; }
    public float IvoryTop { get; private set; }         // WoodHeight + IvoryThickness
    public float BlackKeyLength { get; private set; }
    public float BlackTotalHeight { get; private set; }

    public void RebuildIfNeeded(PianoLayout layout)
    {
        float wkw = (float)layout.WhiteKeyWidth;
        if (Math.Abs(wkw - _cachedWhiteKeyWidth) < 0.0001f) return;
        _cachedWhiteKeyWidth = wkw;

        KeyLength = wkw * KeyLengthRatio;
        WoodHeight = wkw * WoodHeightRatio;
        IvoryThickness = wkw * IvoryThicknessRatio;
        IvoryTop = WoodHeight + IvoryThickness;
        BlackKeyLength = KeyLength * BlackKeyLengthRatio;
        BlackTotalHeight = wkw * BlackTotalHeightRatio;

        float ivoryOverhang = wkw * IvoryOverhangRatio;
        float blackUpperH = wkw * BlackUpperHeightRatio;
        float cornerR = wkw * IvoryCornerRadius;

        _faces.Clear();

        for (int note = 0; note < 128; note++)
        {
            if (PianoLayout.IsBlackKey[note % 12])
                BuildBlackKey(note, layout, blackUpperH, cornerR);
            else
                BuildWhiteKey(note, layout, ivoryOverhang, cornerR);
        }
    }

    private void BuildWhiteKey(int note, PianoLayout layout, float ivoryOverhang, float cornerR)
    {
        // White key has a T-shaped top view:
        //   narrow top section (KeyTopLeft/Right) from Z=0 to Z=blackKeyZone
        //   wide bottom section (WhiteKeyBottomLeft/Right) from Z=blackKeyZone to Z=keyLength
        // In 3D the "top" portion (narrow) is at the BACK of the key (higher Z),
        // and the "bottom" portion (wide) is at the FRONT.
        //
        // Actually, in piano convention: the player sits at the front (Z=0).
        // The narrow portion is where black keys sit (near the back).
        // The wide portion is the front, closest to the player.
        //
        // PianoLayout's "top" = near the black keys = back of key in 3D
        // PianoLayout's "bottom" = near the player = front of key in 3D

        float botL = (float)layout.WhiteKeyBottomLeft[note];    // front-left (wide)
        float botR = (float)layout.WhiteKeyBottomRight[note];   // front-right (wide)
        float topL = (float)layout.KeyTopLeft[note];            // back-left (narrow)
        float topR = (float)layout.KeyTopRight[note];           // back-right (narrow)

        if (botL < 0) return; // not a valid white key

        // If KeyTopLeft is -1 (partial octave edge case), use bottom width
        if (topL < 0) topL = botL;
        if (topR < 0) topR = botR;

        // The divider is where the wide (front) section meets the narrow (back) section.
        // Black keys occupy the BACK portion of the keyboard (60% of total length).
        // So the front wide section is 40% of total length.
        float zFront = -ivoryOverhang; // ivory overhangs past Z=0
        float zDivider = KeyLength * (1.0f - BlackKeyLengthRatio); // 40% from front
        float zBack = KeyLength;

        // === WOOD BLOCK ===
        // Front face of wood (recessed behind ivory overhang)
        // The wood front face sits at Z=0, width = full bottom width
        AddQuad(note, FacePart.WhiteWood,
            new Vector3(botL, 0, 0),
            new Vector3(botR, 0, 0),
            new Vector3(botR, WoodHeight, 0),
            new Vector3(botL, WoodHeight, 0),
            -Vector3.UnitZ);

        // Wood top face — only the tiny strip not covered by ivory at back edge
        // (Ivory cap covers most of the top, but this strip would be invisible anyway)
        // Skip — ivory top covers the same area plus overhang.

        // === IVORY CAP ===
        // Ivory top face — the main visible surface.
        // T-shaped: wide front section + narrow back section
        // Use two quads for the T shape.

        // Front wide section: from zFront to zDivider
        float iy = IvoryTop;

        // Chamfer the two front corners of the ivory top
        int chamferSegs = 3;
        float cr = Math.Min(cornerR, (botR - botL) * 0.15f);

        // Build front edge vertices with chamfered corners
        // Left chamfer: from (botL, iy, zFront) curving to (botL, iy, zFront+cr) and (botL+cr, iy, zFront)
        // Right chamfer: mirror
        var frontTopVerts = new List<Vector3>();

        // Start at back-left of front section, go clockwise when viewed from above
        frontTopVerts.Add(new Vector3(botL, iy, zDivider));
        frontTopVerts.Add(new Vector3(botL, iy, zFront + cr));

        // Left front chamfer
        for (int i = 0; i <= chamferSegs; i++)
        {
            float t = (float)i / chamferSegs;
            float angle = MathF.PI / 2 * (1 - t); // 90° to 0°
            float cx = botL + cr - cr * MathF.Cos(angle);
            float cz = zFront + cr - cr * MathF.Sin(angle);
            frontTopVerts.Add(new Vector3(cx, iy, cz));
        }

        // Right front chamfer
        for (int i = 0; i <= chamferSegs; i++)
        {
            float t = (float)i / chamferSegs;
            float angle = MathF.PI / 2 * t; // 0° to 90°
            float cx = botR - cr + cr * MathF.Cos(angle);
            float cz = zFront + cr - cr * MathF.Sin(angle);
            frontTopVerts.Add(new Vector3(cx, iy, cz));
        }

        frontTopVerts.Add(new Vector3(botR, iy, zDivider));

        // Add as a polygon face (fan from first vertex)
        AddPolygon(note, FacePart.WhiteIvory, frontTopVerts.ToArray(), Vector3.UnitY);

        // Back narrow section: from zDivider to zBack
        AddQuad(note, FacePart.WhiteIvory,
            new Vector3(topL, iy, zDivider),
            new Vector3(topR, iy, zDivider),
            new Vector3(topR, iy, zBack),
            new Vector3(topL, iy, zBack),
            Vector3.UnitY);

        // Ivory front face (the overhanging lip at Z = zFront)
        // Build this with the chamfered shape too
        var frontFaceVerts = new List<Vector3>();
        frontFaceVerts.Add(new Vector3(botL + cr, WoodHeight, zFront));

        // Left chamfer on front face (vertical curve)
        for (int i = chamferSegs; i >= 0; i--)
        {
            float t = (float)i / chamferSegs;
            float angle = MathF.PI / 2 * (1 - t);
            float cx = botL + cr - cr * MathF.Cos(angle);
            float cy = iy - cr + cr * MathF.Sin(angle);
            frontFaceVerts.Add(new Vector3(cx, cy, zFront));
        }

        // Straight across bottom
        frontFaceVerts.Insert(0, new Vector3(botL, WoodHeight, zFront));

        // We need to rebuild this properly — front face is a rectangle with rounded top corners
        // Let's simplify: front face polygon from bottom-left, across bottom, up right side,
        // chamfer top-right, across top, chamfer top-left, down left side
        frontFaceVerts.Clear();

        // Bottom edge
        frontFaceVerts.Add(new Vector3(botL, WoodHeight, zFront));
        frontFaceVerts.Add(new Vector3(botR, WoodHeight, zFront));

        // Right side up to chamfer
        frontFaceVerts.Add(new Vector3(botR, iy - cr, zFront));

        // Top-right chamfer
        for (int i = 0; i <= chamferSegs; i++)
        {
            float t = (float)i / chamferSegs;
            float angle = MathF.PI / 2 * t;
            float cx = botR - cr + cr * MathF.Cos(angle);
            float cy = iy - cr + cr * MathF.Sin(angle);
            frontFaceVerts.Add(new Vector3(cx, cy, zFront));
        }

        // Top-left chamfer
        for (int i = 0; i <= chamferSegs; i++)
        {
            float t = (float)i / chamferSegs;
            float angle = MathF.PI / 2 * (1 - t);
            float cx = botL + cr - cr * MathF.Cos(angle);
            float cy = iy - cr + cr * MathF.Sin(angle);
            frontFaceVerts.Add(new Vector3(cx, cy, zFront));
        }

        // Left side down
        frontFaceVerts.Add(new Vector3(botL, iy - cr, zFront));

        AddPolygon(note, FacePart.WhiteIvory, frontFaceVerts.ToArray(), -Vector3.UnitZ);

        // Ivory bottom face under overhang (tiny strip visible from low camera angle)
        AddQuad(note, FacePart.WhiteWood,
            new Vector3(botL, WoodHeight, zFront),
            new Vector3(botR, WoodHeight, zFront),
            new Vector3(botR, WoodHeight, 0),
            new Vector3(botL, WoodHeight, 0),
            -Vector3.UnitY);

        // Ivory left side face
        AddQuad(note, FacePart.WhiteIvory,
            new Vector3(botL, WoodHeight, zFront),
            new Vector3(botL, iy, zFront + cr),
            new Vector3(botL, iy, zDivider),
            new Vector3(botL, WoodHeight, zDivider),
            -Vector3.UnitX);

        // Wood left side face (below ivory, front section only)
        AddQuad(note, FacePart.WhiteWood,
            new Vector3(botL, 0, 0),
            new Vector3(botL, WoodHeight, 0),
            new Vector3(botL, WoodHeight, zDivider),
            new Vector3(botL, 0, zDivider),
            -Vector3.UnitX);

        // Ivory right side face
        AddQuad(note, FacePart.WhiteIvory,
            new Vector3(botR, iy, zFront + cr),
            new Vector3(botR, WoodHeight, zFront),
            new Vector3(botR, WoodHeight, zDivider),
            new Vector3(botR, iy, zDivider),
            Vector3.UnitX);

        // Wood right side face
        AddQuad(note, FacePart.WhiteWood,
            new Vector3(botR, WoodHeight, 0),
            new Vector3(botR, 0, 0),
            new Vector3(botR, 0, zDivider),
            new Vector3(botR, WoodHeight, zDivider),
            Vector3.UnitX);

        // Back narrow section side faces (left and right of the narrow part)
        // Left side of narrow back section
        AddQuad(note, FacePart.WhiteIvory,
            new Vector3(topL, 0, zDivider),
            new Vector3(topL, iy, zDivider),
            new Vector3(topL, iy, zBack),
            new Vector3(topL, 0, zBack),
            -Vector3.UnitX);

        // Right side of narrow back section
        AddQuad(note, FacePart.WhiteIvory,
            new Vector3(topR, iy, zDivider),
            new Vector3(topR, 0, zDivider),
            new Vector3(topR, 0, zBack),
            new Vector3(topR, iy, zBack),
            Vector3.UnitX);

        // Inner step face at divider (where wide meets narrow) — left notch
        if (topL > botL + 0.01f)
        {
            AddQuad(note, FacePart.WhiteWood,
                new Vector3(botL, 0, zDivider),
                new Vector3(topL, 0, zDivider),
                new Vector3(topL, iy, zDivider),
                new Vector3(botL, iy, zDivider),
                -Vector3.UnitZ);
        }

        // Inner step face — right notch
        if (botR > topR + 0.01f)
        {
            AddQuad(note, FacePart.WhiteWood,
                new Vector3(topR, 0, zDivider),
                new Vector3(botR, 0, zDivider),
                new Vector3(botR, iy, zDivider),
                new Vector3(topR, iy, zDivider),
                -Vector3.UnitZ);
        }
    }

    private void BuildBlackKey(int note, PianoLayout layout, float blackUpperH, float cornerR)
    {
        float left = (float)layout.KeyTopLeft[note];
        float right = (float)layout.KeyTopRight[note];
        if (left < 0 || right < 0) return;

        float keyW = right - left;
        // Black keys sit at the BACK of the keyboard, starting at the divider
        float zFront = KeyLength * (1.0f - BlackKeyLengthRatio);  // same as white key divider
        float zBack = KeyLength;

        // Lower block: from Y=0 to Y=IvoryTop, straight vertical sides
        // Mostly hidden between white keys — only front face potentially visible
        float lowerTop = IvoryTop; // aligns with white key ivory surface

        // Upper block: from Y=IvoryTop to Y=BlackTotalHeight
        // Tapers inward from base to top
        float taper = keyW * BlackUpperTaper;
        float upperTopL = left + taper;
        float upperTopR = right - taper;
        float upperTop = BlackTotalHeight;

        // Front face of lower block
        AddQuad(note, FacePart.BlackLower,
            new Vector3(left, 0, zFront),
            new Vector3(right, 0, zFront),
            new Vector3(right, lowerTop, zFront),
            new Vector3(left, lowerTop, zFront),
            -Vector3.UnitZ);

        // Front face of upper block (trapezoidal — wider at base, narrower at top)
        AddQuad(note, FacePart.BlackUpper,
            new Vector3(left, lowerTop, zFront),
            new Vector3(right, lowerTop, zFront),
            new Vector3(upperTopR, upperTop, zFront),
            new Vector3(upperTopL, upperTop, zFront),
            -Vector3.UnitZ);

        // Top face of upper block
        AddQuad(note, FacePart.BlackUpper,
            new Vector3(upperTopL, upperTop, zFront),
            new Vector3(upperTopR, upperTop, zFront),
            new Vector3(upperTopR, upperTop, zBack),
            new Vector3(upperTopL, upperTop, zBack),
            Vector3.UnitY);

        // Left side face of upper block (tapered)
        AddQuad(note, FacePart.BlackUpper,
            new Vector3(left, lowerTop, zFront),
            new Vector3(upperTopL, upperTop, zFront),
            new Vector3(upperTopL, upperTop, zBack),
            new Vector3(left, lowerTop, zBack),
            ComputeNormal(
                new Vector3(left, lowerTop, zFront),
                new Vector3(upperTopL, upperTop, zFront),
                new Vector3(upperTopL, upperTop, zBack)));

        // Right side face of upper block (tapered)
        AddQuad(note, FacePart.BlackUpper,
            new Vector3(upperTopR, upperTop, zFront),
            new Vector3(right, lowerTop, zFront),
            new Vector3(right, lowerTop, zBack),
            new Vector3(upperTopR, upperTop, zBack),
            ComputeNormal(
                new Vector3(upperTopR, upperTop, zFront),
                new Vector3(right, lowerTop, zFront),
                new Vector3(right, lowerTop, zBack)));

        // Back face of upper block
        AddQuad(note, FacePart.BlackUpper,
            new Vector3(upperTopR, upperTop, zBack),
            new Vector3(upperTopL, upperTop, zBack),
            new Vector3(left, lowerTop, zBack),
            new Vector3(right, lowerTop, zBack),
            Vector3.UnitZ);
    }

    /// <summary>
    /// Generates shadow quads on white key surfaces adjacent to black keys.
    /// Called separately since shadows depend on active key state.
    /// </summary>
    public void AddShadowQuads(PianoLayout layout, int[] activeKeyChannel, float shadowFracNormal, float shadowFracPressed)
    {
        // Remove any previous shadow faces
        _faces.RemoveAll(f => f.Part == FacePart.Shadow);

        float iy = IvoryTop + 0.01f; // slightly above ivory to avoid z-fighting
        float blackZstart = KeyLength * (1.0f - BlackKeyLengthRatio);  // where black keys begin

        for (int note = 0; note < 128; note++)
        {
            if (PianoLayout.IsBlackKey[note % 12]) continue;
            if (note <= 0 || !PianoLayout.IsBlackKey[(note - 1) % 12]) continue;

            // Shadow sits on the white key's narrow (back) section, left edge
            float topL = (float)layout.KeyTopLeft[note];
            if (topL < 0) continue;

            bool isPressed = activeKeyChannel[note] >= 0;
            float shadowFrac = isPressed ? shadowFracPressed : shadowFracNormal;
            float shadowW = (float)layout.BlackKeyWidth * shadowFrac;
            float shadowZend = isPressed ? KeyLength * 1.02f : KeyLength;

            AddQuad(note, FacePart.Shadow,
                new Vector3(topL, iy, blackZstart),
                new Vector3(topL + shadowW, iy, blackZstart),
                new Vector3(topL + shadowW, iy, shadowZend),
                new Vector3(topL, iy, shadowZend),
                Vector3.UnitY);
        }
    }

    private void AddQuad(int keyIndex, FacePart part, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal)
    {
        _faces.Add(new Face3D
        {
            Vertices = [v0, v1, v2, v3],
            Normal = normal,
            KeyIndex = keyIndex,
            Part = part,
        });
    }

    private void AddPolygon(int keyIndex, FacePart part, Vector3[] vertices, Vector3 normal)
    {
        _faces.Add(new Face3D
        {
            Vertices = vertices,
            Normal = normal,
            KeyIndex = keyIndex,
            Part = part,
        });
    }

    private static Vector3 ComputeNormal(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        return Vector3.Normalize(Vector3.Cross(edge1, edge2));
    }
}
