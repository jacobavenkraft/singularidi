namespace Singularidi.Visualization;

public enum GuideLineStyle
{
    /// <summary>One guide line per note, centered on the widest portion of the key (bottom center for white keys).</summary>
    KeyWidthCentered,

    /// <summary>One guide line per note, centered on the narrow top portion of each key. Produces uniform spacing.</summary>
    UniformCentered,

    /// <summary>Lines only at octave boundaries (between B and C keys).</summary>
    Octave
}
