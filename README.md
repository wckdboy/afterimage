# Afterimage

A portable Windows screen recorder for documenting workflows, reviewing steps, and preparing automation training data. Native Windows Forms UI on .NET 10, Direct3D desktop capture, and Windows H.264 encoding. No browser engine, background service, account, or cloud upload.

## Repository

The repository contains source and build instructions. Build locally to create `Afterimage.exe`. Recordings, generated annotations, personal settings, test outputs, and compiled executables are ignored by Git.

## Use

Run `Afterimage.exe`. Name the session, choose a display and quality preset, then press **Record**. Fine-tune capture and storage in **Settings**. **Pause** closes the current clip; resuming starts another. **Mark** adds a workflow marker. **Review** opens the latest completed clips with a clickable activity timeline in your browser. Recent session cards persist across restarts; **View all** opens a searchable library. Older sessions without previews remain usable.

Minimizing hides the app in the system tray. Closing the window during recording also keeps recording in the tray. Use **Stop** or the tray's **Exit** command to finish. Recording is off when the app is first opened unless **Record on app launch** is enabled. The app does not register itself to start with Windows.

| Shortcut | Action |
|---|---|
| Ctrl+Alt+R | Record / stop |
| Ctrl+Alt+P | Pause / resume |
| Ctrl+Alt+M | Mark a workflow step |

## Settings

Presets: **Lean** = 2 FPS / 1280 px / 1 Mbps; **Balanced** = 5 FPS / 1920 px / 2.5 Mbps; **Detailed** = 15 FPS / 2560 px / 5 Mbps. Default: Balanced, five-minute clips. Aspect ratio is preserved and smaller screens are not upscaled. Choose 1–30 FPS, 1280–3840 px, and 1–10 Mbps. Resolution and frame rate have the largest effect on capture and encoding cost.

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
| `session.json` | Session name, settings, duration, capture backend, status and errors |
| `thumbnail.jpg` | Local preview of the first captured frame |

Keep the session files together. Open `Review.html` directly in a browser supporting H.264. No server is needed. Refresh the session index to see clips finalized after opening it. The MP4 is unchanged; captions are displayed by the review page or an SRT-capable player.

For training data, `elapsed_ms` is monotonic time since the session began. `utc` is anchored to session-start UTC, so later system clock changes do not introduce jumps. The frame log's zero-based `frame` identifies a submitted capture, not necessarily a decoded frame. Windows may quantize timestamps or duplicate/drop frames during encoding/decoding; annotations are aligned at the selected frame-rate granularity. App changes are sampled at capture time; very brief switches between captures can be missed. Mouse event coordinates are physical desktop pixels; `normalized_x/y` are relative to the selected display.

Review annotations that land during encoder startup or after the last submitted frame are clamped to the nearest playable instant. `source_at_ms` preserves the original clip-relative offset; the full event log retains original elapsed time and UTC.

Pauses contain no screen captures and start new clips on resume. Long encoding stalls skip capture deadlines rather than building an unbounded frame queue. The timer shows recorded duration, excluding pauses.

## Requirements and limits

64-bit Windows 10/11, the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), and Windows Media Foundation. This is a framework-dependent build: keep the EXE, DLLs, and runtime JSON files together. The app files total about 1.9 MiB, excluding the shared .NET runtime. Windows N editions may need Microsoft's Media Feature Pack.

One display per session. Automatic capture uses Vortice Direct3D11/DXGI 3.8.3 and Windows Desktop Duplication. Vortice MediaFoundation 3.8.3 provides typed encoder bindings. Versions and dependency hashes are locked. Capture uses one reusable staging texture with CPU readback and scaling; it is not a zero-copy GPU pipeline. Unsupported/rotated displays, capture errors, and pointer-disabled recording use the GDI compatibility path. You can select Compatibility explicitly. HDR fidelity and device-loss recovery have not been validated. GPU-protected surfaces and secure desktops cannot be recorded normally. Lock, disconnect and suspend events pause recording; a display layout change stops the session. A crash or power loss may leave the current `.partial.mp4` unreadable; previously finalized clips are preserved. Use shorter clips to reduce that exposure. The app does not claim crash recovery or unattended multi-day certification.

The executable is unsigned. Source is included for inspection and rebuilding.

## Build and verify

Install the .NET 10 SDK, then run `powershell -ExecutionPolicy Bypass -File .\build.ps1`. The script restores locked NuGet packages and publishes into this folder. A custom SDK can be passed with `-DotNet C:\path\to\dotnet.exe`. Close the app before rebuilding. Dependency licenses are in `THIRD-PARTY-NOTICES.txt`.

`Afterimage.exe --self-test C:\path\to\test-output` runs synthetic color/orientation, segment rotation, pause/resume, subtitle/review output, marker serialization across clip boundaries, thumbnail/library persistence, decoder, and disk-reserve tests. Append `software` to exercise software encoding explicitly. It writes `test-result.json`. This mode does not capture screen pixels.

`Afterimage.exe --validate C:\path\to\session-folder` decodes completed clips and writes `validation.json` plus first-frame PNGs in that folder. It is an optional diagnostic and may take time on long sessions.

Validation performed during development: actual desktop capture at 1920 × 1200, playback decoding, mouse annotations, manual pause and stop; synthetic automatic rotation, resume, orientation/color, hardware-allowed and software encoding, and free-space protection. Short tests passed. Hardware-allowed desktop capture on this PC showed approximately 1–2% total CPU and 221–229 MB working set with Direct3D capture; values depend on driver, screen content, resolution and settings. The small executable does not imply equally small encoder memory.

API references: [Media Foundation sink writer](https://learn.microsoft.com/en-us/windows/win32/medfound/tutorial--using-the-sink-writer-to-encode-video), [hardware transform option](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-readwrite-enable-hardware-transforms), [image stride](https://learn.microsoft.com/en-us/windows/win32/medfound/image-stride).

Design references: [Arcade recording controls](https://mobbin.com/screens/15fed3f5-ec2e-45f6-bde0-cc53fd949eff) and [Loom video library](https://mobbin.com/screens/f2cf240e-0a9c-4935-ac4a-20c76c231c8d), viewed through Mobbin. These are desktop web references; the implementation uses native Windows controls.
