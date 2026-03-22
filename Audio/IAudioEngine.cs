namespace Singularidi.Audio;

public interface IAudioEngine : IDisposable
{
    void LoadFile(string midiFilePath);
    void Play();
    void Pause();
    void Stop();
}
