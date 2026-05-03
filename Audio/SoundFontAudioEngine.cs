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

    // Guards _synthesizer and _sequencer access between the audio thread (Read)
    // and the UI thread (NoteOn/NoteOff/Play/Stop).
    private readonly object _synthLock = new();

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
        StopSequencerOnly();
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

        EnsureAudioRunning();

        var midiFile = new MeltySynth.MidiFile(_midiFilePath);
        lock (_synthLock)
        {
            _sequencer = new MidiFileSequencer(_synthesizer!);
            _sequencer.Play(midiFile, false);
        }
        _elapsedSeconds = 0;
        _paused = false;
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
        lock (_synthLock)
        {
            _sequencer = null;
            _synthesizer = null;
        }
        _elapsedSeconds = 0;
    }

    public void NoteOn(int channel, int noteNumber, int velocity)
    {
        if (_soundFont == null) return;
        EnsureAudioRunning();
        lock (_synthLock)
        {
            _synthesizer?.NoteOn(channel, noteNumber, velocity);
        }
    }

    public void NoteOff(int channel, int noteNumber)
    {
        lock (_synthLock)
        {
            _synthesizer?.NoteOff(channel, noteNumber);
        }
    }

    private void StopSequencerOnly()
    {
        lock (_synthLock)
        {
            _sequencer = null;
        }
        _elapsedSeconds = 0;
    }

    private void EnsureAudioRunning()
    {
        if (_soundFont == null) return;
        lock (_synthLock)
        {
            _synthesizer ??= new Synthesizer(_soundFont, SampleRate);
        }
        if (_waveOut == null)
        {
            _waveOut = new WaveOutEvent();
            _waveOut.Init(this);
            _waveOut.Play();
        }
    }

    // Called by NAudio on its audio thread. Mixes whichever audio sources are active:
    //   - sequencer driving the synth (during MIDI playback), or
    //   - the synth alone (when only click-to-play notes are active), or
    //   - silence (when nothing is loaded yet).
    public int Read(byte[] buffer, int offset, int count)
    {
        int bytesWritten = 0;
        while (bytesWritten < count)
        {
            int samplesThisChunk = Math.Min(ChunkSamples, (count - bytesWritten) / 4);
            if (samplesThisChunk == 0) break;

            bool rendered;
            lock (_synthLock)
            {
                if (_sequencer != null)
                {
                    _sequencer.Render(_leftBuf.AsSpan(0, samplesThisChunk), _rightBuf.AsSpan(0, samplesThisChunk));
                    _elapsedSeconds += (double)samplesThisChunk / SampleRate;
                    if (_totalDurationSeconds > 0 && _elapsedSeconds >= _totalDurationSeconds + 1.0)
                    {
                        // File playback complete; drop sequencer so the synth keeps producing
                        // any decay tail and manual click-to-play voices.
                        _sequencer = null;
                    }
                    rendered = true;
                }
                else if (_synthesizer != null)
                {
                    _synthesizer.Render(_leftBuf.AsSpan(0, samplesThisChunk), _rightBuf.AsSpan(0, samplesThisChunk));
                    rendered = true;
                }
                else
                {
                    rendered = false;
                }
            }

            if (!rendered)
            {
                Array.Clear(buffer, offset + bytesWritten, samplesThisChunk * 4);
                bytesWritten += samplesThisChunk * 4;
                continue;
            }

            for (int i = 0; i < samplesThisChunk; i++)
            {
                short l = FloatToShort(_leftBuf[i]);
                short r = FloatToShort(_rightBuf[i]);
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
