using System.Diagnostics;
using Singularidi.Audio;

namespace Singularidi.Midi;

public enum PlaybackState { Idle, Loaded, Playing, Paused, Finished }

public sealed class MidiPlaybackEngine : IDisposable
{
    private readonly Stopwatch _clock = new();
    private TimeSpan _pauseOffset = TimeSpan.Zero;
    private IAudioEngine? _audioEngine;
    private List<NoteEvent> _notes = new();
    private double _totalDuration;
    private int _nextNoteIndex;
    private string? _currentFilePath;

    public PlaybackState State { get; private set; } = PlaybackState.Idle;
    public IReadOnlyList<NoteEvent> Notes => _notes;
    public TimeSpan CurrentTime => _pauseOffset + _clock.Elapsed;
    public double TotalDurationSeconds => _totalDuration;

    public event Action<NoteEvent>? NoteTriggered;

    public void SetAudioEngine(IAudioEngine engine)
    {
        bool wasPlaying = State == PlaybackState.Playing;
        if (wasPlaying) _audioEngine?.Stop();
        _audioEngine?.Dispose();
        _audioEngine = engine;

        if (_currentFilePath != null && State is PlaybackState.Loaded or PlaybackState.Paused
                or PlaybackState.Playing or PlaybackState.Finished)
        {
            _audioEngine.LoadFile(_currentFilePath);
        }
    }

    public void Load(string midiFilePath)
    {
        var prevState = State;
        if (prevState == PlaybackState.Playing || prevState == PlaybackState.Paused)
            Stop();

        var (notes, duration) = MidiFileParser.Parse(midiFilePath);
        _notes = notes;
        _totalDuration = duration;
        _nextNoteIndex = 0;
        _pauseOffset = TimeSpan.Zero;
        _currentFilePath = midiFilePath;
        _audioEngine?.LoadFile(midiFilePath);
        State = PlaybackState.Loaded;
    }

    public void Play()
    {
        if (State is not (PlaybackState.Loaded or PlaybackState.Paused or PlaybackState.Finished))
            return;

        if (State == PlaybackState.Finished)
        {
            _pauseOffset = TimeSpan.Zero;
            _nextNoteIndex = 0;
        }

        _clock.Restart();
        _audioEngine?.Play();
        State = PlaybackState.Playing;
    }

    public void Pause()
    {
        if (State != PlaybackState.Playing) return;
        _pauseOffset += _clock.Elapsed;
        _clock.Reset();
        _audioEngine?.Pause();
        State = PlaybackState.Paused;
    }

    public void Stop()
    {
        if (State == PlaybackState.Idle) return;
        _clock.Reset();
        _pauseOffset = TimeSpan.Zero;
        _nextNoteIndex = 0;
        _audioEngine?.Stop();
        State = _currentFilePath != null ? PlaybackState.Loaded : PlaybackState.Idle;
    }

    /// <summary>
    /// Called by the visualizer render loop each frame to emit NoteTriggered events
    /// and detect end-of-file.
    /// </summary>
    public void UpdateNoteEvents()
    {
        if (State != PlaybackState.Playing) return;
        double now = CurrentTime.TotalSeconds;

        while (_nextNoteIndex < _notes.Count && _notes[_nextNoteIndex].StartSeconds <= now)
        {
            NoteTriggered?.Invoke(_notes[_nextNoteIndex]);
            _nextNoteIndex++;
        }

        if (_totalDuration > 0 && now >= _totalDuration)
        {
            _clock.Reset();
            State = PlaybackState.Finished;
        }
    }

    public void Dispose()
    {
        _audioEngine?.Dispose();
        _audioEngine = null;
    }
}
