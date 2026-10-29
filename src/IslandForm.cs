using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Afterimage
{
    internal readonly record struct IslandState(bool Active, bool Paused, bool Busy, string Status, double Seconds, string Activity = "");

    // One alpha surface; native child buttons retain hit testing and accessibility.
    internal sealed class IslandForm : Form
    {
        private const float Inset = 6;
        private static readonly Color Dim = Color.FromArgb(168, 174, 189);
        private readonly Func<IslandState> readState;
        private readonly Func<Settings> readSettings;
        private readonly Timer animation = new Timer { Interval = 8 }, leave = new Timer { Interval = 180 };
        private readonly Stopwatch motion = new Stopwatch();
        private readonly LayeredSurface surface = new LayeredSurface();
        private readonly Font textFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel), timerFont = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly IslandButton record, pause, stop, more;
        private readonly ContextMenuStrip menu;
        private readonly ToolTip tips = new ToolTip();
        private IslandState state;
        private bool expanded, menuOpen, drawing;
        private float expansion, velocity, restExpansion, restVelocity;
        private double lastFrame;
        private Size canvasSize;
        private Rectangle displayArea;
        internal bool IsAnimating => animation.Enabled;
        internal float Expansion => expansion;
        internal int PresentCount { get; private set; }
        private float ScaleFactor => DeviceDpi / 96f;
        private float RestWidth => state.Active ? (state.Activity.Length > 0 ? 138 : 114) : state.Activity.Length > 0 ? 84 : 56;
        private RectangleF Body { get { float width = 56 + 82 * restExpansion; return new RectangleF(Inset, Inset, width + (324 - width) * expansion, 26 + 74 * expansion); } }
        internal IslandForm(Func<IslandState> readState, Func<Settings> readSettings, Action start, Action togglePause, Action finish, Action open, Action library, Action hide, Action exit, Action<string> setPosition, Action mark = null, Action<string> workspace = null)
        {
            this.readState = readState; this.readSettings = readSettings;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; Text = "Afterimage controls"; AccessibleName = "Afterimage floating recording controls"; Font = new Font("Segoe UI", 9);
            record = AddButton("Record", "record", () => { if (state.Active) mark?.Invoke(); else start(); });
            pause = AddButton("Chat", "chat", () => { if (state.Active) togglePause(); else workspace?.Invoke("chat"); });
            stop = AddButton("Notes", "notes", () => { if (state.Active) finish(); else workspace?.Invoke("notes"); });
            more = AddButton("", "more", ShowMenu); more.AccessibleName = "More options";
            tips.SetToolTip(record, "Record · Ctrl+Alt+R"); tips.SetToolTip(pause, "Pause / resume · Ctrl+Alt+P"); tips.SetToolTip(stop, "Stop · Ctrl+Alt+R"); tips.SetToolTip(more, "Position, sessions and settings");
            menu = new ContextMenuStrip { ShowImageMargin = false, Font = Font, BackColor = Color.FromArgb(30, 33, 41), ForeColor = Color.White, Renderer = new IslandMenuRenderer() };
            menu.Items.Add("Chat", null, (_, _) => workspace?.Invoke("chat")); menu.Items.Add("Notes", null, (_, _) => workspace?.Invoke("notes")); menu.Items.Add("Recording settings", null, (_, _) => open()); menu.Items.Add("Sessions", null, (_, _) => library()); menu.Items.Add(new ToolStripSeparator());
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
            animation.Tick += (_, _) => Animate();
            leave.Tick += (_, _) => { if (!menuOpen && !Bounds.Contains(Cursor.Position)) { leave.Stop(); SetExpanded(false); } };
            MouseEnter += (_, _) => EnterIsland(); MouseLeave += (_, _) => leave.Start();
            MouseDown += (_, e) => { if (e.Button == MouseButtons.Right) ShowMenu(); else SetExpanded(true); };
            DpiChanged += (_, _) => BeginInvoke((Action)RenderFrame);
            VisibleChanged += (_, _) => { if (Visible) { RenderFrame(); if (Math.Abs(expansion - (expanded ? 1 : 0)) > .0007f) StartAnimation(); } else { animation.Stop(); leave.Stop(); } };
            CacheDisplay(); RefreshState();
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08080080; return p; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }
            if (m.Msg == 0x84 && IsHandleCreated) {
                long packed = m.LParam.ToInt64(); var point = PointToClient(new Point((short)packed, (short)(packed >> 16)));
                if (!BodyContains(point)) { m.Result = (IntPtr)(-1); return; }
            }
            base.WndProc(ref m);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ExcludeFromCapture(Handle); RenderFrame(); }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        internal void RefreshState()
        {
            if (IsDisposed) return; var next = readState(); if (next == state) return; state = next;
            float rest = (RestWidth - 56) / 82;
            if (!Visible || expansion == 1 || !ClientAnimationsEnabled()) { restExpansion = rest; restVelocity = 0; }
            else if (Math.Abs(restExpansion - rest) > .0007f) StartAnimation();
            UpdateControls(); RenderFrame();
        }
        private void CacheDisplay() { var config = readSettings(); displayArea = (Screen.AllScreens.FirstOrDefault(s => s.DeviceName == config.Monitor) ?? Screen.PrimaryScreen).WorkingArea; }
        internal void Reposition() { if (!IsDisposed) { CacheDisplay(); RenderFrame(); } }
        internal bool ContainsControlPoint(Point point) => Visible && BodyContains(PointToClient(point)) || menu.Visible && menu.Bounds.Contains(point) || menu.Items.OfType<ToolStripMenuItem>().Any(item => item.HasDropDownItems && item.DropDown.Visible && item.DropDown.Bounds.Contains(point));
        private bool BodyContains(Point point)
        {
            var b = Body; float x = point.X / ScaleFactor, y = point.Y / ScaleFactor, r = 13 + 11 * expansion;
            if (!b.Contains(x, y)) return false;
            float dx = x - Math.Clamp(x, b.Left + r, b.Right - r), dy = y - Math.Clamp(y, b.Top + r, b.Bottom - r);
            return dx * dx + dy * dy <= r * r;
        }
        private IslandButton AddButton(string text, string glyph, Action action)
        {
            var b = new IslandButton(this) { Text = text, Glyph = glyph, Font = Font, AccessibleName = text, TabStop = true };
            b.Click += (_, _) => { action(); RefreshState(); }; b.MouseEnter += (_, _) => EnterIsland(); b.MouseLeave += (_, _) => leave.Start(); Controls.Add(b); return b;
        }
        private void EnterIsland() { leave.Stop(); SetExpanded(true); }
        internal void SetExpanded(bool value)
        {
            if (expanded == value) return; expanded = value;
            // Reversals preserve velocity instead of restarting an ease from rest.
            if (!ClientAnimationsEnabled()) { expansion = value ? 1 : 0; velocity = 0; UpdateControls(); RenderFrame(); return; }
            StartAnimation(); UpdateControls();
        }
        private void StartAnimation() { if (animation.Enabled) return; motion.Restart(); lastFrame = 0; animation.Start(); }
        internal static void StepSpring(ref float position, ref float speed, float target, float seconds)
        {
            const float frequency = 46;
            float error = position - target, c = speed + frequency * error, decay = MathF.Exp(-frequency * seconds);
            position = target + (error + c * seconds) * decay; speed = (speed - frequency * c * seconds) * decay;
            if (position < 0 || position > 1) { position = Math.Clamp(position, 0, 1); speed = 0; }
        }
        private void Animate()
        {
            double now = motion.Elapsed.TotalSeconds; float dt = (float)Math.Min(.075, now - lastFrame); lastFrame = now;
            float target = expanded ? 1 : 0; StepSpring(ref expansion, ref velocity, target, dt);
            bool moving = Math.Abs(expansion - target) > .0007f || Math.Abs(velocity) > .012f;
            if (!moving) { expansion = target; velocity = 0; }
            float rest = (RestWidth - 56) / 82; StepSpring(ref restExpansion, ref restVelocity, rest, dt);
            bool restMoving = Math.Abs(restExpansion - rest) > .0007f || Math.Abs(restVelocity) > .012f;
            if (!restMoving) { restExpansion = rest; restVelocity = 0; } moving |= restMoving;
            foreach (var b in Controls.OfType<IslandButton>()) moving |= b.AnimateHover(dt);
            UpdateControls(); RenderFrame(); if (!moving) animation.Stop();
        }
        private void ShowMenu() { SetExpanded(true); menu.Show(this, new Point(Math.Max(0, Width - Px(185)), Height + Px(3))); }
        private int Px(float n) => (int)Math.Round(n * ScaleFactor);
        internal static Point AnchorPoint(Rectangle area, Size size, string position, int margin, int top)
        {
            int x = position == "left" ? area.Left + margin : position == "right" ? area.Right - margin - size.Width : area.Left + (area.Width - size.Width) / 2;
            return new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width)), area.Top + top);
        }
        private void RenderFrame()
        {
            if (drawing || !IsHandleCreated || IsDisposed) return; drawing = true;
            try {
                float scale = ScaleFactor; var body = Body;
                canvasSize = new Size((int)Math.Ceiling((body.Width + Inset * 2) * scale), (int)Math.Ceiling((body.Height + Inset * 2) * scale));
                surface.EnsureCapacity(new Size((int)Math.Ceiling(336 * scale), (int)Math.Ceiling(112 * scale)));
                float spacing = (body.Width - 24) / 300, row = body.Height - 51;
                Place(record, 12, row, 98 * spacing, 38); Place(pause, 12 + 104 * spacing, row, 94 * spacing, 38); Place(stop, 12 + 204 * spacing, row, 96 * spacing, 38); Place(more, body.Width - 43, 9, 30, 29);
                surface.Draw(canvasSize, scale, DrawScene);
                var location = AnchorPoint(displayArea, canvasSize, readSettings().IslandPosition, Px(18 - Inset), Px(8 - Inset));
                surface.Present(Handle, location, canvasSize); PresentCount++;
            } finally { drawing = false; }
        }
        private void Place(IslandButton b, float x, float y, float width, float height)
        {
            b.VisualBounds = new RectangleF(Inset + x, Inset + y, width, height); b.SetBounds(Px(Inset + x), Px(Inset + y), Px(width), Px(height));
        }
        private void UpdateControls()
        {
            // Keep native input targets in sync with the visible alpha surface.
            bool interactive = expanded && expansion > .86f; foreach (Control c in Controls) c.Visible = interactive;
            record.Enabled = !state.Busy && !state.Paused; record.Text = state.Active ? "Mark" : "Record"; record.Glyph = state.Active ? "mark" : "record"; record.AccessibleName = record.Text;
            tips.SetToolTip(record, state.Active ? "Mark a step · Ctrl+Alt+M" : "Record · Ctrl+Alt+R");
            pause.Enabled = !state.Busy; stop.Enabled = !state.Busy || state.Status == "Starting";
            pause.Text = !state.Active ? "Chat" : state.Paused ? "Resume" : "Pause"; pause.Glyph = !state.Active ? "chat" : state.Paused ? "play" : "pause"; pause.AccessibleName = pause.Text;
            stop.Text = state.Active ? "Stop" : "Notes"; stop.Glyph = state.Active ? "stop" : "notes"; stop.AccessibleName = stop.Text;
            tips.SetToolTip(pause, state.Active ? "Pause / resume · Ctrl+Alt+P" : "Open AI chat · Ctrl+Alt+Space"); tips.SetToolTip(stop, state.Active ? "Stop · Ctrl+Alt+R" : "Open notes");
            AccessibleDescription = state.Status + (state.Active ? " " + SessionLibrary.Duration(state.Seconds, true) : "");
        }
        internal Bitmap Snapshot() => surface.Snapshot(canvasSize);
        internal Bitmap SnapshotAtScale(float scale)
        {
            using var raster = new LayeredSurface(); var body = Body;
            var size = new Size((int)Math.Ceiling((body.Width + Inset * 2) * scale), (int)Math.Ceiling((body.Height + Inset * 2) * scale));
            raster.EnsureCapacity(size); raster.Draw(size, scale, DrawScene); return raster.Snapshot(size);
        }
        private static float Smooth(float from, float to, float value) { float t = Math.Clamp((value - from) / (to - from), 0, 1); return t * t * (3 - 2 * t); }
        private static Color Fade(Color color, float opacity) => Color.FromArgb((int)Math.Clamp(Math.Round(color.A * opacity), 0, 255), color.R, color.G, color.B);
        private void DrawScene(Graphics g)
        {
            var body = Body; float radius = 13 + 11 * expansion;
            for (int i = 5; i >= 1; i--) {
                var box = body; box.Inflate(i, i); box.Y += 1;
                using var shadow = Round(box, radius + i); using var brush = new SolidBrush(Color.FromArgb(4 + (5 - i) * 2, 5, 7, 14)); g.FillPath(brush, shadow);
            }
            using var shape = Round(body, radius);
            using (var glass = new LinearGradientBrush(body, Color.FromArgb(239, 43, 47, 58), Color.FromArgb(231, 17, 19, 26), 90)) g.FillPath(glass, shape);
            var rimBox = RectangleF.Inflate(body, -.55f, -.55f);
            using (var rim = Round(rimBox, radius - .55f))
            using (var reflection = new LinearGradientBrush(body, Color.FromArgb(72, 224, 232, 255), Color.FromArgb(15, 205, 215, 245), 90))
            using (var edge = new Pen(reflection, .8f)) g.DrawPath(edge, rim);
            var contentState = g.Save(); g.SetClip(shape);
            float header = Smooth(.25f, .82f, expansion), compact = 1 - Smooth(.06f, .45f, expansion), reveal = Smooth(.48f, .86f, expansion);
            Color status = state.Busy ? Dim : state.Paused ? Color.FromArgb(242, 193, 107) : state.Active ? Color.FromArgb(255, 102, 124) : state.Activity.Length > 0 ? Brand.Frost : Color.FromArgb(167, 175, 194);
            float dotX = Inset + 38 - 21 * expansion, dotY = Inset + 13 + 9 * expansion;
            if (compact > 0) { var saved = g.Save(); g.SetClip(new RectangleF(Inset, Inset, 30, 26)); Brand.Draw(g, new RectangleF(Inset + 8, Inset + 4, 18, 18), null, compact); g.Restore(saved); }
            using (var dot = new SolidBrush(status)) g.FillEllipse(dot, dotX - 2.3f, dotY - 2.3f, 4.6f, 4.6f);
            string compactText = (state.Active ? SessionLibrary.Duration(state.Seconds, true) : "") + (state.Activity.Length > 0 ? (state.Active ? " · " : "") + state.Activity : "");
            DrawLabel(g, compactText, textFont, new RectangleF(Inset + 48, Inset, RestWidth - 52, 26), Color.FromArgb(229, 233, 242), compact);
            DrawLabel(g, state.Status + (state.Activity.Length > 0 ? " · " + state.Activity : ""), textFont, new RectangleF(Inset + 28, Inset + 8, 132, 30), Dim, header);
            if (state.Active || state.Seconds > 0) DrawLabel(g, SessionLibrary.Duration(state.Seconds, true), timerFont, new RectangleF(body.Right - 160, Inset + 8, 108, 30), Color.FromArgb(242, 244, 250), header, StringAlignment.Far);
            if (reveal > 0) foreach (var b in Controls.OfType<IslandButton>()) DrawButton(g, b, b == more ? header : reveal);
            g.Restore(contentState);
        }
        private void DrawButton(Graphics g, IslandButton button, float opacity)
        {
            var box = button.VisualBounds; bool primary = button.Glyph == "record" && button.Enabled; float hover = button.Hover;
            if (button.Glyph != "more" || hover > 0) {
                Color top = primary ? Color.FromArgb(248, 244, 247, 255) : Color.FromArgb((int)(18 + hover * 15), 220, 229, 255);
                Color bottom = primary ? Color.FromArgb(235, 217, 224, 240) : Color.FromArgb((int)(10 + hover * 12), 208, 219, 248);
                if (button.Pressed && button.Enabled) { top = Color.FromArgb(80, 175, 190, 220); bottom = Color.FromArgb(60, 134, 152, 186); }
                float alpha = opacity * (button.Glyph == "more" ? hover : 1);
                using var path = Round(box, 11); using var fill = new LinearGradientBrush(box, Fade(top, alpha), Fade(bottom, alpha), 90); g.FillPath(fill, path);
                using var border = new Pen(Fade(Color.FromArgb(primary ? 100 : 23, 236, 242, 255), alpha), .6f); g.DrawPath(border, path);
            }
            Color fg = !button.Enabled ? Color.FromArgb(100, 108, 125) : primary ? Color.FromArgb(27, 33, 46) : Color.FromArgb(235, 240, 250);
            using var ink = new SolidBrush(Fade(fg, opacity)); float x = box.Left + 17, y = box.Top + box.Height / 2;
            switch (button.Glyph) {
                case "record": using (var red = new SolidBrush(Fade(button.Enabled ? Color.FromArgb(222, 61, 89) : fg, opacity))) g.FillEllipse(red, x - 3.7f, y - 3.7f, 7.4f, 7.4f); break;
                case "mark": g.FillPolygon(ink, new[] { new PointF(x - 3, y - 4), new PointF(x + 3, y - 4), new PointF(x + 3, y + 4), new PointF(x, y + 2), new PointF(x - 3, y + 4) }); break;
                case "chat": using (var p = Round(new RectangleF(x - 4, y - 4, 8, 7), 2)) { using var pen = new Pen(Fade(fg, opacity), 1.2f); g.DrawPath(pen, p); g.DrawLine(pen, x - 2, y + 3, x - 3, y + 5); } break;
                case "notes": using (var pen = new Pen(Fade(fg, opacity), 1.2f)) { for (int i = -1; i <= 1; i++) g.DrawLine(pen, x - 4, y + i * 3, x + (i == 1 ? 1 : 4), y + i * 3); } break;
                case "stop": using (var p = Round(new RectangleF(x - 3.6f, y - 3.6f, 7.2f, 7.2f), 1.8f)) g.FillPath(ink, p); break;
                case "pause": using (var left = Round(new RectangleF(x - 3.5f, y - 4, 2.4f, 8), .7f)) g.FillPath(ink, left); using (var right = Round(new RectangleF(x + 1.1f, y - 4, 2.4f, 8), .7f)) g.FillPath(ink, right); break;
                case "play": g.FillPolygon(ink, new[] { new PointF(x - 2.8f, y - 4.4f), new PointF(x + 4.3f, y), new PointF(x - 2.8f, y + 4.4f) }); break;
                case "more": for (int i = -1; i <= 1; i++) g.FillEllipse(ink, box.Left + box.Width / 2 + i * 5 - 1, y - 1, 2, 2); break;
            }
            if (button.Glyph != "more") DrawLabel(g, button.Text, textFont, new RectangleF(box.Left + 30, box.Top, box.Width - 33, box.Height), fg, opacity);
        }
        private static void DrawLabel(Graphics g, string text, Font font, RectangleF box, Color color, float opacity, StringAlignment align = StringAlignment.Near)
        {
            if (opacity <= 0) return;
            using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = align, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
            using var brush = new SolidBrush(Fade(color, opacity)); g.DrawString(text ?? "", font, brush, box, format);
        }
        internal static GraphicsPath Round(RectangleF b, float radius)
        {
            var p = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(b.Width, b.Height));
            p.AddArc(b.Left, b.Top, d, d, 180, 90); p.AddArc(b.Right - d, b.Top, d, d, 270, 90); p.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90); p.AddArc(b.Left, b.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        protected override void Dispose(bool disposing) { if (disposing) { animation.Dispose(); leave.Dispose(); tips.Dispose(); menu.Dispose(); surface.Dispose(); textFont.Dispose(); timerFont.Dispose(); } base.Dispose(disposing); }
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
        private static bool ClientAnimationsEnabled() => !SystemParametersInfo(0x1042, 0, out int enabled, 0) || enabled != 0;
        private static void ExcludeFromCapture(IntPtr handle) { if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) SetWindowDisplayAffinity(handle, 0x11); }
        private sealed class IslandButton : Button
        {
            internal string Glyph;
            internal RectangleF VisualBounds;
            internal float Hover;
            internal bool Pressed;
            private float hoverTarget;
            private readonly IslandForm owner;
            internal IslandButton(IslandForm owner) { this.owner = owner; Cursor = Cursors.Hand; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; }
            internal bool AnimateHover(float dt) { Hover += (hoverTarget - Hover) * (1 - MathF.Exp(-24 * dt)); if (Math.Abs(Hover - hoverTarget) < .005f) { Hover = hoverTarget; return false; } return true; }
            protected override void OnMouseEnter(EventArgs e) { hoverTarget = Enabled ? 1 : 0; owner.StartAnimation(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hoverTarget = 0; Pressed = false; owner.StartAnimation(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { Pressed = true; owner.RenderFrame(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; owner.RenderFrame(); base.OnMouseUp(e); }
            protected override void OnPaint(PaintEventArgs e) { }
            protected override void OnPaintBackground(PaintEventArgs e) { }
        }
        private sealed class IslandMenuRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { if (e.Item.Selected) using (var b = new SolidBrush(Color.FromArgb(54, 60, 75))) e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size)); }
        }
    }
}
