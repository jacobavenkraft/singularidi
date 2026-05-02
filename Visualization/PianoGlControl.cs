using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Singularidi.Themes;
using static Avalonia.OpenGL.GlConsts;

namespace Singularidi.Visualization;

/// <summary>
/// OpenGL control that renders the 3D piano keyboard using GPU acceleration.
/// Extends Avalonia's OpenGlControlBase for managed GL context lifecycle.
/// </summary>
public sealed class PianoGlControl : OpenGlControlBase
{
    // Additional GL constants not in Avalonia's GlConsts
    private const int GL_UNSIGNED_INT = 0x1405;
    private const int GL_BLEND = 0x0BE2;
    private const int GL_SRC_ALPHA = 0x0302;
    private const int GL_ONE_MINUS_SRC_ALPHA = 0x0303;
    private const int GL_LEQUAL = 0x0203;
    private const int GL_TRUE = 1;
    private const int GL_FALSE = 0;
    private const int GL_TEXTURE1 = GL_TEXTURE0 + 1;

    // GL function delegates resolved via GetProcAddress
    private delegate void GlUniform1iDelegate(int location, int value);
    private delegate void GlUniform3fDelegate(int location, float v0, float v1, float v2);
    private delegate void GlBlendFuncDelegate(int sfactor, int dfactor);
    private delegate int GlGetErrorDelegate();

    private GlUniform1iDelegate _glUniform1i = null!;
    private GlUniform3fDelegate _glUniform3f = null!;
    private GlBlendFuncDelegate _glBlendFunc = null!;

    // GL resources
    private int _program;
    private int _vao;
    private int _vbo;
    private int _ebo;
    private int _keyColorTex;   // 128x1 RGBA texture for per-key colors
    private int _keyActiveTex;  // 128x1 R texture for active flags
    private int _indexCount;

    // Uniform locations
    private int _uProjectionMode;
    private int _uTD_PianoY, _uTD_PianoHeight, _uTD_HeightScale;
    private int _uP_VanishX, _uP_VanishY, _uP_RoadBottom, _uP_Znear, _uP_Zpiano, _uP_HeightScale;
    private int _uKeyLength, _uBlackKeyLength;
    private int _uScreenWidth, _uScreenHeight;
    private int _uWhitePivotAngle, _uBlackPivotAngle;
    private int _uKeyColors, _uKeyActive;
    private int _uLightDirection, _uAmbientIntensity;

    // State set by the backend each frame
    private readonly GpuPianoMesh _mesh = new();
    private Piano3DGeometry? _geometry;
    private PianoLayout? _layout;
    private bool _meshDirty = true;

    // Per-frame rendering state
    public PianoProjectionMode ProjectionMode { get; set; }
    public double TD_PianoY { get; set; }
    public double TD_PianoHeight { get; set; }
    public double TD_HeightScale { get; set; }
    public double P_VanishX { get; set; }
    public double P_VanishY { get; set; }
    public double P_RoadBottom { get; set; }
    public double P_Znear { get; set; }
    public double P_Zpiano { get; set; }
    public double P_HeightScale { get; set; }
    public Vector3 LightDirection { get; set; }
    public float AmbientIntensity { get; set; }
    public float WhitePivotAngle { get; set; }
    public float BlackPivotAngle { get; set; }
    public float ShadowFracNormal { get; set; }
    public float ShadowFracPressed { get; set; }

    // Per-frame data
    private readonly byte[] _keyColorData = new byte[128 * 4]; // RGBA per key
    private readonly byte[] _keyActiveData = new byte[128];     // R channel per key

    public new bool IsInitialized { get; private set; }
    public bool InitFailed { get; private set; }

