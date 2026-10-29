using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Afterimage
{
    internal readonly record struct IslandState(bool Active, bool Paused, bool Busy, string Status, double Seconds);

    // A native, non-activating window: using the controls leaves the workflow app focused.
    internal sealed class IslandForm : Form
    {
        private static readonly Color Surface = Color.FromArgb(20, 21, 24), Dim = Color.FromArgb(153, 156, 165);
        private readonly Func<IslandState> readState;
        private readonly Func<Settings> readSettings;
        private readonly Timer animation = new Timer { Interval = 16 }, leave = new Timer { Interval = 400 };
        private readonly Stopwatch motion = new Stopwatch();
        private readonly IslandButton record, pause, stop, more;
        private readonly ContextMenuStrip menu;
        private readonly ToolTip tips = new ToolTip();
        private IslandState state;
        private bool expanded, menuOpen;
        private float expansion, from, target;
        internal IslandForm(Func<IslandState> readState, Func<Settings> readSettings, Action start, Action togglePause, Action finish, Action open, Action library, Action hide, Action exit, Action<string> setPosition)
        {
            this.readState = readState; this.readSettings = readSettings;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = Surface; DoubleBuffered = true; Text = "Afterimage controls";
            AccessibleName = "Afterimage floating recording controls";
            Font = new Font("Segoe UI", 9);
            record = AddButton("Record", "record", start); pause = AddButton("Pause", "pause", togglePause); stop = AddButton("Stop", "stop", finish);
            more = AddButton("", "more", () => ShowMenu()); more.AccessibleName = "More options";
            tips.SetToolTip(record, "Record · Ctrl+Alt+R"); tips.SetToolTip(pause, "Pause / resume · Ctrl+Alt+P"); tips.SetToolTip(stop, "Stop · Ctrl+Alt+R"); tips.SetToolTip(more, "Position, sessions and settings");
            menu = new ContextMenuStrip { ShowImageMargin = false, Font = Font, BackColor = Color.FromArgb(32, 33, 38), ForeColor = Color.White, Renderer = new IslandMenuRenderer() };
            menu.Items.Add("Open Afterimage", null, (_, _) => open()); menu.Items.Add("Sessions", null, (_, _) => library());
            menu.Items.Add(new ToolStripSeparator());
            var positions = new ToolStripMenuItem("Position");
            foreach (string value in new[] { "left", "center", "right" }) {
                var item = new ToolStripMenuItem(value == "center" ? "Center" : value == "left" ? "Left" : "Right") { Tag = value };
                item.Click += (_, _) => { setPosition(value); Reposition(); }; positions.DropDownItems.Add(item);
            }
            menu.Items.Add(positions); menu.Items.Add("Hide controls", null, (_, _) => hide()); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, (_, _) => exit());
            menu.Opened += (_, _) => ExcludeFromCapture(menu.Handle);
            positions.DropDownOpened += (_, _) => { positions.DropDown.BackColor = menu.BackColor; positions.DropDown.ForeColor = Color.White; ExcludeFromCapture(positions.DropDown.Handle); };
            menu.Opening += (_, _) => { menuOpen = true; leave.Stop(); foreach (ToolStripMenuItem item in positions.DropDownItems) item.Checked = (string)item.Tag == readSettings().IslandPosition; };
            menu.Closed += (_, _) => { menuOpen = false; leave.Start(); };
            animation.Tick += (_, _) => {
                float t = Math.Min(1, (float)motion.Elapsed.TotalMilliseconds / 180);
                expansion = from + (target - from) * (1 - (float)Math.Pow(1 - t, 3)); LayoutIsland();
                if (t >= 1) { animation.Stop(); UpdateControls(); }
            };
            leave.Tick += (_, _) => { if (!menuOpen && !Bounds.Contains(Cursor.Position)) { leave.Stop(); SetExpanded(false); } };
            MouseEnter += (_, _) => EnterIsland(); MouseLeave += (_, _) => leave.Start();
            MouseDown += (_, e) => { if (e.Button == MouseButtons.Right) ShowMenu(); else SetExpanded(true); };
            DpiChanged += (_, _) => BeginInvoke((Action)LayoutIsland);
            RefreshState();
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000080; return p; } }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } base.WndProc(ref m); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ExcludeFromCapture(Handle);
            LayoutIsland();
        }
        internal void RefreshState() { if (IsDisposed) return; var next = readState(); if (next == state) return; state = next; UpdateControls(); Invalidate(); }
        internal void Reposition() { if (!IsDisposed) LayoutIsland(); }
        internal bool ContainsControlPoint(Point point) => Visible && Bounds.Contains(point) || menu.Visible && menu.Bounds.Contains(point) || menu.Items.OfType<ToolStripMenuItem>().Any(item => item.HasDropDownItems && item.DropDown.Visible && item.DropDown.Bounds.Contains(point));
        private IslandButton AddButton(string text, string glyph, Action action)
        {
            var b = new IslandButton { Text = text, Glyph = glyph, Font = Font, AccessibleName = text, TabStop = true };
            b.Click += (_, _) => { action(); RefreshState(); }; b.MouseEnter += (_, _) => EnterIsland(); b.MouseLeave += (_, _) => leave.Start(); Controls.Add(b); return b;
        }
        private void EnterIsland() { leave.Stop(); SetExpanded(true); }
        internal void SetExpanded(bool value)
        {
            if (expanded == value) return; expanded = value; from = expansion; target = value ? 1 : 0;
            motion.Restart(); animation.Start(); UpdateControls();
        }
        private void ShowMenu() { SetExpanded(true); menu.Show(this, new Point(Width - Px(185), Height + Px(6))); }
        private int Px(float n) => (int)Math.Round(n * DeviceDpi / 96f);
        internal static Point AnchorPoint(Rectangle area, Size size, string position, int margin, int top)
        {
            int x = position == "left" ? area.Left + margin : position == "right" ? area.Right - margin - size.Width : area.Left + (area.Width - size.Width) / 2;
            return new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width)), area.Top + top);
        }
        private void LayoutIsland()
        {
            Size = new Size(Px(136 + 188 * expansion), Px(30 + 70 * expansion));
            var config = readSettings(); var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == config.Monitor) ?? Screen.PrimaryScreen;
            Location = AnchorPoint(screen.WorkingArea, Size, config.IslandPosition, Px(18), Px(8));
            using var shape = Round(new RectangleF(0, 0, Width, Height), Px(15 + 6 * expansion));
            var old = Region; Region = new Region(shape); old?.Dispose();
            record.SetBounds(Px(12), Px(49), Px(98), Px(38)); pause.SetBounds(Px(116), Px(49), Px(94), Px(38)); stop.SetBounds(Px(216), Px(49), Px(96), Px(38)); more.SetBounds(Px(281), Px(9), Px(30), Px(29));
            Invalidate();
        }
        private void UpdateControls()
        {
            bool show = expanded && expansion > .95f;
            foreach (Control c in Controls) c.Visible = show;
            record.Enabled = !state.Active && !state.Busy; pause.Enabled = state.Active && !state.Busy; stop.Enabled = state.Active && (!state.Busy || state.Status == "Starting");
            pause.Text = state.Paused ? "Resume" : "Pause"; pause.Glyph = state.Paused ? "play" : "pause"; pause.AccessibleName = pause.Text;
            AccessibleDescription = state.Status + (state.Active ? " " + SessionLibrary.Duration(state.Seconds, true) : "");
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Surface);
            using var shape = Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), Px(15 + 6 * expansion));
            using var border = new Pen(Color.FromArgb(55, 57, 64), Math.Max(1, Px(.5f))); g.DrawPath(border, shape);
            Color status = state.Busy ? Dim : state.Paused ? Color.FromArgb(239, 188, 91) : state.Active ? Color.FromArgb(255, 83, 104) : Color.FromArgb(128, 132, 144);
            bool full = expansion > .8f; int dotX = Px(full ? 17 : 15), dotY = Px(full ? 22 : 15);
            using var dot = new SolidBrush(status); g.FillEllipse(dot, dotX - Px(3), dotY - Px(3), Px(6), Px(6));
            string label = full ? state.Status : state.Active ? SessionLibrary.Duration(state.Seconds, true) : "Afterimage";
            TextRenderer.DrawText(g, label, Font, new Rectangle(Px(28), Px(full ? 8 : 0), Px(full ? 132 : 102), Px(30)), full ? Dim : Color.FromArgb(223, 225, 230), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (full) {
                using var timerFont = new Font("Segoe UI", 10, FontStyle.Regular);
                TextRenderer.DrawText(g, SessionLibrary.Duration(state.Seconds, true), timerFont, new Rectangle(Px(164), Px(8), Px(108), Px(30)), Color.FromArgb(238, 239, 242), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }
        internal static GraphicsPath Round(RectangleF b, float radius)
        {
            var p = new GraphicsPath(); float d = radius * 2;
            p.AddArc(b.Left, b.Top, d, d, 180, 90); p.AddArc(b.Right - d, b.Top, d, d, 270, 90); p.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90); p.AddArc(b.Left, b.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        protected override void Dispose(bool disposing) { if (disposing) { animation.Dispose(); leave.Dispose(); tips.Dispose(); menu.Dispose(); } base.Dispose(disposing); }
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        private static void ExcludeFromCapture(IntPtr handle) { if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) SetWindowDisplayAffinity(handle, 0x11); }

        private sealed class IslandButton : Button
        {
            internal string Glyph;
            private bool hover, pressed;
            internal IslandButton() { DoubleBuffered = true; Cursor = Cursors.Hand; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; float s = DeviceDpi / 96f; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Surface);
                bool primary = Glyph == "record" && Enabled;
                Color fill = primary ? Color.FromArgb(241, 243, 248) : Color.FromArgb(39, 41, 47);
                if (hover && Enabled) fill = primary ? Color.White : Color.FromArgb(58, 60, 68); if (pressed && Enabled) fill = Color.FromArgb(88, 90, 100);
                if (Glyph != "more" || hover) { using var path = Round(new RectangleF(0, 0, Width, Height), 10 * s); using var b = new SolidBrush(fill); g.FillPath(b, path); }
                Color fg = !Enabled ? Color.FromArgb(89, 92, 102) : primary ? Surface : Color.FromArgb(230, 232, 238);
                using var ink = new SolidBrush(fg); float x = 17 * s, y = Height / 2f;
                if (Glyph == "record") { using var red = new SolidBrush(Enabled ? Color.FromArgb(226, 52, 76) : fg); g.FillEllipse(red, x - 4 * s, y - 4 * s, 8 * s, 8 * s); }
                if (Glyph == "stop") { using var p = Round(new RectangleF(x - 4 * s, y - 4 * s, 8 * s, 8 * s), 2 * s); g.FillPath(ink, p); }
                if (Glyph == "pause") { g.FillRectangle(ink, x - 4 * s, y - 4 * s, 3 * s, 8 * s); g.FillRectangle(ink, x + s, y - 4 * s, 3 * s, 8 * s); }
                if (Glyph == "play") g.FillPolygon(ink, new[] { new PointF(x - 3 * s, y - 5 * s), new PointF(x + 5 * s, y), new PointF(x - 3 * s, y + 5 * s) });
                if (Glyph == "more") { for (int i = -1; i <= 1; i++) g.FillEllipse(ink, Width / 2f + i * 5 * s - s, y - s, 2 * s, 2 * s); }
                else TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(30 * s), 0, Width - (int)(33 * s), Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }
        private sealed class IslandMenuRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { if (e.Item.Selected) using (var b = new SolidBrush(Color.FromArgb(61, 64, 73))) e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size)); }
        }
    }
}
