namespace Singularidi.Visualization;

public sealed class CircularPianoLayout
{
    // Positions on the ellipse for each MIDI note (0–127)
    public readonly double[] X = new double[128];
    public readonly double[] Y = new double[128];
    public readonly double[] Angle = new double[128]; // radians, for key orientation
    public readonly double[] KeySize = new double[128]; // visual size of each key

    public double CenterX { get; private set; }
    public double CenterY { get; private set; }
    public double RadiusX { get; private set; }
    public double RadiusY { get; private set; }

    // 270° arc from 7 o'clock (225°) to 5 o'clock (315° via 0°)
    // In radians: 225° = 5π/4, going clockwise 270° to 315° = 7π/4
    // We go counter-clockwise from 5π/4 through 0 to -π/4 (i.e., 7π/4)
    private const double StartAngle = 5.0 * Math.PI / 4.0; // 225° (7 o'clock)
    private const double ArcSpan = -3.0 * Math.PI / 2.0;   // -270° (clockwise sweep)

    private double _cachedWidth = -1;
    private double _cachedHeight = -1;

    public void RebuildIfNeeded(double width, double height)
    {
        if (Math.Abs(width - _cachedWidth) < 0.001 && Math.Abs(height - _cachedHeight) < 0.001)
            return;

        _cachedWidth = width;
        _cachedHeight = height;

        // Ellipse centered in the control, occupying most of the space
        CenterX = width / 2;
        CenterY = height * 0.50; // slightly above center
        RadiusX = width * 0.42;
        RadiusY = height * 0.42;

        for (int note = 0; note < 128; note++)
        {
            double t = note / 127.0;
            double angle = StartAngle + t * ArcSpan;
            Angle[note] = angle;

            // Black keys sit slightly inward
            double r = PianoLayout.IsBlackKey[note % 12] ? 0.90 : 1.0;
            X[note] = CenterX + RadiusX * r * Math.Cos(angle);
            Y[note] = CenterY + RadiusY * r * Math.Sin(angle);

            // Key size proportional to the ellipse
            double baseSize = Math.Min(width, height) / 128.0 * 2.5;
            KeySize[note] = PianoLayout.IsBlackKey[note % 12] ? baseSize * 0.65 : baseSize;
        }
    }
}
