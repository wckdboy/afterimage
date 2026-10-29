using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Afterimage
{
    internal static class Theme
    {
        internal static readonly Color Ink = Color.FromArgb(30, 32, 38), Muted = Color.FromArgb(112, 116, 126), Line = Color.FromArgb(229, 230, 235), Paper = Color.FromArgb(250, 250, 252);
        internal static Label Label(string text, float size, FontStyle weight) { return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", size, weight), ForeColor = Ink, Margin = new Padding(0) }; }
        internal static Button Button(string text, int width, bool primary)
        {
            Button b = new SoftButton { Text = text, Width = width, Height = 42, FlatStyle = FlatStyle.Flat, BackColor = primary ? Ink : Color.White, ForeColor = primary ? Color.White : Ink, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0), UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = primary ? 0 : 1; b.FlatAppearance.BorderColor = Line; b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(55, 58, 67) : Paper;
            return b;
        }
        internal static Button Link(string text) { Button b = Button(text, 90, false); b.Height = 32; b.FlatAppearance.BorderSize = 0; b.BackColor = Paper; b.ForeColor = Muted; return b; }
        internal static void Open(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        internal sealed class ChoiceBox : ComboBox
        {
            internal ChoiceBox() { DrawMode = DrawMode.OwnerDrawFixed; }
            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                e.DrawBackground();
                string value = e.Index >= 0 && e.Index < Items.Count ? Items[e.Index].ToString() : Text;
                var bounds = new Rectangle(e.Bounds.X + 3, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, value, Font, bounds, Enabled ? e.ForeColor : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            }
        }
        private sealed class SoftButton : Button
        {
            private bool hover, pressed;
            internal SoftButton() { DoubleBuffered = true; }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Parent?.BackColor ?? Theme.Paper);
                float r = 8f * DeviceDpi / 96f; var box = new RectangleF(.5f, .5f, Width - 1, Height - 1);
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddArc(box.Left, box.Top, r * 2, r * 2, 180, 90); path.AddArc(box.Right - r * 2, box.Top, r * 2, r * 2, 270, 90);
                path.AddArc(box.Right - r * 2, box.Bottom - r * 2, r * 2, r * 2, 0, 90); path.AddArc(box.Left, box.Bottom - r * 2, r * 2, r * 2, 90, 90); path.CloseFigure();
                Color fill = !Enabled ? Theme.Line : (hover || pressed) ? FlatAppearance.MouseOverBackColor : BackColor;
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (FlatAppearance.BorderSize > 0) using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5), ForeColor, fill);
            }
        }
    }
    internal sealed class MainForm : Form
    {
        private readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private Settings settings;
        private Recorder recorder;
        private IslandForm island;
        private CompanionForm companion;
        private bool closingWorkspace, workspaceClosed;
        private bool interfaceStarted, stopping;
        private readonly bool persistSettings;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Timer ticker = new Timer { Interval = 1000 };
        private readonly ToolTip tips = new ToolTip();
        private readonly Label state, elapsed, format, resources;
        private readonly Button record, pause, marker, preferences, review;
        private readonly ComboBox displayPicker = new Theme.ChoiceBox(), preset = new Theme.ChoiceBox();
        private readonly TextBox sessionName = new TextBox();
        private readonly FlowLayoutPanel recent = new FlowLayoutPanel();
        private bool updatingOptions;
        private int libraryGeneration;
        private readonly Icon idleIcon, recordingIcon, pausedIcon;
        private Win.HookProc hookProc;
        private IntPtr hook;
        private bool exitRequested, handledEnd;
        private string latestSession;
        private TimeSpan lastCpu;
        private DateTime lastCpuAt = DateTime.UtcNow;
        private DateTime markedUntil;
        internal string RecordingState => recorder?.State;
        internal bool CompletedReviewAvailable => review.Enabled;
        private bool checkingReview;
        private DateTime reviewCheckedAt;
        internal MainForm(Settings initial = null, bool preview = false)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Afterimage"; Font = new Font("Segoe UI", 10); BackColor = Theme.Paper; ForeColor = Theme.Ink;
            ClientSize = new Size(480, 582); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            settings = initial ?? Json.Load(settingsPath);
            persistSettings = !preview;
            idleIcon = Brand.Icon(); recordingIcon = Brand.Icon(Brand.Accent); pausedIcon = Brand.Icon(Color.FromArgb(242, 193, 107)); Icon = idleIcon;
            Label title = Theme.Label("Afterimage", 12, FontStyle.Bold); title.Location = new Point(24, 22); Controls.Add(title);
            Button workspace = Theme.Link("Chat & notes"); workspace.SetBounds(322, 17, 134, 32); workspace.Click += (_, _) => OpenWorkspace("chat"); Controls.Add(workspace);
            state = Theme.Label("Ready", 9, FontStyle.Regular); state.ForeColor = Theme.Muted; state.AutoSize = false; state.TextAlign = ContentAlignment.MiddleRight; state.SetBounds(300, 20, 156, 26); Controls.Add(state);
            elapsed = Theme.Label("00:00:00", 37, FontStyle.Regular); elapsed.Location = new Point(19, 64); Controls.Add(elapsed);
            format = Theme.Label("", 9, FontStyle.Regular); format.ForeColor = Theme.Muted; format.Location = new Point(25, 132); Controls.Add(format);
            var nameLabel = Theme.Label("Session", 9, FontStyle.Regular); nameLabel.ForeColor = Theme.Muted; nameLabel.Location = new Point(24, 175); Controls.Add(nameLabel);
            sessionName.SetBounds(100, 168, 356, 28); sessionName.PlaceholderText = "Name this session"; sessionName.MaxLength = 160; sessionName.BorderStyle = BorderStyle.FixedSingle; sessionName.BackColor = Color.White; sessionName.AccessibleName = "Session name"; Controls.Add(sessionName);
            displayPicker.SetBounds(24, 213, 276, 30); displayPicker.DropDownStyle = ComboBoxStyle.DropDownList; displayPicker.FlatStyle = FlatStyle.Standard; displayPicker.BackColor = Color.White; displayPicker.AccessibleName = "Display"; Controls.Add(displayPicker);
            preset.SetBounds(312, 213, 144, 30); preset.DropDownStyle = ComboBoxStyle.DropDownList; preset.FlatStyle = FlatStyle.Standard; preset.Items.AddRange(new object[] { "Lean", "Balanced", "Detailed", "Custom" }); preset.AccessibleName = "Recording quality"; Controls.Add(preset);
            displayPicker.SelectedIndexChanged += delegate { if (!updatingOptions && displayPicker.SelectedItem is DisplayChoice d) { settings.Monitor = d.DeviceName; SaveSettings(); } };
            preset.SelectedIndexChanged += delegate {
                if (updatingOptions || preset.SelectedIndex < 0) return;
                if (preset.SelectedIndex == 3) {
                    using (var form = new SettingsForm(settings)) if (form.ShowDialog(this) == DialogResult.OK) { settings = form.Result; SaveSettings(); RefreshRecent(); }
                    UpdateOptions(); return;
                }
                int i = preset.SelectedIndex; settings.Fps = new[] { 2, 5, 15 }[i]; settings.MaxDimension = new[] { 1280, 1920, 2560 }[i]; settings.Bitrate = new[] { 1000000, 2500000, 5000000 }[i]; SaveSettings(); UpdateFormat();
            };
            record = Theme.Button("Record", 216, true); record.SetBounds(24, 266, 216, 46); Controls.Add(record); record.Click += delegate { ToggleRecording(); };
            pause = Theme.Button("Pause", 96, false); pause.SetBounds(252, 266, 96, 46); pause.Enabled = false; Controls.Add(pause); pause.Click += delegate { TogglePause(); };
            marker = Theme.Button("Mark", 96, false); marker.SetBounds(360, 266, 96, 46); marker.Enabled = false; Controls.Add(marker); marker.Click += delegate { MarkStep(); };
            Panel divider = new Panel { BackColor = Theme.Line }; divider.SetBounds(24, 336, 432, 1); Controls.Add(divider);
            Label recentTitle = Theme.Label("Recent sessions", 10, FontStyle.Bold); recentTitle.Location = new Point(24, 357); Controls.Add(recentTitle);
            Button all = Theme.Link("View all"); all.SetBounds(376, 349, 88, 32); all.Click += delegate { using var library = new LibraryForm(settings.OutputFolder); library.ShowDialog(this); }; Controls.Add(all);
            recent.SetBounds(24, 391, 444, 136); recent.WrapContents = false; Controls.Add(recent);
            preferences = Theme.Link("Settings"); preferences.SetBounds(14, 543, 76, 32); Controls.Add(preferences); preferences.Click += delegate { using (SettingsForm form = new SettingsForm(settings)) if (form.ShowDialog(this) == DialogResult.OK) { settings = form.Result; SaveSettings(); UpdateOptions(); RefreshRecent(); } };
            Button folder = Theme.Link("Folder"); folder.SetBounds(90, 543, 62, 32); Controls.Add(folder); folder.Click += delegate { try { Directory.CreateDirectory(settings.OutputFolder); Theme.Open(settings.OutputFolder); } catch (Exception e) { ShowError(e.Message); } };
            review = Theme.Link("Review"); review.SetBounds(152, 543, 66, 32); review.Enabled = false; Controls.Add(review); review.Click += delegate { try { Theme.Open(Path.Combine(latestSession, "Review.html")); } catch (Exception e) { ShowError(e.Message); } };
            resources = Theme.Label("", 8, FontStyle.Regular); resources.AutoSize = false; resources.ForeColor = Theme.Muted; resources.TextAlign = ContentAlignment.MiddleRight; resources.SetBounds(226, 545, 230, 28); Controls.Add(resources);
            UpdateOptions();
            tips.SetToolTip(record, "Ctrl+Alt+R"); tips.SetToolTip(pause, "Ctrl+Alt+P"); tips.SetToolTip(marker, "Mark a workflow step · Ctrl+Alt+M"); tips.SetToolTip(review, "Review completed clips and annotations");
            ContextMenuStrip menu = new ContextMenuStrip(); menu.Items.Add("Open", null, delegate { ShowWindow(); }); menu.Items.Add("Record / stop", null, delegate { ToggleRecording(); }); menu.Items.Add("Pause / resume", null, delegate { TogglePause(); }); menu.Items.Add("Mark", null, delegate { if (recorder != null) recorder.Mark(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, delegate { ExitApp(); });
            tray.Icon = idleIcon; tray.Text = "Afterimage · ready"; tray.ContextMenuStrip = menu; tray.Visible = !preview; tray.DoubleClick += delegate { ShowWindow(); };
            menu.Items.Insert(1, new ToolStripMenuItem("Show floating controls", null, delegate { settings.IslandEnabled = true; SaveSettings(); }));
            menu.Items.Insert(1, new ToolStripMenuItem("Chat & notes", null, (_, _) => OpenWorkspace("chat")));
            ticker.Tick += delegate { Tick(); }; ticker.Start(); SystemEvents.SessionSwitch += SessionChanged; SystemEvents.PowerModeChanged += PowerChanged;
            SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            ResumeLayout(true);
        }
        internal void StartInterface()
        {
            _ = Handle;
            BeginInvoke((Action)(() => {
                interfaceStarted = true; RefreshRecent();
                bool r = Win.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.R), p = Win.RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.P), m = Win.RegisterHotKey(Handle, 3, 0x4003, (uint)Keys.M);
                Win.RegisterHotKey(Handle, 4, 0x4003, (uint)Keys.Space);
                if (!r) tips.SetToolTip(record, "Ctrl+Alt+R is in use by another app"); if (!p) tips.SetToolTip(pause, "Ctrl+Alt+P is in use by another app"); if (!m) tips.SetToolTip(marker, "Ctrl+Alt+M is in use by another app");
                SyncIsland(); if (!settings.IslandEnabled) ShowWindow();
                if (settings.StartOnLaunch) BeginRecording();
            }));
        }
        private IslandState ReadIslandState()
        {
            bool active = recorder != null && recorder.IsAlive;
            bool busy = active && (stopping || recorder.State == "Starting" || recorder.State == "Stopping");
            string status = active ? stopping ? "Saving" : recorder.ManualPaused ? "Paused" : recorder.State == "Paused" ? "Resuming" : recorder.State : recorder?.Error != null ? "Error" : recorder != null ? "Saved" : "Afterimage";
            if (active && recorder.State == "Recording" && !recorder.ManualPaused && DateTime.UtcNow < markedUntil) status = "Marked";
            return new IslandState(active, active && recorder.ManualPaused, busy, status, Math.Floor(recorder?.RecordedSeconds ?? 0), companion?.IsStreaming == true ? "AI" : "");
        }
        private void SyncIsland()
        {
            if (!interfaceStarted || exitRequested) return;
            if (!settings.IslandEnabled) { island?.Hide(); return; }
            if (island == null) island = new IslandForm(ReadIslandState, () => settings, BeginRecording, TogglePause, StopRecording, ShowWindow,
                () => { ShowWindow(); using var library = new LibraryForm(settings.OutputFolder); library.ShowDialog(this); },
                () => { settings.IslandEnabled = false; SaveSettings(); }, ExitApp,
                position => { settings.IslandPosition = position; SaveSettings(); }, MarkStep, OpenWorkspace);
            island.Reposition(); island.RefreshState(); if (!island.Visible) island.Show();
        }
        private void DisplaysChanged(object sender, EventArgs e) { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => { UpdateOptions(); island?.Reposition(); })); }
        private void UpdateOptions()
        {
            updatingOptions = true; displayPicker.Items.Clear();
            foreach (var s in Screen.AllScreens) displayPicker.Items.Add(new DisplayChoice(s));
            displayPicker.SelectedIndex = Math.Max(0, Array.FindIndex(Screen.AllScreens, s => s.DeviceName == settings.Monitor));
            preset.SelectedIndex = settings.Fps == 2 && settings.MaxDimension == 1280 && settings.Bitrate == 1000000 ? 0 : settings.Fps == 5 && settings.MaxDimension == 1920 && settings.Bitrate == 2500000 ? 1 : settings.Fps == 15 && settings.MaxDimension == 2560 && settings.Bitrate == 5000000 ? 2 : 3;
            updatingOptions = false; UpdateFormat();
        }
        private void SetOptionsEnabled(bool enabled) { preferences.Enabled = displayPicker.Enabled = preset.Enabled = sessionName.Enabled = enabled; }
        private async void RefreshRecent()
        {
            int generation = ++libraryGeneration; string folder = settings.OutputFolder;
            var sessions = await Task.Run(() => SessionLibrary.Read(folder));
            if (IsDisposed || generation != libraryGeneration) return;
            foreach (Control c in recent.Controls.Cast<Control>().ToArray()) c.Dispose();
            foreach (var item in sessions.Take(2)) { var card = new SessionCard(item, true); card.Scale(new SizeF(DeviceDpi / 96f, DeviceDpi / 96f)); recent.Controls.Add(card); }
            if (sessions.Count == 0) { var empty = Theme.Label("No sessions yet", 10, FontStyle.Regular); empty.ForeColor = Theme.Muted; empty.Margin = new Padding(0, 20, 0, 0); recent.Controls.Add(empty); }
            latestSession = sessions.FirstOrDefault(s => File.Exists(Path.Combine(s.Folder, "Review.html")))?.Folder;
            review.Enabled = latestSession != null;
        }
        private void UpdateFormat() { format.Text = String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} fps   ·   {1} px   ·   {2:0.#} Mbps{3}", settings.Fps, settings.MaxDimension, settings.Bitrate / 1000000.0, settings.WorkflowEvents ? "   ·   Annotated" : ""); }
        private void SaveSettings() { try { if (persistSettings) Json.Save(settingsPath, settings); SyncIsland(); } catch (Exception e) { ShowError("Could not save settings: " + e.Message); } }
        private void BeginRecording()
        {
            if (recorder != null && recorder.IsAlive || exitRequested) return;
            try {
                Screen display = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Monitor) ?? Screen.PrimaryScreen; settings.Monitor = display.DeviceName; settings.SessionName = sessionName.Text; settings.Validate(); if (persistSettings) Json.Save(settingsPath, settings);
                if (settings.WorkflowEvents) InstallMouseHook();
                recorder = new Recorder(settings, display.Bounds); handledEnd = stopping = false; recorder.Start(); ticker.Interval = 100; SetOptionsEnabled(false); record.Text = "Stop recording"; state.Text = "Starting"; pause.Enabled = true; island?.RefreshState();
                using (Process p = Process.GetCurrentProcess()) lastCpu = p.TotalProcessorTime; lastCpuAt = DateTime.UtcNow;
            } catch (Exception e) { RemoveHook(); ShowError(e.Message); }
        }
        private void StopRecording() { if (recorder != null && recorder.IsAlive && !stopping) { stopping = true; recorder.Stop("Stopped by user"); record.Enabled = pause.Enabled = marker.Enabled = false; state.Text = "Saving"; island?.RefreshState(); } }
        private void ToggleRecording() { if (recorder != null && recorder.IsAlive) StopRecording(); else BeginRecording(); }
        private void TogglePause() { if (recorder != null && recorder.IsAlive && !stopping) { recorder.Pause(!recorder.ManualPaused); island?.RefreshState(); } }
        private void MarkStep() { if (recorder?.State == "Recording" && !recorder.ManualPaused && !stopping) { recorder.Mark(); markedUntil = DateTime.UtcNow.AddMilliseconds(900); state.Text = "Marked"; island?.RefreshState(); } }
        private void Tick()
        {
            island?.RefreshState();
            if (recorder == null) return;
            if (recorder.SessionFolder != null && latestSession != recorder.SessionFolder) { latestSession = recorder.SessionFolder; review.Enabled = false; reviewCheckedAt = DateTime.MinValue; }
            if (Visible && !checkingReview && (DateTime.UtcNow - reviewCheckedAt).TotalSeconds >= 1) RefreshReviewAvailability();
            elapsed.Text = SessionLibrary.Duration(recorder.RecordedSeconds, true);
            if (!recorder.IsAlive && !handledEnd) {
                handledEnd = true; ticker.Interval = 1000; RemoveHook(); SetOptionsEnabled(true); record.Enabled = true; record.Text = "Record"; pause.Enabled = marker.Enabled = false; pause.Text = "Pause"; state.Text = recorder.Error == null ? "Saved" : "Error"; state.ForeColor = recorder.Error == null ? Theme.Muted : Color.Firebrick; tray.Icon = idleIcon; tray.Text = "Afterimage · stopped"; RefreshRecent();
                if (recorder.Error != null) ShowError(recorder.Error); else if (recorder.StopReason != null && recorder.StopReason.StartsWith("Stopped:")) ShowError(recorder.StopReason);
                if (exitRequested) { Close(); return; }
            } else if (recorder.IsAlive) {
                state.Text = ReadIslandState().Status;
                state.ForeColor = recorder.State == "Recording" ? Color.FromArgb(194, 36, 65) : Theme.Muted; pause.Text = recorder.ManualPaused ? "Resume" : "Pause"; marker.Enabled = recorder.State == "Recording";
                var nextIcon = recorder.ManualPaused ? pausedIcon : recordingIcon; if (tray.Icon != nextIcon) tray.Icon = nextIcon;
                string trayText = "Afterimage · " + recorder.State.ToLowerInvariant(); if (tray.Text != trayText) tray.Text = trayText;
            }
            if (!Visible || (DateTime.UtcNow - lastCpuAt).TotalSeconds < 1) return;
            using (Process p = Process.GetCurrentProcess()) {
                DateTime now = DateTime.UtcNow; double cpu = Math.Max(0, (p.TotalProcessorTime - lastCpu).TotalSeconds / Math.Max(.1, (now - lastCpuAt).TotalSeconds) / Environment.ProcessorCount * 100);
                lastCpu = p.TotalProcessorTime; lastCpuAt = now; resources.Text = String.Format("{0:0}% CPU · {1:0} MB", cpu, p.WorkingSet64 / 1048576.0);
                tips.SetToolTip(resources, String.Format("{0} · {1:N0} frames · {2} clips · {3:0.00} GB in folder", recorder.CaptureBackend, recorder.Frames, recorder.SegmentCount, recorder.Bytes / 1073741824.0));
            }
        }
        private async void RefreshReviewAvailability()
        {
            string folder = latestSession; checkingReview = true; reviewCheckedAt = DateTime.UtcNow;
            try {
                bool available = folder != null && await Task.Run(() => File.Exists(Path.Combine(folder, "Review.html")));
                if (!IsDisposed && latestSession == folder) review.Enabled = available;
            } finally { checkingReview = false; }
        }
        private void InstallMouseHook()
        {
            RemoveHook();
            hookProc = delegate(int code, IntPtr message, IntPtr data) {
                try {
                    if (code >= 0 && recorder != null && recorder.EventsEnabled) {
                        int m = message.ToInt32(); string action = m == 0x201 ? "left_click" : m == 0x204 ? "right_click" : m == 0x207 ? "middle_click" : m == 0x20A ? "scroll" : null;
                        if (action != null) { Win.MouseData d = (Win.MouseData)Marshal.PtrToStructure(data, typeof(Win.MouseData)); if (island == null || !island.ContainsControlPoint(new Point(d.Point.X, d.Point.Y))) recorder.AddClick(action, d.Point.X, d.Point.Y, m == 0x20A ? (short)(d.Data >> 16) : 0); }
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
        private void OpenWorkspace(string page)
        {
            if (exitRequested) return;
            if (companion == null || companion.IsDisposed) {
                companion = new CompanionForm(() => settings.OutputFolder, text => {
                    if (recorder?.State != "Recording" || recorder.ManualPaused || stopping) return false;
                    recorder.Mark(text); markedUntil = DateTime.UtcNow.AddMilliseconds(900); island?.RefreshState(); return true;
                }, persistSettings ? null : new WorkspaceStore(Path.Combine(settings.OutputFolder, "workspace-test")));
                companion.ActivityChanged += () => island?.RefreshState();
            }
            companion.OpenPage(page);
        }
        internal void OpenCompanion() => OpenWorkspace("chat");
        private void ShowError(string text) { MessageBox.Show(this, text, "Afterimage", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        internal void ExitApp() { exitRequested = true; if (recorder != null && recorder.IsAlive) { stopping = true; recorder.Stop("Application exit"); record.Enabled = pause.Enabled = marker.Enabled = false; island?.RefreshState(); } else Close(); }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x312) { if (m.WParam.ToInt32() == 1) ToggleRecording(); if (m.WParam.ToInt32() == 2) TogglePause(); if (m.WParam.ToInt32() == 3) MarkStep(); if (m.WParam.ToInt32() == 4) OpenWorkspace("chat"); } if (m.Msg == 0x11 && recorder != null) recorder.Stop("Windows shutdown"); base.WndProc(ref m); }
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing && (settings.IslandEnabled || recorder != null && recorder.IsAlive)) { e.Cancel = true; Hide(); return; }
            if (companion != null && !companion.IsDisposed && !workspaceClosed) {
                e.Cancel = true;
                if (!closingWorkspace) {
                    closingWorkspace = true;
                    if (!await companion.ShutdownAsync()) { closingWorkspace = false; exitRequested = false; return; }
                    workspaceClosed = true; Close();
                }
                return;
            }
            if (recorder != null && recorder.IsAlive) { recorder.Stop("Application closing"); recorder.Wait(); }
            RemoveHook(); ticker.Stop(); island?.Dispose(); tray.Visible = false; tray.Dispose(); SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            for (int i = 1; i <= 4; i++) Win.UnregisterHotKey(Handle, i); idleIcon.Dispose(); recordingIcon.Dispose(); pausedIcon.Dispose(); base.OnFormClosing(e);
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        private static Icon MakeIcon(Color color) { using (Bitmap b = new Bitmap(32, 32)) { using (Graphics g = Graphics.FromImage(b)) { g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Color.Transparent); using (Brush brush = new SolidBrush(color)) g.FillEllipse(brush, 3, 3, 26, 26); g.FillRectangle(Brushes.White, 12, 12, 8, 8); } IntPtr p = b.GetHicon(); try { using (Icon i = Icon.FromHandle(p)) return (Icon)i.Clone(); } finally { DestroyIcon(p); } } }
    }
    internal sealed class SettingsForm : Form
    {
        internal Settings Result;
        private readonly ComboBox monitor = new ComboBox(), size = new ComboBox(), bitrate = new ComboBox(), capture = new ComboBox();
        private readonly NumericUpDown fps = new NumericUpDown(), minutes = new NumericUpDown(), limit = new NumericUpDown(), free = new NumericUpDown();
        private readonly CheckBox cursor = new CheckBox(), annotations = new CheckBox(), titles = new CheckBox(), hardware = new CheckBox(), launch = new CheckBox(), floating = new CheckBox();
        private readonly TextBox folder = new TextBox();
        private int rowIndex;
        internal SettingsForm(Settings config)
        {
            SuspendLayout(); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10); Text = "Settings"; BackColor = Theme.Paper; ForeColor = Theme.Ink; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(510, 624); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            SetupCombo(monitor); foreach (Screen s in Screen.AllScreens) monitor.Items.Add(new DisplayItem(s)); monitor.SelectedIndex = Math.Max(0, Array.FindIndex(Screen.AllScreens, s => s.DeviceName == config.Monitor)); Row("Display", monitor);
            Number(fps, 1, 30, config.Fps); Row("Frame rate", fps);
            SetupCombo(size); size.Items.AddRange(new object[] { "1280 px", "1920 px", "2560 px", "3840 px" }); size.SelectedIndex = Math.Max(0, Array.IndexOf(new int[] { 1280, 1920, 2560, 3840 }, config.MaxDimension)); Row("Longest edge", size);
            SetupCombo(bitrate); bitrate.Items.AddRange(new object[] { "1 Mbps", "2.5 Mbps", "5 Mbps", "10 Mbps" }); bitrate.SelectedIndex = Math.Max(0, Array.IndexOf(new int[] { 1000000, 2500000, 5000000, 10000000 }, config.Bitrate)); Row("Bitrate", bitrate);
            SetupCombo(capture); capture.Items.AddRange(new object[] { "Automatic · Direct3D 11", "Compatibility" }); capture.SelectedIndex = config.CaptureMode == "compatibility" ? 1 : 0; Row("Capture", capture);
            Number(minutes, 1, 60, Math.Max(1, config.SegmentSeconds / 60)); Row("Clip length · min", minutes);
            Number(limit, 1, 10000, config.MaxStorageGB); Row("Folder limit · GB", limit);
            Number(free, 1, 1000, config.MinFreeGB); Row("Free space · GB", free);
            folder.Width = 266; folder.Text = config.OutputFolder; Row("Output folder", folder); Button browse = Theme.Button("…", 35, false); browse.AccessibleName = "Choose output folder"; browse.SetBounds(441, 340, 35, 28); Controls.Add(browse); browse.Click += delegate { using (FolderBrowserDialog d = new FolderBrowserDialog { SelectedPath = folder.Text }) if (d.ShowDialog() == DialogResult.OK) folder.Text = d.SelectedPath; };
            Check(cursor, "Mouse pointer", config.Cursor); Check(annotations, "Auto annotations", config.WorkflowEvents); Check(titles, "Include window titles", config.WindowTitles); Check(hardware, "Hardware encoding", config.Hardware); Check(launch, "Record on app launch", config.StartOnLaunch);
            titles.Enabled = annotations.Checked; annotations.CheckedChanged += delegate { titles.Enabled = annotations.Checked; };
            Label optionLabel = Theme.Label("Options", 10, FontStyle.Regular); optionLabel.Location = new Point(24, 394); Controls.Add(optionLabel);
            Check(floating, "Floating controls", config.IslandEnabled);
            CheckBox[] checks = { cursor, annotations, titles, hardware, launch, floating }; for (int i = 0; i < checks.Length; i++) { checks[i].Location = new Point(166, 390 + i * 28); Controls.Add(checks[i]); }
            Button save = Theme.Button("Save", 100, true); save.SetBounds(384, 569, 100, 36); Controls.Add(save); AcceptButton = save;
            Button cancel = Theme.Link("Cancel"); cancel.SetBounds(288, 571, 86, 32); cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel); CancelButton = cancel;
            save.Click += delegate { try { Result = new Settings { CaptureMode = capture.SelectedIndex == 1 ? "compatibility" : "auto", SessionName = config.SessionName, IslandEnabled = floating.Checked, IslandPosition = config.IslandPosition, Monitor = ((DisplayItem)monitor.SelectedItem).Screen.DeviceName, Fps = (int)fps.Value, MaxDimension = new int[] { 1280, 1920, 2560, 3840 }[size.SelectedIndex], Bitrate = new int[] { 1000000, 2500000, 5000000, 10000000 }[bitrate.SelectedIndex], SegmentSeconds = (int)minutes.Value * 60, MaxStorageGB = (int)limit.Value, MinFreeGB = (int)free.Value, OutputFolder = folder.Text, Cursor = cursor.Checked, WorkflowEvents = annotations.Checked, WindowTitles = annotations.Checked && titles.Checked, Hardware = hardware.Checked, StartOnLaunch = launch.Checked }; Result.Validate(); DialogResult = DialogResult.OK; } catch (Exception e) { MessageBox.Show(this, e.Message, "Settings"); } };
            ResumeLayout(true);
        }
        private void SetupCombo(ComboBox c) { c.DropDownStyle = ComboBoxStyle.DropDownList; c.Width = 308; c.Margin = new Padding(0, 0, 0, 12); }
        private void Number(NumericUpDown c, int min, int max, int value) { c.Minimum = min; c.Maximum = max; c.Value = Math.Max(min, Math.Min(max, value)); c.Width = 100; c.Margin = new Padding(0, 0, 0, 12); }
        private void Check(CheckBox c, string text, bool value) { c.Text = text; c.Checked = value; c.AutoSize = true; c.Margin = new Padding(0, 2, 0, 6); }
        private void Row(string name, Control control) { int y = 20 + rowIndex++ * 40; Label label = Theme.Label(name, 10, FontStyle.Regular); label.Location = new Point(24, y + 4); control.Location = new Point(166, y); Controls.Add(label); Controls.Add(control); }
        private sealed class DisplayItem { internal readonly Screen Screen; internal DisplayItem(Screen screen) { Screen = screen; } public override string ToString() { return Screen.DeviceName.Replace(@"\\.\", "") + " · " + Screen.Bounds.Width + " × " + Screen.Bounds.Height; } }
    }
}
