using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Afterimage
{
    // Exercises the real controller and buttons using synthetic pixels, without a desktop recording.
    internal static class IslandTest
    {
        internal static int Run(string path)
        {
            string output = Path.GetFullPath(path); Directory.CreateDirectory(output); int result = 1;
            var settings = new Settings { OutputFolder = output, TestPattern = true, Hardware = false, MaxDimension = 1280, Monitor = Screen.PrimaryScreen.DeviceName };
            using var main = new MainForm(settings, true);
            using var start = new Timer { Interval = 200 };
            main.FormClosed += (_, _) => Application.ExitThread();
            start.Tick += async (_, _) => {
                start.Stop();
                try {
                    await Until(() => Application.OpenForms.OfType<IslandForm>().Any(f => f.Visible));
                    var island = Application.OpenForms.OfType<IslandForm>().Single();
                    Check(!main.Visible, "Main window appeared on island startup.");
                    Check(island.TopMost && !island.ShowInTaskbar, "Floating window flags are incorrect.");
                    if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) Check(GetWindowDisplayAffinity(island.Handle, out uint affinity) && affinity == 0x11, "Capture exclusion was not applied.");
                    Check(island.Region == null, "A hard-edged window region is still applied.");
                    Snapshot(island, output, "idle"); CheckAlpha(island);
                    await Until(() => !island.IsAnimating); int restingFrames = island.PresentCount; await Task.Delay(300);
                    Check(restingFrames == island.PresentCount, "Idle controls keep repainting.");
                    CheckSpring();
                    await CaptureMotion(island, output);
                    island.SetExpanded(false); await Task.Delay(90); Snapshot(island, output, "transition");
                    island.SetExpanded(false); await Task.Delay(50); island.SetExpanded(true);
                    await Until(() => !island.IsAnimating && island.Expansion == 1);
                    Button record = Button(island, "Record"), pause = Button(island, "Pause"), stop = Button(island, "Stop");
                    Check(record.Visible && record.Enabled && !pause.Enabled && !stop.Enabled, "Idle controls are incorrect.");
                    var hitPoint = record.PointToScreen(new Point(record.Width / 2, record.Height / 2));
                    Check(WindowFromPoint(hitPoint) == record.Handle, "The native Record button cannot receive pointer input.");
                    Snapshot(island, output, "expanded");
                    foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f }) { using var raster = island.SnapshotAtScale(scale); Check(raster.GetPixel(0, 0).A == 0, "Scaling introduced an opaque corner."); raster.Save(Path.Combine(output, "scale-" + (int)(scale * 100) + ".png"), ImageFormat.Png); }
                    record.PerformClick(); await Until(() => island.AccessibleDescription.StartsWith("Recording")); await Task.Delay(1100);
                    Check(!record.Enabled && pause.Enabled && stop.Enabled, "Recording controls are incorrect.");
                    Snapshot(island, output, "recording");
                    pause.PerformClick(); await Until(() => island.AccessibleDescription.StartsWith("Paused"));
                    Check(pause.Text == "Resume", "Pause did not expose Resume."); Snapshot(island, output, "paused");
                    pause.PerformClick(); await Until(() => island.AccessibleDescription.StartsWith("Recording")); await Task.Delay(1000);
                    stop.PerformClick(); await Until(() => island.AccessibleDescription.StartsWith("Saved"));
                    Check(record.Enabled && !pause.Enabled && !stop.Enabled, "Stopped controls are incorrect.");
                    foreach (string position in new[] { "left", "center", "right" }) {
                        settings.IslandPosition = position; island.Reposition(); var area = Screen.PrimaryScreen.WorkingArea;
                        Check(area.Contains(island.Bounds), "Controls exceed the display at " + position);
                    }
                    var negative = new Rectangle(-1920, -200, 1920, 1080); var size = new Size(324, 100);
                    foreach (string position in new[] { "left", "center", "right" }) Check(negative.Contains(new Rectangle(IslandForm.AnchorPoint(negative, size, position, 18, 8), size)), "Negative display coordinates failed.");
                    settings.IslandPosition = "center"; island.SetExpanded(false); await Until(() => !island.IsAnimating && island.Expansion == 0);
                    Check(island.Width == (int)Math.Ceiling(148 * island.DeviceDpi / 96f), "Controls did not collapse.");
                    main.Show(); main.Close(); Check(!main.Visible && !main.IsDisposed && island.Visible, "Closing the library stopped the island.");
                    var sessions = SessionLibrary.Read(output); Check(sessions.Count == 1 && sessions[0].Clips == 2, "Pause/resume did not finalize two clips.");
                    Check(Validation.Run(sessions[0].Folder) == 0, "Recorded clips failed to decode.");
                    File.WriteAllText(Path.Combine(output, "island-result.json"), Json.Encode(new { passed = true, dpi = island.DeviceDpi, clips = sessions[0].Clips, capture_exclusion = true, session = sessions[0].Folder })); result = 0;
                } catch (Exception e) { File.WriteAllText(Path.Combine(output, "island-result.json"), Json.Encode(new { passed = false, error = e.ToString() })); }
                finally { main.ExitApp(); }
            };
            main.StartInterface(); start.Start(); Application.Run(); return result;
        }
        private static Button Button(IslandForm f, string name) => f.Controls.OfType<Button>().Single(b => b.AccessibleName == name);
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static async Task Until(Func<bool> predicate) { var clock = Stopwatch.StartNew(); while (!predicate()) { if (clock.Elapsed.TotalSeconds > 15) throw new TimeoutException("Recorder state did not change."); await Task.Delay(100); } }
        private static void Snapshot(IslandForm f, string output, string name) { using var bmp = f.Snapshot(); bmp.Save(Path.Combine(output, name + ".png"), ImageFormat.Png); }
        private static void CheckAlpha(IslandForm f)
        {
            using var bmp = f.Snapshot(); Check(bmp.GetPixel(0, 0).A == 0, "The outer corner is opaque.");
            var levels = new System.Collections.Generic.HashSet<byte>(); int extent = Math.Min(bmp.Height, (int)(25 * f.DeviceDpi / 96f));
            for (int y = 0; y < extent; y++) for (int x = 0; x < extent; x++) levels.Add(bmp.GetPixel(x, y).A);
            Check(levels.Count > 30, "The curve is not smoothly alpha blended.");
        }
        private static void CheckSpring()
        {
            float a = 0, va = 0, b = 0, vb = 0;
            for (int i = 0; i < 60; i++) IslandForm.StepSpring(ref a, ref va, 1, 1f / 60);
            for (int i = 0; i < 144; i++) IslandForm.StepSpring(ref b, ref vb, 1, 1f / 144);
            Check(Math.Abs(a - b) < .0001f && a > .999f, "Animation depends on display refresh rate.");
            IslandForm.StepSpring(ref a, ref va, 0, .08f); float previous = a;
            IslandForm.StepSpring(ref a, ref va, 1, .001f);
            Check(Math.Abs(a - previous) < .01f, "Reversing the animation causes a jump.");
        }
        private static async Task CaptureMotion(IslandForm form, string output)
        {
            var frames = new System.Collections.Generic.List<(Bitmap Image, double Time)>();
            using var sample = new Timer { Interval = 8 }; var clock = Stopwatch.StartNew(); int previous = -1;
            sample.Tick += (_, _) => { if (form.PresentCount != previous) { previous = form.PresentCount; frames.Add((form.Snapshot(), clock.Elapsed.TotalMilliseconds)); } };
            try {
                frames.Add((form.Snapshot(), 0)); sample.Start(); form.SetExpanded(true); await Until(() => !form.IsAnimating); sample.Stop();
                string directory = Path.Combine(output, "motion"); Directory.CreateDirectory(directory);
                for (int i = 0; i < frames.Count; i++) frames[i].Image.Save(Path.Combine(directory, i.ToString("D3") + ".png"), ImageFormat.Png);
                File.WriteAllText(Path.Combine(directory, "timing.json"), Json.Encode(frames.Select(f => new { time_ms = f.Time, width = f.Image.Width, height = f.Image.Height }).ToArray()));
            } finally { sample.Stop(); foreach (var frame in frames) frame.Image.Dispose(); }
        }
        [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    }
}
