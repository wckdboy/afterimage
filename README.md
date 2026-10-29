# Afterimage

A small Windows workspace for recording workflows, keeping notes, and chatting with AI. Native Windows Forms UI on .NET 10, Direct3D desktop capture, and Windows H.264 encoding. No browser engine or background service. Recording and notes work offline; AI is optional.

![Afterimage logo](assets/afterimage.svg)

## Repository

The repository contains source and build instructions. Build locally to create `Afterimage.exe`. Recordings, generated annotations, personal settings, test outputs, and compiled executables are ignored by Git.

## Use

Run `Afterimage.exe`. A small island appears at the top center of your selected display. At rest it shows only the logo and a status light, with no wordmark. Hover to reveal **Record**, **Chat**, and **Notes**. While recording, these become **Mark**, **Pause / Resume**, and **Stop**. A timer appears only during recording; **AI** appears while a reply is being generated. The timer excludes pauses. The island does not take keyboard focus from your workflow.

Use **⋯ → Position** for **Left**, **Center**, or **Right**; the position is saved. **⋯ → Recording settings** opens the main recorder window. **⋯ → Sessions** opens the searchable library. **Chat** and **Notes** remain available in this menu during recording. **Hide controls** hides the island; the tray's **Show floating controls** brings it back. Disable **Floating controls** in Settings to use the main window on launch instead.

**Pause** closes the current clip; resuming starts another. **Mark** adds a workflow marker. **Review** opens completed clips with a clickable activity timeline in your browser. Recent session cards persist across restarts; **View all** opens a searchable library. Older sessions without previews remain usable.

Minimizing hides the main window. Closing it leaves the floating controls running, and always preserves an active recording. Use **Stop** to finish a recording or **Exit** in the pill/tray menu to quit. Recording is off when first opened unless **Record on app launch** is enabled. The app does not register itself to start with Windows.

The island is **56 × 26 logical pixels** at rest and 324 × 100 when expanded, plus a six-pixel transparent shadow margin. It grows only for recording time or AI activity. It follows the selected display and avoids the taskbar. Its glass surface uses supersampled alpha blending. A faster damped spring preserves motion when hover reverses; controls become usable before the animation finishes. Drawing buffers are reused, display bounds are cached, and animation stops at rest. Recording status updates every 100 ms without repeating disk checks. Expansion respects the Windows client-animation preference. Windows 10 version 2004 and newer exclude the island and menus from capture through `WDA_EXCLUDEFROMCAPTURE`; older Windows versions may include the controls. Clicks on the island and menus are excluded from workflow annotations.

| Shortcut | Action |
|---|---|
| Ctrl+Alt+R | Record / stop |
| Ctrl+Alt+P | Pause / resume |
| Ctrl+Alt+M | Mark a workflow step |
| Ctrl+Alt+Space | Open chat |

## Chat and notes

Open **Chat → Connect**. The default is **Local · Ollama**, at `http://localhost:11434`. Start Ollama with a model already installed, select **Find models**, choose a model, and save. Afterimage does not install or download models. Local model memory and GPU use are separate from the recorder and depend on the chosen model.

Optional connections: **OpenAI** uses the Responses API; **Compatible API** uses a `/v1`-style base URL and streaming Chat Completions. Enter your own key and model. Model discovery is asynchronous; a model name can also be entered manually. Remote servers require HTTPS; HTTP is allowed only on loopback. API keys are protected using Windows DPAPI for your account, never saved as plaintext or copied to a changed endpoint. API use may incur the provider's charges.

Replies stream into the panel. **Stop** cancels the request; recording controls remain responsive. Enter sends; Shift+Enter adds a line. Closing the panel hides it and lets a running reply finish, with an **AI** indicator on the island. **New chat** clears the current conversation; **Export** saves a text copy. Launching Afterimage again opens the existing workspace instead of leaving a hidden duplicate dialog.

**Attach** lets you pick a `clip-*.annotations.json` file and review the exact text before attaching it. Only the selected text and chat history are sent to the selected model when you press Send. Attachments are capped at 180 events / approximately 18,000 characters, with a visible truncation note. Video, screenshots, recordings folders, and notes are never attached automatically. This is text-based workflow assistance, not video understanding or autonomous computer control. The AI cannot start recordings, execute code, or operate other apps.

**Notes** autosave locally. **Use in chat** copies the selection (or the note) into the composer for review. **Mark selection** adds up to 240 characters as a named recording marker while recording is active. Notes and chat survive app restarts. **Export** saves the current note as text.

Workspace data and connection settings live in `%LOCALAPPDATA%\Afterimage\workspace.json` and `ai-connection.json`. Notes and chats are ordinary local text data; API keys alone are encrypted. Recording preferences and recordings retain their existing paths. The AI panel and normal windows can appear in a recording, just like other apps; only the floating island and its menus are excluded.

## Settings

Presets: **Lean** = 2 FPS / 1280 px / 1 Mbps; **Balanced** = 5 FPS / 1920 px / 2.5 Mbps; **Detailed** = 15 FPS / 2560 px / 5 Mbps. Default: Balanced, five-minute clips. Aspect ratio is preserved and smaller screens are not upscaled. Choose 1–30 FPS, 1280–3840 px, and 1–10 Mbps. Resolution and frame rate have the largest effect on capture and encoding cost.

