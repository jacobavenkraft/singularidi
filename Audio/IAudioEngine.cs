namespace Singularidi.Audio;

public interface IAudioEngine : IDisposable
{
    void LoadFile(string midiFilePath);
    void Play();
    void Pause();
    void Stop();

    /// <summary>Trigger a note immediately, bypassing any timeline-driven playback.</summary>
    void NoteOn(int channel, int noteNumber, int velocity);

    /// <summary>Release a previously-triggered note.</summary>
    void NoteOff(int channel, int noteNumber);
}
