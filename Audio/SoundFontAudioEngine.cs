using MeltySynth;
using NAudio.Wave;

namespace Singularidi.Audio;

public sealed class SoundFontAudioEngine : IAudioEngine, IWaveProvider
{
    private const int SampleRate = 44100;
    private const int ChunkSamples = 4096;

    private SoundFont? _soundFont;
    private Synthesizer? _synthesizer;
    private MidiFileSequencer? _sequencer;
    private string? _midiFilePath;
    private double _totalDurationSeconds;
    private double _elapsedSeconds;

    private WaveOutEvent? _waveOut;
    private volatile bool _paused;

    private readonly float[] _leftBuf  = new float[ChunkSamples];
    private readonly float[] _rightBuf = new float[ChunkSamples];

    public WaveFormat WaveFormat { get; } = new WaveFormat(SampleRate, 16, 2);

    public SoundFontAudioEngine(string soundFontPath)
    {
        if (!string.IsNullOrEmpty(soundFontPath) && File.Exists(soundFontPath))
            LoadSoundFont(soundFontPath);
    }

    private void LoadSoundFont(string path)
    {
        try { _soundFont = new SoundFont(path); }
        catch (Exception ex) { Console.Error.WriteLine($"[SoundFontAudioEngine] SoundFont load failed: {ex.Message}"); }
    }

    public void LoadFile(string midiFilePath)
    {
        Stop();
        _midiFilePath = midiFilePath;
        try
        {
            var mf = new MeltySynth.MidiFile(midiFilePath);
            _totalDurationSeconds = mf.Length.TotalSeconds;
        }
        catch { _totalDurationSeconds = 0; }
    }

    public void Play()
    {
        if (_paused && _waveOut != null)
        {
            _paused = false;
            _waveOut.Play();
            return;
        }

        if (_midiFilePath == null) return;
        if (_soundFont == null)
        {
            Console.Error.WriteLine("[SoundFontAudioEngine] No SoundFont loaded.");
            return;
        }

        _synthesizer = new Synthesizer(_soundFont, SampleRate);
        var midiFile = new MeltySynth.MidiFile(_midiFilePath);
        _sequencer = new MidiFileSequencer(_synthesizer);
        _sequencer.Play(midiFile, false);
        _elapsedSeconds = 0;
        _paused = false;

        _waveOut = new WaveOutEvent();
        _waveOut.Init(this);
        _waveOut.Play();
    }

    public void Pause()
    {
        if (_waveOut == null || _paused) return;
        _paused = true;
        _waveOut.Pause();
    }

    public void Stop()
    {
        _paused = false;
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _sequencer = null;
        _synthesizer = null;
        _elapsedSeconds = 0;
    }

    // Called by NAudio on its audio thread — render MeltySynth into the buffer.
    public int Read(byte[] buffer, int offset, int count)
    {
        if (_sequencer == null)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        int bytesWritten = 0;
        while (bytesWritten < count)
        {
            int samplesThisChunk = Math.Min(ChunkSamples, (count - bytesWritten) / 4);
            if (samplesThisChunk == 0) break;

            _sequencer.Render(_leftBuf.AsSpan(0, samplesThisChunk), _rightBuf.AsSpan(0, samplesThisChunk));
            _elapsedSeconds += (double)samplesThisChunk / SampleRate;

            bool silence = _totalDurationSeconds > 0 && _elapsedSeconds >= _totalDurationSeconds + 1.0;

            for (int i = 0; i < samplesThisChunk; i++)
            {
                short l = silence ? (short)0 : FloatToShort(_leftBuf[i]);
                short r = silence ? (short)0 : FloatToShort(_rightBuf[i]);
                int pos = offset + bytesWritten + i * 4;
                buffer[pos]     = (byte)(l & 0xFF);
                buffer[pos + 1] = (byte)((l >> 8) & 0xFF);
                buffer[pos + 2] = (byte)(r & 0xFF);
                buffer[pos + 3] = (byte)((r >> 8) & 0xFF);
            }

            bytesWritten += samplesThisChunk * 4;
        }

        return count;
    }

    private static short FloatToShort(float f)
    {
        float v = f * 32767f;
        if (v > 32767f)  return 32767;
        if (v < -32768f) return -32768;
        return (short)v;
    }

    public void Dispose() => Stop();
}
