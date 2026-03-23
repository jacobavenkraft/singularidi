using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace Singularidi.Midi;

public static class MidiFileParser
{
    public static (List<NoteEvent> Notes, double TotalDurationSeconds) Parse(string path)
    {
        var file = MidiFile.Read(path);
        var tempoMap = file.GetTempoMap();
        var chunks = file.GetTrackChunks().ToList();

        var notes = new List<NoteEvent>();
        for (int trackIndex = 0; trackIndex < chunks.Count; trackIndex++)
        {
            foreach (var n in chunks[trackIndex].GetNotes())
            {
                notes.Add(new NoteEvent(
                    n.NoteNumber,
                    n.Channel,
                    n.Velocity,
                    TimeConverter.ConvertTo<MetricTimeSpan>(n.Time, tempoMap).TotalSeconds,
                    TimeConverter.ConvertTo<MetricTimeSpan>(n.EndTime, tempoMap).TotalSeconds,
                    trackIndex));
            }
        }

        notes.Sort((a, b) => a.StartSeconds.CompareTo(b.StartSeconds));

        double totalDuration = 0;
        if (notes.Count > 0)
            totalDuration = notes.Max(n => n.EndSeconds);

        return (notes, totalDuration);
    }
}