Recording stops at the configured folder limit (100 GB by default), or when free disk space falls below the reserve (2 GB). Existing recordings are never automatically deleted. Limits use GiB, labelled GB in the interface, and are checked about once per second. A clip finalization and encoder buffering can use additional disk space after a limit is reached. Only recorder session folders in the selected output folder count toward the folder limit.

At 2.5 Mbps the target is about 1.13 decimal GB/hour; actual files can be smaller. The live CPU/RAM indicator reports this process's resource use. Hardware encoding is allowed when available, not guaranteed. Disable it if your graphics driver has problems.

Auto annotations include observed foreground app names, mouse clicks, scrolling, and manually marked steps. Window titles are optional and off by default. These are event labels, not AI interpretations of the workflow. Recording does not capture microphone/system audio or log typed keys. It makes no network requests. AI requests occur only when you explicitly find models or send a chat message. Screen pixels still contain whatever is visible.

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

The executable is unsigned. Source is included for inspection and rebuilding. `Afterimage.exe --quit` asks the running instance to finish its recording, save the workspace and exit; wait for that process to exit before replacing app files.

## Build and verify

Install the .NET 10 SDK, then run `powershell -ExecutionPolicy Bypass -File .\build.ps1`. The script restores locked NuGet packages and publishes into this folder. A custom SDK can be passed with `-DotNet C:\path\to\dotnet.exe`. Close the app before rebuilding. Dependency licenses are in `THIRD-PARTY-NOTICES.txt`.

`Afterimage.exe --self-test C:\path\to\test-output` runs synthetic color/orientation, segment rotation, pause/resume, subtitle/review output, marker serialization across clip boundaries, thumbnail/library persistence, decoder, and disk-reserve tests. Append `software` to exercise software encoding explicitly. It writes `test-result.json`. This mode does not capture screen pixels.

`Afterimage.exe --validate C:\path\to\session-folder` decodes completed clips and writes `validation.json` plus first-frame PNGs in that folder. It is an optional diagnostic and may take time on long sessions.

`Afterimage.exe --island-test C:\path\to\test-output` exercises the floating buttons and controller with synthetic pixels: hidden-window startup, native pointer hit testing, record/pause/resume/stop, finalized clip decoding, capture-exclusion flags, all three anchors, negative display coordinates, collapse and main-window close behavior. It also checks alpha edges, idle repainting, spring reversal and refresh-rate independence. It writes `island-result.json`, rendered control states at 100–200% scaling, and actual animation frames with timestamps. Native window/control tests were run at 200% display scaling.

`Afterimage.exe --assistant-test C:\path\to\test-output` checks protocol parsing with fragmented UTF-8, Ollama/OpenAI/compatible streaming fixtures, cancellation, failed/truncated responses, model discovery, explicit context limits, DPAPI, ordered atomic writes, single-instance messages, responsive UI during streaming, DPI layout, and note/chat persistence. It renders the actual panel for review. It uses synthetic replies and sends no requests to a real AI provider. Run it under your normal Windows account, since restricted service accounts may not have a usable DPAPI profile.

Validation performed during development: actual desktop capture at 1920 × 1200, playback decoding, mouse annotations, manual pause and stop; synthetic automatic rotation, resume, orientation/color, hardware-allowed and software encoding, and free-space protection. Short tests passed. Hardware-allowed desktop capture on this PC showed approximately 1–2% total CPU and 221–229 MB working set with Direct3D capture; values depend on driver, screen content, resolution and settings. The small executable does not imply equally small encoder memory.

API references: [Media Foundation sink writer](https://learn.microsoft.com/en-us/windows/win32/medfound/tutorial--using-the-sink-writer-to-encode-video), [hardware transform option](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-readwrite-enable-hardware-transforms), [image stride](https://learn.microsoft.com/en-us/windows/win32/medfound/image-stride), [layered-window composition](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows), [Ollama chat](https://docs.ollama.com/api/chat), [Ollama models](https://docs.ollama.com/api/tags), [OpenAI streaming](https://developers.openai.com/api/docs/guides/streaming-responses).

Design references: [Arcade recording controls](https://mobbin.com/screens/15fed3f5-ec2e-45f6-bde0-cc53fd949eff), [Loom video library](https://mobbin.com/screens/f2cf240e-0a9c-4935-ac4a-20c76c231c8d), and [Riverside recording toolbar](https://mobbin.com/screens/515364d5-121f-45b8-a9dd-13353c69a21a), viewed through Mobbin. These are desktop web references; the floating island follows the requested shape and uses native Windows controls.

The companion panel keeps conversation content above a persistent composer, informed by the desktop web chat screens from [Attio](https://mobbin.com/screens/3fd10268-cb96-42b2-8886-0ab9dcf50793) and [Searchable](https://mobbin.com/screens/77132c86-87b3-4223-a05b-3c14af96d3d2), viewed through Mobbin. Afterimage retains its own dark palette and native controls.
