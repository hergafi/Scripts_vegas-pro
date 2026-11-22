VEGAS Pro Scripts

Free C# scripts for **VEGAS Pro** (built for VEGAS Pro 2026 / ScriptPortal.Vegas)

## Repository Contents

- `SmartZoom_v4.cs` — **Smart Zoom v4** (`Vegasaur Toolkit — Smart Zoom v4`)[cite: 4, 9]:
  - Opens a GUI panel with presets on the right (`Zoom In 5%`, `Zoom Out 5%`, `Ken Burns`, `Pan Left`, `Pan Right`, `Tilt 3°`, `Beat Pulse`)[cite: 4, 9].
  - Includes sliders for Zoom, Rotation, and Pan (X/Y), easing curves, start/end frame preview, intermediate points, and `Reset Original`[cite: 4, 9].
  - **Apply to:** Selected events, all video events on the timeline, or auto mode[cite: 4, 9].
  - Custom presets are saved automatically to `Documents\VegasaurToolkit\SmartZoomPresets.txt`[cite: 4, 9].

- `Smart Batch Stabilization.cs` — **Smart Batch Stabilization**[cite: 5, 9]:
  - Applies and manages VEGAS `Video Stabilization` (`Smooth motion` and `Freeze motion`, presets `(Default)`, `full`, `1.4`, `7.0`, `Smoothing` from `0.0` to `10.0`, `Trigger analysis`, `Skip existing`, `Apply Forced`, and `RESET`)[cite: 5, 9].
  - Supports **Event FX** (via subclip take for segment-only stabilization) and **Media FX** (whole media), with automatic project saving when modified[cite: 5, 9].
  - Displays operations in a live English log box, saves custom presets to `Documents\SmartStabilization\presets.txt`, and writes logs to the Desktop[cite: 5, 9].
  - Plays audio cues from `Sounds\success.mp3` and `Sounds\failed.mp3`[cite: 5, 9].

- `Sounds\success.mp3` & `Sounds\failed.mp3` — Audio feedback files copied into `Script Menu\Sounds` during installation[cite: 3, 6, 9].
- `Install.cmd` — Automated installer script for VEGAS Pro `Script Menu` folders[cite: 3, 6, 9].
- `LICENSE.txt` / `README.txt` — Licensing and product notes (if included in your package)[cite: 9].

## Installation (`Install.cmd`)

1. Close **VEGAS Pro** if it is currently running[cite: 6, 9].
2. Extract the `.zip` archive completely (do not run directly from inside a compressed archive) and double-click `Install.cmd` (it automatically detects VEGAS `Script Menu` folders and requests administrator rights only when needed)[cite: 6, 9].
3. Open **VEGAS Pro** and launch the scripts from **Script Menu**[cite: 6, 9].

> **Notes:**
> - The interface and logs are in English[cite: 5, 9].
> - Newly placed scripts may take a moment to appear in `Script Menu`[cite: 9].
> - To uninstall later, run `Install.cmd /uninstall`[cite: 6].

## Links

- **Repository:** [github.com/hergafi/Scripts_vegas-pro](https://github.com/hergafi/Scripts_vegas-pro)[cite: 8]
