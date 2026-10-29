# Afterimage

A portable Windows screen recorder for documenting workflows, reviewing steps, and preparing automation training data. Native Windows Forms UI, built-in H.264 encoding, no downloads or third-party packages at runtime.

## Repository

The repository contains source and build instructions. Build locally to create `Afterimage.exe`. Recordings, generated annotations, personal settings, test outputs, and compiled executables are ignored by Git.

## Use

Run `Afterimage.exe`. Choose a display and output folder in **Settings**, then press **Record**. **Pause** closes the current clip; resuming starts another. **Mark** adds a workflow marker. **Review** opens the completed clips with a clickable activity timeline in your browser.

Minimizing hides the app in the system tray. Closing the window during recording also keeps recording in the tray. Use **Stop** or the tray's **Exit** command to finish. Recording is off when the app is first opened unless **Record on app launch** is enabled. The app does not register itself to start with Windows.

| Shortcut | Action |
|---|---|
| Ctrl+Alt+R | Record / stop |
| Ctrl+Alt+P | Pause / resume |
| Ctrl+Alt+M | Mark a workflow step |

## Settings

Defaults: 5 FPS, longest edge 1920 px, 2.5 Mbps target bitrate, five-minute clips. Aspect ratio is preserved and smaller screens are not upscaled. Choose 1–30 FPS, 1280–3840 px, and 1–10 Mbps. Resolution and frame rate have the largest effect on capture and encoding cost.

Recording stops at the configured folder limit (100 GB by default), or when free disk space falls below the reserve (2 GB). Existing recordings are never automatically deleted. Limits use GiB, labelled GB in the interface, and are checked about once per second. A clip finalization and encoder buffering can use additional disk space after a limit is reached. Only recorder session folders in the selected output folder count toward the folder limit.

At 2.5 Mbps the target is about 1.13 decimal GB/hour; actual files can be smaller. The live CPU/RAM indicator reports this process's resource use. Hardware encoding is allowed when available, not guaranteed. Disable it if your graphics driver has problems.

Auto annotations include observed foreground app names, mouse clicks, scrolling, and manually marked steps. Window titles are optional and off by default. These are event labels, not AI interpretations of the workflow. No microphone, system audio, typed text, network calls, or uploads are implemented. Screen pixels still contain whatever is visible; pause capture when appropriate.

## Files

Each run creates a `session-*` folder:

| File | Contents |
|---|---|
| `clip-00001.mp4` | H.264 screen recording |
| `clip-00001.srt` | Automatic subtitle labels for compatible video players |
| `clip-00001.annotations.json` | Clip-relative annotation timestamps |
| `clip-00001.review.html` | Local video player with annotations and jump-to-event controls |
| `Review.html` | Session index for completed clips |
| `frames.jsonl` | Capture sequence, requested video timestamps, monotonic elapsed time and UTC |
| `events.jsonl` | App/mouse events, markers, clip boundaries, pauses and stop reasons |
| `session.json` | Recording settings, display coordinates, final status and errors |

Keep the session files together. Open `Review.html` directly in a browser supporting H.264. No server is needed. Refresh the session index to see clips finalized after opening it. The MP4 is unchanged; captions are displayed by the review page or an SRT-capable player.

For training data, `elapsed_ms` is monotonic time since the session began. `utc` is anchored to session-start UTC, so later system clock changes do not introduce jumps. The frame log's zero-based `frame` identifies a submitted capture, not necessarily a decoded frame. Windows may quantize timestamps or duplicate/drop frames during encoding/decoding; annotations are aligned at the selected frame-rate granularity. App changes are sampled at capture time; very brief switches between captures can be missed. Mouse event coordinates are physical desktop pixels; `normalized_x/y` are relative to the selected display.

Pauses contain no screen captures and start new clips on resume. Long encoding stalls skip capture deadlines rather than building an unbounded frame queue. The timer shows recorded duration, excluding pauses.

## Requirements and limits

64-bit Windows 10/11 with .NET Framework 4.8 and Windows Media Foundation. Standard Windows installations include these components; Windows N editions may require Microsoft's Media Feature Pack. No administrator access is requested.

One display per session. The recorder uses GDI desktop capture, so its capture path is not GPU-only. GPU-protected surfaces and secure desktops cannot be recorded normally. Lock, disconnect and suspend events pause recording; a display layout change stops the session. A crash or power loss may leave the current `.partial.mp4` unreadable; previously finalized clips are preserved. Use shorter clips to reduce that exposure. The app does not claim crash recovery or unattended multi-day certification.

The executable is unsigned. Source is included for inspection and rebuilding.

## Build and verify

Run `powershell -ExecutionPolicy Bypass -File .\build.ps1` from this folder. The script uses the .NET Framework C# compiler included with Windows; no SDK or package restore is needed. Close the app before rebuilding.

`Afterimage.exe --self-test C:\path\to\test-output` runs synthetic color/orientation, segment rotation, pause/resume, subtitle/review output, decoder, and disk-reserve tests. Append `software` to exercise software encoding explicitly. It writes `test-result.json`. This mode does not capture screen pixels.

`Afterimage.exe --validate C:\path\to\session-folder` decodes completed clips and writes `validation.json` plus first-frame PNGs in that folder. It is an optional diagnostic and may take time on long sessions.

Validation performed during development: actual desktop capture at 1920 × 1200, playback decoding, mouse annotations, manual pause and stop; synthetic automatic rotation, resume, orientation/color, hardware-allowed and software encoding, and free-space protection. Short tests passed. Hardware-allowed desktop capture on this PC showed approximately 2–3% total CPU and 200–215 MB working set; values depend on driver, screen content, resolution and settings. The small executable does not imply equally small encoder memory.

API references: [Media Foundation sink writer](https://learn.microsoft.com/en-us/windows/win32/medfound/tutorial--using-the-sink-writer-to-encode-video), [hardware transform option](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-readwrite-enable-hardware-transforms), [image stride](https://learn.microsoft.com/en-us/windows/win32/medfound/image-stride).
