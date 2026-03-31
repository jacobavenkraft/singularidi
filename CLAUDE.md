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

**Singularidi** is a cross-platform MIDI visualizer with multiple visualization engines, a theming system, and MP4 video export. Supports software synthesis or hardware MIDI output.

### Data Flow

1. User opens MIDI file → `MainWindowViewModel.OnMidiFileOpened()` → `MidiPlaybackEngine.Load()` → `MidiFileParser.Parse()` + `IAudioEngine.LoadFile()`
2. User clicks Play → `MidiPlaybackEngine.Play()` → `IAudioEngine.Play()`
3. `NoteVisualizerControl`'s 60fps `DispatcherTimer` calls `MidiPlaybackEngine.UpdateNoteEvents()` each tick, then delegates rendering to the active `IVisualizationEngine`

Audio (NAudio/DryWetMidi) runs independently on its own thread; `MidiPlaybackEngine`'s `Stopwatch`-based clock is the single source of truth for `CurrentTime`.

### Key Abstractions

- **`IAudioEngine`** (`Audio/`) — interface with `LoadFile/Play/Pause/Stop/Dispose`. Two implementations:
  - `SoundFontAudioEngine` — implements `IWaveProvider`; NAudio calls `Read()` on the audio thread to pull PCM16 from MeltySynth
  - `MidiDeviceAudioEngine` — wraps DryWetMidi `Playback` to send MIDI to hardware output
- **`MidiPlaybackEngine`** (`Midi/`) — state machine (Idle/Loaded/Playing/Paused/Finished), owns the master `Stopwatch` clock, holds parsed `NoteEvent` list, delegates all sound to `IAudioEngine`
- **`IVisualizationEngine`** (`Visualization/`) — interface for pluggable visualization rendering. Receives `DrawingContext`, dimensions, notes, time, theme, and active-key state per frame. Two implementations:
  - `VerticalFallEngine` — classic top-to-bottom falling notes with piano at the bottom
  - `HorizontalCrawlEngine` — 3D perspective (1/z projection) with notes approaching from a vanishing point, configurable sky fraction, depth ratio, and guide line fading
- **`PianoLayout`** (`Visualization/`) — shared piano key geometry using "equal segments within groups" approach. CDE group = 5 equal segments, FGAB group = 7 equal segments. 75 white keys with uniform bottom widths and per-group segment widths at the top. Caches `XCenter[128]`, `NoteWidth[128]`, `KeyTopLeft/Right[128]`, `WhiteKeyBottomLeft/Right[128]`, `GuideXUniform[128]`, `OctaveBoundaryX`
- **`ColorHelper`** (`Visualization/`) — note color resolution (channel/track/overrides) and color lerping
- **`GuideLineStyle`** (`Visualization/`) — enum: `KeyWidthCentered`, `UniformCentered`, `Octave`
- **`NoteVisualizerControl`** (`Controls/`) — thin Avalonia `Control` host; delegates all drawing to the active `IVisualizationEngine`
- **`IVisualTheme` / `ThemeData`** (`Themes/`) — theme interface and serializable JSON implementation. Supports background/guide colors, note shape (Rectangular/DotBlock), corner radius, channel colors (16), track colors (variable), per-note and per-key color overrides, active highlight blending
- **`ThemeRegistry`** (`Themes/`) — manages built-in + custom themes, persists custom themes to AppConfig
- **`MainWindowViewModel`** (`ViewModels/`) — `INotifyPropertyChanged` via CommunityToolkit.Mvvm, all commands, visualization/theme/guide-line switching, audio engine management, MP4 export orchestration

### Configuration

`ConfigService` persists `AppConfig` as JSON to `%APPDATA%\Singularidi\config.json`. Properties: `SoundFontPath`, `OutputMode`, `PreferredMidiDevice`, `HighlightActiveNotes`, `LastMidiFilePath`, `ThemeName`, `VisualizationType`, `GuideLineStyle`, `CustomThemes`, `FfmpegPath`, `ExportWidth`, `ExportHeight`, `ExportFps`.

### UI Layout

`MainWindow.axaml` uses a `DockPanel`: menu bar → toolbar (Open/Play/Pause/Stop + status) → `NoteVisualizerControl` fills remaining space. Menus: File (Open, Export to MP4), View (Visualization, Guide Lines, Theme, Highlight Active Notes), Audio (output mode, device selection). `ThemeEditorWindow` provides a full theme editing UI.

### MP4 Export Pipeline (`Export/`)

- `OfflineAudioRenderer` — renders MIDI to WAV via MeltySynth (no NAudio playback, direct buffer writing)
- `OfflineFrameRenderer` — captures visualization frames to raw pixel data via Avalonia's `RenderTargetBitmap`
- `Mp4Exporter` — orchestrates: render WAV → pipe raw frames to FFmpeg stdin → produce MP4
- `ExportSettings` — resolution, FPS, optional FFmpeg path override
- Requires FFmpeg installed on the system (not bundled)

## Dependencies

| Library | Version | Purpose |
|---|---|---|
| Avalonia | 11.2.3 | UI (Fluent dark theme) |
| CommunityToolkit.Mvvm | latest | ObservableProperty, RelayCommand source generators |
| Melanchall.DryWetMidi | 8.0.3 | MIDI parsing + hardware output |
| MeltySynth | 2.2.0 | SoundFont synthesis |
| NAudio | 2.2.1 | Audio streaming (WaveOutEvent — no native DLL) |

**Note:** DryWetMidi 8.4.0 does not exist — do not upgrade past 8.0.3 without verifying the version exists. `FilePickerFileType.All` does not exist in Avalonia 11; use inline `FilePickerFileType` definitions instead.
