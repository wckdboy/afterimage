using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Afterimage
{
    internal sealed class DisplayChoice
    {
        internal readonly string DeviceName;
        private readonly string label;
        internal DisplayChoice(Screen screen) { DeviceName = screen.DeviceName; label = "Display " + (Array.FindIndex(Screen.AllScreens, s => s.DeviceName == DeviceName) + 1) + "  ·  " + screen.Bounds.Width + " × " + screen.Bounds.Height; }
        public override string ToString() { return label; }
    }
    internal sealed class SessionItem
    {
        internal string Folder, Name, Status;
        internal DateTime Started;
        internal double Seconds;
        internal int Clips;
        internal string Detail { get { return Started.ToString("dd MMM · HH:mm") + "   ·   " + SessionLibrary.Duration(Seconds, false); } }
        internal void Open(IWin32Window owner)
        {
            try { string review = Path.Combine(Folder, "Review.html"); Theme.Open(File.Exists(review) ? review : Folder); }
            catch (Exception e) { MessageBox.Show(owner, e.Message, "Afterimage"); }
        }
    }
    internal static class SessionLibrary
    {
        internal static string Duration(double seconds, bool hours)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return hours || t.TotalHours >= 1 ? ((int)t.TotalHours).ToString("D2") + ":" + t.Minutes.ToString("D2") + ":" + t.Seconds.ToString("D2") : ((int)t.TotalMinutes).ToString() + ":" + t.Seconds.ToString("D2");
        }
        internal static List<SessionItem> Read(string folder)
        {
            var items = new List<SessionItem>();
            try {
                if (!Directory.Exists(folder)) return items;
                foreach (string dir in Directory.EnumerateDirectories(folder, "session-*").OrderByDescending(x => x, StringComparer.Ordinal)) {
                    try {
                        string file = Path.Combine(dir, "session.json");
                        if (!File.Exists(file) || new FileInfo(file).Length > 1024 * 1024) continue;
                        using var doc = JsonDocument.Parse(File.ReadAllText(file)); var root = doc.RootElement;
                        if (!root.TryGetProperty("started_utc", out var start) || !start.TryGetDateTime(out var started)) continue;
                        int clips = root.TryGetProperty("segments", out var segment) ? segment.GetInt32() : 0;
                        if (clips == 0) continue;
                        string name = root.TryGetProperty("name", out var title) ? title.GetString() : null;
                        double seconds = root.TryGetProperty("recorded_seconds", out var duration) ? duration.GetDouble() : root.TryGetProperty("elapsed_seconds", out duration) ? duration.GetDouble() : 0;
                        if (!double.IsFinite(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds) seconds = 0;
                        items.Add(new SessionItem { Folder = dir, Name = string.IsNullOrWhiteSpace(name) ? started.ToLocalTime().ToString("ddd, dd MMM · HH:mm") : name, Started = started.ToLocalTime(), Seconds = seconds, Clips = clips, Status = root.TryGetProperty("status", out var status) ? status.GetString() : "" });
                    } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (JsonException) { } catch (InvalidOperationException) { } catch (FormatException) { }
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return items;
        }
    }
    internal sealed class SessionCard : Button
    {
        private readonly SessionItem item;
        private Image preview;
        private bool hover;
        internal SessionCard(SessionItem item, bool compact)
        {
            this.item = item; Width = compact ? 210 : 224; Height = compact ? 136 : 164; Margin = new Padding(0, 0, 12, 16);
            AccessibleName = item.Name + ", " + item.Detail; Text = item.Name; FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand;
            DoubleBuffered = true; BackColor = Theme.Paper; ForeColor = Theme.Ink; Font = new Font("Segoe UI", 9);
            try { string path = Path.Combine(item.Folder, "thumbnail.jpg"); if (File.Exists(path) && new FileInfo(path).Length < 5 * 1024 * 1024) { using var stream = File.OpenRead(path); using var image = Image.FromStream(stream); preview = new Bitmap(image); } } catch (Exception e) when (e is IOException || e is ArgumentException || e is OutOfMemoryException || e is UnauthorizedAccessException) { }
            Click += delegate { item.Open(FindForm()); };
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = DeviceDpi / 96f; int lower = (int)(46 * scale), imageHeight = Height - lower;
            e.Graphics.Clear(Theme.Paper);
            var imageRect = new Rectangle(0, 0, Width - 1, imageHeight);
            using (var brush = new SolidBrush(Theme.Line)) e.Graphics.FillRectangle(brush, imageRect);
            if (preview != null) {
                float ratio = Math.Min((float)imageRect.Width / preview.Width, (float)imageRect.Height / preview.Height);
                int w = (int)(preview.Width * ratio), h = (int)(preview.Height * ratio);
                e.Graphics.DrawImage(preview, new Rectangle((Width - w) / 2, (imageHeight - h) / 2, w, h));
            }
            TextRenderer.DrawText(e.Graphics, item.Name, Font, new Rectangle(0, imageHeight + (int)(7 * scale), Width, (int)(18 * scale)), Theme.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            using var small = new Font("Segoe UI", 8);
            TextRenderer.DrawText(e.Graphics, item.Detail, small, new Rectangle(0, imageHeight + (int)(26 * scale), Width, (int)(18 * scale)), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (hover || Focused) using (var pen = new Pen(Theme.Muted)) e.Graphics.DrawRectangle(pen, imageRect);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
        }
        protected override void Dispose(bool disposing) { if (disposing) preview?.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class LibraryForm : Form
    {
        private readonly FlowLayoutPanel grid = new FlowLayoutPanel();
        private readonly TextBox search = new TextBox();
        private readonly Label count = Theme.Label("Loading…", 9, FontStyle.Regular);
        private List<SessionItem> sessions = new List<SessionItem>();
        internal LibraryForm(string folder)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Sessions · Afterimage"; Font = new Font("Segoe UI", 10); BackColor = Theme.Paper; StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 530); MinimumSize = new Size(540, 390);
            var title = Theme.Label("Sessions", 16, FontStyle.Bold); title.Location = new Point(24, 22); Controls.Add(title);
            search.PlaceholderText = "Search sessions"; search.AccessibleName = "Search sessions"; search.SetBounds(24, 72, 470, 30); search.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(search); search.TextChanged += delegate { Render(); };
            var folderButton = Theme.Button("Open folder", 130, false); folderButton.SetBounds(606, 66, 130, 38); folderButton.Anchor = AnchorStyles.Right | AnchorStyles.Top; folderButton.Click += delegate { try { Directory.CreateDirectory(folder); Theme.Open(folder); } catch (Exception e) { MessageBox.Show(this, e.Message, Text); } }; Controls.Add(folderButton);
            count.Location = new Point(24, 118); count.ForeColor = Theme.Muted; Controls.Add(count);
            grid.SetBounds(24, 153, 730, 350); grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; grid.AutoScroll = true; Controls.Add(grid);
            Shown += async delegate { sessions = await Task.Run(() => SessionLibrary.Read(folder)); if (!IsDisposed) Render(); };
            ResumeLayout(true);
        }
        private void Render()
        {
            foreach (Control c in grid.Controls.Cast<Control>().ToArray()) c.Dispose();
            var selected = sessions.Where(s => (s.Name + " " + s.Detail).Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();
            // Bound native handles and decoded thumbnails. Search still covers the full library.
            count.Text = selected.Count == 0 ? "No sessions" : selected.Count > 100 ? "100 of " + selected.Count + " sessions · refine your search" : selected.Count + (selected.Count == 1 ? " session" : " sessions");
            grid.SuspendLayout();
            foreach (var item in selected.Take(100)) { var card = new SessionCard(item, false); card.Scale(new SizeF(DeviceDpi / 96f, DeviceDpi / 96f)); grid.Controls.Add(card); }
            grid.ResumeLayout();
        }
    }
}
