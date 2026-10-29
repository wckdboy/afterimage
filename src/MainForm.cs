using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Afterimage
{
    internal static class Theme
    {
        internal static readonly Color Ink = Color.FromArgb(30, 32, 38), Muted = Color.FromArgb(112, 116, 126), Line = Color.FromArgb(229, 230, 235), Paper = Color.FromArgb(250, 250, 252);
        internal static Label Label(string text, float size, FontStyle weight) { return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", size, weight), ForeColor = Ink, Margin = new Padding(0) }; }
        internal static Button Button(string text, int width, bool primary)
        {
            Button b = new Button { Text = text, Width = width, Height = 42, FlatStyle = FlatStyle.Flat, BackColor = primary ? Ink : Color.White, ForeColor = primary ? Color.White : Ink, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0), UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = primary ? 0 : 1; b.FlatAppearance.BorderColor = Line; b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(55, 58, 67) : Paper;
            return b;
        }
        internal static Button Link(string text) { Button b = Button(text, 90, false); b.Height = 32; b.FlatAppearance.BorderSize = 0; b.BackColor = Paper; b.ForeColor = Muted; return b; }
    }
    internal sealed class MainForm : Form
    {
        private readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private Settings settings;
        private Recorder recorder;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Timer ticker = new Timer { Interval = 1000 };
        private readonly ToolTip tips = new ToolTip();
        private readonly Label state, elapsed, format, resources;
        private readonly Button record, pause, marker, preferences, review;
        private readonly Icon idleIcon, recordingIcon, pausedIcon;
        private Win.HookProc hookProc;
        private IntPtr hook;
        private bool exitRequested, handledEnd;
        private string latestSession;
        private TimeSpan lastCpu;
        private DateTime lastCpuAt = DateTime.UtcNow;
        internal MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Afterimage"; Font = new Font("Segoe UI", 10); BackColor = Theme.Paper; ForeColor = Theme.Ink;
            ClientSize = new Size(440, 300); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            settings = Json.Load(settingsPath);
            idleIcon = MakeIcon(Theme.Muted); recordingIcon = MakeIcon(Color.FromArgb(218, 56, 77)); pausedIcon = MakeIcon(Color.FromArgb(201, 147, 41)); Icon = idleIcon;
            Label title = Theme.Label("Afterimage", 12, FontStyle.Bold); title.Location = new Point(26, 24); Controls.Add(title);
            state = Theme.Label("Ready", 9, FontStyle.Regular); state.ForeColor = Theme.Muted; state.AutoSize = false; state.TextAlign = ContentAlignment.MiddleRight; state.SetBounds(285, 23, 128, 24); Controls.Add(state);
            elapsed = Theme.Label("00:00:00", 37, FontStyle.Regular); elapsed.Location = new Point(21, 64); Controls.Add(elapsed);
            format = Theme.Label("", 9, FontStyle.Regular); format.ForeColor = Theme.Muted; format.Location = new Point(27, 132); Controls.Add(format); UpdateFormat();
            record = Theme.Button("Record", 174, true); record.Location = new Point(26, 171); Controls.Add(record); record.Click += delegate { ToggleRecording(); };
            pause = Theme.Button("Pause", 94, false); pause.Location = new Point(210, 171); pause.Enabled = false; Controls.Add(pause); pause.Click += delegate { TogglePause(); };
            marker = Theme.Button("Mark", 94, false); marker.Location = new Point(314, 171); marker.Enabled = false; Controls.Add(marker); marker.Click += delegate { if (recorder != null) { recorder.Mark(); state.Text = "Marked"; } };
            Panel divider = new Panel { BackColor = Theme.Line }; divider.SetBounds(26, 233, 382, 1); Controls.Add(divider);
            preferences = Theme.Link("Settings"); preferences.SetBounds(16, 245, 82, 32); Controls.Add(preferences); preferences.Click += delegate { using (SettingsForm form = new SettingsForm(settings)) if (form.ShowDialog(this) == DialogResult.OK) { settings = form.Result; SaveSettings(); UpdateFormat(); } };
            Button folder = Theme.Link("Folder"); folder.SetBounds(100, 245, 66, 32); Controls.Add(folder); folder.Click += delegate { try { Directory.CreateDirectory(settings.OutputFolder); Process.Start(settings.OutputFolder); } catch (Exception e) { ShowError(e.Message); } };
            review = Theme.Link("Review"); review.SetBounds(170, 245, 74, 32); review.Enabled = false; Controls.Add(review); review.Click += delegate { try { Process.Start(Path.Combine(latestSession, "Review.html")); } catch (Exception e) { ShowError(e.Message); } };
            resources = Theme.Label("", 8, FontStyle.Regular); resources.AutoSize = false; resources.ForeColor = Theme.Muted; resources.TextAlign = ContentAlignment.MiddleRight; resources.SetBounds(248, 247, 160, 28); Controls.Add(resources);
            tips.SetToolTip(record, "Ctrl+Alt+R"); tips.SetToolTip(pause, "Ctrl+Alt+P"); tips.SetToolTip(marker, "Mark a workflow step · Ctrl+Alt+M"); tips.SetToolTip(review, "Review completed clips and annotations");
            ContextMenuStrip menu = new ContextMenuStrip(); menu.Items.Add("Open", null, delegate { ShowWindow(); }); menu.Items.Add("Record / stop", null, delegate { ToggleRecording(); }); menu.Items.Add("Pause / resume", null, delegate { TogglePause(); }); menu.Items.Add("Mark", null, delegate { if (recorder != null) recorder.Mark(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, delegate { ExitApp(); });
            tray.Icon = idleIcon; tray.Text = "Afterimage · ready"; tray.ContextMenuStrip = menu; tray.Visible = true; tray.DoubleClick += delegate { ShowWindow(); };
            ticker.Tick += delegate { Tick(); }; ticker.Start(); SystemEvents.SessionSwitch += SessionChanged; SystemEvents.PowerModeChanged += PowerChanged;
            Shown += delegate {
                bool r = Win.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.R), p = Win.RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.P), m = Win.RegisterHotKey(Handle, 3, 0x4003, (uint)Keys.M);
                if (!r) tips.SetToolTip(record, "Ctrl+Alt+R is in use by another app"); if (!p) tips.SetToolTip(pause, "Ctrl+Alt+P is in use by another app"); if (!m) tips.SetToolTip(marker, "Ctrl+Alt+M is in use by another app");
                if (settings.StartOnLaunch) BeginRecording();
            };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            ResumeLayout(true);
        }
        private void UpdateFormat() { format.Text = String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} fps   ·   {1} px   ·   {2:0.#} Mbps{3}", settings.Fps, settings.MaxDimension, settings.Bitrate / 1000000.0, settings.WorkflowEvents ? "   ·   Annotated" : ""); }
        private void SaveSettings() { try { Json.Save(settingsPath, settings); } catch (Exception e) { ShowError("Could not save settings: " + e.Message); } }
        private void BeginRecording()
        {
            try {
                Screen display = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Monitor) ?? Screen.PrimaryScreen; settings.Monitor = display.DeviceName; settings.Validate(); Json.Save(settingsPath, settings);
                if (settings.WorkflowEvents) InstallMouseHook();
                recorder = new Recorder(settings, display.Bounds); handledEnd = false; recorder.Start(); preferences.Enabled = false; record.Text = "Stop"; state.Text = "Starting"; pause.Enabled = true;
                using (Process p = Process.GetCurrentProcess()) lastCpu = p.TotalProcessorTime; lastCpuAt = DateTime.UtcNow;
            } catch (Exception e) { RemoveHook(); ShowError(e.Message); }
        }
        private void ToggleRecording() { if (recorder != null && recorder.IsAlive) { recorder.Stop("Stopped by user"); record.Enabled = pause.Enabled = marker.Enabled = false; state.Text = "Saving"; } else BeginRecording(); }
        private void TogglePause() { if (recorder != null && recorder.IsAlive) recorder.Pause(!recorder.ManualPaused); }
        private void Tick()
        {
            if (recorder == null) return;
            if (recorder.SessionFolder != null) latestSession = recorder.SessionFolder;
            review.Enabled = latestSession != null && File.Exists(Path.Combine(latestSession, "Review.html"));
            elapsed.Text = TimeSpan.FromSeconds(recorder.RecordedSeconds).ToString(@"hh\:mm\:ss");
            if (!recorder.IsAlive && !handledEnd) {
                handledEnd = true; RemoveHook(); preferences.Enabled = record.Enabled = true; record.Text = "Record"; pause.Enabled = marker.Enabled = false; pause.Text = "Pause"; state.Text = recorder.Error == null ? "Saved" : "Error"; state.ForeColor = recorder.Error == null ? Theme.Muted : Color.Firebrick; tray.Icon = idleIcon; tray.Text = "Afterimage · stopped";
                if (recorder.Error != null) ShowError(recorder.Error); else if (recorder.StopReason != null && recorder.StopReason.StartsWith("Stopped:")) ShowError(recorder.StopReason);
                if (exitRequested) { Close(); return; }
            } else if (recorder.IsAlive) {
                state.Text = recorder.State == "Recording" ? "● Recording" : recorder.State;
                state.ForeColor = recorder.State == "Recording" ? Color.FromArgb(194, 36, 65) : Theme.Muted; pause.Text = recorder.ManualPaused ? "Resume" : "Pause"; marker.Enabled = recorder.State == "Recording";
                tray.Icon = recorder.State == "Paused" ? pausedIcon : recordingIcon; tray.Text = "Afterimage · " + recorder.State.ToLowerInvariant();
            }
            using (Process p = Process.GetCurrentProcess()) {
                DateTime now = DateTime.UtcNow; double cpu = Math.Max(0, (p.TotalProcessorTime - lastCpu).TotalSeconds / Math.Max(.1, (now - lastCpuAt).TotalSeconds) / Environment.ProcessorCount * 100);
                lastCpu = p.TotalProcessorTime; lastCpuAt = now; resources.Text = String.Format("{0:0}% CPU · {1:0} MB", cpu, p.WorkingSet64 / 1048576.0);
                tips.SetToolTip(resources, String.Format("{0:N0} frames · {1} clips · {2:0.00} GB in folder", recorder.Frames, recorder.SegmentCount, recorder.Bytes / 1073741824.0));
            }
        }
        private void InstallMouseHook()
        {
            RemoveHook();
            hookProc = delegate(int code, IntPtr message, IntPtr data) {
                try {
                    if (code >= 0 && recorder != null && recorder.EventsEnabled) {
                        int m = message.ToInt32(); string action = m == 0x201 ? "left_click" : m == 0x204 ? "right_click" : m == 0x207 ? "middle_click" : m == 0x20A ? "scroll" : null;
                        if (action != null) { Win.MouseData d = (Win.MouseData)Marshal.PtrToStructure(data, typeof(Win.MouseData)); recorder.AddClick(action, d.Point.X, d.Point.Y, m == 0x20A ? (short)(d.Data >> 16) : 0); }
                    }
                } catch { }
                return Win.CallNextHookEx(hook, code, message, data);
            };
            hook = Win.SetWindowsHookEx(14, hookProc, Win.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new InvalidOperationException("Mouse annotations are unavailable. Disable auto annotations in Settings to record video only.");
        }
        private void RemoveHook() { if (hook != IntPtr.Zero) { Win.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
        private void SessionChanged(object sender, SessionSwitchEventArgs e) { if (recorder == null) return; if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.RemoteDisconnect || e.Reason == SessionSwitchReason.ConsoleDisconnect) recorder.SystemPause(true); else if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.RemoteConnect || e.Reason == SessionSwitchReason.ConsoleConnect) recorder.SystemPause(false); }
        private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (recorder != null) { if (e.Mode == PowerModes.Suspend) recorder.SystemPause(true); if (e.Mode == PowerModes.Resume) recorder.SystemPause(false); } }
        private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        private void ShowError(string text) { MessageBox.Show(this, text, "Afterimage", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        private void ExitApp() { exitRequested = true; if (recorder != null && recorder.IsAlive) { recorder.Stop("Application exit"); record.Enabled = pause.Enabled = marker.Enabled = false; } else Close(); }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x312) { if (m.WParam.ToInt32() == 1) ToggleRecording(); if (m.WParam.ToInt32() == 2) TogglePause(); if (m.WParam.ToInt32() == 3 && recorder != null) recorder.Mark(); } if (m.Msg == 0x11 && recorder != null) recorder.Stop("Windows shutdown"); base.WndProc(ref m); }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing && recorder != null && recorder.IsAlive) { e.Cancel = true; Hide(); tray.BalloonTipTitle = "Recording continues"; tray.BalloonTipText = "Use the tray menu to stop or exit."; tray.ShowBalloonTip(3000); return; }
            if (recorder != null && recorder.IsAlive) { recorder.Stop("Application closing"); recorder.Wait(); }
            RemoveHook(); ticker.Stop(); tray.Visible = false; tray.Dispose(); SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged;
            for (int i = 1; i <= 3; i++) Win.UnregisterHotKey(Handle, i); base.OnFormClosing(e);
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        private static Icon MakeIcon(Color color) { using (Bitmap b = new Bitmap(32, 32)) { using (Graphics g = Graphics.FromImage(b)) { g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Color.Transparent); using (Brush brush = new SolidBrush(color)) g.FillEllipse(brush, 3, 3, 26, 26); g.FillRectangle(Brushes.White, 12, 12, 8, 8); } IntPtr p = b.GetHicon(); try { using (Icon i = Icon.FromHandle(p)) return (Icon)i.Clone(); } finally { DestroyIcon(p); } } }
    }
    internal sealed class SettingsForm : Form
    {
        internal Settings Result;
        private readonly ComboBox monitor = new ComboBox(), size = new ComboBox(), bitrate = new ComboBox();
        private readonly NumericUpDown fps = new NumericUpDown(), minutes = new NumericUpDown(), limit = new NumericUpDown(), free = new NumericUpDown();
        private readonly CheckBox cursor = new CheckBox(), annotations = new CheckBox(), titles = new CheckBox(), hardware = new CheckBox(), launch = new CheckBox();
        private readonly TextBox folder = new TextBox();
        private int rowIndex;
        internal SettingsForm(Settings config)
        {
            SuspendLayout(); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10); Text = "Settings"; BackColor = Theme.Paper; ForeColor = Theme.Ink; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(510, 584); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            SetupCombo(monitor); foreach (Screen s in Screen.AllScreens) monitor.Items.Add(new DisplayItem(s)); monitor.SelectedIndex = Math.Max(0, Array.FindIndex(Screen.AllScreens, s => s.DeviceName == config.Monitor)); Row("Display", monitor);
            Number(fps, 1, 30, config.Fps); Row("Frame rate", fps);
            SetupCombo(size); size.Items.AddRange(new object[] { "1280 px", "1920 px", "2560 px", "3840 px" }); size.SelectedIndex = Math.Max(0, Array.IndexOf(new int[] { 1280, 1920, 2560, 3840 }, config.MaxDimension)); Row("Longest edge", size);
            SetupCombo(bitrate); bitrate.Items.AddRange(new object[] { "1 Mbps", "2.5 Mbps", "5 Mbps", "10 Mbps" }); bitrate.SelectedIndex = Math.Max(0, Array.IndexOf(new int[] { 1000000, 2500000, 5000000, 10000000 }, config.Bitrate)); Row("Bitrate", bitrate);
            Number(minutes, 1, 60, Math.Max(1, config.SegmentSeconds / 60)); Row("Clip length · min", minutes);
            Number(limit, 1, 10000, config.MaxStorageGB); Row("Folder limit · GB", limit);
            Number(free, 1, 1000, config.MinFreeGB); Row("Free space · GB", free);
            folder.Width = 266; folder.Text = config.OutputFolder; Row("Output folder", folder); Button browse = Theme.Button("…", 35, false); browse.SetBounds(441, 300, 35, 28); Controls.Add(browse); browse.Click += delegate { using (FolderBrowserDialog d = new FolderBrowserDialog { SelectedPath = folder.Text }) if (d.ShowDialog() == DialogResult.OK) folder.Text = d.SelectedPath; };
            Check(cursor, "Mouse pointer", config.Cursor); Check(annotations, "Auto annotations", config.WorkflowEvents); Check(titles, "Include window titles", config.WindowTitles); Check(hardware, "Hardware encoding", config.Hardware); Check(launch, "Record on app launch", config.StartOnLaunch);
            titles.Enabled = annotations.Checked; annotations.CheckedChanged += delegate { titles.Enabled = annotations.Checked; };
            Label optionLabel = Theme.Label("Options", 10, FontStyle.Regular); optionLabel.Location = new Point(24, 354); Controls.Add(optionLabel);
            CheckBox[] checks = { cursor, annotations, titles, hardware, launch }; for (int i = 0; i < checks.Length; i++) { checks[i].Location = new Point(166, 350 + i * 28); Controls.Add(checks[i]); }
            Button save = Theme.Button("Save", 100, true); save.SetBounds(384, 529, 100, 36); Controls.Add(save); AcceptButton = save;
            Button cancel = Theme.Link("Cancel"); cancel.SetBounds(288, 531, 86, 32); cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel); CancelButton = cancel;
            save.Click += delegate { try { Result = new Settings { Monitor = ((DisplayItem)monitor.SelectedItem).Screen.DeviceName, Fps = (int)fps.Value, MaxDimension = new int[] { 1280, 1920, 2560, 3840 }[size.SelectedIndex], Bitrate = new int[] { 1000000, 2500000, 5000000, 10000000 }[bitrate.SelectedIndex], SegmentSeconds = (int)minutes.Value * 60, MaxStorageGB = (int)limit.Value, MinFreeGB = (int)free.Value, OutputFolder = folder.Text, Cursor = cursor.Checked, WorkflowEvents = annotations.Checked, WindowTitles = annotations.Checked && titles.Checked, Hardware = hardware.Checked, StartOnLaunch = launch.Checked }; Result.Validate(); DialogResult = DialogResult.OK; } catch (Exception e) { MessageBox.Show(this, e.Message, "Settings"); } };
            ResumeLayout(true);
        }
        private void SetupCombo(ComboBox c) { c.DropDownStyle = ComboBoxStyle.DropDownList; c.Width = 308; c.Margin = new Padding(0, 0, 0, 12); }
        private void Number(NumericUpDown c, int min, int max, int value) { c.Minimum = min; c.Maximum = max; c.Value = Math.Max(min, Math.Min(max, value)); c.Width = 100; c.Margin = new Padding(0, 0, 0, 12); }
        private void Check(CheckBox c, string text, bool value) { c.Text = text; c.Checked = value; c.AutoSize = true; c.Margin = new Padding(0, 2, 0, 6); }
        private void Row(string name, Control control) { int y = 20 + rowIndex++ * 40; Label label = Theme.Label(name, 10, FontStyle.Regular); label.Location = new Point(24, y + 4); control.Location = new Point(166, y); Controls.Add(label); Controls.Add(control); }
        private sealed class DisplayItem { internal readonly Screen Screen; internal DisplayItem(Screen screen) { Screen = screen; } public override string ToString() { return Screen.DeviceName.Replace(@"\\.\", "") + " · " + Screen.Bounds.Width + " × " + Screen.Bounds.Height; } }
    }
}
