using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Afterimage
{
    internal static class SelfTest
    {
        internal static int Run(string[] args)
        {
            string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestRecordings");
            Directory.CreateDirectory(output);
            try {
                Screen display = Screen.PrimaryScreen;
                Settings config = new Settings { OutputFolder = output, SessionName = "Capture check", Monitor = display.DeviceName, Fps = 5, MaxDimension = 1280, SegmentSeconds = 3, WorkflowEvents = true, Hardware = args.Length < 3 || args[2] != "software", TestPattern = true };
                Recorder r = new Recorder(config, display.Bounds); r.Start();
                Thread.Sleep(4500); r.Mark(); r.AddClick("left_click", display.Bounds.X + 100, display.Bounds.Y + 100, 0);
                r.Pause(true); Thread.Sleep(1200); long pausedFrames = r.Frames; Thread.Sleep(600);
                if (r.Frames != pausedFrames) throw new Exception("Pause did not stop frame capture.");
                r.Pause(false); Thread.Sleep(3500); r.Stop("Self-test completed"); r.Wait();
                if (r.Error != null) throw new Exception(r.Error);
                if (r.Frames < 12 || Directory.GetFiles(r.SessionFolder, "clip-*.mp4").Length < 2) throw new Exception("Insufficient frames or clips.");
                if (Directory.GetFiles(r.SessionFolder, "*.partial.mp4").Length != 0) throw new Exception("A clip was not finalized.");
                if (!File.Exists(Path.Combine(r.SessionFolder, "Review.html"))) throw new Exception("Review page was not generated.");
                if (!File.Exists(Path.Combine(r.SessionFolder, "thumbnail.jpg"))) throw new Exception("Session thumbnail was not generated.");
                bool marker = false;
                foreach (string file in Directory.GetFiles(r.SessionFolder, "*.annotations.json")) {
                    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                    foreach (var cue in document.RootElement.GetProperty("annotations").EnumerateArray()) {
                        if (cue.GetProperty("text").GetString().Contains("Workflow step marked")) marker = true;
                        if (cue.GetProperty("at_ms").GetDouble() < 0) throw new Exception("Negative annotation timestamp.");
                    }
                }
                if (!marker) throw new Exception("Workflow marker did not survive serialization.");
                if (!SessionLibrary.Read(output).Exists(s => s.Folder == r.SessionFolder && s.Name == "Capture check")) throw new Exception("Saved session missing from library.");
                if (Validation.Run(r.SessionFolder) != 0) throw new Exception("The video decoder rejected a clip.");
                foreach (string image in Directory.GetFiles(r.SessionFolder, "*.first-frame.png")) using (Bitmap b = new Bitmap(image)) {
                    Color top = b.GetPixel(20, 20), bottom = b.GetPixel(20, b.Height - 20);
                    if (top.R < 180 || top.B > 80 || bottom.B < 180 || bottom.R > 80) throw new Exception("Encoded video orientation or color is wrong.");
                }
                Settings blocked = config.Copy(); blocked.MinFreeGB = Int32.MaxValue;
                Recorder limited = new Recorder(blocked, display.Bounds); limited.Start(); limited.Wait();
                if (limited.Frames != 0 || limited.Error != null || limited.StopReason == null || !limited.StopReason.Contains("reserve")) throw new Exception("Free-space protection failed.");
                File.WriteAllText(Path.Combine(output, "test-result.json"), Json.Encode(new { passed = true, frames = r.Frames, session = r.SessionFolder, segments = r.SegmentCount, elapsed_seconds = r.ElapsedSeconds }));
                return 0;
            } catch (Exception e) { File.WriteAllText(Path.Combine(output, "test-result.json"), Json.Encode(new { passed = false, error = e.ToString() })); return 1; }
        }
    }
}