    /// <summary>
    /// Update mesh and per-key data for the current frame.
    /// Must be called before the GL render pass.
    /// </summary>
    public void UpdateFrame(
        PianoLayout layout,
        Piano3DGeometry geometry,
        IVisualTheme theme,
        int[] activeKeyChannel,
        int[] activeKeyTrack)
    {
        // Check if layout changed (mesh needs rebuild)
        if (_layout != layout || _geometry != geometry)
        {
            _layout = layout;
            _geometry = geometry;
            _meshDirty = true;
        }

        // Rebuild mesh if geometry changed
        if (_meshDirty && _geometry != null && _layout != null)
        {
            _geometry.RebuildIfNeeded(_layout);
            _geometry.AddShadowQuads(_layout, activeKeyChannel, ShadowFracNormal, ShadowFracPressed);
            _mesh.Rebuild(_geometry.Faces);
            _meshDirty = false;
        }

        // Compute per-key colors (same logic as Piano3DRenderer.GetBaseColor, but simplified
        // to one color per key rather than per-face since the GPU shader handles face part darkening)
        for (int key = 0; key < 128; key++)
        {
            bool isBlack = PianoLayout.IsBlackKey[key % 12];
            bool isActive = activeKeyChannel[key] >= 0;

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
            }

            if (isActive)
            {
                float blend = isBlack ? theme.ActiveBlackKeyBlend : theme.ActiveWhiteKeyBlend;
                baseColor = ColorHelper.ResolveActiveKeyColor(
                    key, activeKeyChannel, activeKeyTrack,
                    theme.ColorMode,
                    theme.ChannelColors, theme.TrackColors,
                    theme.ActiveHighlightColor, blend);
            }

            int offset = key * 4;
            _keyColorData[offset + 0] = baseColor.R;
            _keyColorData[offset + 1] = baseColor.G;
            _keyColorData[offset + 2] = baseColor.B;
            _keyColorData[offset + 3] = baseColor.A;

            _keyActiveData[key] = isActive ? (byte)255 : (byte)0;
        }
    }

    public void MarkMeshDirty() => _meshDirty = true;

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            // Resolve additional GL functions
            _glUniform1i = Marshal.GetDelegateForFunctionPointer<GlUniform1iDelegate>(gl.GetProcAddress("glUniform1i"));
            _glUniform3f = Marshal.GetDelegateForFunctionPointer<GlUniform3fDelegate>(gl.GetProcAddress("glUniform3f"));
            _glBlendFunc = Marshal.GetDelegateForFunctionPointer<GlBlendFuncDelegate>(gl.GetProcAddress("glBlendFunc"));

            // Compile shaders
            _program = CreateShaderProgram(gl);
            if (_program == 0)
            {
                InitFailed = true;
                return;
            }

            // Get uniform locations
            gl.UseProgram(_program);
            _uProjectionMode = gl.GetUniformLocationString(_program, "uProjectionMode");
            _uTD_PianoY = gl.GetUniformLocationString(_program, "uTD_PianoY");
            _uTD_PianoHeight = gl.GetUniformLocationString(_program, "uTD_PianoHeight");
            _uTD_HeightScale = gl.GetUniformLocationString(_program, "uTD_HeightScale");
            _uP_VanishX = gl.GetUniformLocationString(_program, "uP_VanishX");
            _uP_VanishY = gl.GetUniformLocationString(_program, "uP_VanishY");
            _uP_RoadBottom = gl.GetUniformLocationString(_program, "uP_RoadBottom");
            _uP_Znear = gl.GetUniformLocationString(_program, "uP_Znear");
            _uP_Zpiano = gl.GetUniformLocationString(_program, "uP_Zpiano");
            _uP_HeightScale = gl.GetUniformLocationString(_program, "uP_HeightScale");
            _uKeyLength = gl.GetUniformLocationString(_program, "uKeyLength");
            _uBlackKeyLength = gl.GetUniformLocationString(_program, "uBlackKeyLength");
            _uScreenWidth = gl.GetUniformLocationString(_program, "uScreenWidth");
            _uScreenHeight = gl.GetUniformLocationString(_program, "uScreenHeight");
            _uWhitePivotAngle = gl.GetUniformLocationString(_program, "uWhitePivotAngle");
            _uBlackPivotAngle = gl.GetUniformLocationString(_program, "uBlackPivotAngle");
            _uKeyColors = gl.GetUniformLocationString(_program, "uKeyColors");
            _uKeyActive = gl.GetUniformLocationString(_program, "uKeyActive");
            _uLightDirection = gl.GetUniformLocationString(_program, "uLightDirection");
            _uAmbientIntensity = gl.GetUniformLocationString(_program, "uAmbientIntensity");

            // Create VAO, VBO, EBO
            _vao = gl.GenVertexArray();
            _vbo = gl.GenBuffer();
            _ebo = gl.GenBuffer();

            // Create textures for per-key data
            _keyColorTex = gl.GenTexture();
            gl.ActiveTexture(GL_TEXTURE0);
            gl.BindTexture(GL_TEXTURE_2D, _keyColorTex);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, 128, 1, 0, GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);

            _keyActiveTex = gl.GenTexture();
            gl.ActiveTexture(GL_TEXTURE1);
            gl.BindTexture(GL_TEXTURE_2D, _keyActiveTex);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            // Use GL_RGBA for the active texture too (some drivers don't support GL_RED for ES)
            gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, 128, 1, 0, GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);

            IsInitialized = true;
        }
        catch
        {
            InitFailed = true;
        }
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!IsInitialized || InitFailed || _mesh.Vertices.Length == 0) return;

        var scaling = this.VisualRoot?.RenderScaling ?? 1.0;
        int pixelW = Math.Max(1, (int)(Bounds.Width * scaling));
        int pixelH = Math.Max(1, (int)(Bounds.Height * scaling));
        gl.Viewport(0, 0, pixelW, pixelH);

        // Enable depth test and blending (for shadow transparency)
        gl.Enable(GL_DEPTH_TEST);
        gl.DepthFunc(GL_LEQUAL);
        gl.DepthMask(GL_TRUE);
        gl.Enable(GL_BLEND);
        _glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

        // Clear with transparent background + depth
        gl.ClearColor(0, 0, 0, 0);
        gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        gl.UseProgram(_program);

        // Upload mesh data
        gl.BindVertexArray(_vao);

        gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
        fixed (float* vData = _mesh.Vertices)
        {
            gl.BufferData(GL_ARRAY_BUFFER,
                new IntPtr(_mesh.Vertices.Length * sizeof(float)),
                new IntPtr(vData), GL_STATIC_DRAW);
        }

        gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ebo);
        fixed (uint* iData = _mesh.Indices)
        {
            gl.BufferData(GL_ELEMENT_ARRAY_BUFFER,
                new IntPtr(_mesh.Indices.Length * sizeof(uint)),
                new IntPtr(iData), GL_STATIC_DRAW);
        }
        _indexCount = _mesh.Indices.Length;

        // Set up vertex attributes
        int stride = GpuPianoMesh.FloatsPerVertex * sizeof(float);
        // position (location 0)
        gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, stride, IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        // normal (location 1)
        gl.VertexAttribPointer(1, 3, GL_FLOAT, 0, stride, new IntPtr(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        // keyIndex (location 2)
        gl.VertexAttribPointer(2, 1, GL_FLOAT, 0, stride, new IntPtr(6 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        // facePart (location 3)
        gl.VertexAttribPointer(3, 1, GL_FLOAT, 0, stride, new IntPtr(7 * sizeof(float)));
        gl.EnableVertexAttribArray(3);

        // Set uniforms
        _glUniform1i(_uProjectionMode, ProjectionMode == PianoProjectionMode.TopDown ? 0 : 1);

        gl.Uniform1f(_uTD_PianoY, (float)TD_PianoY);
        gl.Uniform1f(_uTD_PianoHeight, (float)TD_PianoHeight);
        gl.Uniform1f(_uTD_HeightScale, (float)TD_HeightScale);

        gl.Uniform1f(_uP_VanishX, (float)P_VanishX);
        gl.Uniform1f(_uP_VanishY, (float)P_VanishY);
        gl.Uniform1f(_uP_RoadBottom, (float)P_RoadBottom);
        gl.Uniform1f(_uP_Znear, (float)P_Znear);
        gl.Uniform1f(_uP_Zpiano, (float)P_Zpiano);
        gl.Uniform1f(_uP_HeightScale, (float)P_HeightScale);

        gl.Uniform1f(_uKeyLength, _geometry?.KeyLength ?? 1f);
        gl.Uniform1f(_uBlackKeyLength, _geometry?.BlackKeyLength ?? 1f);

        // Screen dimensions in logical (DIP) coordinates — must match the engine's projection parameters
        gl.Uniform1f(_uScreenWidth, (float)Bounds.Width);
        gl.Uniform1f(_uScreenHeight, (float)Bounds.Height);

        gl.Uniform1f(_uWhitePivotAngle, WhitePivotAngle);
        gl.Uniform1f(_uBlackPivotAngle, BlackPivotAngle);

        _glUniform3f(_uLightDirection, LightDirection.X, LightDirection.Y, LightDirection.Z);
        gl.Uniform1f(_uAmbientIntensity, AmbientIntensity);

        // Upload key color texture
        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D, _keyColorTex);
        fixed (byte* colorData = _keyColorData)
        {
            gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, 128, 1, 0, GL_RGBA, GL_UNSIGNED_BYTE, new IntPtr(colorData));
        }
        _glUniform1i(_uKeyColors, 0); // texture unit 0

        // Upload key active texture (pack into RGBA — R channel used)
        gl.ActiveTexture(GL_TEXTURE1);
        gl.BindTexture(GL_TEXTURE_2D, _keyActiveTex);
        // Pack active data into RGBA format
        byte* activeRgba = stackalloc byte[128 * 4];
        for (int i = 0; i < 128; i++)
        {
            activeRgba[i * 4 + 0] = _keyActiveData[i];
            activeRgba[i * 4 + 1] = 0;
            activeRgba[i * 4 + 2] = 0;
            activeRgba[i * 4 + 3] = 255;
        }
        gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, 128, 1, 0, GL_RGBA, GL_UNSIGNED_BYTE, new IntPtr(activeRgba));
        _glUniform1i(_uKeyActive, 1); // texture unit 1

        // Draw
        gl.DrawElements(GL_TRIANGLES, _indexCount, GL_UNSIGNED_INT, IntPtr.Zero);

        // Cleanup state
        gl.Disable(GL_DEPTH_TEST);
        gl.Disable(GL_BLEND);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_program != 0) gl.DeleteProgram(_program);
        if (_vao != 0) gl.DeleteVertexArray(_vao);
        if (_vbo != 0) gl.DeleteBuffer(_vbo);
        if (_ebo != 0) gl.DeleteBuffer(_ebo);
        if (_keyColorTex != 0) gl.DeleteTexture(_keyColorTex);
        if (_keyActiveTex != 0) gl.DeleteTexture(_keyActiveTex);

        _program = _vao = _vbo = _ebo = _keyColorTex = _keyActiveTex = 0;
        IsInitialized = false;
    }

    private static int CreateShaderProgram(GlInterface gl)
    {
        int vs = gl.CreateShader(GL_VERTEX_SHADER);
        string? vsError = gl.CompileShaderAndGetError(vs, PianoShaders.VertexShader);
        if (!string.IsNullOrEmpty(vsError))
        {
            System.Diagnostics.Debug.WriteLine($"Vertex shader error: {vsError}");
            gl.DeleteShader(vs);
            return 0;
        }

        int fs = gl.CreateShader(GL_FRAGMENT_SHADER);
        string? fsError = gl.CompileShaderAndGetError(fs, PianoShaders.FragmentShader);
        if (!string.IsNullOrEmpty(fsError))
        {
            System.Diagnostics.Debug.WriteLine($"Fragment shader error: {fsError}");
            gl.DeleteShader(vs);
            gl.DeleteShader(fs);
            return 0;
        }

        int program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        string? linkError = gl.LinkProgramAndGetError(program);
        if (!string.IsNullOrEmpty(linkError))
        {
            System.Diagnostics.Debug.WriteLine($"Link error: {linkError}");
            gl.DeleteProgram(program);
            gl.DeleteShader(vs);
            gl.DeleteShader(fs);
            return 0;
        }

        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return program;
    }
}
