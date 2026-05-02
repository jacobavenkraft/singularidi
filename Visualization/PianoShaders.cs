namespace Singularidi.Visualization;

/// <summary>
/// GLSL ES 300 shader sources for GPU piano rendering.
/// Vertex shader handles per-key pivot depression and engine-specific projection.
/// Fragment shader handles Phong lighting and per-key color lookup.
/// </summary>
public static class PianoShaders
{
    // Key colors are passed as a texture (128x1 RGBA) since uniform arrays have size limits.
    // FacePart enum values: WhiteWood=0, WhiteIvory=1, BlackUpper=2, BlackLower=3, Shadow=4

    public const string VertexShader = """
        #version 300 es
        precision highp float;

        // Per-vertex attributes
        layout(location = 0) in vec3 aPosition;  // world X, Y, Z
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in float aKeyIndex;  // 0-127
        layout(location = 3) in float aFacePart;  // FacePart enum as float

        // Projection uniforms
        uniform int uProjectionMode;  // 0 = TopDown, 1 = Perspective

        // TopDown parameters
        uniform float uTD_PianoY;
        uniform float uTD_PianoHeight;
        uniform float uTD_HeightScale;

        // Perspective parameters
        uniform float uP_VanishX;
        uniform float uP_VanishY;
        uniform float uP_RoadBottom;
        uniform float uP_Znear;
        uniform float uP_Zpiano;
        uniform float uP_HeightScale;

        // Geometry info
        uniform float uKeyLength;
        uniform float uBlackKeyLength;

        // Screen dimensions for final NDC transform
        uniform float uScreenWidth;
        uniform float uScreenHeight;

        // Depression: per-key active state + pivot angles
        uniform sampler2D uKeyColors;  // 128x1 texture, also encodes active state in alpha
        uniform float uWhitePivotAngle;
        uniform float uBlackPivotAngle;

        // Active key flags: 128x1 texture, R channel > 0.5 means active
        uniform sampler2D uKeyActive;

        out vec3 vNormal;
        out float vFacePart;
        out float vKeyIndex;
        out float vDepth;

        // Black key pattern within an octave
        bool isBlackKey(int note) {
            int n = note - (note / 12) * 12;
            return n == 1 || n == 3 || n == 6 || n == 8 || n == 10;
        }

        void main() {
            int keyIdx = int(aKeyIndex + 0.5);
            float texU = (aKeyIndex + 0.5) / 128.0;

            // Check if key is active
            float activeFlag = texture(uKeyActive, vec2(texU, 0.5)).r;
            bool isActive = activeFlag > 0.5;

            vec3 pos = aPosition;
            vec3 norm = aNormal;

            // Apply pivot depression for active keys.
            // All keys hinge at the back of the keyboard (z = uKeyLength), like a real piano.
            if (isActive) {
                float pivotZ = uKeyLength;
                float angle = isBlackKey(keyIdx) ? uBlackPivotAngle : uWhitePivotAngle;

                float dz = pos.z - pivotZ;
                float dy = pos.y;
                float cosA = cos(angle);
                float sinA = sin(angle);

                pos.y = dy * cosA + dz * sinA;
                pos.z = -dy * sinA + dz * cosA + pivotZ;

                // Rotate normal too
                float ny = norm.y * cosA + norm.z * sinA;
                float nz = -norm.y * sinA + norm.z * cosA;
                norm = normalize(vec3(norm.x, ny, nz));
            }

            // Project to screen coordinates
            float sx, sy, depth;

            if (uProjectionMode == 0) {
                // TopDown projection
                sx = pos.x;
                float zFrac = pos.z / uKeyLength;
                sy = uTD_PianoY + uTD_PianoHeight * (1.0 - zFrac);
                sy -= pos.y * uTD_HeightScale;
                depth = pos.z;
            } else {
                // Perspective 1/z projection
                float zFrac = pos.z / uKeyLength;
                float projZ = uP_Znear + zFrac * (uP_Zpiano - uP_Znear);
                projZ = max(projZ, 0.001);
                float scale = uP_Znear / projZ;

                sx = uP_VanishX + (pos.x - uP_VanishX) * scale;
                sy = uP_VanishY + (uP_RoadBottom - uP_VanishY) * scale;
                sy -= pos.y * uP_HeightScale * scale;
                depth = projZ;
            }

            // Convert screen pixels to NDC [-1, 1]
            float ndcX = (sx / uScreenWidth) * 2.0 - 1.0;
            float ndcY = 1.0 - (sy / uScreenHeight) * 2.0;  // flip Y

            // Map depth to [0, 1] range for depth buffer
            // For TopDown: Z range is [0, keyLength], farther = higher Z
            // For Perspective: projZ range is [Znear, Zpiano], farther = higher projZ
            float ndcZ;
            if (uProjectionMode == 0) {
                ndcZ = depth / uKeyLength;
            } else {
                ndcZ = (depth - uP_Znear) / (uP_Zpiano - uP_Znear);
            }
            ndcZ = clamp(ndcZ, 0.0, 1.0);

            gl_Position = vec4(ndcX, ndcY, ndcZ, 1.0);

            vNormal = norm;
            vFacePart = aFacePart;
            vKeyIndex = aKeyIndex;
            vDepth = ndcZ;
        }
        """;

    public const string FragmentShader = """
        #version 300 es
        precision highp float;

        in vec3 vNormal;
        in float vFacePart;
        in float vKeyIndex;
        in float vDepth;

        // Lighting
        uniform vec3 uLightDirection;
        uniform float uAmbientIntensity;

        // Per-key colors (128x1 RGBA texture)
        uniform sampler2D uKeyColors;

        // When set, output a flat dark color (used by the outline pass over white keys).
        uniform float uLineMode;

        out vec4 fragColor;

        void main() {
            if (uLineMode > 0.5) {
                fragColor = vec4(0.0, 0.0, 0.0, 1.0);
                return;
            }

            float texU = (vKeyIndex + 0.5) / 128.0;
            int facePart = int(vFacePart + 0.5);

            // FacePart: 0=WhiteWood, 1=WhiteIvory, 2=BlackUpper, 3=BlackLower, 4=Shadow
            if (facePart == 4) {
                // Shadow face
                fragColor = vec4(0.0, 0.0, 0.0, 0.196);  // alpha ~50/255
                return;
            }

            vec4 keyColor = texture(uKeyColors, vec2(texU, 0.5));

            // Darken wood faces
            if (facePart == 0) {
                keyColor.rgb *= 0.85;
            }

            // Phong lighting (ambient + diffuse)
            vec3 normal = normalize(vNormal);
            float ndotl = max(dot(normal, uLightDirection), 0.0);
            float intensity = uAmbientIntensity + (1.0 - uAmbientIntensity) * ndotl;
            intensity = clamp(intensity, 0.0, 1.0);

            vec3 litColor = keyColor.rgb * intensity;
            fragColor = vec4(litColor, keyColor.a);
        }
        """;
}
