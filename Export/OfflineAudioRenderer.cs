using MeltySynth;

namespace Singularidi.Export;

public static class OfflineAudioRenderer
{
    private const int SampleRate = 44100;
    private const int ChunkSamples = 4096;

    public static async Task RenderToWavFileAsync(
        string midiFilePath,
        string soundFontPath,
        string outputWavPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            var soundFont = new SoundFont(soundFontPath);
            var synth = new Synthesizer(soundFont, SampleRate);
            var midiFile = new MidiFile(midiFilePath);
            var sequencer = new MidiFileSequencer(synth);
            sequencer.Play(midiFile, false);

            double totalSeconds = midiFile.Length.TotalSeconds;
            long totalSamples = (long)(totalSeconds * SampleRate) + SampleRate; // +1s for tail
            long samplesRendered = 0;

            var leftBuf = new float[ChunkSamples];
            var rightBuf = new float[ChunkSamples];

            using var fs = new FileStream(outputWavPath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // Write WAV header placeholder
            WriteWavHeader(bw, 0, SampleRate, 16, 2);

            while (samplesRendered < totalSamples)
            {
                ct.ThrowIfCancellationRequested();

                int samplesToRender = (int)Math.Min(ChunkSamples, totalSamples - samplesRendered);
                sequencer.Render(leftBuf.AsSpan(0, samplesToRender), rightBuf.AsSpan(0, samplesToRender));

                for (int i = 0; i < samplesToRender; i++)
                {
                    bw.Write(FloatToShort(leftBuf[i]));
                    bw.Write(FloatToShort(rightBuf[i]));
                }

                samplesRendered += samplesToRender;
                progress?.Report((double)samplesRendered / totalSamples);
            }

            // Go back and fix the WAV header with actual data size
            long dataSize = samplesRendered * 2 * 2; // 2 channels * 2 bytes per sample
            fs.Seek(0, SeekOrigin.Begin);
            WriteWavHeader(bw, dataSize, SampleRate, 16, 2);

        }, ct);
    }

    private static void WriteWavHeader(BinaryWriter bw, long dataSize, int sampleRate, int bitsPerSample, int channels)
    {
        int blockAlign = channels * bitsPerSample / 8;
        int byteRate = sampleRate * blockAlign;

        bw.Write("RIFF"u8);
        bw.Write((int)(36 + dataSize));
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);                  // chunk size
        bw.Write((short)1);            // PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);
        bw.Write("data"u8);
        bw.Write((int)dataSize);
    }

    private static short FloatToShort(float f)
    {
        float v = f * 32767f;
        if (v > 32767f)  return 32767;
        if (v < -32768f) return -32768;
        return (short)v;
    }
}
