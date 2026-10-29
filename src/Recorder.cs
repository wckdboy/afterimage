using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Text.Json;

namespace Afterimage
{
    public sealed class Settings
    {
        public string OutputFolder { get; set; }
        public string Monitor { get; set; }
        public int Fps { get; set; }
        public int MaxDimension { get; set; }
        public int Bitrate { get; set; }
        public int SegmentSeconds { get; set; }
        public int MaxStorageGB { get; set; }
        public int MinFreeGB { get; set; }
        public bool Cursor { get; set; }
        public bool WorkflowEvents { get; set; }
        public bool WindowTitles { get; set; }
        public bool Hardware { get; set; }
        public bool StartOnLaunch { get; set; }
        public string CaptureMode { get; set; } = "auto";
        public string SessionName { get; set; } = "";
        internal bool TestPattern;
        public Settings() { OutputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recordings"); Fps = 5; MaxDimension = 1920; Bitrate = 2500000; SegmentSeconds = 300; MaxStorageGB = 100; MinFreeGB = 2; Cursor = true; Hardware = true; WorkflowEvents = true; }
        internal void Validate()
        {
            if (CaptureMode != "auto" && CaptureMode != "compatibility") CaptureMode = "auto";
            SessionName = (SessionName ?? "").Trim(); if (SessionName.Length > 160) SessionName = SessionName.Substring(0, 160);
            if (Fps < 1 || Fps > 30 || MaxDimension < 320 || MaxDimension > 3840 || Bitrate < 250000 || Bitrate > 20000000 || SegmentSeconds < 1 || SegmentSeconds > 3600 || MaxStorageGB < 1 || MinFreeGB < 1)
                throw new ArgumentException("Recording settings are outside their supported range.");
            OutputFolder = Path.GetFullPath(OutputFolder);
            if (OutputFolder.StartsWith(@"\\")) throw new ArgumentException("Use a local disk for continuous recording.");
        }
        internal Settings Copy() { return (Settings)MemberwiseClone(); }
    }
    internal static class Json
    {
        private static readonly JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields = true };
        internal static string Encode(object value) { return JsonSerializer.Serialize(value, options); }
        internal static void Save(string path, object value)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, Encode(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        internal static Settings Load(string path) { try { if (File.Exists(path)) { Settings s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)); s.Validate(); return s; } } catch { } return new Settings(); }
    }
    internal sealed class Recorder
    {
        internal readonly Settings Config;
        internal readonly Rectangle Bounds;
        internal string SessionFolder { get; private set; }
        internal volatile string State = "Starting";
        internal volatile string CaptureBackend = "Starting";
        internal string Error { get; private set; }
        internal string StopReason { get; private set; }
        internal long Frames { get { return Interlocked.Read(ref frames); } }
        internal long Bytes { get { return Interlocked.Read(ref totalBytes); } }
        internal bool IsAlive { get { return thread != null && thread.IsAlive; } }
        internal bool ManualPaused { get { return manualPaused; } }
        internal bool EventsEnabled { get { return Config.WorkflowEvents && State == "Recording"; } }
        internal double ElapsedSeconds { get { return clock.Elapsed.TotalSeconds; } }
        internal double RecordedSeconds { get { return Interlocked.CompareExchange(ref recordedMilliseconds, 0, 0) / 1000; } }
        internal int SegmentCount { get { return segmentIndex; } }
        private Thread thread;
        private volatile bool stop, manualPaused, systemPaused;
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Stopwatch clock = new Stopwatch();
        private DateTime startUtc;
        private long frames, totalBytes, previousBytes;
        private double recordedMilliseconds, completedMilliseconds;
        private int segmentIndex, queuedEvents;
        private readonly ConcurrentQueue<Dictionary<string, object>> queue = new ConcurrentQueue<Dictionary<string, object>>();
        private StreamWriter events, frameLog;
        private VideoWriter video;
        private string segmentName, partialPath;
        private double segmentStart = -1, lastVideoMs;
        private long segmentFrames;
        private bool segmentHardware;
        private IntPtr lastWindow;
        private string lastTitle;
        private readonly AnnotationTrack annotations = new AnnotationTrack();

        internal Recorder(Settings settings, Rectangle bounds) { Config = settings.Copy(); Config.Validate(); Bounds = bounds; }
        internal void Start() { if (thread != null) throw new InvalidOperationException(); thread = new Thread(Run) { IsBackground = true, Name = "Screen capture", Priority = ThreadPriority.BelowNormal }; thread.SetApartmentState(ApartmentState.MTA); thread.Start(); }
        internal void Stop(string reason) { StopReason = reason; stop = true; wake.Set(); }
        internal void Pause(bool value) { manualPaused = value; wake.Set(); }
        internal void SystemPause(bool value) { systemPaused = value; wake.Set(); }
        internal void Wait() { if (thread != null) thread.Join(); }
        internal void AddClick(string button, int x, int y, int wheel)
        {
            if (!EventsEnabled || !Bounds.Contains(x, y)) return;
            Dictionary<string, object> e = Stamp("mouse_" + button);
            e["screen_x"] = x; e["screen_y"] = y;
            e["normalized_x"] = (double)(x - Bounds.X) / Bounds.Width; e["normalized_y"] = (double)(y - Bounds.Y) / Bounds.Height;
            if (wheel != 0) e["wheel_delta"] = wheel;
            Enqueue(e);
        }
        internal void Mark()
        {
            if (State == "Recording") Enqueue(Stamp("workflow_marker"));
        }
        private void Enqueue(Dictionary<string, object> item)
        {
            if (Interlocked.Increment(ref queuedEvents) > 4096) { Interlocked.Decrement(ref queuedEvents); return; }
            queue.Enqueue(item);
        }
        private Dictionary<string, object> Stamp(string kind)
        {
            double ms = clock.Elapsed.TotalMilliseconds;
            return new Dictionary<string, object> { { "type", kind }, { "elapsed_ms", Math.Round(ms, 3) }, { "utc", startUtc.AddMilliseconds(ms).ToString("o") } };
        }
        private void Log(string kind, params object[] fields)
        {
            Dictionary<string, object> e = Stamp(kind);
            for (int i = 0; i < fields.Length; i += 2) e[(string)fields[i]] = fields[i + 1];
            events.WriteLine(Json.Encode(e));
            if (video != null && segmentStart >= 0) annotations.Add(e);
        }
        private void DrainEvents() { Dictionary<string, object> item; while (queue.TryDequeue(out item)) { Interlocked.Decrement(ref queuedEvents); events.WriteLine(Json.Encode(item)); if (video != null && segmentStart >= 0) annotations.Add(item); } }
        private void WindowEvent()
        {
            if (!Config.WorkflowEvents) return;
            IntPtr window = Win.GetForegroundWindow();
            string title = null;
            if (Config.WindowTitles) { StringBuilder b = new StringBuilder(1024); Win.GetWindowText(window, b, b.Capacity); title = b.ToString(); }
            if (window == lastWindow && title == lastTitle) return;
            lastWindow = window; lastTitle = title;
            uint pid; Win.GetWindowThreadProcessId(window, out pid); string name = "unknown";
            try { using (Process p = Process.GetProcessById((int)pid)) name = p.ProcessName; } catch { }
            if (Config.WindowTitles) Log("foreground_app", "process", name, "process_id", pid, "window_title", title);
            else Log("foreground_app", "process", name, "process_id", pid);
        }
        private void CheckStorage()
        {
            ulong available, total, free;
            if (!Win.GetDiskFreeSpaceEx(Config.OutputFolder, out available, out total, out free)) throw new IOException("Cannot check available disk space.");
            long current = 0;
            foreach (string f in Directory.EnumerateFiles(SessionFolder)) current += new FileInfo(f).Length;
            Interlocked.Exchange(ref totalBytes, previousBytes + current);
            if (available < (ulong)Config.MinFreeGB * 1024 * 1024 * 1024) throw new StorageLimitException("Stopped: free disk space reached the " + Config.MinFreeGB + " GB reserve.");
            if (Bytes >= (long)Config.MaxStorageGB * 1024 * 1024 * 1024) throw new StorageLimitException("Stopped: recordings reached the " + Config.MaxStorageGB + " GB folder limit.");
        }
        private void OpenSegment(ScreenCapture capture)
        {
            CheckStorage(); segmentIndex++; segmentFrames = 0; segmentStart = -1; lastVideoMs = 0;
            segmentName = "clip-" + segmentIndex.ToString("D5") + ".mp4";
            partialPath = Path.Combine(SessionFolder, "clip-" + segmentIndex.ToString("D5") + ".partial.mp4");
            segmentHardware = Config.Hardware;
            try { video = new VideoWriter(partialPath, capture.Width, capture.Height, Config.Fps, Config.Bitrate, segmentHardware); }
            catch (Exception first) {
                if (!Config.Hardware) throw;
                if (File.Exists(partialPath)) File.Delete(partialPath); // Only this newly created, empty segment.
                segmentHardware = false;
                Log("encoder_fallback", "reason", first.Message);
                video = new VideoWriter(partialPath, capture.Width, capture.Height, Config.Fps, Config.Bitrate, false);
            }
        }
        private void CloseSegment()
        {
            if (video == null) return;
            DrainEvents();
            VideoWriter v = video; video = null;
            v.Dispose();
            if (segmentFrames > 0) {
                File.Move(partialPath, Path.Combine(SessionFolder, segmentName));
                Log("segment_closed", "file", segmentName, "frames", segmentFrames, "start_elapsed_ms", Math.Round(segmentStart, 3), "duration_ms", Math.Round(lastVideoMs + 1000.0 / Config.Fps, 3));
                annotations.Save(SessionFolder, segmentName, lastVideoMs + 1000.0 / Config.Fps);
                completedMilliseconds += lastVideoMs + 1000.0 / Config.Fps;
            } else if (File.Exists(partialPath)) File.Delete(partialPath);
            frameLog.Flush(); events.Flush();
        }
        private void SaveSession(string state)
        {
            Json.Save(Path.Combine(SessionFolder, "session.json"), new {
                schema_version = 1, application = "Afterimage", status = state,
                started_utc = startUtc.ToString("o"), elapsed_seconds = Math.Round(clock.Elapsed.TotalSeconds, 3),
                name = Config.SessionName, recorded_seconds = Math.Round(RecordedSeconds, 3), capture_backend = CaptureBackend,
                frames = Frames, segments = segmentIndex, stop_reason = StopReason, error = Error,
                capture_bounds = new { x = Bounds.X, y = Bounds.Y, width = Bounds.Width, height = Bounds.Height },
                settings = Config, timing = "Each submitted capture includes elapsed_ms, UTC and requested video_time_ms. Codec timestamps may be quantized to the frame rate. Pauses contain no captures."
            });
        }
        private void Run()
        {
            bool com = false, mf = false;
            try {
                MF.Check(MF.CoInitializeEx(IntPtr.Zero, 0)); com = true;
                MF.Check(MF.MFStartup(0x00020070, 0)); mf = true;
                Directory.CreateDirectory(Config.OutputFolder);
                // Count only recorder-created session directories. Never remove old recordings.
                foreach (string d in Directory.EnumerateDirectories(Config.OutputFolder, "session-*"))
                    if (File.Exists(Path.Combine(d, "session.json"))) foreach (string f in Directory.EnumerateFiles(d)) previousBytes += new FileInfo(f).Length;
                SessionFolder = Path.Combine(Config.OutputFolder, "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(SessionFolder);
                events = new StreamWriter(Path.Combine(SessionFolder, "events.jsonl"), false, new UTF8Encoding(false), 16384);
                frameLog = new StreamWriter(Path.Combine(SessionFolder, "frames.jsonl"), false, new UTF8Encoding(false), 32768);
                startUtc = DateTime.UtcNow; clock.Start(); SaveSession("recording");
                using (ScreenCapture capture = new ScreenCapture(Bounds, Config.MaxDimension, !Config.TestPattern && Config.Cursor && Config.CaptureMode == "auto")) {
                    Log("session_start", "width", capture.Width, "height", capture.Height, "fps", Config.Fps);
                    double next = 0, nextStorage = 0, nextApp = 0, nextFlush = 0;
                    bool wasPaused = false;
                    while (!stop) {
                        double now = clock.Elapsed.TotalMilliseconds;
                        bool paused = manualPaused || systemPaused || !Win.DesktopAvailable();
                        if (paused) {
                            if (!wasPaused) { State = "Paused"; CloseSegment(); DrainEvents(); Log("paused", "reason", manualPaused ? "manual" : "desktop_unavailable"); events.Flush(); wasPaused = true; }
                            wake.WaitOne(100); continue;
                        }
                        if (wasPaused) { Log("resumed"); lastWindow = IntPtr.Zero; wasPaused = false; next = now; }
                        if (now >= nextStorage) { CheckStorage(); nextStorage = now + 1000; }
                        if (now < next) { wake.WaitOne(Math.Max(1, Math.Min(50, (int)(next - now)))); continue; }
                        if (video != null && now - segmentStart >= Config.SegmentSeconds * 1000.0) CloseSegment();
                        if (video == null) OpenSegment(capture);
                        // Display changes invalidate the fixed capture coordinates; finish cleanly instead of recording the wrong area.
                        if (!System.Windows.Forms.Screen.AllScreens.Any(s => s.DeviceName == Config.Monitor && s.Bounds == Bounds)) throw new IOException("Display layout changed. Select the display again and start a new session.");
                        if (Config.TestPattern) capture.DrawTestPattern((int)Frames); else capture.Capture(Config.Cursor);
                        string backend = Config.TestPattern ? "Test pattern" : capture.Backend;
                        if (CaptureBackend != backend) { CaptureBackend = backend; Log("capture_backend", "backend", backend, "fallback_reason", capture.FallbackReason); }
                        if (Frames == 0) try { capture.SaveThumbnail(Path.Combine(SessionFolder, "thumbnail.jpg")); } catch (Exception e) { Log("thumbnail_unavailable", "reason", e.Message); }
                        double capturedAt = clock.Elapsed.TotalMilliseconds;
                        if (segmentStart < 0) { segmentStart = capturedAt; annotations.Begin(segmentStart); lastWindow = IntPtr.Zero; Log("segment_opened", "file", segmentName, "start_elapsed_ms", Math.Round(segmentStart, 3), "hardware_allowed", segmentHardware); }
                        double pts = capturedAt - segmentStart;
                        video.Write(capture.Pixels, (long)(pts * 10000), 10000000L / Config.Fps);
                        segmentFrames++; Interlocked.Increment(ref frames); lastVideoMs = pts; State = "Recording";
                        Interlocked.Exchange(ref recordedMilliseconds, completedMilliseconds + pts + 1000.0 / Config.Fps);
                        Dictionary<string, object> frame = new Dictionary<string, object> {
                            { "segment", segmentName }, { "frame", segmentFrames - 1 }, { "video_time_ms", Math.Round(pts, 3) },
                            { "elapsed_ms", Math.Round(capturedAt, 3) }, { "utc", startUtc.AddMilliseconds(capturedAt).ToString("o") }
                        };
                        if (Config.WorkflowEvents) { Win.Point p; if (Win.GetCursorPos(out p)) { frame["screen_cursor_x"] = p.X; frame["screen_cursor_y"] = p.Y; } }
                        frameLog.WriteLine(Json.Encode(frame));
                        if (now >= nextApp) { WindowEvent(); nextApp = now + 250; }
                        DrainEvents();
                        if (now >= nextFlush) { frameLog.Flush(); events.Flush(); nextFlush = now + 1000; }
                        // Skip missed deadlines instead of building a queue or encoding a catch-up burst.
                        next += 1000.0 / Config.Fps;
                        double after = clock.Elapsed.TotalMilliseconds;
                        if (next < after) next = after + 1000.0 / Config.Fps;
                    }
                }
            } catch (StorageLimitException e) { StopReason = e.Message; }
            catch (Exception e) { Error = e.Message + " (0x" + e.HResult.ToString("X8") + ")"; }
            finally {
                State = "Stopping";
                try { CloseSegment(); } catch (Exception e) { Error = (Error == null ? "" : Error + " | ") + "Could not finalize the last clip: " + e.Message; }
                try { if (events != null) { DrainEvents(); Log("session_stopped", "reason", StopReason ?? "Stopped", "error", Error); } } catch { }
                try { if (frameLog != null) frameLog.Dispose(); if (events != null) events.Dispose(); } catch (Exception e) { if (Error == null) Error = e.Message; }
                clock.Stop();
                try { if (SessionFolder != null) SaveSession(Error == null ? "complete" : "error"); } catch (Exception e) { if (Error == null) Error = "Could not save session metadata: " + e.Message; }
                if (mf) MF.MFShutdown(); if (com) MF.CoUninitialize();
                State = Error == null ? "Stopped" : "Error";
            }
        }
        private sealed class StorageLimitException : IOException { internal StorageLimitException(string message) : base(message) { } }
    }
}
