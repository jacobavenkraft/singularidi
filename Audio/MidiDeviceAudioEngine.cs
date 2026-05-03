using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace Singularidi.Audio;

public sealed class MidiDeviceAudioEngine : IAudioEngine
{
    private OutputDevice? _outputDevice;
    private Playback? _playback;
    private readonly string _preferredDeviceName;
    private string? _loadedFilePath;

    public MidiDeviceAudioEngine(string preferredDeviceName)
    {
        _preferredDeviceName = preferredDeviceName;
    }

    public static IReadOnlyList<string> GetAvailableDevices()
    {
        try
        {
            return OutputDevice.GetAll().Select(d => d.Name).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void LoadFile(string midiFilePath)
    {
        DisposePlayback();
        _loadedFilePath = midiFilePath;
        InitializePlayback(midiFilePath);
    }

    private void InitializePlayback(string midiFilePath)
    {
        try
        {
            _outputDevice?.Dispose();
            _outputDevice = null;

            var devices = OutputDevice.GetAll().ToList();
            if (devices.Count == 0) return;

            var device = devices.FirstOrDefault(d => d.Name == _preferredDeviceName)
                         ?? devices[0];
            _outputDevice = device;

            var file = MidiFile.Read(midiFilePath);
            _playback = file.GetPlayback(_outputDevice);
            _playback.InterruptNotesOnStop = true;
            _playback.TrackNotes = true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MidiDeviceAudioEngine] Failed to init: {ex.Message}");
        }
    }

    public void Play()
    {
        try
        {
            _playback?.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MidiDeviceAudioEngine] Play failed: {ex.Message}");
        }
    }

    public void Pause()
    {
        try
        {
            _playback?.Stop();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MidiDeviceAudioEngine] Pause failed: {ex.Message}");
        }
    }

    public void Stop()
    {
        try
        {
            _playback?.Stop();
            _playback?.MoveToStart();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MidiDeviceAudioEngine] Stop failed: {ex.Message}");
        }
    }

    public void NoteOn(int channel, int noteNumber, int velocity)
    {
        try
        {
            EnsureOutputDevice();
            _outputDevice?.SendEvent(new NoteOnEvent
            {
                Channel = (FourBitNumber)channel,
                NoteNumber = (SevenBitNumber)noteNumber,
                Velocity = (SevenBitNumber)velocity,
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MidiDeviceAudioEngine] NoteOn failed: {ex.Message}");
        }
    }

    public void NoteOff(int channel, int noteNumber)
    {
        try
        {
            _outputDevice?.SendEvent(new NoteOffEvent
            {
                Channel = (FourBitNumber)channel,
                NoteNumber = (SevenBitNumber)noteNumber,
                Velocity = (SevenBitNumber)0,
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MidiDeviceAudioEngine] NoteOff failed: {ex.Message}");
        }
    }

    private void EnsureOutputDevice()
    {
        if (_outputDevice != null) return;
        var devices = OutputDevice.GetAll().ToList();
        if (devices.Count == 0) return;
        _outputDevice = devices.FirstOrDefault(d => d.Name == _preferredDeviceName)
                        ?? devices[0];
    }

    private void DisposePlayback()
    {
        _playback?.Stop();
        _playback?.Dispose();
        _playback = null;
    }

    public void Dispose()
    {
        DisposePlayback();
        _outputDevice?.Dispose();
        _outputDevice = null;
    }
}
