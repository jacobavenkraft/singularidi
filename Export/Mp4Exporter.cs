using System.Diagnostics;
using Avalonia.Threading;
using Singularidi.Midi;
using Singularidi.Themes;
using Singularidi.Visualization;

namespace Singularidi.Export;

public sealed class Mp4Exporter
{
    public static bool IsFfmpegAvailable(string? ffmpegPath = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath ?? "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task ExportAsync(
        string midiFilePath,
        string soundFontPath,
        string outputMp4Path,
        ExportSettings settings,
        IVisualizationEngine vizEngine,
        IReadOnlyList<NoteEvent> notes,
        double totalDurationSeconds,
        IVisualTheme theme,
        bool highlightActiveNotes,
        IProgress<(double progress, string status)>? progress = null,
        CancellationToken ct = default)
    {
        string ffmpeg = settings.FfmpegPath ?? "ffmpeg";
        int w = settings.Width;
        int h = settings.Height;
        int fps = settings.Fps;

        // Step 1: Render audio to temp WAV
        string tempWav = Path.Combine(Path.GetTempPath(), $"singularidi_export_{Guid.NewGuid():N}.wav");
        try
        {
            progress?.Report((0, "Rendering audio..."));
            await OfflineAudioRenderer.RenderToWavFileAsync(
                midiFilePath, soundFontPath, tempWav,
                new Progress<double>(p => progress?.Report((p * 0.3, $"Rendering audio... {p:P0}"))),
                ct);

            ct.ThrowIfCancellationRequested();

            // Step 2: Start FFmpeg process
            progress?.Report((0.3, "Starting video encoding..."));
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -f rawvideo -pix_fmt bgra -s {w}x{h} -r {fps} " +
                           $"-i pipe:0 -i \"{tempWav}\" " +
                           $"-c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p " +
                           $"-c:a aac -b:a 192k -shortest \"{outputMp4Path}\"",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var ffmpegProc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start FFmpeg.");

            // Step 3: Render frames and pipe to FFmpeg
            int totalFrames = (int)(totalDurationSeconds * fps) + 1;
            double frameStep = 1.0 / fps;

            // Build active key arrays for offline use
            var activeKeyChannel = new int[128];
            var activeKeyTrack = new int[128];

            for (int frame = 0; frame < totalFrames; frame++)
            {
                ct.ThrowIfCancellationRequested();

                double t = frame * frameStep;

                // Update active keys for this time
                Array.Fill(activeKeyChannel, -1);
                Array.Fill(activeKeyTrack, -1);
                foreach (var note in notes)
                {
                    if (note.StartSeconds <= t && note.EndSeconds >= t)
                    {
                        activeKeyChannel[note.NoteNumber] = note.Channel;
                        activeKeyTrack[note.NoteNumber] = note.Track;
                    }
                }

                // Render frame on the UI thread
                byte[]? pixels = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var renderer = GetOrCreateRenderer(w, h);
                    pixels = renderer.RenderFrame(vizEngine, notes, t, theme,
                        highlightActiveNotes, activeKeyChannel, activeKeyTrack);
                });

                // Write to FFmpeg stdin
                await ffmpegProc.StandardInput.BaseStream.WriteAsync(pixels!, 0, pixels!.Length, ct);

                double videoProgress = 0.3 + (double)frame / totalFrames * 0.7;
                progress?.Report((videoProgress, $"Encoding frame {frame + 1}/{totalFrames}..."));
            }

            // Close stdin to signal end of input
            ffmpegProc.StandardInput.Close();
            await ffmpegProc.WaitForExitAsync(ct);

            if (ffmpegProc.ExitCode != 0)
            {
                string stderr = await ffmpegProc.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException($"FFmpeg exited with code {ffmpegProc.ExitCode}: {stderr}");
            }

            progress?.Report((1.0, "Export complete!"));
        }
        finally
        {
            // Clean up temp WAV
            try { File.Delete(tempWav); } catch { }
        }
    }

    // Reuse renderer across frames to avoid allocations
    private OfflineFrameRenderer? _renderer;
    private int _rendererW, _rendererH;

    private OfflineFrameRenderer GetOrCreateRenderer(int w, int h)
    {
        if (_renderer == null || _rendererW != w || _rendererH != h)
        {
            _renderer = new OfflineFrameRenderer(w, h);
            _rendererW = w;
            _rendererH = h;
        }
        return _renderer;
    }
}
