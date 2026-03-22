# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build
dotnet build

# Run
dotnet run

# Publish self-contained Windows executable
dotnet publish -c Release -r win-x64 --self-contained
```

There are no tests in this project.

## Architecture

**Singularidi** is a cross-platform MIDI visualizer: falling-note animation + piano keyboard, with software synthesis or hardware MIDI output.

### Data Flow

1. User opens MIDI file → `MainWindowViewModel.OnMidiFileOpened()` → `MidiPlaybackEngine.Load()` → `MidiFileParser.Parse()` + `IAudioEngine.LoadFile()`
2. User clicks Play → `MidiPlaybackEngine.Play()` → `IAudioEngine.Play()`
3. `NoteVisualizerControl`'s 60fps `DispatcherTimer` calls `MidiPlaybackEngine.UpdateNoteEvents()` each tick, then redraws falling notes and piano keys

Audio (NAudio/DryWetMidi) runs independently on its own thread; `MidiPlaybackEngine`'s `Stopwatch`-based clock is the single source of truth for `CurrentTime`.

### Key Abstractions

- **`IAudioEngine`** (`Audio/`) — interface with `LoadFile/Play/Pause/Stop/Dispose`. Two implementations:
  - `SoundFontAudioEngine` — implements `IWaveProvider`; NAudio calls `Read()` on the audio thread to pull PCM16 from MeltySynth
  - `MidiDeviceAudioEngine` — wraps DryWetMidi `Playback` to send MIDI to hardware output
- **`MidiPlaybackEngine`** (`Midi/`) — state machine (Idle/Loaded/Playing/Paused/Finished), owns the master `Stopwatch` clock, holds parsed `NoteEvent` list, delegates all sound to `IAudioEngine`
- **`NoteVisualizerControl`** (`Controls/`) — custom Avalonia `Control`; renders entirely in `OnRender`. Notes fall top→bottom; Y position: `yBottom = vizHeight - (startSeconds - now) / LookAheadSeconds * vizHeight` with a 4-second lookahead. Piano keyboard occupies bottom 15% of the control.
- **`MainWindowViewModel`** (`ViewModels/`) — `INotifyPropertyChanged`, all commands, switches audio engine at runtime via `RebuildAudioEngine()`

### Configuration

`ConfigService` persists `AppConfig` as JSON to `%APPDATA%\Singularidi\config.json`. Properties: `SoundFontPath`, `OutputMode` (SoundFont | MidiDevice), `PreferredMidiDevice`, `HighlightActiveNotes`, `LastMidiFilePath`.

### UI Layout

`MainWindow.axaml` uses a `DockPanel`: menu bar → toolbar (Open/Play/Pause/Stop + status) → `NoteVisualizerControl` fills remaining space. Menu items for audio output mode and MIDI device selection are populated dynamically at runtime.

## Dependencies

| Library | Version | Purpose |
|---|---|---|
| Avalonia | 11.2.3 | UI (Fluent dark theme) |
| Melanchall.DryWetMidi | 8.0.3 | MIDI parsing + hardware output |
| MeltySynth | 2.2.0 | SoundFont synthesis |
| NAudio | 2.2.1 | Audio streaming (WaveOutEvent — no native DLL) |

**Note:** DryWetMidi 8.4.0 does not exist — do not upgrade past 8.0.3 without verifying the version exists. `FilePickerFileType.All` does not exist in Avalonia 11; use inline `FilePickerFileType` definitions instead.
