namespace Singularidi.Midi;

public sealed record NoteEvent(
    int NoteNumber,      // 0–127
    int Channel,         // 0–15
    int Velocity,        // 0–127
    double StartSeconds,
    double EndSeconds,
    int Track = -1       // MIDI track chunk index
);
