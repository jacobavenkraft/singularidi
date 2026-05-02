using System.Numerics;

namespace Singularidi.Visualization;

/// <summary>
/// Converts Piano3DGeometry face data into triangulated vertex/index buffers
/// suitable for GPU rendering. Vertex format: pos(3) + normal(3) + keyIndex(1) + facePart(1) = 8 floats.
/// </summary>
public sealed class GpuPianoMesh
{
    public const int FloatsPerVertex = 8;

    public float[] Vertices { get; private set; } = [];
    public uint[] Indices { get; private set; } = [];
    public int TriangleCount { get; private set; }

    /// <summary>
    /// Rebuild GPU buffers from the current Piano3DGeometry faces.
    /// Call after Piano3DGeometry.RebuildIfNeeded() and AddShadowQuads().
    /// </summary>
    public void Rebuild(IReadOnlyList<Face3D> faces)
    {
        // Count vertices and triangles
        int totalVerts = 0;
        int totalTris = 0;
        foreach (var face in faces)
        {
            if (face.Part == FacePart.BlackLower) continue; // skipped in software renderer too
            int n = face.Vertices.Length;
            totalVerts += n;
            totalTris += n - 2; // fan triangulation
        }

        var verts = new float[totalVerts * FloatsPerVertex];
        var indices = new uint[totalTris * 3];
        int vi = 0; // vertex index (count of vertices added)
        int ii = 0; // index index

        foreach (var face in faces)
        {
            if (face.Part == FacePart.BlackLower) continue;

            int baseVertex = vi;
            var normal = face.Normal;
            float keyIdx = face.KeyIndex;
            float part = (float)face.Part;

            // Pack vertices
            for (int i = 0; i < face.Vertices.Length; i++)
            {
                var v = face.Vertices[i];
                int offset = vi * FloatsPerVertex;
                verts[offset + 0] = v.X;
                verts[offset + 1] = v.Y;
                verts[offset + 2] = v.Z;
                verts[offset + 3] = normal.X;
                verts[offset + 4] = normal.Y;
                verts[offset + 5] = normal.Z;
                verts[offset + 6] = keyIdx;
                verts[offset + 7] = part;
                vi++;
            }

            // Fan triangulation (all faces are convex)
            for (int i = 1; i < face.Vertices.Length - 1; i++)
            {
                indices[ii++] = (uint)baseVertex;
                indices[ii++] = (uint)(baseVertex + i);
                indices[ii++] = (uint)(baseVertex + i + 1);
            }
        }

        Vertices = verts;
        Indices = indices;
        TriangleCount = totalTris;
    }
}
